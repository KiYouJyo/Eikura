[CmdletBinding()]
param([switch]$RemoveCertificate)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'InstallerMetadata.ps1')

$metadata = Get-InstallerMetadata $PSScriptRoot
if ($metadata.packageIdentityName -cne 'Eizo' -or
    $metadata.publisher -cne 'CN=AppPublisher') {
    throw 'Installer metadata does not describe the Eikura GitHub package identity.'
}

$certificatePath =
    Get-SafePayloadFilePath $PSScriptRoot $metadata.certificateFileName
if (-not (Test-Path -LiteralPath $certificatePath -PathType Leaf)) {
    throw 'CER is missing; cannot verify the exact certificate thumbprint.'
}

$certificate =
    [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
if ($certificate.HasPrivateKey -or $certificate.Subject -cne $metadata.publisher) {
    throw 'CER does not match the Eikura GitHub publisher or contains a private key.'
}
$thumbprint = $certificate.Thumbprint.ToUpperInvariant()

$package =
    Get-AppxPackage -Name $metadata.packageIdentityName -ErrorAction SilentlyContinue |
    Where-Object Publisher -ceq $metadata.publisher |
    Sort-Object Version -Descending |
    Select-Object -First 1

if ($package) {
    Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop

    $remaining = @(
        Get-AppxPackage -Name $metadata.packageIdentityName -ErrorAction SilentlyContinue |
        Where-Object Publisher -ceq $metadata.publisher
    )

    if ($remaining.Count -ne 0) {
        throw "Eikura is still installed: $($remaining[0].PackageFullName)"
    }

    Write-Output "Removed $($package.PackageFullName)."
}
else {
    Write-Output 'Eikura is not installed.'
}

if ($RemoveCertificate) {
    $certificateStorePath = "Cert:\LocalMachine\TrustedPeople\$thumbprint"
    if (Test-Path -LiteralPath $certificateStorePath) {
        Remove-Item -LiteralPath $certificateStorePath -ErrorAction Stop
        Write-Output "Removed certificate $thumbprint."
    }
    else {
        Write-Output "Certificate $thumbprint was not present in LocalMachine TrustedPeople."
    }
}
