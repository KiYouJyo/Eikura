param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-LastExitCode([string]$message) {
    if ($LASTEXITCODE -ne 0) { throw "$message ($LASTEXITCODE)" }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$manifestPath = Join-Path $repoRoot 'src/Eizo.App/Package.appxmanifest'
$storeIdentityPath = Join-Path $repoRoot 'release/MicrosoftStore/store-identity.json'
$releaseMetadataPath = Join-Path $repoRoot 'release/release.json'
$originalManifestText = $null

Push-Location $repoRoot
try {
    if (-not (Test-Path -LiteralPath $releaseMetadataPath -PathType Leaf)) {
        throw "Release metadata is missing: $releaseMetadataPath"
    }
    $release = Get-Content -LiteralPath $releaseMetadataPath -Raw | ConvertFrom-Json
    $version = [string]$release.product.version
    $packageVersion = [string]$release.product.packageVersion
    if ($version -notmatch '^\d+\.\d+\.\d+$' -or
        $packageVersion -notmatch '^\d+\.\d+\.\d+\.\d+$' -or
        -not $packageVersion.StartsWith("$version.")) {
        throw "Invalid release version metadata: $version / $packageVersion"
    }

    $runnerTemp = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { [IO.Path]::GetTempPath() } else { $env:RUNNER_TEMP }
    $assets = Join-Path $runnerTemp "Eikura-v$version-store-upload"
    $appPackages = Join-Path $runnerTemp "Eikura-AppPackages-v$version-store"
    foreach ($path in @($assets, $appPackages)) {
        if (Test-Path $path) { Remove-Item -LiteralPath $path -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $path | Out-Null
    }

    Write-Host "== Validate Eikura $version Store package baseline =="
    & ./scripts/Test-ReleaseVersionContract.ps1

    if (-not (Test-Path -LiteralPath $storeIdentityPath -PathType Leaf)) {
        throw "Partner Center identity file is missing: $storeIdentityPath"
    }
    $storeIdentity = Get-Content -LiteralPath $storeIdentityPath -Raw | ConvertFrom-Json
    $expectedIdentityName = [string]$storeIdentity.packageIdentityName
    $expectedPublisher = [string]$storeIdentity.publisher
    $expectedPublisherDisplayName = [string]$storeIdentity.publisherDisplayName
    $expectedPackageFamilyName = [string]$storeIdentity.packageFamilyName

    foreach ($requiredValue in @(
        $expectedIdentityName,
        $expectedPublisher,
        $expectedPublisherDisplayName,
        $expectedPackageFamilyName)) {
        if ([string]::IsNullOrWhiteSpace($requiredValue)) {
            throw 'Partner Center identity configuration contains an empty required value.'
        }
    }

    $originalManifestText = [IO.File]::ReadAllText($manifestPath, [Text.Encoding]::UTF8)
    [xml]$sourceManifest = $originalManifestText
    $sourceIdentity = $sourceManifest.Package.Identity
    if ([string]$sourceIdentity.Version -ne $packageVersion) {
        throw "Store package version mismatch: actual='$($sourceIdentity.Version)' expected='$packageVersion'"
    }
    if ([string]$sourceIdentity.Name -cne 'Eizo' -or
        [string]$sourceIdentity.Publisher -cne 'CN=AppPublisher') {
        throw "Source manifest must retain the GitHub sideload identity. Actual Name='$($sourceIdentity.Name)' Publisher='$($sourceIdentity.Publisher)'."
    }

    # Apply the Partner Center identity only in the temporary Store build workspace.
    $sourceIdentity.SetAttribute('Name', $expectedIdentityName)
    $sourceIdentity.SetAttribute('Publisher', $expectedPublisher)
    $sourceManifest.Package.Properties.PublisherDisplayName = $expectedPublisherDisplayName

    $writerSettings = [Xml.XmlWriterSettings]::new()
    $writerSettings.Encoding = [Text.UTF8Encoding]::new($false)
    $writerSettings.Indent = $true
    $writer = [Xml.XmlWriter]::Create($manifestPath, $writerSettings)
    try { $sourceManifest.Save($writer) }
    finally { $writer.Dispose() }

    [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
    $identity = $manifest.Package.Identity
    $publisherDisplayName = [string]$manifest.Package.Properties.PublisherDisplayName
    if ([string]$identity.Name -cne $expectedIdentityName -or
        [string]$identity.Publisher -cne $expectedPublisher -or
        $publisherDisplayName -cne $expectedPublisherDisplayName) {
        throw "Store identity injection failed: Name='$($identity.Name)' Publisher='$($identity.Publisher)' PublisherDisplayName='$publisherDisplayName'"
    }

    Write-Host "Partner Center identity injected: Name='$expectedIdentityName' Publisher='$expectedPublisher' PublisherDisplayName='$expectedPublisherDisplayName' ExpectedPFN='$expectedPackageFamilyName'"

    Write-Host '== Restore pinned dependencies =='
    & ./scripts/Restore-EizoPlayback.ps1
    if ($LASTEXITCODE -ne 0) { throw "Playback restore failed ($LASTEXITCODE)" }
    & ./scripts/Restore-EizoMetadata.ps1
    if ($LASTEXITCODE -ne 0) { throw "Metadata restore failed ($LASTEXITCODE)" }

    Write-Host '== Build unsigned StoreUpload package =='
    $msbuildArgs = @(
        'src\Eizo.App\Eizo.App.csproj',
        '/restore',
        '/m',
        '/p:Configuration=Release',
        '/p:Platform=x64',
        '/p:GenerateAppxPackageOnBuild=true',
        '/p:AppxPackageSigningEnabled=false',
        '/p:AppxBundle=Always',
        '/p:AppxBundlePlatforms=x64',
        '/p:UapAppxPackageBuildMode=StoreUpload',
        "/p:AppxPackageDir=$appPackages\"
    )
    & msbuild @msbuildArgs
    Assert-LastExitCode 'StoreUpload build failed'

    $upload = @(Get-ChildItem $appPackages -Recurse -Filter '*.msixupload' -File | Sort-Object Length -Descending) | Select-Object -First 1
    if (-not $upload) {
        $upload = @(Get-ChildItem $appPackages -Recurse -Filter '*.appxupload' -File | Sort-Object Length -Descending) | Select-Object -First 1
    }
    if (-not $upload) {
        throw 'Store upload package (.msixupload/.appxupload) was not produced.'
    }

    $extension = $upload.Extension.ToLowerInvariant()
    $storeUpload = Join-Path $assets "Eikura_${packageVersion}_x64$extension"
    Copy-Item -LiteralPath $upload.FullName -Destination $storeUpload -Force

    Write-Host '== Deep-verify Partner Center identity in StoreUpload payload =='
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $verifyRoot = Join-Path $runnerTemp "Eikura-v$version-store-identity-verify"
    $uploadExtract = Join-Path $verifyRoot 'upload'
    $bundleExtract = Join-Path $verifyRoot 'bundle'
    if (Test-Path $verifyRoot) { Remove-Item -LiteralPath $verifyRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $uploadExtract, $bundleExtract | Out-Null

    [System.IO.Compression.ZipFile]::ExtractToDirectory($storeUpload, $uploadExtract)
    $uploadBundle = @(Get-ChildItem $uploadExtract -Recurse -Filter '*.msixbundle' -File | Sort-Object Length -Descending) | Select-Object -First 1
    if (-not $uploadBundle) {
        throw 'StoreUpload payload contains no MSIXBundle.'
    }
    [System.IO.Compression.ZipFile]::ExtractToDirectory($uploadBundle.FullName, $bundleExtract)

    $innerPackages = @(Get-ChildItem $bundleExtract -File | Where-Object Extension -in @('.msix', '.appx') | Sort-Object Name)
    if ($innerPackages.Count -lt 1) {
        throw 'Store bundle contains no inner packages.'
    }

    $publisherResults = foreach ($package in $innerPackages) {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
        try {
            $manifestEntry = $archive.GetEntry('AppxManifest.xml')
            if (-not $manifestEntry) { throw "Inner package is missing AppxManifest.xml: $($package.Name)" }
            $stream = $manifestEntry.Open()
            try {
                $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true)
                try { [xml]$innerManifest = $reader.ReadToEnd() }
                finally { $reader.Dispose() }
            }
            finally { $stream.Dispose() }

            $innerIdentityName = $innerManifest.Package.Identity.GetAttribute('Name')
            $innerPublisher = $innerManifest.Package.Identity.GetAttribute('Publisher')
            $innerVersion = $innerManifest.Package.Identity.GetAttribute('Version')
            $innerPublisherDisplayName = [string]$innerManifest.Package.Properties.PublisherDisplayName
            if ($innerIdentityName -cne $expectedIdentityName) {
                throw "Identity Name mismatch in $($package.Name): actual='$innerIdentityName' expected='$expectedIdentityName'"
            }
            if ($innerPublisher -cne $expectedPublisher) {
                throw "Publisher mismatch in $($package.Name): actual='$innerPublisher' expected='$expectedPublisher'"
            }
            if ($innerPublisherDisplayName -cne $expectedPublisherDisplayName) {
                throw "PublisherDisplayName mismatch in $($package.Name): actual='$innerPublisherDisplayName' expected='$expectedPublisherDisplayName'"
            }
            if ($innerVersion -and $innerVersion -cne $packageVersion) {
                throw "Package version mismatch in $($package.Name): actual='$innerVersion' expected='$packageVersion'"
            }

            [pscustomobject]@{
                Package = $package.Name
                IdentityName = $innerIdentityName
                Publisher = $innerPublisher
                PublisherDisplayName = $innerPublisherDisplayName
                Version = $innerVersion
                ResourceId = $innerManifest.Package.Identity.GetAttribute('ResourceId')
            }
        }
        finally { $archive.Dispose() }
    }

    $publisherResults | Format-Table -AutoSize | Out-String | Write-Host
    Write-Host "Partner Center identity deep verification PASS: $($publisherResults.Count) inner package manifests."

    $bundle = @(Get-ChildItem $appPackages -Recurse -Filter '*.msixbundle' -File | Sort-Object Length -Descending) | Select-Object -First 1
    if ($bundle) {
        Copy-Item -LiteralPath $bundle.FullName -Destination (Join-Path $assets "Eikura_${packageVersion}_x64_store-inner.msixbundle") -Force
    }

    @(
        'Eikura Microsoft StoreUpload'
        "Version: $packageVersion"
        'Architecture: x64'
        "Identity Name: $($identity.Name)"
        "Publisher: $($identity.Publisher)"
        "Publisher display name: $publisherDisplayName"
        "Expected package family name: $expectedPackageFamilyName"
        "Verified inner package manifests: $($publisherResults.Count)"
        'Signing: unsigned (Microsoft Store signs accepted packages)'
        'Identity status: Partner Center identity applied'
        "Upload: $(Split-Path -Leaf $storeUpload)"
    ) | Set-Content -LiteralPath (Join-Path $assets 'STORE-IDENTITY.txt') -Encoding utf8

    $sumLines = Get-ChildItem $assets -File | Where-Object Extension -in @('.msixupload','.appxupload','.msixbundle') | ForEach-Object {
        "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
    }
    Set-Content -LiteralPath (Join-Path $assets 'SHA256SUMS.txt') -Value $sumLines -Encoding ascii
    Get-Content -LiteralPath (Join-Path $assets 'STORE-IDENTITY.txt')
    Get-Content -LiteralPath (Join-Path $assets 'SHA256SUMS.txt')
    Write-Host "Eikura $version StoreUpload PASS. Assets=$assets"
}
finally {
    if ($null -ne $originalManifestText) {
        [IO.File]::WriteAllText($manifestPath, $originalManifestText, [Text.UTF8Encoding]::new($false))
    }
    Pop-Location
}
