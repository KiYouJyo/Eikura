param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-LastExitCode([string]$message) {
    if ($LASTEXITCODE -ne 0) { throw "$message ($LASTEXITCODE)" }
}

function Invoke-WebRequestWithRetry(
    [string]$uri,
    [string]$outFile,
    [int]$attempts = 4) {
    $lastError = $null
    for ($attempt = 1; $attempt -le $attempts; $attempt++) {
        try {
            if (Test-Path -LiteralPath $outFile) {
                Remove-Item -LiteralPath $outFile -Force
            }
            Invoke-WebRequest -Uri $uri -OutFile $outFile -UseBasicParsing
            if ((Test-Path -LiteralPath $outFile -PathType Leaf) -and
                (Get-Item -LiteralPath $outFile).Length -gt 0) {
                return
            }
            throw 'Downloaded file is empty.'
        }
        catch {
            $lastError = $_
            if ($attempt -lt $attempts) {
                Start-Sleep -Seconds ([Math]::Min(5 * $attempt, 15))
            }
        }
    }
    throw ("Download failed after {0} attempts: {1}. Last error: {2}" -f $attempts, $uri, $lastError)
}

function Get-EizoProcesses([string]$installRoot) {
    $root = [IO.Path]::GetFullPath($installRoot)
    return @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try {
            $_.Path -and ([IO.Path]::GetFullPath($_.Path)).StartsWith($root, [StringComparison]::OrdinalIgnoreCase)
        }
        catch { $false }
    })
}

function Start-EizoAndAssertAlive($package, [int]$waitSeconds = 8) {
    $manifest = Get-AppxPackageManifest -Package $package
    $appId = [string]$manifest.Package.Applications.Application.Id
    $activation = "shell:AppsFolder\$($package.PackageFamilyName)!$appId"
    Start-Process explorer.exe -ArgumentList $activation
    Start-Sleep -Seconds $waitSeconds
    $running = @(Get-EizoProcesses ([string]$package.InstallLocation))
    if ($running.Count -eq 0) { throw 'Eizo did not remain running after activation.' }
    return $running
}

if ([string]::IsNullOrWhiteSpace($env:RELEASE_CERTIFICATE_BASE64) -or
    [string]::IsNullOrWhiteSpace($env:RELEASE_CERTIFICATE_PASSWORD)) {
    throw 'GitHub release signing secrets are not configured.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$metadataPin = Get-Content -LiteralPath (Join-Path $repoRoot 'eng/Eizo.Metadata.json') -Raw | ConvertFrom-Json
$expectedMetadataRuntimeVersion = [string]$metadataPin.version
if ([string]::IsNullOrWhiteSpace($expectedMetadataRuntimeVersion)) {
    throw 'Pinned Eizo.Metadata version is missing.'
}
$expectedMetadataRuntimeRegex = [regex]::Escape($expectedMetadataRuntimeVersion)

Push-Location $repoRoot
try {
    $runnerTemp = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { [IO.Path]::GetTempPath() } else { $env:RUNNER_TEMP }
    $assets = Join-Path $runnerTemp 'Eizo-v1.3.1-acceptance'
    $appPackages = Join-Path $runnerTemp 'Eizo-AppPackages-v131'
    $bundleExtract = Join-Path $runnerTemp 'Eizo-bundle-extract-v131'
    $oneClickStaging = Join-Path $runnerTemp 'Eizo-one-click-v131'
    foreach ($path in @($assets, $appPackages, $bundleExtract, $oneClickStaging)) {
        if (Test-Path $path) { Remove-Item -LiteralPath $path -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $path | Out-Null
    }

    Write-Host '== Restore pinned dependencies =='
    & ./scripts/Restore-EizoPlayback.ps1
    & ./scripts/Restore-EizoMetadata.ps1

    Write-Host '== Validate v1.3.1 native Bangumi anime search/index and prior release regressions =='
    & ./scripts/Test-MediaModelStage1Contract.ps1
    & ./scripts/Test-TmdbCoreStage2Contract.ps1
    & ./scripts/Test-TmdbSeasonStage2Contract.ps1
    & ./scripts/Test-TmdbDetailsStage2Contract.ps1
    & ./scripts/Test-ProviderRouterStage3Contract.ps1
    & ./scripts/Test-FieldMergeStage3Contract.ps1
    & ./scripts/Test-MergeProfilesStage3Contract.ps1
    & ./scripts/Test-IdentityBindingStage4Contract.ps1
    & ./scripts/Test-ScanPipelineStage5Contract.ps1
    & ./scripts/Test-RealContentUiV059Contract.ps1
    & ./scripts/Test-RealContentPresentationV0510Contract.ps1
    & ./scripts/Test-PerTitleMetadataV0511Contract.ps1
    & ./scripts/Test-RealContentUiCloseoutV0512Contract.ps1
    & ./scripts/Test-TmdbProductionIntegrationV0513Contract.ps1
    & ./scripts/Test-ScrapingArchitectureV0514Contract.ps1
    & ./scripts/Test-UpdateManagementV060Contract.ps1
    & ./scripts/Test-BangumiPublicIntegration.ps1
    & ./scripts/Test-BangumiAccountIntegration.ps1
    & ./scripts/Test-BangumiOAuthIntegration.ps1
    & ./scripts/Test-BangumiCommunityIntegration.ps1
    & ./scripts/Test-CacheSystemContract.ps1
    & ./scripts/Test-SettingsContentIntegrationContract.ps1
    & ./scripts/Test-FirstRunGuideV100Contract.ps1
    & ./scripts/Test-PictureInPictureV110Contract.ps1
    & ./scripts/Test-CardUiV120Contract.ps1
    & ./scripts/Test-ManualVideoSingleFileV121Contract.ps1
    & ./scripts/Test-AnimeIndexV130Contract.ps1
    & ./scripts/Test-StartupProgressBarV131Contract.ps1
    & ./scripts/Test-LibraryAggregationUiContract.ps1
    dotnet run --project tools/Eizo.WebDavV030Probe/Eizo.WebDavV030Probe.csproj --configuration Release
    Assert-LastExitCode 'WebDAV range runtime probe failed'
    & ./scripts/Test-PlayerContentV040Contract.ps1
    & ./scripts/Test-PlayerInteractionV130Contract.ps1
    & ./scripts/Test-PlaybackQueueUxV130Contract.ps1
    dotnet test tests/Eizo.Media.Tests/Eizo.Media.Tests.csproj --configuration Release
    Assert-LastExitCode 'Media model tests failed'
    dotnet test tests/Eizo.MetadataIntegration.Tests/Eizo.MetadataIntegration.Tests.csproj --configuration Release
    Assert-LastExitCode 'Metadata integration tests failed'
    dotnet test tests/Eizo.Cache.Tests/Eizo.Cache.Tests.csproj --configuration Release
    Assert-LastExitCode 'Cache integration tests failed'
    dotnet test tests/Eizo.Bangumi.Tests/Eizo.Bangumi.Tests.csproj --configuration Release
    Assert-LastExitCode 'Bangumi integration tests failed'
    dotnet test tests/Eizo.Subtitle.Tests/Eizo.Subtitle.Tests.csproj --configuration Release
    Assert-LastExitCode 'Subtitle parser tests failed'

    Write-Host '== Compile Release =='
    msbuild src\Eizo.App\Eizo.App.csproj /restore /m /p:Configuration=Release /p:Platform=x64 /p:AppxPackageSigningEnabled=false /p:GenerateAppxPackageOnBuild=false
    Assert-LastExitCode 'Release build failed'

    $releaseOut = (Resolve-Path 'src\Eizo.App\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64').Path
    $playbackAssemblies = @(
        'Eizo.Playback.Abstractions.dll',
        'Eizo.Playback.Core.dll',
        'Eizo.Playback.LibVLC.dll',
        'Eizo.Playback.LibVLC.WinUI.dll'
    )
    $metadataAssemblies = @(
        'Eizo.Metadata.Recognition.dll',
        'Eizo.Metadata.Core.dll',
        'Eizo.Metadata.Providers.dll'
    )
    $updateableAssemblies = @($playbackAssemblies + $metadataAssemblies)
    foreach ($name in $updateableAssemblies) {
        if (Test-Path (Join-Path $releaseOut $name)) {
            throw "Updateable implementation remains in the default probing root: $name"
        }
    }
    foreach ($name in $playbackAssemblies) {
        if (-not (Test-Path (Join-Path $releaseOut "Components\Bundled\Playback\$name"))) {
            throw "Bundled Playback fallback is missing: $name"
        }
    }
    foreach ($name in $metadataAssemblies) {
        if (-not (Test-Path (Join-Path $releaseOut "Components\Bundled\Recognition\$name"))) {
            throw "Bundled Metadata fallback is missing: $name"
        }
    }
    $playbackPin = Get-Content -LiteralPath 'eng/Eizo.Playback.json' -Raw | ConvertFrom-Json
    if ([string]$playbackPin.version -ne '0.2.3' -or [string]$playbackPin.commit -ne 'caa985c78e8c2c74523d1cf76ea511aff9872cbb') {
        throw "Playback pin mismatch: version=$($playbackPin.version) commit=$($playbackPin.commit)"
    }
    Write-Host 'Relocatable component output contract PASS. Playback=0.2.3 official merge commit.'

    Write-Host '== Build MSIX bundle =='
    msbuild src\Eizo.App\Eizo.App.csproj /restore /m /p:Configuration=Release /p:Platform=x64 /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=false /p:AppxBundle=Always /p:AppxBundlePlatforms=x64 /p:UapAppxPackageBuildMode=SideloadOnly "/p:AppxPackageDir=$appPackages\"
    Assert-LastExitCode 'MSIX build failed'
    $producedBundle = @(Get-ChildItem $appPackages -Recurse -Filter '*.msixbundle' -File) | Select-Object -First 1
    if (-not $producedBundle) { throw 'MSIX bundle was not produced.' }
    $bundlePath = Join-Path $assets 'Eizo_1.3.1.0_x64.msixbundle'
    Copy-Item -LiteralPath $producedBundle.FullName -Destination $bundlePath -Force

    Write-Host '== Verify packaged component probing contract =='
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($bundlePath, $bundleExtract)
    $innerCandidates = @(Get-ChildItem $bundleExtract -Recurse -Filter '*.msix' -File)
    $inner = @($innerCandidates |
        Where-Object { $_.Name.EndsWith('_x64.msix', [StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object Length -Descending) | Select-Object -First 1
    if (-not $inner) {
        $inner = @($innerCandidates | Sort-Object Length -Descending) | Select-Object -First 1
    }
    if (-not $inner) { throw 'Bundle contains no inner MSIX.' }
    Write-Host "Inspecting architecture package: $($inner.Name)"
    $archive = [System.IO.Compression.ZipFile]::OpenRead($inner.FullName)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\','/') })
        foreach ($name in @('libvlc.dll', 'libvlccore.dll')) {
            if (-not ($entries | Where-Object { $_ -match "(^|/)$([regex]::Escape($name))$" })) {
                throw "MSIX does not contain $name"
            }
        }
        $plugins = @($entries | Where-Object { $_ -match '(^|/)plugins/.+\.dll$' })
        if ($plugins.Count -eq 0) { throw 'MSIX contains no LibVLC plugins.' }
        foreach ($name in $updateableAssemblies) {
            if ($entries -contains $name) { throw "MSIX root still contains updateable implementation: $name" }
        }
        foreach ($name in $playbackAssemblies) {
            if ($entries -notcontains "Components/Bundled/Playback/$name") {
                throw "MSIX bundled Playback fallback is missing: $name"
            }
        }
        foreach ($name in $metadataAssemblies) {
            if ($entries -notcontains "Components/Bundled/Recognition/$name") {
                throw "MSIX bundled Metadata fallback is missing: $name"
            }
        }
        Write-Host "Component bundle contract PASS. LibVLC plugins=$($plugins.Count)"
    }
    finally { $archive.Dispose() }

    Write-Host '== Sign bundle =='
    $pfx = Join-Path $runnerTemp 'eizo-v131-release-signing.pfx'
    [IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:RELEASE_CERTIFICATE_BASE64))
    $password = ConvertTo-SecureString $env:RELEASE_CERTIFICATE_PASSWORD -AsPlainText -Force
    $certificate = Import-PfxCertificate -FilePath $pfx -CertStoreLocation Cert:\CurrentUser\My -Password $password
    if ($certificate.Subject -cne 'CN=AppPublisher' -or
        $certificate.Thumbprint -cne 'BD85AD77A651C86CA01A480C8E9BC64952993F98' -or
        -not $certificate.HasPrivateKey) {
        throw "Unexpected signing certificate: Subject=$($certificate.Subject); Thumbprint=$($certificate.Thumbprint)"
    }
    $cer = Join-Path $assets 'Eizo-v1.3.1-AppPublisher.cer'
    Export-Certificate -Cert $certificate -FilePath $cer | Out-Null
    $kitsRoot = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    $signtool = Get-ChildItem (Join-Path $kitsRoot 'Windows Kits\10\bin') -Recurse -Filter signtool.exe |
        Where-Object FullName -match '\\x64\\signtool.exe$' |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $signtool) { throw 'x64 signtool.exe was not found.' }
    & $signtool.FullName sign /fd SHA256 /sha1 $certificate.Thumbprint /tr http://timestamp.digicert.com /td SHA256 $bundlePath
    Assert-LastExitCode 'signtool sign failed'
    $signature = Get-AuthenticodeSignature -FilePath $bundlePath
    if (-not $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -cne $certificate.Thumbprint) {
        throw 'Signed bundle signer mismatch.'
    }
    Remove-Item -LiteralPath $pfx -Force

    Write-Host '== Install and launch bundled fallback =='
    if (-not (Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
        Where-Object Thumbprint -eq $certificate.Thumbprint | Select-Object -First 1)) {
        Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    }
    Get-AppxPackage -Name Eizo -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
    $legacyComponentsRoot = Join-Path $env:LOCALAPPDATA 'Eizo\Components'
    Remove-Item -LiteralPath $legacyComponentsRoot -Recurse -Force -ErrorAction SilentlyContinue

    $runtimeInstaller = Join-Path $runnerTemp 'WindowsAppRuntimeInstall-x64.exe'
    Invoke-WebRequestWithRetry -Uri 'https://aka.ms/windowsappsdk/1.8/1.8.260710003/windowsappruntimeinstall-x64.exe' -OutFile $runtimeInstaller
    $runtimeSignature = Get-AuthenticodeSignature -FilePath $runtimeInstaller
    if (-not $runtimeSignature.SignerCertificate -or $runtimeSignature.Status -ne 'Valid' -or
        $runtimeSignature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
        throw 'Windows App Runtime installer signature validation failed.'
    }
    & $runtimeInstaller --quiet
    Assert-LastExitCode 'Windows App Runtime installer failed'

    Add-AppxPackage -Path $bundlePath -ForceApplicationShutdown
    $pkg = Get-AppxPackage -Name Eizo
    if (-not $pkg -or [string]$pkg.Version -ne '1.3.1.0') {
        throw "Installed package version mismatch: $($pkg.Version)"
    }

    $packageLocalState = Join-Path $env:LOCALAPPDATA "Packages\$($pkg.PackageFamilyName)\LocalState"
    $componentsRoot = Join-Path $packageLocalState 'Eizo\Components'
    Remove-Item -LiteralPath $componentsRoot -Recurse -Force -ErrorAction SilentlyContinue

    $running = Start-EizoAndAssertAlive $pkg
    $installedManifest = Get-AppxPackageManifest -Package $pkg
    if (-not $installedManifest.OuterXml.Contains('Protocol Name="eizo"', [StringComparison]::Ordinal)) {
        throw 'Installed MSIX is missing the eizo protocol registration.'
    }
    Start-Process 'eizo://bangumi-auth'
    Start-Sleep -Seconds 3
    if (@(Get-EizoProcesses ([string]$pkg.InstallLocation)).Count -eq 0) {
        throw 'Warm protocol activation closed Eizo.'
    }
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Process 'eizo://bangumi-auth'
    Start-Sleep -Seconds 8
    $running = @(Get-EizoProcesses ([string]$pkg.InstallLocation))
    if ($running.Count -eq 0) { throw 'Cold protocol activation did not launch Eizo.' }
    $bundledRuntimeLog = Join-Path $componentsRoot 'recognition-runtime.log'
    if (-not (Test-Path $bundledRuntimeLog)) {
        throw "Bundled Recognition runtime probe log was not written at $bundledRuntimeLog"
    }
    $bundledRuntimeLine = @(Get-Content -LiteralPath $bundledRuntimeLog | Where-Object { $_ -match '\tversion=' }) | Select-Object -Last 1
    $bundledAssembly = Join-Path ([string]$pkg.InstallLocation) 'Components\Bundled\Recognition\Eizo.Metadata.Recognition.dll'
    if ($bundledRuntimeLine -notmatch "\tversion=$expectedMetadataRuntimeRegex\texternal=False\tprobe=" -or
        $bundledRuntimeLine -notmatch ([regex]::Escape($bundledAssembly))) {
        throw "Bundled Recognition runtime was not actually invoked from the packaged fallback. Log:`n$bundledRuntimeLine"
    }

    $bundledMetadataLog = Join-Path $componentsRoot 'metadata-runtime.log'
    if (-not (Test-Path $bundledMetadataLog)) {
        throw "Bundled Metadata runtime probe log was not written at $bundledMetadataLog"
    }
    $bundledMetadataLine = @(Get-Content -LiteralPath $bundledMetadataLog | Where-Object { $_ -match '\tversion=' }) | Select-Object -Last 1
    $bundledCore = Join-Path ([string]$pkg.InstallLocation) 'Components\Bundled\Recognition\Eizo.Metadata.Core.dll'
    $bundledProviders = Join-Path ([string]$pkg.InstallLocation) 'Components\Bundled\Recognition\Eizo.Metadata.Providers.dll'
    if ($bundledMetadataLine -notmatch "\tversion=$expectedMetadataRuntimeRegex\texternal=False\tprobe=ok\t" -or
        $bundledMetadataLine -notmatch ([regex]::Escape($bundledCore)) -or
        $bundledMetadataLine -notmatch ([regex]::Escape($bundledProviders))) {
        throw "Bundled Metadata Core/Providers were not actually invoked from the packaged fallback. Log:`n$bundledMetadataLine"
    }

    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Write-Host "Bundled Metadata $expectedMetadataRuntimeVersion fallback and real Recognition/Core/Providers calls PASS."

    Write-Host '== Build one-click acceptance assets =='
    Get-AppxPackage -Name Eizo -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $componentsRoot -Recurse -Force -ErrorAction SilentlyContinue
    & ./packaging/New-GitHubOneClickInstallerPackage.ps1 -SignedBundlePath $bundlePath -PublicCertificatePath $cer -OutputDirectory $oneClickStaging -DisplayVersion '1.3.1' -PackageVersion '1.3.1.0'
    $packageRoot = Join-Path $oneClickStaging 'Eizo-v1.3.1-x64-one-click'
    & ./packaging/Test-GitHubOneClickInstallerPackage.ps1 -ReleaseDirectory $packageRoot
    $oneClickZip = Join-Path $assets 'Eizo-v1.3.1-x64-one-click.zip'
    Compress-Archive -LiteralPath $packageRoot -DestinationPath $oneClickZip -CompressionLevel Optimal

    $sumLines = foreach ($file in @($bundlePath, $oneClickZip)) {
        $item = Get-Item -LiteralPath $file
        "{0}  {1}" -f (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant(), $item.Name
    }
    $sumPath = Join-Path $assets 'SHA256SUMS.txt'
    Set-Content -LiteralPath $sumPath -Value $sumLines -Encoding ascii
    Get-Content -LiteralPath $sumPath
    Write-Host "Eizo 1.3.1 startup loading refinement acceptance PASS. Assets=$assets"
}
finally {
    Pop-Location
}
