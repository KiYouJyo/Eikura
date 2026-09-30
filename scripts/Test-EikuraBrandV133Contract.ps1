param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Read-Text([string]$relativePath) {
    $path = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required Eikura 1.3.3 file is missing: $relativePath" }
    return [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
}

$release = Read-Text 'release/release.json' | ConvertFrom-Json
& (Join-Path $PSScriptRoot 'Test-ReleaseVersionContract.ps1')
if ([Version]$release.product.version -lt [Version]'1.3.3') {
    throw 'The infinite-scroll contract requires Eikura 1.3.3 or later.'
}

& (Join-Path $PSScriptRoot 'Test-EikuraBrandV132Contract.ps1')
& (Join-Path $PSScriptRoot 'Test-EikuraUpgradeCompatibilityV132Contract.ps1')

$mainWindow = Read-Text 'src/Eizo.App/MainWindow.xaml'
if (-not $mainWindow.Contains('Text="Eikura  映蔵"', [StringComparison]::Ordinal)) {
    throw 'Eikura 1.3.3 must ship the Eikura  映蔵 shell wordmark.'
}

$publicXaml = Read-Text 'src/Eizo.App/Views/BangumiPublicView.xaml'
$publicCode = Read-Text 'src/Eizo.App/Views/BangumiPublicView.InfiniteScroll.cs'
foreach ($marker in @(
    'x:Name="PageScrollViewer"',
    'ViewChanged="PageScrollViewer_ViewChanged"',
    'ScrollViewer.VerticalScrollMode="Disabled"',
    'x:Name="LoadMoreButton"',
    'Height="0"')) {
    if (-not $publicXaml.Contains($marker, [StringComparison]::Ordinal)) {
        throw "Rank/discover infinite-scroll surface is missing: $marker"
    }
}
foreach ($marker in @(
    'preloadDistance = 520d',
    'LoadMoreButton.Visibility != Visibility.Visible',
    'append: true')) {
    if (-not $publicCode.Contains($marker, [StringComparison]::Ordinal)) {
        throw "Rank/discover infinite-scroll behavior is missing: $marker"
    }
}
if ($publicXaml -match 'MinWidth="132"|HorizontalAlignment="Center"\s*\r?\n\s*MinWidth="132"') {
    throw 'The visible rank/discover Load More button must not return.'
}

$animeXaml = Read-Text 'src/Eizo.App/Views/BangumiAnimeIndexView.xaml'
if ($animeXaml -notmatch 'Grid.Row="2"[\s\S]*ProgressRing[\s\S]*ElementName=LoadingRing' -or
    $animeXaml -notmatch 'Text="\{Binding Text, ElementName=StatusText\}"') {
    throw 'Anime index bottom loading indicator is missing.'
}

Write-Host 'Eikura 1.3.3 infinite-scroll and shell-wordmark contract PASS.'
