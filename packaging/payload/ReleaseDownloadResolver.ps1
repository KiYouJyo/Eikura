function Write-DownloadResolverLog([scriptblock]$Log, [string]$Message) {
    if ($Log) { & $Log $Message }
}

function Test-TransientNetworkException([Exception]$Exception) {
    $text = $Exception.ToString()
    return $text -match '(?i)(unexpected EOF|0 bytes from the transport|timed? ?out|timeout|connection reset|connection.*closed|temporarily unavailable|503|502|504|429|network stream)'
}

function Remove-DownloadPartialFile([string]$Path) {
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    }
}

function Assert-DownloadedAssetSize(
    [string]$Path,
    [long]$ExpectedBytes,
    [string]$ReleaseTag,
    [string]$AssetName,
    [scriptblock]$Log) {

    $actualBytes = if (Test-Path -LiteralPath $Path -PathType Leaf) {
        (Get-Item -LiteralPath $Path).Length
    } else { 0 }

    Write-DownloadResolverLog $Log "ReleaseTag=$ReleaseTag; Asset=$AssetName; ExpectedBytes=$ExpectedBytes; ActualBytes=$actualBytes"
    if ($actualBytes -ne $ExpectedBytes) { throw 'IncompleteDownload' }
}

function Download-ReleaseAssetRobust {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][long]$ExpectedBytes,
        [Parameter(Mandatory)][string]$ReleaseTag,
        [Parameter(Mandatory)][string]$AssetName,
        [scriptblock]$Log,
        [int]$FallbackAttempts = 3,
        [switch]$DisableBits
    )

    $bitsAvailable =
        -not $DisableBits -and
        $null -ne (Get-Command Start-BitsTransfer -ErrorAction SilentlyContinue)

    if ($bitsAvailable) {
        try {
            Remove-DownloadPartialFile $Destination
            Write-DownloadResolverLog $Log "DownloadMethod=BITS; Attempt=1/1; ReleaseTag=$ReleaseTag; Asset=$AssetName; ExpectedBytes=$ExpectedBytes"
            Start-BitsTransfer -Source $Uri -Destination $Destination -DisplayName "Eikura $ReleaseTag $AssetName" -Description 'Eikura resilient GitHub Release download' -RetryInterval 60 -RetryTimeout 1200 -ErrorAction Stop
            Assert-DownloadedAssetSize $Destination $ExpectedBytes $ReleaseTag $AssetName $Log
            Write-DownloadResolverLog $Log 'BundleDownloadCompleted'
            return
        }
        catch {
            Write-DownloadResolverLog $Log "BITS failed: ExceptionType=$($_.Exception.GetType().FullName); HRESULT=$($_.Exception.HResult); Message=$($_.Exception.Message)"
            Remove-DownloadPartialFile $Destination
        }
    }

    $delays = @(2, 5, 10)
    for ($attempt = 1; $attempt -le $FallbackAttempts; $attempt++) {
        Remove-DownloadPartialFile $Destination
        try {
            Write-DownloadResolverLog $Log "DownloadMethod=InvokeWebRequest; Attempt=$attempt/$FallbackAttempts; ReleaseTag=$ReleaseTag; Asset=$AssetName; ExpectedBytes=$ExpectedBytes"
            Invoke-WebRequest -Uri $Uri -UseBasicParsing -OutFile $Destination -ErrorAction Stop
            Assert-DownloadedAssetSize $Destination $ExpectedBytes $ReleaseTag $AssetName $Log
            Write-DownloadResolverLog $Log 'BundleDownloadCompleted'
            return
        }
        catch {
            Write-DownloadResolverLog $Log "InvokeWebRequest failed: Attempt=$attempt/$FallbackAttempts; ExceptionType=$($_.Exception.GetType().FullName); HRESULT=$($_.Exception.HResult); Message=$($_.Exception.Message)"
            Remove-DownloadPartialFile $Destination
            if ($attempt -ge $FallbackAttempts -or -not (Test-TransientNetworkException $_.Exception)) {
                break
            }
            Start-Sleep -Seconds $delays[[Math]::Min($attempt - 1, $delays.Count - 1)]
        }
    }

    Write-DownloadResolverLog $Log 'BundleDownloadFailed'
    throw 'DownloadFailed'
}

function Download-SmallReleaseAssetWithRetry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string]$ReleaseTag,
        [Parameter(Mandatory)][string]$AssetName,
        [scriptblock]$Log,
        [int]$Attempts = 3
    )

    $delays = @(2, 5, 10)
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        Remove-DownloadPartialFile $Destination
        try {
            Write-DownloadResolverLog $Log "DownloadMethod=InvokeWebRequest; Attempt=$attempt/$Attempts; ReleaseTag=$ReleaseTag; Asset=$AssetName"
            Invoke-WebRequest -Uri $Uri -UseBasicParsing -OutFile $Destination -ErrorAction Stop
            return
        }
        catch {
            Write-DownloadResolverLog $Log "Checksum asset download failed: Attempt=$attempt/$Attempts; ExceptionType=$($_.Exception.GetType().FullName); HRESULT=$($_.Exception.HResult); Message=$($_.Exception.Message)"
            Remove-DownloadPartialFile $Destination
            if ($attempt -ge $Attempts -or -not (Test-TransientNetworkException $_.Exception)) {
                throw 'ChecksumDownloadFailed'
            }
            Start-Sleep -Seconds $delays[[Math]::Min($attempt - 1, $delays.Count - 1)]
        }
    }
}
