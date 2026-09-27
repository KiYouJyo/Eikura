param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$xamlPath = Join-Path $repoRoot 'src/Eizo.App/MainWindow.xaml'
$xaml = [IO.File]::ReadAllText($xamlPath, [Text.Encoding]::UTF8)

$required = @(
    '<ProgressBar x:Name="StartupProgressBar"',
    'Width="72"',
    'Height="2"',
    'IsIndeterminate="True"',
    'Foreground="{ThemeResource TextFillColorSecondaryBrush}"',
    'Background="Transparent"',
    'HorizontalAlignment="Center"'
)

foreach ($fragment in $required) {
    if (-not $xaml.Contains($fragment, [StringComparison]::Ordinal)) {
        throw "Eizo 1.3.1 startup loading contract is missing: $fragment"
    }
}

if ($xaml.Contains('<ProgressRing x:Name="StartupProgressRing"', [StringComparison]::Ordinal)) {
    throw 'Legacy startup ProgressRing is still present.'
}

if ($xaml.Contains('Foreground="#0067C0"', [StringComparison]::Ordinal)) {
    throw 'Legacy fixed blue startup foreground is still present.'
}

Write-Host 'Eizo 1.3.1 startup ProgressBar contract PASS.'
