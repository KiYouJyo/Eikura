param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Read-Text([string]$relativePath) {
    $path = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Eikura compatibility file is missing: $relativePath"
    }
    return [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
}

$release = Read-Text 'release/release.json' | ConvertFrom-Json
$version = [string]$release.product.version
$packageVersion = [string]$release.product.packageVersion

[xml]$manifest = Read-Text 'src/Eizo.App/Package.appxmanifest'
if ([string]$manifest.Package.Identity.Name -cne 'Eizo' -or
    [string]$manifest.Package.Identity.Version -cne $packageVersion -or
    [string]$manifest.Package.Identity.Publisher -cne 'CN=AppPublisher') {
    throw "GitHub MSIX identity no longer preserves the in-place upgrade chain for Eikura $version."
}

$protocols = @($manifest.Package.Applications.Application.Extensions.Extension.Protocol | ForEach-Object Name)
if ($protocols -notcontains 'eikura' -or $protocols -notcontains 'eizo') {
    throw 'Eikura must register both the new and legacy Bangumi activation protocols.'
}

$storeIdentity = Read-Text 'release/MicrosoftStore/store-identity.json' | ConvertFrom-Json
if ([string]$storeIdentity.packageIdentityName -cne 'JoKiy.Eizo' -or
    [string]$storeIdentity.packageFamilyName -cne 'JoKiy.Eizo_4wdwgytaw3v2m') {
    throw 'Microsoft Store identity changed during the Eikura compatibility period.'
}

$legacyDataFiles = @(
    'src/Eizo.App/AppSettingsStore.cs',
    'src/Eizo.App/Models/FirstRunExperienceService.cs',
    'src/Eizo.App/Models/MediaCatalogStore.cs',
    'src/Eizo.App/Models/MediaIdentityBindingStore.cs',
    'src/Eizo.App/Models/PlaybackHistoryStore.cs',
    'src/Eizo.App/Models/PlaybackTrackPreferenceStore.cs',
    'src/Eizo.App/ProductAppUpdateService.cs'
)
foreach ($relativePath in $legacyDataFiles) {
    $text = Read-Text $relativePath
    if (-not $text.Contains('"Eizo"', [StringComparison]::Ordinal)) {
        throw "Legacy Eizo data root was removed from compatibility-sensitive storage: $relativePath"
    }
}

$credentials = Read-Text 'src/Eizo.App/Models/BangumiAccountCredentialStore.cs'
foreach ($key in @('"Eizo.Bangumi"', '"Eizo.Bangumi.OAuth"')) {
    if (-not $credentials.Contains($key, [StringComparison]::Ordinal)) {
        throw "Credential Locker compatibility key changed: $key"
    }
}

$singleInstance = Read-Text 'src/Eizo.App/SingleInstanceActivation.cs'
if (-not $singleInstance.Contains('"Eizo.Main"', [StringComparison]::Ordinal)) {
    throw 'Single-instance compatibility key changed during brand migration.'
}

$catalog = Read-Text 'src/Eizo.App/Views/CatalogView.xaml.cs'
foreach ($schemaField in @('EizoItemMediaId', 'EizoSubjectId')) {
    if (-not $catalog.Contains($schemaField, [StringComparison]::Ordinal)) {
        throw "Legacy Recognition CSV schema field changed: $schemaField"
    }
}

Write-Host "Eikura upgrade compatibility contract PASS: $version / $packageVersion"
