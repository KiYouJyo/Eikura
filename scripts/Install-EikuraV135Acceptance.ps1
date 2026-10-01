[CmdletBinding()]
param([switch]$ImportCertificateOnly)

$ErrorActionPreference = 'Stop'
$certPath = Join-Path $PSScriptRoot 'Eikura-1.3.5-Acceptance-AppPublisher.cer'
$bundlePath = Join-Path $PSScriptRoot 'Eikura_1.3.5.9002_x64_WebDAV-Restore-Acceptance.msixbundle'
$runtimePath = Join-Path $PSScriptRoot 'WindowsAppRuntimeInstall-x64.exe'

# Verify the artifact contents before importing a certificate or installing code.
$hashes = @{}
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'SHA256SUMS.txt') | ForEach-Object {
    if ($_ -match '^(?<hash>[a-fA-F0-9]{64})  (?<name>.+)$') {
        $hashes[$matches.name] = $matches.hash
    }
}
foreach ($path in @($certPath, $bundlePath, $runtimePath)) {
    $name = Split-Path -Leaf $path
    if (-not $hashes.ContainsKey($name) -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $hashes[$name]) {
        throw "Acceptance file checksum mismatch: $name"
    }
}

$cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certPath)
if ($cert.Subject -cne 'CN=AppPublisher' -or $cert.HasPrivateKey) {
    throw 'Unexpected acceptance public certificate.'
}
$signature = Get-AuthenticodeSignature -FilePath $bundlePath
if (-not $signature.SignerCertificate -or
    $signature.SignerCertificate.Thumbprint -cne $cert.Thumbprint) {
    throw 'Acceptance bundle signer does not match the supplied certificate.'
}

$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$storePath = "Cert:\LocalMachine\TrustedPeople\$($cert.Thumbprint)"
if ($ImportCertificateOnly) {
    if (-not $isAdmin) { throw 'Certificate trust requires elevation.' }
    Import-Certificate -FilePath $certPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    exit 0
}
if (-not (Test-Path -LiteralPath $storePath)) {
    if ($isAdmin) {
        Import-Certificate -FilePath $certPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    }
    else {
        # Elevate only certificate setup; register the app for the calling user.
        $arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -ImportCertificateOnly' -f $PSCommandPath
        $helper = Start-Process powershell.exe -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru
        if ($helper.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $storePath)) {
            throw 'Acceptance certificate trust setup failed or was cancelled.'
        }
    }
}
$signature = Get-AuthenticodeSignature -FilePath $bundlePath
if ($signature.Status -ne 'Valid' -or -not $signature.TimeStamperCertificate) {
    throw "Acceptance signature or timestamp validation failed: $($signature.Status)"
}
$runtimeSignature = Get-AuthenticodeSignature -FilePath $runtimePath
if ($runtimeSignature.Status -ne 'Valid' -or
    -not $runtimeSignature.SignerCertificate -or
    $runtimeSignature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
    throw 'Windows App Runtime installer signature validation failed.'
}
& $runtimePath --quiet
if ($LASTEXITCODE -ne 0) { throw "Windows App Runtime installation failed: $LASTEXITCODE" }
Add-AppxPackage -Path $bundlePath -ForceApplicationShutdown -ForceUpdateFromAnyVersion
$package = Get-AppxPackage -Name Eizo
if (-not $package -or [string]$package.Version -ne '1.3.5.9002' -or
    [string]$package.Status -ne 'Ok') {
    throw 'Acceptance package registration verification failed.'
}
Write-Host 'Eikura 1.3.5 WebDAV restore acceptance build installed (1.3.5.9002).'
