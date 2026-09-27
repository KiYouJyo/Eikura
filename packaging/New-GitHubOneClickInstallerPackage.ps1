[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SignedBundlePath,
    [Parameter(Mandatory)][string]$PublicCertificatePath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$DisplayVersion,
    [Parameter(Mandatory)][string]$PackageVersion
)

$ErrorActionPreference = 'Stop'
$out = [IO.Path]::GetFullPath($OutputDirectory)

if ($DisplayVersion -notmatch '^\d+\.\d+\.\d+$' -or
    $PackageVersion -notmatch '^\d+\.\d+\.\d+\.\d+$' -or
    -not $PackageVersion.StartsWith("$DisplayVersion.")) {
    throw 'Invalid version input.'
}

foreach ($path in @($SignedBundlePath, $PublicCertificatePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing input: $path"
    }
}

$bundle = Get-Item -LiteralPath $SignedBundlePath
$certificate =
    [Security.Cryptography.X509Certificates.X509Certificate2]::new(
        (Resolve-Path $PublicCertificatePath))

if ($certificate.HasPrivateKey -or $certificate.Subject -cne 'CN=AppPublisher') {
    throw 'Invalid public certificate.'
}

$root = Join-Path $out "Eikura-v$DisplayVersion-x64-one-click"
if (Test-Path -LiteralPath $root) {
    Remove-Item -LiteralPath $root -Recurse -Force
}
$payload = Join-Path $root 'payload'
New-Item -ItemType Directory -Path $payload -Force | Out-Null

$certificateFileName = "Eikura-v$DisplayVersion-Framework-Dependent.cer"
$metadata = [ordered]@{
    schemaVersion = 3
    displayVersion = $DisplayVersion
    packageVersion = $PackageVersion
    releaseTag = "v$DisplayVersion"
    packageIdentityName = 'Eizo'
    publisher = 'CN=AppPublisher'
    architecture = 'x64'
    remoteBundleFileName = $bundle.Name
    certificateFileName = $certificateFileName
    releaseApiUri = "https://api.github.com/repos/KiYouJyo/Eikura/releases/tags/v$DisplayVersion"
    checksumFileName = 'SHA256SUMS.txt'
}
$metadata | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $payload 'InstallerMetadata.json') -Encoding UTF8

$entryScripts = @(Get-ChildItem -LiteralPath $PSScriptRoot -File -Filter '*.cmd' | Sort-Object Name)
if ($entryScripts.Count -ne 2) {
    throw "Expected exactly two installer CMD entry files, found $($entryScripts.Count)."
}
foreach ($entry in $entryScripts) {
    Copy-Item -LiteralPath $entry.FullName -Destination $root
}

$readme = Get-ChildItem -LiteralPath $PSScriptRoot -File -Filter '*.txt' | Select-Object -First 1
if (-not $readme) { throw 'Installer readme is missing.' }

(Get-Content -Raw -LiteralPath $readme.FullName -Encoding UTF8).
    Replace('{{DISPLAY_VERSION}}', $DisplayVersion).
    Replace('{{PACKAGE_VERSION}}', $PackageVersion) |
    Set-Content -LiteralPath (Join-Path $root $readme.Name) -Encoding UTF8

foreach ($name in @(
    'Install.ps1',
    'Uninstall.ps1',
    'InstallLauncher.ps1',
    'UninstallLauncher.ps1',
    'InstallerMetadata.ps1',
    'ChecksumResolver.ps1',
    'ReleaseDownloadResolver.ps1')) {

    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "payload\$name") -Destination $payload
}

Copy-Item -LiteralPath $PublicCertificatePath -Destination (Join-Path $payload $certificateFileName)

$hashLines =
    Get-ChildItem -LiteralPath $payload -Recurse -File |
    Where-Object Name -ne 'SHA256SUMS.txt' |
    Sort-Object FullName |
    ForEach-Object {
        $relative = $_.FullName.Substring($payload.Length).TrimStart('\')
        "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToUpperInvariant()) *$relative"
    }

Set-Content -LiteralPath (Join-Path $payload 'SHA256SUMS.txt') -Value $hashLines -Encoding UTF8
Write-Output $root
