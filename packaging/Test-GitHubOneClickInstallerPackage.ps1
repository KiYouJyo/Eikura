[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleaseDirectory,
    [string]$ZipPath
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$payload = Join-Path $root 'payload'

$rootCmdFiles = @(Get-ChildItem -LiteralPath $root -File -Filter '*.cmd' | Sort-Object Name)
if ($rootCmdFiles.Count -ne 2) {
    throw 'One-click package must contain exactly two root CMD entry files.'
}

$rootTextFiles = @(Get-ChildItem -LiteralPath $root -File -Filter '*.txt')
if ($rootTextFiles.Count -ne 1) {
    throw 'One-click package must contain exactly one root readme.'
}

if (-not (Test-Path -LiteralPath $payload -PathType Container)) {
    throw 'One-click payload directory is missing.'
}

$required = @(
    'InstallerMetadata.json',
    'Install.ps1',
    'Uninstall.ps1',
    'InstallLauncher.ps1',
    'UninstallLauncher.ps1',
    'InstallerMetadata.ps1',
    'ChecksumResolver.ps1',
    'ReleaseDownloadResolver.ps1',
    'SHA256SUMS.txt'
)

foreach ($file in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $file) -PathType Leaf)) {
        throw "Missing one-click payload file: $file"
    }
}

$embeddedPackages = @(
    Get-ChildItem -LiteralPath $root -Recurse -File |
    Where-Object {
        $_.Extension.ToLowerInvariant() -in @(
            '.msix','.msixbundle','.appinstaller','.pfx','.p12')
    }
)
if ($embeddedPackages.Count -gt 0) {
    throw "One-click bootstrap must not embed application packages or private keys: $($embeddedPackages.Name -join ', ')"
}

$metadata =
    Get-Content -Raw -LiteralPath (Join-Path $payload 'InstallerMetadata.json') -Encoding UTF8 |
    ConvertFrom-Json

if ([int]$metadata.schemaVersion -ne 3 -or
    $metadata.packageIdentityName -cne 'Eizo' -or
    $metadata.publisher -cne 'CN=AppPublisher' -or
    $metadata.architecture -cne 'x64' -or
    $metadata.releaseApiUri -cne "https://api.github.com/repos/KiYouJyo/Eikura/releases/tags/v$($metadata.displayVersion)" -or
    $metadata.remoteBundleFileName -cne "Eikura_$($metadata.packageVersion)_x64.msixbundle") {
    throw 'One-click installer metadata does not match the Eikura release contract.'
}

$certPath = Join-Path $payload $metadata.certificateFileName
if (-not (Test-Path -LiteralPath $certPath -PathType Leaf)) {
    throw 'One-click public certificate is missing.'
}
$certificate =
    [Security.Cryptography.X509Certificates.X509Certificate2]::new($certPath)
if ($certificate.HasPrivateKey -or $certificate.Subject -cne 'CN=AppPublisher') {
    throw 'One-click certificate is invalid.'
}

$install = Get-Content -Raw -LiteralPath (Join-Path $payload 'Install.ps1')
$downloadResolver =
    Get-Content -Raw -LiteralPath (Join-Path $payload 'ReleaseDownloadResolver.ps1')

foreach ($requiredText in @(
    'Invoke-ReleaseMetadataWithRetry',
    'Download-ReleaseAssetRobust',
    'Download-SmallReleaseAssetWithRetry',
    'Get-FileHash',
    'Get-AuthenticodeSignature',
    'Add-AppxPackage',
    'Get-AppxPackage')) {
    if ($install -notmatch [regex]::Escape($requiredText)) {
        throw "One-click installer is missing required behavior: $requiredText"
    }
}

foreach ($requiredText in @(
    'Start-BitsTransfer',
    'ExpectedBytes',
    'InvokeWebRequest',
    'RetryInterval',
    'RetryTimeout')) {
    if ($downloadResolver -notmatch [regex]::Escape($requiredText)) {
        throw "One-click downloader is missing required behavior: $requiredText"
    }
}

$hashes = @{}
Get-Content -LiteralPath (Join-Path $payload 'SHA256SUMS.txt') | ForEach-Object {
    if ($_ -match '^(?<hash>[A-Fa-f0-9]{64}) \*(?<name>.+)$') {
        $hashes[$matches.name.Replace('\','/')] = $matches.hash.ToUpperInvariant()
    }
}

Get-ChildItem -LiteralPath $payload -Recurse -File |
    Where-Object Name -ne 'SHA256SUMS.txt' |
    ForEach-Object {
        $relative = $_.FullName.Substring($payload.Length).TrimStart('\').Replace('\','/')
        if (-not $hashes.ContainsKey($relative)) {
            throw "Payload SHA256SUMS.txt is missing $relative"
        }
        $actual =
            (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($actual -ne $hashes[$relative]) {
            throw "Payload SHA-256 mismatch: $relative"
        }
    }

if ($ZipPath) {
    $zip = Get-Item -LiteralPath $ZipPath -ErrorAction Stop
    if ($zip.Length -gt 5MB) {
        throw 'One-click bootstrap is unexpectedly large; an application payload may have been embedded.'
    }
}

Write-Output 'Eikura GitHub one-click installer package validation passed.'
