param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$version = '1.3.4'
$packageVersion = '1.3.4.0'
& (Join-Path $PSScriptRoot 'Test-ReleaseVersionContract.ps1')
& (Join-Path $PSScriptRoot 'Test-EikuraBrandV132Contract.ps1')
& (Join-Path $PSScriptRoot 'Test-EikuraUpgradeCompatibilityV132Contract.ps1')
$view = [IO.File]::ReadAllText((Join-Path $repoRoot 'src/Eizo.App/Views/AboutView.xaml'))
$code = [IO.File]::ReadAllText((Join-Path $repoRoot 'src/Eizo.App/Views/AboutView.xaml.cs'))
$session = [IO.File]::ReadAllText((Join-Path $repoRoot 'src/Eizo.App/AboutUpdateSessionState.cs'))
$service = [IO.File]::ReadAllText((Join-Path $repoRoot 'src/Eizo.App/ProductAppUpdateService.cs'))
$identity = Get-Content (Join-Path $repoRoot 'release/MicrosoftStore/store-identity.json') -Raw | ConvertFrom-Json
if ($view -match 'Recognition|Text="Metadata"|ComponentsTitle|CheckPlaybackUpdate' -or $session -match 'ComponentUpdateService') {
    throw 'About update management must expose only the product updater.'
}
if (-not $service.Contains([string]$identity.packageFamilyName)) { throw 'Store updater identity differs from packaging identity.' }
foreach ($required in @('GetAppAndOptionalStorePackageUpdatesAsync', 'RequestDownloadAndInstallStorePackageUpdatesAsync', 'InitializeWithWindow.Initialize', 'DispatcherQueue.HasThreadAccess', 'StorePackageUpdateState.Canceled')) {
    if (-not $service.Contains($required)) { throw "Store updater contract missing: $required" }
}
if ($service -match 'newest|update.Package.Id.Version') { throw 'Do not display installed Store package version as available version.' }
if (($service | Select-String -Pattern 'if \(!IsGitHubBuild\)' -AllMatches).Matches.Count -ne 3) { throw 'Every GitHub update operation must reject other package identities.' }
if (-not $code.Contains('UpdateSourceText.Text = store ? "Microsoft Store" : "GitHub Releases"') -or -not $code.Contains('StoreUri : ReleasesUri')) {
    throw 'About page must route Store update information to the Store.'
}
Write-Host "Eikura dual-channel acceptance PASS: $version / $packageVersion"
