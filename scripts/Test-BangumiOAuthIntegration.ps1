param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$manifest = [IO.File]::ReadAllText((Join-Path $root 'src/Eizo.App/Package.appxmanifest'))
$app = [IO.File]::ReadAllText((Join-Path $root 'src/Eizo.App/App.xaml.cs'))
$store = [IO.File]::ReadAllText((Join-Path $root 'src/Eizo.App/Models/BangumiAccountCredentialStore.cs'))
$service = [IO.File]::ReadAllText((Join-Path $root 'src/Eizo.App/Models/BangumiOAuthService.cs'))
$worker = [IO.File]::ReadAllText((Join-Path $root 'cloudflare/eikura-bangumi-auth/worker.js'))
$wrangler = [IO.File]::ReadAllText((Join-Path $root 'cloudflare/eikura-bangumi-auth/wrangler.jsonc'))
$dialog = [IO.File]::ReadAllText((Join-Path $root 'src/Eizo.App/Views/BangumiAccountDialogService.cs'))
foreach ($item in @(
    @($manifest, '<uap:Protocol Name="eikura">'),
    @($manifest, '<uap:Protocol Name="eizo">'),
    @($app, 'OnInitialActivation'),
    @($app, 'OnRedirectedActivation'),
    @($app, 'IsBangumiAuthCallback'),
    @($store, 'PasswordVault'),
    @($service, 'ConnectOAuthAsync'),
    @($service, 'PrimaryRelayBase'),
    @($service, 'LegacyRelayBase'),
    @($service, 'ResolveRelayAsync'),
    @($worker, "new URL('eikura://bangumi-auth')"),
    @($worker, 'OAUTH_ROLLOUT_ENABLED'),
    @($worker, 'reserveCallback'),
    @($worker, 'storage.transaction'),
    @($wrangler, '"OAUTH_ROLLOUT_ENABLED": "false"')
)) {
    if (-not $item[0].Contains($item[1], [StringComparison]::Ordinal)) {
        throw "Bangumi OAuth integration missing: $($item[1])"
    }
}
foreach ($forbidden in @('Bangumi_ManualLogin', 'ShowManualConnectAsync', 'PasswordBox')) {
    if ($dialog.Contains($forbidden, [StringComparison]::Ordinal)) {
        throw "Legacy manual token login UI remains in OAuth release: $forbidden"
    }
}
& node --test (Join-Path $root 'cloudflare/eikura-bangumi-auth/worker.test.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Bangumi OAuth Worker tests failed.' }
Write-Host 'Eikura 1.3.2 Bangumi OAuth dual-protocol migration PASS.'
