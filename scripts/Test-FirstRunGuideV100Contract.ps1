param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Read-Text([string]$relativePath) {
    $path = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required first-run guide file is missing: $relativePath"
    }
    return [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
}

$xaml = Read-Text 'src/Eizo.App/Views/FirstRunGuideHost.xaml'
$code = Read-Text 'src/Eizo.App/Views/FirstRunGuideHost.xaml.cs'
$restoreXaml = Read-Text 'src/Eizo.App/Views/FirstRunWebDavRestoreView.xaml'
$restoreCode = Read-Text 'src/Eizo.App/Views/FirstRunWebDavRestoreView.xaml.cs'
$state = Read-Text 'src/Eizo.App/Models/FirstRunGuideState.cs'
$service = Read-Text 'src/Eizo.App/Models/FirstRunExperienceService.cs'
$window = Read-Text 'src/Eizo.App/MainWindow.xaml'
$startup = Read-Text 'src/Eizo.App/MainWindow.Startup.cs'

foreach ($name in @(
    'WelcomeStep',
    'RestoreStep',
    'SourcesStep',
    'TmdbStep',
    'BangumiStep',
    'PlaybackStep',
    'CompleteStep')) {
    if (-not $xaml.Contains("x:Name=`"$name`"")) {
        throw "First-run guide is missing step surface: $name"
    }
}

if (-not $xaml.Contains('x:Name="SourceSetupHost"') -or
    -not $code.Contains('SourceSetupHost.Content = new SourcesView()') -or
    -not $code.Contains('MediaSourceStore.Default')) {
    throw 'First-run media-source setup must lazily reuse the production SourcesView/store.'
}

foreach ($required in @(
    'MediaCredentialStore.Default',
    'TmdbConnectionVerifier.CheckAsync',
    'ReloadMetadataService',
    'BangumiOAuthService.Default.StartAsync',
    'BangumiAccountService',
    'AppSettingsStore.Update',
    'PreferredSecondarySubtitleLanguage',
    'FullscreenControlsTimeoutSeconds',
    'StartInitialScansAsync')) {
    if (-not $code.Contains($required)) {
        throw "First-run guide integration is missing: $required"
    }
}

if (-not $service.Contains('CurrentVersion = 1') -or
    -not $service.Contains('CurrentSchemaVersion = 2') -or
    -not $service.Contains('LastStepIndex = 6') -or
    -not $service.Contains('CompletedGuideVersion') -or
    -not $service.Contains('GetResumeStep') -or
    -not $service.Contains('RecordStep') -or
    -not $service.Contains('loaded.LastStep++') -or
    -not $state.Contains('CompletedGuideVersion') -or
    -not $state.Contains('LastStep')) {
    throw 'First-run guide lifecycle/resume migration contract is missing.'
}

$settingsXaml = Read-Text 'src/Eizo.App/Views/SettingsView.xaml'
$settingsCode = Read-Text 'src/Eizo.App/Views/SettingsView.xaml.cs'
$onboardingWindow = Read-Text 'src/Eizo.App/MainWindow.Onboarding.cs'

if (-not $window.Contains('FirstRunGuideHost') -or
    -not $startup.Contains('ShouldShowAutomatically') -or
    -not $startup.Contains('FirstRunGuideHost.Show()')) {
    throw 'MainWindow startup does not host/launch the first-run guide.'
}

if (-not $settingsXaml.Contains('ReopenFirstRunGuideButton') -or
    -not $settingsCode.Contains('ShowFirstRunGuideFromSettings') -or
    -not $onboardingWindow.Contains('ShowFromStart')) {
    throw 'Settings must expose a non-destructive first-run guide restart action.'
}

if (-not $code.Contains('VirtualKey.Escape') -or
    -not $code.Contains('TryMarkCompleted')) {
    throw 'First-run interruption/completion semantics are missing.'
}

foreach ($stepLabel in 0..6) {
    if (-not $xaml.Contains("x:Name=`"StepLabel$stepLabel`"")) {
        throw "First-run stepper is missing evenly distributed label StepLabel$stepLabel."
    }
}
if (-not $xaml.Contains('ColumnDefinitions="*,*,*,*,*,*,*"') -or
    -not $xaml.Contains('Maximum="7"') -or
    -not $code.Contains('BackButton.Content = L("上一步"') -or
    -not $code.Contains('BackButton.Visibility = _step > 0') -or
    -not $xaml.Contains('x:Name="SkipButton"') -or
    -not $xaml.Contains('HorizontalAlignment="Left"')) {
    throw 'First-run seven-step stepper/footer alignment contract is missing.'
}

# The configuration-backup WebDAV endpoint is deliberately a different concept
# from a playback WebDAV media source. The first-run restore surface may create a
# temporary transport adapter for ConfigBackupService, but it must never add that
# endpoint to MediaSourceStore and must remove its transient credential afterward.
if (-not $xaml.Contains('FirstRunWebDavRestoreView') -or
    -not $restoreXaml.Contains('x:Name="WebDavUrlBox"') -or
    -not $restoreXaml.Contains('x:Name="WebDavPasswordBox"') -or
    $restoreXaml.Contains('BackupPassphraseBox') -or
    -not $restoreCode.Contains('ConfigBackupService') -or
    -not $restoreCode.Contains('config-backup-{Guid.NewGuid():N}') -or
    -not $restoreCode.Contains('_credentials.RemoveWebDav(endpoint.Id)') -or
    $restoreCode.Contains('MediaSourceStore.Default.AddWebDav') -or
    $restoreCode.Contains('MediaSourceStore.Default.AddLocalFolder')) {
    throw 'Configuration-backup WebDAV must remain separate from playback media sources.'
}

if (-not $code.Contains('RestoreStep.Visibility = _step == 1') -or
    -not $code.Contains('SourcesStep.Visibility = _step == 2') -or
    -not $code.Contains('FinalStepIndex = 6') -or
    -not $code.Contains('配置备份仓库，不是播放用媒体来源')) {
    throw 'First-run restore step ordering or backup/media-source distinction is missing.'
}

$sourcesXaml = Read-Text 'src/Eizo.App/Views/SourcesView.xaml'
if (-not $sourcesXaml.Contains('ColumnDefinitions="*,260,Auto"') -or
    -not $sourcesXaml.Contains('Width="128"') -or
    -not $sourcesXaml.Contains('Spacing="8"')) {
    throw 'Media source card actions must be compact, grouped, and centered.'
}

Write-Host 'Eikura first-run guide contract PASS.'
Write-Host 'Seven steps: Welcome / Restore / Sources / TMDB / Bangumi / Playback / Finish.'
Write-Host 'Configuration-backup WebDAV is isolated from playback media sources.'
