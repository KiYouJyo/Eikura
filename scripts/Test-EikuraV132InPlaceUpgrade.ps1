[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$oldVersion = '1.3.1'
$oldPackageVersion = '1.3.1.0'
$newVersion = '1.3.2'
$newPackageVersion = '1.3.2.0'
$repo = 'KiYouJyo/Eikura'
$packageIdentityName = 'Eizo'
$publisher = 'CN=AppPublisher'
$tempRoot = Join-Path $env:RUNNER_TEMP ("Eikura-v132-upgrade-" + [Guid]::NewGuid().ToString('N'))
$dataRoot = Join-Path $env:LOCALAPPDATA 'Eizo'

function Download([string]$Uri, [string]$Destination) {
    Write-Host "Downloading $Uri"
    Invoke-WebRequest -Uri $Uri -OutFile $Destination -UseBasicParsing
    if (-not (Test-Path -LiteralPath $Destination -PathType Leaf) -or (Get-Item $Destination).Length -eq 0) {
        throw "Download failed: $Uri"
    }
}

function Get-Package {
    $packages = @(Get-AppxPackage -Name $packageIdentityName -ErrorAction SilentlyContinue |
        Where-Object Publisher -eq $publisher)
    if ($packages.Count -ne 1) {
        throw "Expected one installed package, found $($packages.Count)."
    }
    return $packages[0]
}

function Assert-DataState([string]$Stage) {
    foreach ($name in @('settings.json', 'catalog.json', 'playback-history.json', 'v132-upgrade-marker.txt')) {
        $path = Join-Path $dataRoot $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "$Stage lost legacy user data file: $name"
        }
    }

    $settings = Get-Content -LiteralPath (Join-Path $dataRoot 'settings.json') -Raw | ConvertFrom-Json
    if ([int]$settings.Language -ne 2 -or
        [double]$settings.DefaultPlaybackRate -ne 1.25 -or
        [double]$settings.LastPlaybackRate -ne 1.5 -or
        -not [bool]$settings.PreserveOfflineCache) {
        throw "$Stage changed persisted settings values."
    }

    $catalog = @(Get-Content -LiteralPath (Join-Path $dataRoot 'catalog.json') -Raw | ConvertFrom-Json)
    $history = @(Get-Content -LiteralPath (Join-Path $dataRoot 'playback-history.json') -Raw | ConvertFrom-Json)
    if ($catalog.Count -ne 0 -or $history.Count -ne 0) {
        throw "$Stage changed seeded catalog/history fixtures."
    }

    $marker = Get-Content -LiteralPath (Join-Path $dataRoot 'v132-upgrade-marker.txt') -Raw
    if ($marker.Trim() -cne 'Eikura-v1.3.2-upgrade-acceptance') {
        throw "$Stage changed the legacy data-root marker."
    }
}

New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
try {
    # Install the same Windows App Runtime generation used by the normal release smoke test.
    $runtimeInstaller = Join-Path $tempRoot 'WindowsAppRuntimeInstall-x64.exe'
    Download 'https://aka.ms/windowsappsdk/1.8/1.8.260710003/windowsappruntimeinstall-x64.exe' $runtimeInstaller
    $runtimeSignature = Get-AuthenticodeSignature -FilePath $runtimeInstaller
    if (-not $runtimeSignature.SignerCertificate -or
        $runtimeSignature.Status -ne 'Valid' -or
        $runtimeSignature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') {
        throw 'Windows App Runtime installer signature validation failed.'
    }
    & $runtimeInstaller --quiet
    if ($LASTEXITCODE -ne 0) { throw "Windows App Runtime setup failed: $LASTEXITCODE" }

    $oldBundle = Join-Path $tempRoot "Eizo_$oldPackageVersion`_x64.msixbundle"
    $newBundle = Join-Path $tempRoot "Eikura_$newPackageVersion`_x64.msixbundle"
    $oldInstallerZip = Join-Path $tempRoot "Eizo-v$oldVersion-x64-one-click.zip"
    $newInstallerZip = Join-Path $tempRoot "Eikura-v$newVersion-x64-one-click.zip"

    Download "https://github.com/$repo/releases/download/v$oldVersion/Eizo_$oldPackageVersion`_x64.msixbundle" $oldBundle
    Download "https://github.com/$repo/releases/download/v$newVersion/Eikura_$newPackageVersion`_x64.msixbundle" $newBundle
    Download "https://github.com/$repo/releases/download/v$oldVersion/Eizo-v$oldVersion-x64-one-click.zip" $oldInstallerZip
    Download "https://github.com/$repo/releases/download/v$newVersion/Eikura-v$newVersion-x64-one-click.zip" $newInstallerZip

    $oldInstallerRoot = Join-Path $tempRoot 'old-installer'
    $newInstallerRoot = Join-Path $tempRoot 'new-installer'
    Expand-Archive -LiteralPath $oldInstallerZip -DestinationPath $oldInstallerRoot -Force
    Expand-Archive -LiteralPath $newInstallerZip -DestinationPath $newInstallerRoot -Force

    $certificates = @(
        Get-ChildItem -LiteralPath $oldInstallerRoot -Recurse -File -Filter '*.cer'
        Get-ChildItem -LiteralPath $newInstallerRoot -Recurse -File -Filter '*.cer'
    )
    if ($certificates.Count -lt 2) { throw 'Release installer certificates are missing.' }

    $trustedStore = 'Cert:\LocalMachine\TrustedPeople'
    foreach ($certFile in $certificates) {
        $cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certFile.FullName)
        if ($cert.HasPrivateKey -or $cert.Subject -cne $publisher) {
            throw "Unexpected release certificate: $($certFile.FullName)"
        }
        $trusted = Get-ChildItem $trustedStore -ErrorAction SilentlyContinue |
            Where-Object Thumbprint -eq $cert.Thumbprint |
            Select-Object -First 1
        if (-not $trusted) {
            Import-Certificate -FilePath $certFile.FullName -CertStoreLocation $trustedStore | Out-Null
        }
    }

    foreach ($bundle in @($oldBundle, $newBundle)) {
        $signature = Get-AuthenticodeSignature -FilePath $bundle
        if (-not $signature.SignerCertificate -or
            $signature.SignerCertificate.Subject -cne $publisher -or
            $signature.Status -ne 'Valid') {
            throw "Release bundle signature validation failed: $bundle"
        }
    }

    Get-AppxPackage -Name $packageIdentityName -ErrorAction SilentlyContinue |
        Remove-AppxPackage -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $dataRoot) {
        Remove-Item -LiteralPath $dataRoot -Recurse -Force
    }

    Write-Host "Installing legacy Eizo $oldVersion..."
    Add-AppxPackage -Path $oldBundle -ForceApplicationShutdown
    $oldPackage = Get-Package
    if ([string]$oldPackage.Version -cne $oldPackageVersion) {
        throw "Legacy install version mismatch: $($oldPackage.Version)"
    }
    $oldFamily = [string]$oldPackage.PackageFamilyName
    $oldName = [string]$oldPackage.Name
    if ($oldName -cne $packageIdentityName) {
        throw "Legacy package identity mismatch: $oldName"
    }

    New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
    @{
        Language = 2
        DefaultPlaybackRate = 1.25
        LastPlaybackRate = 1.5
        PreserveOfflineCache = $true
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dataRoot 'settings.json') -Encoding UTF8
    '[]' | Set-Content -LiteralPath (Join-Path $dataRoot 'catalog.json') -Encoding UTF8
    '[]' | Set-Content -LiteralPath (Join-Path $dataRoot 'playback-history.json') -Encoding UTF8
    'Eikura-v1.3.2-upgrade-acceptance' | Set-Content -LiteralPath (Join-Path $dataRoot 'v132-upgrade-marker.txt') -Encoding UTF8
    Assert-DataState 'Before upgrade'

    Write-Host "Upgrading in place to Eikura $newVersion..."
    Add-AppxPackage -Path $newBundle -ForceApplicationShutdown
    $newPackage = Get-Package
    if ([string]$newPackage.Version -cne $newPackageVersion) {
        throw "Upgrade version mismatch: $($newPackage.Version)"
    }
    if ([string]$newPackage.Name -cne $oldName -or
        [string]$newPackage.PackageFamilyName -cne $oldFamily -or
        [string]$newPackage.Publisher -cne $publisher) {
        throw 'Package identity/family changed across the Eizo -> Eikura upgrade.'
    }
    Assert-DataState 'After package upgrade'

    $manifest = Get-AppxPackageManifest -Package $newPackage
    if ([string]$manifest.Package.Properties.DisplayName -cne 'Eikura') {
        throw "Upgraded package display name is not Eikura: $($manifest.Package.Properties.DisplayName)"
    }
    $appId = [string]$manifest.Package.Applications.Application.Id
    $activation = "shell:AppsFolder\$($newPackage.PackageFamilyName)!$appId"
    Start-Process explorer.exe -ArgumentList $activation
    Start-Sleep -Seconds 8

    $installRoot = [IO.Path]::GetFullPath($newPackage.InstallLocation)
    $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try {
            $_.Path -and ([IO.Path]::GetFullPath($_.Path)).StartsWith($installRoot, [StringComparison]::OrdinalIgnoreCase)
        }
        catch { $false }
    })
    if ($running.Count -eq 0) {
        throw 'Upgraded Eikura package failed to launch.'
    }
    $running | Stop-Process -Force -ErrorAction SilentlyContinue

    Assert-DataState 'After Eikura launch'
    Write-Host "Eikura v1.3.1 -> v1.3.2 in-place upgrade PASS. PackageFamily=$oldFamily"
}
finally {
    Get-AppxPackage -Name $packageIdentityName -ErrorAction SilentlyContinue |
        Remove-AppxPackage -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $dataRoot) {
        Remove-Item -LiteralPath $dataRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
