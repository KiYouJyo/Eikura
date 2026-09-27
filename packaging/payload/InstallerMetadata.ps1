$ErrorActionPreference = 'Stop'

function Get-InstallerMetadata([string]$PayloadRoot) {
    $path = Join-Path $PayloadRoot 'InstallerMetadata.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw 'Missing InstallerMetadata.json.'
    }

    $metadata = Get-Content -Raw -LiteralPath $path -Encoding UTF8 | ConvertFrom-Json

    foreach ($field in @(
        'schemaVersion',
        'displayVersion',
        'packageVersion',
        'packageIdentityName',
        'publisher',
        'architecture',
        'releaseTag',
        'remoteBundleFileName',
        'certificateFileName',
        'releaseApiUri',
        'checksumFileName')) {
        if ([string]::IsNullOrWhiteSpace([string]$metadata.$field)) {
            throw "Missing metadata field: $field"
        }
    }

    if ([int]$metadata.schemaVersion -ne 3 -or $metadata.architecture -cne 'x64') {
        throw 'Unsupported installer metadata.'
    }

    if ($metadata.displayVersion -notmatch '^\d+\.\d+\.\d+$' -or
        $metadata.packageVersion -notmatch '^\d+\.\d+\.\d+\.\d+$' -or
        -not $metadata.packageVersion.StartsWith("$($metadata.displayVersion).")) {
        throw 'Invalid installer version.'
    }

    if ([IO.Path]::GetFileName($metadata.remoteBundleFileName) -cne $metadata.remoteBundleFileName -or
        [IO.Path]::GetExtension($metadata.remoteBundleFileName) -cne '.msixbundle') {
        throw 'Invalid remote bundle filename.'
    }

    $expectedApi =
        "https://api.github.com/repos/KiYouJyo/Eikura/releases/tags/v$($metadata.displayVersion)"

    if ($metadata.releaseTag -cne "v$($metadata.displayVersion)" -or
        $metadata.releaseApiUri -cne $expectedApi -or
        $metadata.checksumFileName -cne 'SHA256SUMS.txt') {
        throw 'Invalid GitHub release metadata.'
    }

    return $metadata
}

function Get-SafePayloadFilePath([string]$PayloadRoot, [string]$FileName) {
    if ([string]::IsNullOrWhiteSpace($FileName) -or
        [IO.Path]::IsPathRooted($FileName) -or
        $FileName.Contains('..') -or
        $FileName.IndexOfAny([char[]]'\/') -ge 0) {
        throw 'Unsafe payload file name.'
    }

    return Join-Path $PayloadRoot $FileName
}
