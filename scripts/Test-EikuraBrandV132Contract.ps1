param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Read-Text([string]$relativePath) {
    $path = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required Eikura 1.3.2 file is missing: $relativePath" }
    return [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
}

[xml]$project = Read-Text 'src/Eizo.App/Eizo.App.csproj'
[xml]$manifest = Read-Text 'src/Eizo.App/Package.appxmanifest'
$version = [string](@($project.Project.PropertyGroup | ForEach-Object Version | Where-Object { $_ })[0])
$packageVersion = [string]$manifest.Package.Identity.Version
if ($version -cne '1.3.2' -or $packageVersion -cne '1.3.2.0') { throw "Eikura 1.3.2 version mismatch: $version / $packageVersion" }

if ([string]$manifest.Package.Properties.DisplayName -cne 'Eikura' -or [string]$manifest.Package.Applications.Application.VisualElements.DisplayName -cne 'Eikura') { throw 'MSIX user-facing display name must be Eikura.' }
if ([string]$manifest.Package.Identity.Name -cne 'Eizo') { throw 'Legacy GitHub MSIX identity must be retained for in-place upgrade compatibility.' }
$protocol = @($manifest.Package.Applications.Application.Extensions.Extension.Protocol | ForEach-Object Name)
if ($protocol -notcontains 'eikura') { throw 'Primary eikura:// protocol registration is missing in 1.3.2.' }
if ($protocol -notcontains 'eizo') { throw 'Legacy eizo:// protocol registration must remain available in 1.3.2.' }

$resourceFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/Eizo.App/Strings') -Recurse -File -Filter '*.resw'
foreach ($resource in $resourceFiles) {
    [xml]$xml = [IO.File]::ReadAllText($resource.FullName, [Text.Encoding]::UTF8)
    foreach ($node in @($xml.root.data)) {
        $value = [string]$node.value
        if ($value -match '(?i)\bEizo\b|映藏|映蔵') {
            $relative = [IO.Path]::GetRelativePath($repoRoot, $resource.FullName)
            throw "Legacy product branding remains in localized UI: $relative / $($node.name)"
        }
    }
}

$currentSurfaces = @('README.md','README.en-US.md','README.ja-JP.md','docs/index.html','docs/privacy/index.html','docs/support/index.html','docs/BRANDING.md','packaging/请先阅读.txt')
foreach ($relativePath in $currentSurfaces) {
    $text = Read-Text $relativePath
    if ($text -match '(?i)\bEizo\b|映藏|映蔵') { throw "Legacy user-facing branding remains in current surface: $relativePath" }
    if ($text -notmatch 'Eikura') { throw "Eikura brand is missing from current surface: $relativePath" }
}

$firstRun = Read-Text 'src/Eizo.App/Views/FirstRunGuideHost.xaml.cs'
if ($firstRun -match '"[^"\r\n]*(?:Eizo|映藏|映蔵)[^"\r\n]*"') {
    throw 'Legacy product branding remains in first-run user-facing strings.'
}

$catalog = Read-Text 'src/Eizo.App/Views/CatalogView.xaml.cs'
$catalogBrandSurface = $catalog.Replace('EizoItem', 'LegacyItem').Replace('EizoSubject', 'LegacySubject')
if ($catalogBrandSurface -match '"[^"\r\n]*(?:Eizo|映藏|映蔵)[^"\r\n]*"') {
    throw 'Legacy product branding remains in recognition-export user-facing strings outside the preserved CSV schema.'
}

$mainWindow = Read-Text 'src/Eizo.App/MainWindow.xaml'
if ($mainWindow -notmatch 'Text="Eikura"' -or $mainWindow -match 'Text="[^"]*(?:Eizo|映藏|映蔵)') { throw 'Main window brand marker is not fully migrated to Eikura.' }
$about = Read-Text 'src/Eizo.App/Views/AboutView.xaml'
if ($about -notmatch 'Text="Eikura"' -or $about -match 'Text="[^"]*(?:Eizo|映藏|映蔵)') { throw 'About surface brand marker is not fully migrated to Eikura.' }

$release = Read-Text 'release/release.json' | ConvertFrom-Json
if ([string]$release.product.version -cne '1.3.2' -or [string]$release.product.packageVersion -cne '1.3.2.0') { throw 'release/release.json is not pinned to Eikura 1.3.2.' }
Write-Host 'Eikura 1.3.2 brand migration contract PASS.'