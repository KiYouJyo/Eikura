[CmdletBinding()]
param(
    [long]$SourceRunId = 36652633368
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($env:GITHUB_REPOSITORY) -or
    [string]::IsNullOrWhiteSpace($env:GITHUB_SHA)) {
    throw 'This reconciliation must run from GitHub Actions.'
}

$repo = $env:GITHUB_REPOSITORY
$tag = 'v1.3.2'
$displayVersion = '1.3.2'
$packageVersion = '1.3.2.0'
$currentBundleName = 'Eikura_1.3.2.0_x64.msixbundle'
$bridgeBundleName = 'Eizo_1.3.2.0_x64.msixbundle'
$oneClickName = 'Eikura-v1.3.2-x64-one-click.zip'
$checksumName = 'SHA256SUMS.txt'
$artifactName = 'Eikura-1.3.2-github-release-assets'
$tempRoot = Join-Path $env:RUNNER_TEMP ('Eikura-v132-bridge-' + [Guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $tempRoot 'source'
$assetsRoot = Join-Path $tempRoot 'assets'

function Invoke-GhJson([string[]]$Arguments) {
    $json = & gh @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "gh failed: gh $($Arguments -join ' ')"
    }
    return ($json | ConvertFrom-Json)
}

function Assert-NeverDownloadedRelease([object[]]$Assets) {
    $downloaded = @($Assets | Where-Object { [int]$_.download_count -gt 0 })
    if ($downloaded.Count -gt 0) {
        $names = ($downloaded | ForEach-Object { "$($_.name)=$($_.download_count)" }) -join ', '
        throw "v1.3.2 already has downloaded assets ($names). Do not reconcile the published release in place."
    }
}

New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null
New-Item -ItemType Directory -Path $assetsRoot -Force | Out-Null

try {
    $release = Invoke-GhJson @('api', "repos/$repo/releases/tags/$tag")
    if ($release.draft -or $release.prerelease -or $release.tag_name -cne $tag) {
        throw 'v1.3.2 is not a stable published release.'
    }

    $remote = @(Invoke-GhJson @('api', "repos/$repo/releases/$($release.id)/assets"))
    if ($remote.Count -ne 3) {
        throw "Expected the original three-asset v1.3.2 release; found $($remote.Count)."
    }
    Assert-NeverDownloadedRelease $remote

    foreach ($required in @($currentBundleName, $oneClickName, $checksumName)) {
        if (@($remote | Where-Object name -eq $required).Count -ne 1) {
            throw "Original v1.3.2 asset contract is missing: $required"
        }
    }

    # Use the immutable artifact produced by the successful publication run so
    # reconciliation itself does not increment GitHub Release download counts.
    & gh run download $SourceRunId --repo $repo --name $artifactName --dir $sourceRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to download source release artifact from run $SourceRunId."
    }

    $sourceBundle = Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter $currentBundleName | Select-Object -First 1
    $sourceOneClick = Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter $oneClickName | Select-Object -First 1
    if (-not $sourceBundle -or -not $sourceOneClick) {
        throw 'Publication artifact does not contain the expected v1.3.2 bundle and one-click package.'
    }

    $bridgeBundle = Join-Path $assetsRoot $bridgeBundleName
    Copy-Item -LiteralPath $sourceBundle.FullName -Destination $bridgeBundle -Force
    $sourceHash = (Get-FileHash -LiteralPath $sourceBundle.FullName -Algorithm SHA256).Hash
    $bridgeHash = (Get-FileHash -LiteralPath $bridgeBundle -Algorithm SHA256).Hash
    if ($sourceHash -cne $bridgeHash) {
        throw 'Bridge bundle bytes differ from the already-validated Eikura bundle.'
    }

    $extractRoot = Join-Path $tempRoot 'original-one-click'
    Expand-Archive -LiteralPath $sourceOneClick.FullName -DestinationPath $extractRoot -Force
    $certificate = Get-ChildItem -LiteralPath $extractRoot -Recurse -File -Filter '*.cer' | Select-Object -First 1
    if (-not $certificate) { throw 'Published one-click package does not contain its public certificate.' }

    $cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificate.FullName)
    if ($cert.HasPrivateKey -or $cert.Subject -cne 'CN=AppPublisher' -or
        $cert.Thumbprint -cne 'BD85AD77A651C86CA01A480C8E9BC64952993F98') {
        throw "Unexpected release certificate: $($cert.Subject) / $($cert.Thumbprint)"
    }

    $staging = Join-Path $tempRoot 'one-click-staging'
    $packageRoot = & ./packaging/New-GitHubOneClickInstallerPackage.ps1 `
        -SignedBundlePath $bridgeBundle `
        -PublicCertificatePath $certificate.FullName `
        -OutputDirectory $staging `
        -DisplayVersion $displayVersion `
        -PackageVersion $packageVersion
    if (-not $packageRoot) { throw 'Failed to build compatibility one-click package.' }

    $metadataPath = Join-Path $packageRoot 'payload\InstallerMetadata.json'
    $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    if ([int]$metadata.schemaVersion -ne 3 -or
        $metadata.displayVersion -cne $displayVersion -or
        $metadata.packageVersion -cne $packageVersion -or
        $metadata.packageIdentityName -cne 'Eizo' -or
        $metadata.publisher -cne 'CN=AppPublisher' -or
        $metadata.remoteBundleFileName -cne $bridgeBundleName -or
        $metadata.releaseApiUri -cne "https://api.github.com/repos/$repo/releases/tags/$tag") {
        throw 'Bridge one-click metadata does not satisfy the Eizo 1.3.1 updater compatibility contract.'
    }

    $newOneClick = Join-Path $assetsRoot $oneClickName
    Compress-Archive -LiteralPath $packageRoot -DestinationPath $newOneClick -CompressionLevel Optimal

    $manifestLines = @($bridgeBundle, $newOneClick) | ForEach-Object {
        $item = Get-Item -LiteralPath $_
        "{0}  {1}" -f (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $item.Name
    }
    $newChecksum = Join-Path $assetsRoot $checksumName
    Set-Content -LiteralPath $newChecksum -Value $manifestLines -Encoding ascii

    $notesPath = Join-Path $tempRoot 'release-notes.bridge.md'
    & ./packaging/New-GitHubReleaseBody.ps1 -Version $displayVersion -OutputPath $notesPath
    if (-not (Test-Path -LiteralPath $notesPath -PathType Leaf)) {
        throw 'Failed to generate bridge release notes.'
    }

    # Re-check immediately before mutation. No Release asset was downloaded by
    # this job: source bytes came from the Actions artifact above.
    $remote = @(Invoke-GhJson @('api', "repos/$repo/releases/$($release.id)/assets"))
    Assert-NeverDownloadedRelease $remote

    git fetch --tags --force
    git config user.name 'github-actions[bot]'
    git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
    git tag -fa $tag $env:GITHUB_SHA -m "Eikura $tag legacy updater bridge"
    git push origin "+refs/tags/$tag"
    if ($LASTEXITCODE -ne 0) { throw "Failed to reconcile tag $tag." }

    foreach ($asset in $remote) {
        & gh release delete-asset $tag $asset.name --repo $repo --yes
        if ($LASTEXITCODE -ne 0) { throw "Failed to remove original release asset: $($asset.name)" }
    }

    & gh release upload $tag $bridgeBundle $newOneClick $newChecksum --repo $repo
    if ($LASTEXITCODE -ne 0) { throw 'Failed to upload bridged v1.3.2 assets.' }

    & gh release edit $tag --repo $repo --title 'Eikura v1.3.2' --notes-file $notesPath
    if ($LASTEXITCODE -ne 0) { throw 'Failed to update v1.3.2 release notes.' }

    $verifiedRelease = Invoke-GhJson @('api', "repos/$repo/releases/tags/$tag")
    $verifiedAssets = @(Invoke-GhJson @('api', "repos/$repo/releases/$($verifiedRelease.id)/assets"))
    if ($verifiedRelease.draft -or $verifiedRelease.prerelease -or $verifiedAssets.Count -ne 3) {
        throw 'Bridged release metadata is invalid.'
    }

    $bundles = @($verifiedAssets | Where-Object { $_.name -like '*.msixbundle' })
    if ($bundles.Count -ne 1 -or $bundles[0].name -cne $bridgeBundleName) {
        throw 'v1.3.2 must expose exactly one MSIXBundle with the legacy updater bridge name.'
    }
    foreach ($required in @($bridgeBundleName, $oneClickName, $checksumName)) {
        if (@($verifiedAssets | Where-Object name -eq $required).Count -ne 1) {
            throw "Bridged v1.3.2 release is missing: $required"
        }
    }

    Write-Host "Eikura v1.3.2 legacy updater bridge reconciliation PASS. Bundle SHA256=$($bridgeHash.ToLowerInvariant())"
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
