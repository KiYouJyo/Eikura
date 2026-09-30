param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$version = '1.3.5'
$packageVersion = '1.3.5.0'

function Read-Text([string]$relativePath) {
    $path = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing Eikura 1.3.5 contract file: $relativePath"
    }
    return [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
}

$service = Read-Text 'src/Eizo.App/ConfigBackupService.cs'
$settingsUi = Read-Text 'src/Eizo.App/Views/SettingsView.ConfigBackup.cs'
[xml]$project = Read-Text 'src/Eizo.App/Eizo.App.csproj'
[xml]$manifest = Read-Text 'src/Eizo.App/Package.appxmanifest'

$projectVersion = @($project.Project.PropertyGroup | ForEach-Object Version | Where-Object { $_ })[0]
$assemblyVersion = @($project.Project.PropertyGroup | ForEach-Object AssemblyVersion | Where-Object { $_ })[0]
if ($projectVersion -ne $version -or
    $assemblyVersion -ne $packageVersion -or
    [string]$manifest.Package.Identity.Version -ne $packageVersion) {
    throw 'Eikura 1.3.5 version contract is inconsistent.'
}

foreach ($required in @(
    'AES-256-GCM',
    'PBKDF2-SHA256',
    'KdfIterations = 210_000',
    '.eikura-config-backup-v1.json',
    'HttpMethod.Put',
    'HttpMethod.Get',
    'MediaCredentialStore.Default',
    'GetTmdbReadAccessToken',
    'SaveTmdbReadAccessToken',
    'destination.Id',
    'CryptographicOperations.ZeroMemory')) {
    if (-not $service.Contains($required)) {
        throw "Configuration backup service is missing required contract marker: $required"
    }
}

foreach ($required in @(
    'Configuration backup and restore',
    'WebDAV backup location',
    'Backup password',
    'Back up now',
    'Restore backup',
    'ConfigBackupService',
    'ContentDialog',
    'GeneralSettingsPanel.Children.Insert')) {
    if (-not $settingsUi.Contains($required)) {
        throw "Settings backup card is missing required contract marker: $required"
    }
}

if ($service -match 'PlaybackHistory|ContinueWatching|MediaCatalogStore|CacheStore') {
    throw 'Configuration backup must not absorb playback history, media database, or cache state.'
}

Write-Host "Eikura configuration-backup acceptance PASS: $version / $packageVersion"
