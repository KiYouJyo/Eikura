[CmdletBinding()]
param([switch]$ImportCertificateOnly)

$ErrorActionPreference = 'Stop'
$certPath = Join-Path $PSScriptRoot 'Eikura-1.3.5-Acceptance-AppPublisher.cer'
$bundlePath = Join-Path $PSScriptRoot 'Eikura_1.3.5.9003_x64_WebDAV-Restore-Acceptance.msixbundle'

# Verify the artifact contents before importing a certificate or installing code.
$hashes = @{}
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'SHA256SUMS.txt') | ForEach-Object {
    if ($_ -match '^(?<hash>[a-fA-F0-9]{64})  (?<name>.+)$') {
        $hashes[$matches.name] = $matches.hash
    }
}
foreach ($path in @($certPath, $bundlePath)) {
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
function Install-RequiredWindowsAppRuntime {
    $runtimePath = Join-Path ([IO.Path]::GetTempPath()) ("Eikura-WindowsAppRuntime-" + [Guid]::NewGuid().ToString('N') + '.exe')
    try {
        Write-Host 'Downloading the missing Windows App Runtime from Microsoft...'
        Invoke-WebRequest -Uri 'https://aka.ms/windowsappsdk/1.8/1.8.260710003/windowsappruntimeinstall-x64.exe' -OutFile $runtimePath -UseBasicParsing
        $runtimeSignature = Get-AuthenticodeSignature -FilePath $runtimePath
        if ($runtimeSignature.Status -ne 'Valid' -or
            -not $runtimeSignature.SignerCertificate -or
            $runtimeSignature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
            throw 'Windows App Runtime installer signature validation failed.'
        }
        $runtime = Start-Process -FilePath $runtimePath -ArgumentList '--quiet' -WindowStyle Hidden -Wait -PassThru
        if ($runtime.ExitCode -ne 0) { throw "Windows App Runtime installation failed: $($runtime.ExitCode)" }
    }
    finally {
        Remove-Item -LiteralPath $runtimePath -Force -ErrorAction SilentlyContinue
    }
}
try {
    # Windows resolves the package's exact framework dependency/version first.
    # Already provisioned machines require no runtime download.
    Add-AppxPackage -Path $bundlePath -ForceApplicationShutdown -ForceUpdateFromAnyVersion
}
catch {
    if ($_.Exception.HResult -ne -2147009293) { throw } # 0x80073CF3: dependency resolution failed
    Install-RequiredWindowsAppRuntime
    Add-AppxPackage -Path $bundlePath -ForceApplicationShutdown -ForceUpdateFromAnyVersion
}
$package = Get-AppxPackage -Name Eizo
if (-not $package -or [string]$package.Version -ne '1.3.5.9003' -or
    [string]$package.Status -ne 'Ok') {
    throw 'Acceptance package registration verification failed.'
}
Write-Host 'Eikura 1.3.5 WebDAV restore acceptance build installed (1.3.5.9003).'
