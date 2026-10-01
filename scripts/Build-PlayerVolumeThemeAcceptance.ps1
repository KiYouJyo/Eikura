param()

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:RELEASE_CERTIFICATE_BASE64) -or [string]::IsNullOrWhiteSpace($env:RELEASE_CERTIFICATE_PASSWORD)) {
    throw 'GitHub release signing secrets are not configured.'
}

if ([string]::IsNullOrWhiteSpace($env:PLAYBACK_MSIX) -or -not (Test-Path -LiteralPath $env:PLAYBACK_MSIX -PathType Leaf)) {
    throw 'PLAYBACK_MSIX is missing.'
}

$signedDir = Join-Path $env:RUNNER_TEMP 'Eizo-player-volume-theme-signed'
Remove-Item $signedDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $signedDir | Out-Null

$signedMsix = Join-Path $signedDir 'Eizo_0.2.0.0_x64_player-volume-theme.msix'
Copy-Item -LiteralPath $env:PLAYBACK_MSIX -Destination $signedMsix -Force

$pfx = Join-Path $env:RUNNER_TEMP 'eizo-release-signing.pfx'
[IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:RELEASE_CERTIFICATE_BASE64))

$password = ConvertTo-SecureString $env:RELEASE_CERTIFICATE_PASSWORD -AsPlainText -Force
$certificate = Import-PfxCertificate -FilePath $pfx -CertStoreLocation Cert:\CurrentUser\My -Password $password

if ($certificate.Subject -cne 'CN=AppPublisher' -or $certificate.Thumbprint -cne 'BD85AD77A651C86CA01A480C8E9BC64952993F98' -or -not $certificate.HasPrivateKey) {
    throw "Unexpected signing certificate: Subject=$($certificate.Subject); Thumbprint=$($certificate.Thumbprint); HasPrivateKey=$($certificate.HasPrivateKey)"
}

$cer = Join-Path $signedDir 'Eizo-v0.2.0-AppPublisher.cer'
Export-Certificate -Cert $certificate -FilePath $cer | Out-Null

$kitsRoot = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
$signtool = Get-ChildItem (Join-Path $kitsRoot 'Windows Kits\10\bin') -Recurse -Filter signtool.exe |
    Where-Object FullName -match '\\x64\\signtool.exe$' |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if (-not $signtool) {
    throw 'x64 signtool.exe was not found.'
}

& $signtool.FullName sign /fd SHA256 /sha1 $certificate.Thumbprint /tr http://timestamp.digicert.com /td SHA256 $signedMsix
if ($LASTEXITCODE -ne 0) {
    throw "signtool sign failed: $LASTEXITCODE"
}

$signature = Get-AuthenticodeSignature -FilePath $signedMsix
if (-not $signature.SignerCertificate -or
    $signature.SignerCertificate.Subject -cne 'CN=AppPublisher' -or
    $signature.SignerCertificate.Thumbprint -cne $certificate.Thumbprint) {
    throw 'Signed MSIX signer verification failed.'
}

$runtimeInstaller = Join-Path $env:RUNNER_TEMP 'WindowsAppRuntimeInstall-x64.exe'
Invoke-WebRequest -Uri 'https://aka.ms/windowsappsdk/1.8/1.8.260710003/windowsappruntimeinstall-x64.exe' -OutFile $runtimeInstaller -UseBasicParsing

$runtimeSignature = Get-AuthenticodeSignature -FilePath $runtimeInstaller
if (-not $runtimeSignature.SignerCertificate -or
    $runtimeSignature.Status -ne 'Valid' -or
    $runtimeSignature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
    throw 'Windows App Runtime installer signature validation failed.'
}

Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null

Get-AppxPackage -Name Eizo -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue

& $runtimeInstaller --quiet
if ($LASTEXITCODE -ne 0) {
    throw "Windows App Runtime installer failed: $LASTEXITCODE"
}

Add-AppxPackage -Path $signedMsix -ForceApplicationShutdown

$pkg = Get-AppxPackage -Name Eizo
if (-not $pkg) {
    throw 'Eizo package was not registered after installation.'
}

$manifest = Get-AppxPackageManifest -Package $pkg
$appId = [string]$manifest.Package.Applications.Application.Id
if ([string]::IsNullOrWhiteSpace($appId)) {
    throw 'Unable to resolve AppUserModel application id.'
}

$activation = "shell:AppsFolder\$($pkg.PackageFamilyName)!$appId"
Start-Process explorer.exe -ArgumentList $activation

$installRoot = [IO.Path]::GetFullPath($pkg.InstallLocation)
$deadline = (Get-Date).AddSeconds(20)
$windowProcess = $null

do {
    Start-Sleep -Milliseconds 500
    $candidates = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try {
            $_.Path -and ([IO.Path]::GetFullPath($_.Path)).StartsWith($installRoot, [StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            $false
        }
    })

    foreach ($candidate in $candidates) {
        $candidate.Refresh()
        if (-not $candidate.HasExited -and $candidate.Responding -and $candidate.MainWindowHandle -ne [IntPtr]::Zero) {
            $windowProcess = $candidate
            break
        }
    }
}
while (-not $windowProcess -and (Get-Date) -lt $deadline)

if (-not $windowProcess) {
    throw 'Eizo did not create a responsive visible main window.'
}

Write-Host "Visible-window smoke PASS. PID=$($windowProcess.Id)"

Start-Sleep -Seconds 15

$survivor = Get-Process -Id $windowProcess.Id -ErrorAction SilentlyContinue
if (-not $survivor -or -not $survivor.Responding -or $survivor.MainWindowHandle -eq [IntPtr]::Zero) {
    throw 'Eizo did not remain alive with a responsive main window for 15 seconds.'
}

Write-Host "Sustained-window smoke PASS. PID=$($survivor.Id)"

$survivor | Stop-Process -Force -ErrorAction SilentlyContinue
Get-AppxPackage -Name Eizo | Remove-AppxPackage -ErrorAction SilentlyContinue

$shaFile = Join-Path $signedDir 'SHA256SUMS.txt'
$lines = foreach ($file in @($signedMsix, $cer)) {
    $item = Get-Item -LiteralPath $file
    "{0}  {1}" -f (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant(), $item.Name
}

Set-Content -LiteralPath $shaFile -Value $lines -Encoding ascii
Get-Content -LiteralPath $shaFile

Remove-Item -LiteralPath $pfx -Force -ErrorAction SilentlyContinue

"SIGNED_MSIX=$signedMsix" | Out-File $env:GITHUB_ENV -Encoding utf8 -Append
"CERTIFICATE_PATH=$cer" | Out-File $env:GITHUB_ENV -Encoding utf8 -Append
"SHA256_PATH=$shaFile" | Out-File $env:GITHUB_ENV -Encoding utf8 -Append

Write-Host "Signed player volume/theme acceptance package ready: $signedMsix"
