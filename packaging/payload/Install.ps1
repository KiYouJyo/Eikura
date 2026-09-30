[CmdletBinding()]
param(
    [switch]$ImportCertificateOnly,
    [string]$BundlePathOverride
)

$ErrorActionPreference = 'Stop'
$payloadRoot = $PSScriptRoot

. (Join-Path $payloadRoot 'InstallerMetadata.ps1')
. (Join-Path $payloadRoot 'ChecksumResolver.ps1')
. (Join-Path $payloadRoot 'ReleaseDownloadResolver.ps1')

$logDirectory = Join-Path $env:LOCALAPPDATA 'Eizo\Logs'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$logPath = Join-Path $logDirectory ("Install-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
$tempRoot = $null

function Log([string]$Message) {
    "{0:u} {1}" -f (Get-Date), $Message | Tee-Object -FilePath $logPath -Append
}

function Test-IsAdministrator {
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-ReleaseMetadataWithRetry([string]$Uri, [hashtable]$Headers, [scriptblock]$LogCallback) {
    $delays = @(2, 5, 10)
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            return Invoke-RestMethod -Uri $Uri -Headers $Headers -Method Get -ErrorAction Stop
        }
        catch {
            & $LogCallback "Release metadata failed: Attempt=$attempt/3; Message=$($_.Exception.Message)"
            if ($attempt -ge 3 -or -not (Test-TransientNetworkException $_.Exception)) {
                throw 'ReleaseMetadataFailed'
            }
            Start-Sleep -Seconds $delays[$attempt - 1]
        }
    }
}

try {
    $metadata = Get-InstallerMetadata $payloadRoot

    $payloadHashes = @{}
    Get-Content -LiteralPath (Join-Path $payloadRoot 'SHA256SUMS.txt') | ForEach-Object {
        if ($_ -match '^(?<hash>[A-Fa-f0-9]{64}) \*(?<name>.+)$') {
            $payloadHashes[$matches.name.Replace('/','\')] = $matches.hash.ToUpperInvariant()
        }
    }

    $certPath = Get-SafePayloadFilePath $payloadRoot $metadata.certificateFileName
    if (-not (Test-Path -LiteralPath $certPath -PathType Leaf)) {
        throw 'Public certificate is missing.'
    }

    $certRelative = [string]$metadata.certificateFileName
    $certHash = (Get-FileHash -LiteralPath $certPath -Algorithm SHA256).Hash.ToUpperInvariant()
    if (-not $payloadHashes.ContainsKey($certRelative) -or $payloadHashes[$certRelative] -ne $certHash) {
        throw 'Public certificate SHA-256 mismatch.'
    }

    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certPath)
    if ($certificate.HasPrivateKey -or $certificate.Subject -cne $metadata.publisher) {
        throw 'Certificate publisher mismatch.'
    }

    $thumbprint = $certificate.Thumbprint.ToUpperInvariant()
    Log "Validated Eikura v$($metadata.displayVersion); Publisher=$($metadata.publisher); Thumbprint=$thumbprint."

    $trusted = Get-ChildItem 'Cert:\LocalMachine\TrustedPeople' -ErrorAction SilentlyContinue |
        Where-Object Thumbprint -eq $thumbprint |
        Select-Object -First 1

    if (-not $trusted -and $ImportCertificateOnly) {
        if (-not (Test-IsAdministrator)) {
            throw 'Certificate trust requires elevation.'
        }
        Import-Certificate -FilePath $certPath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
        Log 'Imported the public certificate into LocalMachine TrustedPeople.'
        exit 0
    }

    if (-not $trusted -and -not (Test-IsAdministrator)) {
        $quotedScript = '"' + $PSCommandPath.Replace('"','""') + '"'
        $arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File $quotedScript -ImportCertificateOnly"
        $elevated = Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Verb RunAs -Wait -PassThru
        if ($elevated.ExitCode -ne 0) {
            throw 'Certificate trust setup was cancelled or failed.'
        }
        Log 'Certificate trust setup completed through a UAC-elevated helper.'
    }
    elseif (-not $trusted) {
        Import-Certificate -FilePath $certPath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
        Log 'Imported the public certificate into LocalMachine TrustedPeople.'
    }
    else {
        Log 'Matching public certificate is already trusted.'
    }

    $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("Eikura-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    $localBundlePath = Join-Path $tempRoot $metadata.remoteBundleFileName

    if ($BundlePathOverride) {
        if (-not (Test-Path -LiteralPath $BundlePathOverride -PathType Leaf)) {
            throw "Test bundle override does not exist: $BundlePathOverride"
        }
        Copy-Item -LiteralPath $BundlePathOverride -Destination $localBundlePath -Force
        Log "Using local bundle override: $localBundlePath."
    }
    else {
        $headers = @{
            Accept = 'application/vnd.github+json'
            'User-Agent' = "Eikura/$($metadata.displayVersion)"
        }
        $logCallback = { param($message) Log $message }
        $release = Invoke-ReleaseMetadataWithRetry -Uri $metadata.releaseApiUri -Headers $headers -LogCallback $logCallback

        if ($release.tag_name -ne $metadata.releaseTag -or $release.draft -or $release.prerelease) {
            throw "Stable release not found: $($metadata.releaseTag)"
        }

        $bundleAssets = @($release.assets | Where-Object { $_.name -like '*.msixbundle' })
        $bundleAsset = @($bundleAssets | Where-Object name -eq $metadata.remoteBundleFileName)
        $checksumAsset = @($release.assets | Where-Object name -eq $metadata.checksumFileName)

        if ($bundleAssets.Count -ne 1 -or $bundleAsset.Count -ne 1) {
            throw 'Release assets are incomplete.'
        }

        $bundleDigest = Get-ValidSha256Digest $bundleAsset[0].digest
        $manifestHash = $null

        if ($checksumAsset.Count -eq 1) {
            $checksumPath = Join-Path $tempRoot $metadata.checksumFileName
            $downloadChecksumArgs = @{
                Uri = $checksumAsset[0].browser_download_url
                Destination = $checksumPath
                ReleaseTag = $metadata.releaseTag
                AssetName = $metadata.checksumFileName
                Log = $logCallback
            }
            Download-SmallReleaseAssetWithRetry @downloadChecksumArgs
            $manifestHash = Resolve-Sha256ManifestHash $checksumPath $metadata.remoteBundleFileName
        }

        if ($bundleDigest -and $manifestHash -and $bundleDigest -cne $manifestHash) {
            throw 'Release checksum sources disagree.'
        }

        $expectedHash = if ($bundleDigest) { $bundleDigest } else { $manifestHash }
        if ([string]::IsNullOrWhiteSpace($expectedHash)) {
            throw "No valid SHA-256 is available for $($metadata.remoteBundleFileName)."
        }

        $downloadBundleArgs = @{
            Uri = $bundleAsset[0].browser_download_url
            Destination = $localBundlePath
            ExpectedBytes = [long]$bundleAsset[0].size
            ReleaseTag = $metadata.releaseTag
            AssetName = $metadata.remoteBundleFileName
            Log = $logCallback
        }
        Download-ReleaseAssetRobust @downloadBundleArgs

        $actualHash = (Get-FileHash -LiteralPath $localBundlePath -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($actualHash -ne $expectedHash) {
            throw 'Downloaded bundle SHA-256 mismatch.'
        }
        Log "Downloaded and verified $($metadata.remoteBundleFileName); SHA256=$actualHash."
    }

    $signature = Get-AuthenticodeSignature -FilePath $localBundlePath
    if (-not $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -cne $metadata.publisher -or
        $signature.SignerCertificate.Thumbprint.ToUpperInvariant() -ne $thumbprint) {
        throw 'MSIXBundle signature mismatch.'
    }

    Add-AppxPackage -Path $localBundlePath -ForceApplicationShutdown -ErrorAction Stop

    $installed = @(
        Get-AppxPackage -Name $metadata.packageIdentityName -ErrorAction SilentlyContinue |
            Where-Object Publisher -eq $metadata.publisher
    )
    if ($installed.Count -ne 1) {
        throw "Package verification failed: expected one Eikura package, found $($installed.Count)."
    }

    $package = $installed[0]
    if ([string]$package.Version -ne $metadata.packageVersion -or
        [string]$package.Architecture -ne 'X64' -or
        [string]$package.Status -ne 'Ok') {
        throw "Package verification failed: Version=$($package.Version); Architecture=$($package.Architecture); Status=$($package.Status)"
    }

    Log "Verified installed package: $($package.PackageFullName); Status=$($package.Status)."
    Write-Output "Eikura v$($metadata.displayVersion) installation completed."
    exit 0
}
catch {
    Log "Installation failed: $($_.Exception.Message)"
    Write-Error $_
    exit 1
}
finally {
    if ($tempRoot -and (Test-Path -LiteralPath $tempRoot)) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Output "INSTALL_LOG_PATH=$logPath"
}
