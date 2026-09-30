param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Read-Text([string]$relativePath) {
    $path = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required release-version file is missing: $relativePath"
    }
    return [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
}

$releasePath = Join-Path $repoRoot 'release/release.json'
$release = Get-Content -LiteralPath $releasePath -Raw | ConvertFrom-Json
$version = [string]$release.product.version
$packageVersion = [string]$release.product.packageVersion

$parsedProductVersion = [Version]::Parse("$version.0")
$parsedPackageVersion = [Version]::Parse($packageVersion)

if ($version -notmatch '^\d+\.\d+\.\d+$' -or
    $parsedPackageVersion.Major -ne $parsedProductVersion.Major -or
    $parsedPackageVersion.Minor -ne $parsedProductVersion.Minor -or
    $parsedPackageVersion.Build -ne $parsedProductVersion.Build -or
    $parsedPackageVersion.Revision -lt 0) {
    throw "Invalid release version contract: version=$version packageVersion=$packageVersion"
}

[xml]$project = Read-Text 'src/Eizo.App/Eizo.App.csproj'
[xml]$manifest = Read-Text 'src/Eizo.App/Package.appxmanifest'

$projectVersion = @($project.Project.PropertyGroup | ForEach-Object Version | Where-Object { $_ })[0]
$assemblyVersion = @($project.Project.PropertyGroup | ForEach-Object AssemblyVersion | Where-Object { $_ })[0]
$fileVersion = @($project.Project.PropertyGroup | ForEach-Object FileVersion | Where-Object { $_ })[0]
$informationalVersion = @($project.Project.PropertyGroup | ForEach-Object InformationalVersion | Where-Object { $_ })[0]

$expected = @{
    Project = $projectVersion
    Assembly = $assemblyVersion
    File = $fileVersion
    Informational = $informationalVersion
    Manifest = [string]$manifest.Package.Identity.Version
}

if ($expected.Project -ne $version -or
    $expected.Assembly -ne $packageVersion -or
    $expected.File -ne $packageVersion -or
    $expected.Informational -ne $version -or
    $expected.Manifest -ne $packageVersion) {
    throw "Release version mismatch: $($expected | ConvertTo-Json -Compress) expected=$version/$packageVersion"
}

$expectedPublisherDisplayName = 'Jo Kiyō'
$publisherDisplayName = [string]$manifest.Package.Properties.PublisherDisplayName
if ($publisherDisplayName -cne $expectedPublisherDisplayName) {
    throw "Store publisher display name mismatch: actual='$publisherDisplayName' expected='$expectedPublisherDisplayName'"
}

if ([string]$manifest.Package.Identity.Name -cne 'Eizo' -or
    [string]$manifest.Package.Identity.Publisher -cne 'CN=AppPublisher') {
    throw "Source manifest must retain GitHub sideload identity Eizo / CN=AppPublisher."
}

$storeIdentityPath = Join-Path $repoRoot 'release/MicrosoftStore/store-identity.json'
$storeIdentity = Get-Content -LiteralPath $storeIdentityPath -Raw | ConvertFrom-Json
$expectedStoreIdentity = @{
    Name = 'JoKiy.Eizo'
    Publisher = 'CN=C4E4B33A-7B77-4121-897C-7D720A5471F8'
    PublisherDisplayName = 'Jo Kiyō'
    PackageFamilyName = 'JoKiy.Eizo_4wdwgytaw3v2m'
}
if ([string]$storeIdentity.packageIdentityName -cne $expectedStoreIdentity.Name -or
    [string]$storeIdentity.publisher -cne $expectedStoreIdentity.Publisher -or
    [string]$storeIdentity.publisherDisplayName -cne $expectedStoreIdentity.PublisherDisplayName -or
    [string]$storeIdentity.packageFamilyName -cne $expectedStoreIdentity.PackageFamilyName) {
    throw "Partner Center Store identity mismatch: $($storeIdentity | ConvertTo-Json -Compress)"
}

$appVersionProvider = Read-Text 'src/Eizo.App/AppVersionProvider.cs'
if ($appVersionProvider -match 'const\s+string\s+(?:Version|DisplayVersion)' -or
    $appVersionProvider -match '(?<!\d)0\.3\.\d+(?!\d)') {
    throw 'AppVersionProvider must derive the About-page version dynamically and must not contain a hard-coded 0.3.x product version.'
}
if ($appVersionProvider -notmatch 'DisplayVersion\s*=>\s*\$"v\{Version\}"' -or
    $appVersionProvider -notmatch 'GetCurrentVersion\(\)') {
    throw 'AppVersionProvider dynamic version contract is missing.'
}

foreach ($relativePath in @(
    'src/Eizo.App/Views/AboutView.xaml',
    'src/Eizo.App/Views/AboutView.xaml.cs')) {
    $text = Read-Text $relativePath
    if ($text -match '(?<!\d)v?0\.3\.\d+(?!\d)') {
        throw "About UI contains a hard-coded product version: $relativePath"
    }
}

$resourceFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/Eizo.App') -Recurse -File -Filter '*.resw'
)
foreach ($resource in $resourceFiles) {
    $text = [IO.File]::ReadAllText($resource.FullName, [Text.Encoding]::UTF8)
    if ($text -match '(?<!\d)v?0\.3\.\d+(?!\d)') {
        $relative = [IO.Path]::GetRelativePath($repoRoot, $resource.FullName)
        throw "Localization resource contains a hard-coded product version: $relative"
    }
}

foreach ($relativePath in @(
    "docs/RELEASE-NOTES-v$version.md",
    "docs/RELEASE-NOTES-v$version.ja.md",
    "docs/RELEASE-NOTES-v$version.en.md")) {
    $text = Read-Text $relativePath
    if ($text -notmatch "(?m)^# Eikura v$([regex]::Escape($version))\s*$") {
        throw "Release Notes header does not match v${version}: $relativePath"
    }
}

$releaseBodyPath = Join-Path ([IO.Path]::GetTempPath()) "Eikura-release-body-$([Guid]::NewGuid().ToString('N')).md"
try {
    & (Join-Path $repoRoot 'packaging/New-GitHubReleaseBody.ps1') -Version $version -OutputPath $releaseBodyPath
    $releaseBody = [IO.File]::ReadAllText($releaseBodyPath, [Text.Encoding]::UTF8)

    foreach ($suffix in @(
        "docs/RELEASE-NOTES-v$version.md",
        "docs/RELEASE-NOTES-v$version.ja.md",
        "docs/RELEASE-NOTES-v$version.en.md")) {
        $expectedUrl = "https://github.com/KiYouJyo/Eikura/blob/v$version/$suffix"
        if (-not $releaseBody.Contains($expectedUrl)) {
            throw "Generated GitHub Release body is missing language URL: $expectedUrl"
        }
    }
}
finally {
    Remove-Item -LiteralPath $releaseBodyPath -Force -ErrorAction SilentlyContinue
}

# Acceptance assets use all numeric SemVer components without dots:
# 0.6.1 -> 061, 0.3.11 -> 0311, 1.0.0 -> 100.
$versionParts = $version.Split('.')
$acceptanceKey = "$($versionParts[0])$($versionParts[1])$($versionParts[2])"
$currentAcceptanceScript = "scripts/Test-EikuraBrandV${acceptanceKey}Contract.ps1"

$workflowPrefix = "v${acceptanceKey}-"
$currentAcceptanceWorkflow = @(
    Get-ChildItem -LiteralPath (Join-Path $repoRoot '.github/workflows') -File -Filter "$workflowPrefix*acceptance.yml"
    | Sort-Object Name
    | Select-Object -First 1
)
if (-not $currentAcceptanceWorkflow) {
    throw "No acceptance workflow found for Eikura $version using prefix $workflowPrefix"
}
$currentAcceptanceWorkflow = [IO.Path]::GetRelativePath(
    $repoRoot,
    $currentAcceptanceWorkflow[0].FullName)

foreach ($relativePath in @($currentAcceptanceScript, $currentAcceptanceWorkflow)) {
    $text = Read-Text $relativePath
    if (-not $text.Contains($version) -or -not $text.Contains($packageVersion)) {
        throw "Current acceptance asset is not pinned to ${version}/${packageVersion}: $relativePath"
    }
    if ($text -match '(?<!\d)0\.3\.(?:1|7|8|9|10)(?!\d)') {
        throw "Current acceptance asset contains a stale product version: $relativePath"
    }
}

Write-Host "Release version contract PASS: Eikura $version / $packageVersion"
Write-Host 'About-page version source: installed Package / Assembly (no hard-coded product version).'
