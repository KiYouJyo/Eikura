[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$tag = "v$Version"

$relativeFiles = @(
    "docs/RELEASE-NOTES-v$Version.md",
    "docs/RELEASE-NOTES-v$Version.ja.md",
    "docs/RELEASE-NOTES-v$Version.en.md"
)

foreach ($relativeFile in $relativeFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativeFile) -PathType Leaf)) {
        throw "Required Release Notes sibling is missing: $relativeFile"
    }
}

$canonicalPath = Join-Path $repositoryRoot $relativeFiles[0]
$canonicalLines =
    [System.IO.File]::ReadAllLines($canonicalPath, [System.Text.Encoding]::UTF8)

if ($canonicalLines.Count -lt 2) {
    throw 'Canonical Release Notes are incomplete.'
}

$labels = @(
    [regex]::Matches($canonicalLines[0], '\[(?<label>[^\]]+)\]') |
        ForEach-Object { $_.Groups['label'].Value }
)

$firstSeparator = $canonicalLines[0].IndexOf(' | ', [StringComparison]::Ordinal)
$hasLanguageSwitcher =
    $firstSeparator -ge 1 -and
    $labels.Count -eq 2

if ($hasLanguageSwitcher) {
    $zhLabel = $canonicalLines[0].Substring(0, $firstSeparator)
    $jaLabel = $labels[0]
    $enLabel = $labels[1]
    $bodyStart = 1
}
else {
    $zhLabel = '简体中文'
    $jaLabel = '日本語'
    $enLabel = 'English'
    $bodyStart = 0
}
$repositoryUrl = 'https://github.com/KiYouJyo/Eikura'
$zhUrl = "$repositoryUrl/blob/$tag/$($relativeFiles[0])"
$jaUrl = "$repositoryUrl/blob/$tag/$($relativeFiles[1])"
$enUrl = "$repositoryUrl/blob/$tag/$($relativeFiles[2])"

$bodyLines =
    if ($bodyStart -lt $canonicalLines.Count) {
        $canonicalLines[$bodyStart..($canonicalLines.Count - 1)]
    }
    else {
        @()
    }

$published =
    "[$zhLabel]($zhUrl) | [$jaLabel]($jaUrl) | [$enLabel]($enUrl)" +
    [Environment]::NewLine +
    ($bodyLines -join [Environment]::NewLine)

foreach ($url in @($zhUrl, $jaUrl, $enUrl)) {
    if (-not $published.Contains($url)) {
        throw "Published Release body is missing tag-pinned URL: $url"
    }
}

$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

[System.IO.File]::WriteAllText(
    $OutputPath,
    $published,
    [System.Text.UTF8Encoding]::new($false))
