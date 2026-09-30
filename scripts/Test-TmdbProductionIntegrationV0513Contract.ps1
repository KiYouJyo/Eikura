param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Read-Text([string]$relativePath) {
    $path = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required 0.5.13 TMDB integration file is missing: $relativePath"
    }
    return [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
}

$service = Read-Text 'src/Eizo.MetadataIntegration/MediaMetadataService.cs'
$coordinator = Read-Text 'src/Eizo.App/Models/MediaScanCoordinator.cs'
$credentialStore = Read-Text 'src/Eizo.App/Models/MediaCredentialStore.cs'
$verifier = Read-Text 'src/Eizo.App/Models/TmdbConnectionVerifier.cs'
$settingsXaml = Read-Text 'src/Eizo.App/Views/SettingsView.xaml'
$settings = Read-Text 'src/Eizo.App/Views/SettingsView.xaml.cs'
$matchDialog = Read-Text 'src/Eizo.App/Views/MetadataMatchDialog.cs'
$aboutXaml = Read-Text 'src/Eizo.App/Views/AboutView.xaml'
$about = Read-Text 'src/Eizo.App/Views/AboutView.xaml.cs'
$project = Read-Text 'src/Eizo.App/Eizo.App.csproj'
$manifest = Read-Text 'src/Eizo.App/Package.appxmanifest'
$release = Read-Text 'release/release.json'

foreach ($required in @(
    'Eizo.Metadata:TMDB',
    'GetTmdbReadAccessToken()',
    'SaveTmdbReadAccessToken(',
    'RemoveTmdbReadAccessToken()',
    'PasswordVault')) {
    if (-not $credentialStore.Contains($required, [StringComparison]::Ordinal)) {
        throw "TMDB PasswordVault integration is incomplete: $required"
    }
}

foreach ($required in @(
    'EIZO_TMDB_READ_ACCESS_TOKEN',
    'TmdbConfigurationSource',
    'ResolveTmdbReadAccessToken()',
    'MediaCredentialStore.Default',
    'ReloadMetadataService()')) {
    if (-not $coordinator.Contains($required, [StringComparison]::Ordinal)) {
        throw "TMDB production activation is incomplete: $required"
    }
}

foreach ($required in @(
    'AvailableProviders',
    'IsProviderAvailable(',
    'ResolveConfiguredBoundPrimary(',
    'ManualIdentityBinding',
    'PersistedIdentityBinding',
    'return Array.Empty<',
    'MediaMetadataMatchCandidate')) {
    if (-not $service.Contains($required, [StringComparison]::Ordinal)) {
        throw "TMDB provider routing/identity integration is incomplete: $required"
    }
}

foreach ($required in @(
    'https://api.themoviedb.org/3/configuration',
    'AuthenticationHeaderValue(',
    '"Bearer"',
    'KiYouJyo/Eikura/1.3.2')) {
    if (-not $verifier.Contains($required, [StringComparison]::Ordinal)) {
        throw "TMDB connection verification is incomplete: $required"
    }
}

foreach ($required in @(
    'x:Name="TmdbTokenBox"',
    'x:Name="TmdbGetTokenButton"',
    'x:Name="TmdbSaveTokenButton"',
    'x:Name="TmdbVerifyTokenButton"',
    'x:Name="TmdbRemoveTokenButton"')) {
    if (-not $settingsXaml.Contains($required, [StringComparison]::Ordinal)) {
        throw "TMDB settings UI is incomplete: $required"
    }
}

foreach ($required in @(
    'TmdbSaveTokenButton_Click',
    'TmdbVerifyTokenButton_Click',
    'TmdbRemoveTokenButton_Click',
    'TmdbGetTokenButton_Click',
    'https://www.themoviedb.org/settings/api')) {
    if (-not $settings.Contains($required, [StringComparison]::Ordinal)) {
        throw "TMDB settings behavior is incomplete: $required"
    }
}

foreach ($required in @(
    'IsTmdbConfigured',
    'SearchMetadataMatchesAsync(',
    'provider: "tmdb"')) {
    if (-not $matchDialog.Contains($required, [StringComparison]::Ordinal)) {
        throw "TMDB manual matching integration is incomplete: $required"
    }
}

# v0.5.13 introduced TMDB-aware manual matching. It does not require
# later releases to retain the original multi-provider selector or label.

$notice = 'This product uses the TMDB API but is not endorsed or certified by TMDB.'
if (-not $about.Contains($notice, [StringComparison]::Ordinal)) {
    throw 'Required TMDB attribution notice is missing from About.'
}
if (-not $aboutXaml.Contains('TmdbAttributionNotice', [StringComparison]::Ordinal) -or
    -not $aboutXaml.Contains('blue_square_1-5bdc75aaebeb75dc7ae79426ddd9be3b2be1e342510f8202baf6bffa71d7f5c4.svg', [StringComparison]::Ordinal) -or
    -not $about.Contains('https://www.themoviedb.org', [StringComparison]::Ordinal)) {
    throw 'TMDB About attribution/logo/link is incomplete.'
}

# Product version is validated separately by Test-ReleaseVersionContract.ps1.
# Keep this historical contract focused on the production TMDB behavior
# introduced in 0.5.13 so later releases can reuse it as a regression test.

Write-Host 'Eizo 0.5.13 TMDB production integration contract PASS.'
