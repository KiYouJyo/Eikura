param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Read-Text([string]$relativePath) {
    $path = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required Eikura file is missing: $relativePath" }
    return [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
}

$release = Read-Text 'release/release.json' | ConvertFrom-Json
$version = [string]$release.product.version
$packageVersion = [string]$release.product.packageVersion
[xml]$project = Read-Text 'src/Eizo.App/Eizo.App.csproj'
[xml]$manifest = Read-Text 'src/Eizo.App/Package.appxmanifest'
$projectVersion = [string](@($project.Project.PropertyGroup | ForEach-Object Version | Where-Object { $_ })[0])
if ($projectVersion -cne $version -or [string]$manifest.Package.Identity.Version -cne $packageVersion) {
    throw "Eikura version mismatch: $projectVersion / $($manifest.Package.Identity.Version) expected $version / $packageVersion"
}

if ([string]$manifest.Package.Properties.DisplayName -cne 'Eikura' -or [string]$manifest.Package.Applications.Application.VisualElements.DisplayName -cne 'Eikura') { throw 'MSIX user-facing display name must be Eikura.' }
if ([string]$manifest.Package.Identity.Name -cne 'Eizo') { throw 'Legacy GitHub MSIX identity must be retained for in-place upgrade compatibility.' }
$protocol = @($manifest.Package.Applications.Application.Extensions.Extension.Protocol | ForEach-Object Name)
if ($protocol -notcontains 'eikura' -or $protocol -notcontains 'eizo') { throw 'Both eikura:// and legacy eizo:// Bangumi activation protocols must remain registered.' }

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

$currentSurfaces = @('README.md','README.en-US.md','README.ja-JP.md','docs/index.html','docs/privacy/index.html','docs/support/index.html','packaging/请先阅读.txt')
foreach ($relativePath in $currentSurfaces) {
    $text = Read-Text $relativePath
    if ($text -match '(?i)\bEizo\b|映藏|映蔵') { throw "Legacy user-facing branding remains in current surface: $relativePath" }
    if ($text -notmatch 'Eikura') { throw "Eikura brand is missing from current surface: $relativePath" }
}

$branding = Read-Text 'docs/BRANDING.md'
if ($branding -match '(?i)\bEizo\b|映藏') { throw 'Legacy Eizo/映藏 branding remains in branding guidance.' }
if ($branding -notmatch 'Eikura  映蔵') { throw 'Branding guidance must document the Eikura  映蔵 shell wordmark exception.' }

$mainWindow = Read-Text 'src/Eizo.App/MainWindow.xaml'
if ($mainWindow -notmatch 'Text="Eikura  映蔵"' -or $mainWindow -match 'Text="[^"]*(?:Eizo|映藏)') { throw 'Main window shell wordmark must be exactly Eikura  映蔵.' }
$about = Read-Text 'src/Eizo.App/Views/AboutView.xaml'
if ($about -notmatch 'Text="Eikura"' -or $about -match 'Text="[^"]*(?:Eizo|映藏|映蔵)') { throw 'About surface brand marker must remain Eikura.' }

Write-Host "Eikura brand compatibility contract PASS: $version / $packageVersion"
