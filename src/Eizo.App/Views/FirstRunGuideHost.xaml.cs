using Eizo.Localization;
using Eizo.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Eizo.Views;

public sealed partial class FirstRunGuideHost : UserControl
{
    private const double PreferredWidth = 980;
    private const double PreferredHeight = 720;
    private const double MinimumWidth = 640;
    private const double MinimumHeight = 560;
    private static readonly int[] TimeoutValues = [2, 4, 6, 10];

    private readonly AppLocalizationService _localization = AppLocalizationService.Default;
    private readonly FirstRunExperienceService _state = FirstRunExperienceService.Default;
    private readonly BangumiAccountService _bangumi = BangumiAccountService.Default;
    private int _step;
    private bool _isBusy;
    private bool _syncPlayback;

    public FirstRunGuideHost()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
        _bangumi.Changed += Bangumi_Changed;
        _localization.LanguageChanged += Localization_LanguageChanged;
        Unloaded += OnUnloaded;
        ApplyStaticText();
    }

    public void Show() => ShowCore(resume: true);

    public void ShowFromStart() => ShowCore(resume: false);

    private void ShowCore(bool resume)
    {
        if (Visibility == Visibility.Visible) return;
        _step = resume ? _state.GetResumeStep() : 0;
        _isBusy = false;
        ApplyStaticText();
        SyncPlaybackControls();
        RefreshStep();
        Visibility = Visibility.Visible;
        UpdateGuideSize();
        DispatcherQueue.TryEnqueue(() => NextButton.Focus(FocusState.Programmatic));
    }

    private string L(string zh, string ja, string en) =>
        _localization.CurrentLanguage switch
        {
            "ja-JP" => ja,
            "en-US" => en,
            _ => zh
        };

    private void ApplyStaticText()
    {
        WelcomeVersionText.Text = $"Eikura {AppVersionProvider.Version}";
        WelcomeLibraryTitle.Text = L("媒体库", "メディアライブラリ", "Media library");
        WelcomeLibraryBody.Text = L(
            "自动整理电影、电视剧与动漫，并聚合系列、季度与剧集。",
            "映画・ドラマ・アニメを整理し、シリーズ・シーズン・エピソードをまとめます。",
            "Organize movies, series, and anime with title, season, and episode aggregation.");
        WelcomePlayerTitle.Text = L("播放器", "プレーヤー", "Player");
        WelcomePlayerBody.Text = L(
            "直接播放本地与 WebDAV 媒体，支持双字幕、音轨、队列与缓存。",
            "ローカル / WebDAV を直接再生し、二重字幕、音声、キュー、キャッシュに対応します。",
            "Play local and WebDAV media with dual subtitles, audio tracks, queues, and caching.");
        WelcomeOnlineTitle.Text = L("在线服务", "オンラインサービス", "Online services");
        WelcomeOnlineBody.Text = L(
            "TMDB 提供影视元数据；Bangumi 提供追番、收藏与社区内容。",
            "TMDB の映像メタデータと Bangumi の視聴管理・コレクション・コミュニティを利用できます。",
            "TMDB supplies metadata while Bangumi adds tracking, collections, and community content.");

        TmdbCardTitle.Text = "TMDB";
        TmdbCardBody.Text = L(
            "获取海报、背景图、简介、演职人员以及季度和剧集信息。当前使用 TMDB Read Access Token，并安全保存在 Windows PasswordVault。",
            "ポスター、背景、あらすじ、キャスト、シーズン、エピソード情報を取得します。TMDB Read Access Token は Windows PasswordVault に保存します。",
            "Fetch posters, backdrops, summaries, credits, seasons, and episodes. The TMDB Read Access Token is stored in Windows PasswordVault.");
        TmdbGetTokenButton.Content = L("获取 Token", "Token を取得", "Get token");
        TmdbConfigureButton.Content = L("保存并验证", "保存して確認", "Save & verify");
        TmdbInfoBar.Title = L("可稍后设置", "後で設定できます", "Optional for now");
        TmdbInfoBar.Message = L(
            "跳过不会影响本地播放；未配置 TMDB 时在线影视元数据能力会受到限制。",
            "スキップしてもローカル再生には影響しません。TMDB 未設定時はオンラインメタデータ機能が制限されます。",
            "Skipping does not affect local playback, but online movie and TV metadata will be limited.");

        BangumiCardTitle.Text = "Bangumi";
        BangumiCardBody.Text = L(
            "登录后可读取追番、收藏与社区内容。登录通过浏览器 OAuth 完成，Eikura 不保存 Bangumi 密码。",
            "ログインすると視聴中、コレクション、コミュニティ情報を利用できます。ブラウザー OAuth を使用し、Eikura は Bangumi のパスワードを保存しません。",
            "Sign in for tracking, collections, and community content. Browser OAuth is used and Eikura never stores your Bangumi password.");
        BangumiInfoBar.Title = L("可选账户", "任意のアカウント", "Optional account");
        BangumiInfoBar.Message = L(
            "不登录 Bangumi 也可以正常使用媒体库和播放器。",
            "Bangumi にログインしなくてもメディアライブラリとプレーヤーは利用できます。",
            "The media library and player work normally without a Bangumi account.");

        AudioLanguageTitle.Text = L("首选音轨", "優先音声", "Preferred audio");
        AudioLanguageBody.Text = L("播放时优先选择的音轨语言。", "再生時に優先する音声言語です。", "The audio language Eikura should prefer.");
        PrimarySubtitleTitle.Text = L("首选字幕", "優先字幕", "Preferred subtitles");
        PrimarySubtitleBody.Text = L("优先作为主字幕显示的语言。", "メイン字幕として優先する言語です。", "The language preferred for primary subtitles.");
        SecondarySubtitleTitle.Text = L("第二字幕", "第2字幕", "Secondary subtitles");
        SecondarySubtitleBody.Text = L("双字幕模式下优先作为第二字幕显示的语言。", "二重字幕で第2字幕として優先する言語です。", "The language preferred for the second subtitle track.");
        ControlsTimeoutTitle.Text = L("全屏控制区驻留时间", "全画面コントロール表示時間", "Fullscreen controls timeout");
        ControlsTimeoutBody.Text = L("鼠标停止移动后，控制区继续显示多久。", "ポインター操作後にコントロールを表示し続ける時間です。", "How long controls remain visible after pointer activity.");

        CompleteSummaryTitle.Text = L("配置摘要", "設定概要", "Configuration summary");
        ScanAfterFinishCheckBox.Content = L(
            "完成后扫描已添加的媒体来源",
            "完了後、追加済みメディアソースをスキャン",
            "Scan added media sources after setup");
    }

    private void RefreshStep()
    {
        var labels = new[]
        {
            L("欢迎", "ようこそ", "Welcome"),
            L("媒体库", "ライブラリ", "Library"),
            "TMDB",
            "Bangumi",
            L("播放", "再生", "Playback"),
            L("完成", "完了", "Finish")
        };
        var stepLabels = new[]
        {
            StepLabel0,
            StepLabel1,
            StepLabel2,
            StepLabel3,
            StepLabel4,
            StepLabel5
        };
        for (var index = 0; index < stepLabels.Length; index++)
        {
            stepLabels[index].Text = index == _step
                ? $"【{labels[index]}】"
                : labels[index];
            stepLabels[index].Opacity = index == _step
                ? 1.0
                : index < _step
                    ? 0.78
                    : 0.58;
        }
        StepProgress.Value = _step + 1;

        (GuideTitle.Text, GuideBody.Text) = _step switch
        {
            0 => (
                L("欢迎使用 Eikura", "Eikura へようこそ", "Welcome to Eikura"),
                L("为本地与网络影音资源打造的现代 Windows 媒体库。用几步完成必要设置即可开始使用。",
                  "ローカルとネットワークの映像をまとめるモダンな Windows メディアライブラリです。",
                  "A modern Windows media library for local and network media. A few quick steps will get you ready.")),
            1 => (
                L("建立你的媒体库", "メディアライブラリを作成", "Build your media library"),
                L("添加电脑或外接硬盘文件夹，或连接 WebDAV。添加后不会自动扫描，最后一步可以统一开始扫描。",
                  "PC や外付けドライブのフォルダー、または WebDAV を追加します。最後にまとめてスキャンできます。",
                  "Add folders from this PC or external drives, or connect WebDAV. You can scan them together at the end.")),
            2 => (
                L("配置 TMDB", "TMDB を設定", "Configure TMDB"),
                L("让 Eikura 获取完整的电影、电视剧、季、集、图片与演职人员信息。",
                  "映画、ドラマ、シーズン、エピソード、画像、キャスト情報を取得できるようにします。",
                  "Enable complete movie, TV, season, episode, artwork, and credits metadata.")),
            3 => (
                L("连接 Bangumi", "Bangumi に接続", "Connect Bangumi"),
                L("通过浏览器登录 Bangumi。完成授权后会自动返回 Eikura。",
                  "ブラウザーで Bangumi にログインし、認証後は自動的に Eikura へ戻ります。",
                  "Sign in to Bangumi in your browser. Eikura resumes automatically after authorization.")),
            4 => (
                L("个性化播放", "再生をカスタマイズ", "Personalize playback"),
                L("只设置最影响第一次播放体验的几项偏好，其他选项以后可以在设置中调整。",
                  "初回再生に影響する基本項目だけを設定します。その他は後から変更できます。",
                  "Set only the preferences that matter most for first playback. Everything else remains in Settings.")),
            _ => (
                L("一切准备就绪", "準備ができました", "You're ready"),
                L("检查配置状态，然后开始使用 Eikura。", "設定内容を確認して Eikura を始めましょう。", "Review your setup, then start using Eikura."))
        };

        WelcomeStep.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
        SourcesStep.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        TmdbStep.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        BangumiStep.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;
        PlaybackStep.Visibility = _step == 4 ? Visibility.Visible : Visibility.Collapsed;
        CompleteStep.Visibility = _step == 5 ? Visibility.Visible : Visibility.Collapsed;

        BackButton.Content = L("上一步", "戻る", "Back");
        BackButton.Visibility = _step > 0 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.IsEnabled = _step > 0 && !_isBusy;
        SkipButton.Visibility = _step is >= 1 and <= 4 ? Visibility.Visible : Visibility.Collapsed;
        SkipButton.Content = L("稍后设置", "後で設定", "Set up later");
        NextButton.Content = _step switch
        {
            0 => L("开始设置", "設定を開始", "Start setup"),
            5 => L("开始使用 Eikura", "Eikura を開始", "Start Eikura"),
            _ => L("下一步", "次へ", "Next")
        };
        NextButton.IsEnabled = !_isBusy;

        if (_step == 1) EnsureSourcesViewLoaded();
        if (_step == 2) RefreshTmdbStatus();
        if (_step == 3) _ = RefreshBangumiStatusAsync(false);
        if (_step == 4) SyncPlaybackControls();
        if (_step == 5) RefreshSummary();
        GuideScrollViewer.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void EnsureSourcesViewLoaded()
    {
        if (SourceSetupHost.Content is null)
            SourceSetupHost.Content = new SourcesView();
    }

    private void SyncPlaybackControls()
    {
        if (AudioLanguageCombo is null) return;
        _syncPlayback = true;
        try
        {
            var languageItems = new[] { L("自动", "自動", "Auto"), "日本語", "简体中文", "English" };
            AudioLanguageCombo.ItemsSource = languageItems;
            PrimarySubtitleCombo.ItemsSource = languageItems.ToArray();
            SecondarySubtitleCombo.ItemsSource = languageItems.ToArray();
            ControlsTimeoutCombo.ItemsSource = TimeoutValues
                .Select(seconds => L($"{seconds} 秒", $"{seconds} 秒", $"{seconds} sec"))
                .ToArray();

            var settings = AppSettingsStore.Current;
            AudioLanguageCombo.SelectedIndex = LanguagePreferenceIndex(settings.PreferredAudioLanguage);
            PrimarySubtitleCombo.SelectedIndex = LanguagePreferenceIndex(settings.PreferredSubtitleLanguage);
            SecondarySubtitleCombo.SelectedIndex = LanguagePreferenceIndex(settings.PreferredSecondarySubtitleLanguage);
            var timeoutIndex = Array.IndexOf(
                TimeoutValues,
                AppSettingsStore.NormalizeFullscreenControlsTimeout(settings.FullscreenControlsTimeoutSeconds));
            ControlsTimeoutCombo.SelectedIndex = timeoutIndex >= 0 ? timeoutIndex : 1;
        }
        finally { _syncPlayback = false; }
    }

    private void OnPlaybackPreferenceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncPlayback ||
            AudioLanguageCombo.SelectedIndex < 0 ||
            PrimarySubtitleCombo.SelectedIndex < 0 ||
            SecondarySubtitleCombo.SelectedIndex < 0 ||
            ControlsTimeoutCombo.SelectedIndex < 0) return;

        AppSettingsStore.Update(settings => settings with
        {
            PreferredAudioLanguage = LanguagePreferenceCode(AudioLanguageCombo.SelectedIndex),
            PreferredSubtitleLanguage = LanguagePreferenceCode(PrimarySubtitleCombo.SelectedIndex),
            PreferredSecondarySubtitleLanguage = LanguagePreferenceCode(SecondarySubtitleCombo.SelectedIndex),
            FullscreenControlsTimeoutSeconds = TimeoutValues[ControlsTimeoutCombo.SelectedIndex]
        });
    }

    private async void OnGetTmdbToken(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri("https://www.themoviedb.org/settings/api"));

    private async void OnConfigureTmdb(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        var token = string.IsNullOrWhiteSpace(TmdbTokenBox.Password)
            ? MediaCredentialStore.Default.GetTmdbReadAccessToken()
            : TmdbTokenBox.Password.Trim();

        SetBusy(true);
        TmdbProgressRing.IsActive = true;
        TmdbProgressRing.Visibility = Visibility.Visible;
        TmdbStatusText.Text = L("正在验证 TMDB 连接…", "TMDB 接続を確認しています…", "Checking TMDB connection…");
        try
        {
            var result = await TmdbConnectionVerifier.CheckAsync(token);
            if (result.IsSuccess && !string.IsNullOrWhiteSpace(token))
            {
                MediaCredentialStore.Default.SaveTmdbReadAccessToken(token);
                MediaScanCoordinator.Default.ReloadMetadataService();
                TmdbTokenBox.Password = string.Empty;
                TmdbStatusText.Text = L("✓ TMDB 已连接", "✓ TMDB に接続しました", "✓ TMDB connected");
            }
            else
            {
                TmdbStatusText.Text = result.State switch
                {
                    "MissingToken" => L("请输入 TMDB Read Access Token。", "TMDB Read Access Token を入力してください。", "Enter a TMDB Read Access Token."),
                    "Unauthorized" => L("Token 无效或未获授权。", "Token が無効、または権限がありません。", "The token is invalid or unauthorized."),
                    "Timeout" => L("TMDB 连接超时，请稍后重试。", "TMDB 接続がタイムアウトしました。", "TMDB connection timed out."),
                    _ => L("无法连接 TMDB，请检查网络后重试。", "TMDB に接続できません。", "Unable to reach TMDB. Check your network and try again.")
                };
            }
        }
        finally
        {
            TmdbProgressRing.IsActive = false;
            TmdbProgressRing.Visibility = Visibility.Collapsed;
            SetBusy(false);
        }
    }

    private void RefreshTmdbStatus()
    {
        var source = MediaScanCoordinator.Default.TmdbConfigurationSource;
        TmdbStatusText.Text = source switch
        {
            "Environment" => L("✓ TMDB 已通过环境变量启用", "✓ 環境変数で TMDB が有効です", "✓ TMDB is enabled through the environment"),
            "PasswordVault" => L("✓ TMDB 已配置", "✓ TMDB は設定済みです", "✓ TMDB is configured"),
            _ => L("○ 尚未配置", "○ 未設定", "○ Not configured")
        };
    }

    private async void OnBangumiLogin(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _bangumi.IsConnected) return;
        SetBusy(true);
        BangumiStatusText.Text = L("正在打开浏览器…", "ブラウザーを開いています…", "Opening your browser…");
        try
        {
            await BangumiOAuthService.Default.StartAsync();
            BangumiStatusText.Text = L(
                "浏览器已打开。完成授权后会自动返回 Eikura。",
                "ブラウザーで認証を完了すると自動的に Eikura へ戻ります。",
                "Browser opened. Eikura resumes automatically after authorization.");
        }
        catch
        {
            BangumiStatusText.Text = L("无法启动 Bangumi 登录，请稍后重试。", "Bangumi ログインを開始できませんでした。", "Unable to start Bangumi sign-in.");
        }
        finally { SetBusy(false); }
    }

    private async Task RefreshBangumiStatusAsync(bool forceRefresh)
    {
        if (!_bangumi.IsConnected)
        {
            BangumiStatusText.Text = L("○ 尚未登录", "○ 未ログイン", "○ Not signed in");
            BangumiLoginButton.Content = L("登录 Bangumi", "Bangumi にログイン", "Sign in to Bangumi");
            BangumiLoginButton.IsEnabled = true;
            return;
        }

        BangumiLoginButton.IsEnabled = false;
        try
        {
            var profile = await _bangumi.GetProfileAsync(forceRefresh);
            if (profile is null)
            {
                BangumiStatusText.Text = L("○ 尚未登录", "○ 未ログイン", "○ Not signed in");
                BangumiLoginButton.IsEnabled = true;
                return;
            }

            var name = string.IsNullOrWhiteSpace(profile.NickName) ? profile.UserName : profile.NickName;
            BangumiStatusText.Text = $"✓ {name} (@{profile.UserName})";
            BangumiLoginButton.Content = L("已连接", "接続済み", "Connected");
        }
        catch
        {
            BangumiStatusText.Text = L(
                "已保存登录信息，但暂时无法刷新账户资料。",
                "ログイン情報は保存されていますが、プロフィールを更新できません。",
                "Sign-in is saved, but the profile could not be refreshed.");
        }
    }

    private void Bangumi_Changed(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (Visibility == Visibility.Visible) await RefreshBangumiStatusAsync(false);
        });

    private void Localization_LanguageChanged(object? sender, AppLanguageChangedEventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyStaticText();
            SyncPlaybackControls();
            if (Visibility == Visibility.Visible) RefreshStep();
        });

    private void RefreshSummary()
    {
        var sources = MediaSourceStore.Default.Snapshot()
            .Where(source => source.Enabled && !source.IsBuiltIn)
            .ToArray();
        var local = sources.Count(source => source.Kind == MediaSourceKind.Local);
        var webDav = sources.Count(source => source.Kind == MediaSourceKind.WebDav);
        CompleteMediaSummary.Text = sources.Length == 0
            ? L("媒体来源　○ 尚未添加", "メディアソース　○ 未追加", "Media sources  ○ None added")
            : L($"媒体来源　✓ {sources.Length} 个（本地 {local} · WebDAV {webDav}）",
                $"メディアソース　✓ {sources.Length} 件（ローカル {local} · WebDAV {webDav}）",
                $"Media sources  ✓ {sources.Length} ({local} local · {webDav} WebDAV)");

        CompleteTmdbSummary.Text = MediaScanCoordinator.Default.TmdbConfigurationSource == "None"
            ? L("TMDB　　　○ 尚未配置", "TMDB　　　○ 未設定", "TMDB          ○ Not configured")
            : L("TMDB　　　✓ 已连接", "TMDB　　　✓ 接続済み", "TMDB          ✓ Connected");
        CompleteBangumiSummary.Text = _bangumi.IsConnected
            ? L("Bangumi　　✓ 已登录", "Bangumi　　✓ ログイン済み", "Bangumi      ✓ Signed in")
            : L("Bangumi　　○ 尚未登录", "Bangumi　　○ 未ログイン", "Bangumi      ○ Not signed in");

        var settings = AppSettingsStore.Current;
        CompletePlaybackSummary.Text = L(
            $"播放偏好　音轨 {LanguageDisplay(settings.PreferredAudioLanguage)} · 主字幕 {LanguageDisplay(settings.PreferredSubtitleLanguage)} · 第二字幕 {LanguageDisplay(settings.PreferredSecondarySubtitleLanguage)} · 控制区 {settings.FullscreenControlsTimeoutSeconds} 秒",
            $"再生設定　音声 {LanguageDisplay(settings.PreferredAudioLanguage)} · 字幕 {LanguageDisplay(settings.PreferredSubtitleLanguage)} · 第2字幕 {LanguageDisplay(settings.PreferredSecondarySubtitleLanguage)} · コントロール {settings.FullscreenControlsTimeoutSeconds} 秒",
            $"Playback     Audio {LanguageDisplay(settings.PreferredAudioLanguage)} · Subtitles {LanguageDisplay(settings.PreferredSubtitleLanguage)} · Secondary {LanguageDisplay(settings.PreferredSecondarySubtitleLanguage)} · Controls {settings.FullscreenControlsTimeoutSeconds} sec");
    }

    private string LanguageDisplay(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "ja" => "日本語",
        "zh" => "简体中文",
        "en" => "English",
        _ => L("自动", "自動", "Auto")
    };

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;
        if (_step < 5)
        {
            _step++;
            _state.RecordStep(_step);
            RefreshStep();
            DispatcherQueue.TryEnqueue(() => NextButton.Focus(FocusState.Programmatic));
            return;
        }

        SetBusy(true);
        if (!_state.TryMarkCompleted(out _))
        {
            SetBusy(false);
            return;
        }

        var scan = ScanAfterFinishCheckBox.IsChecked == true;
        CloseGuide();
        if (scan) _ = StartInitialScansAsync();
    }

    private void OnSkip(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _step is < 1 or > 4) return;
        _step++;
        _state.RecordStep(_step);
        RefreshStep();
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _step == 0) return;
        _step--;
        _state.RecordStep(_step);
        RefreshStep();
    }

    private async Task StartInitialScansAsync()
    {
        foreach (var source in MediaSourceStore.Default.Snapshot()
                     .Where(source => source.Enabled && !source.IsBuiltIn))
        {
            if (MediaScanCoordinator.Default.IsScanning(source.Id)) continue;
            try { await MediaScanCoordinator.Default.StartAsync(source); }
            catch { }
        }
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        BackButton.IsEnabled = !busy && _step > 0;
        NextButton.IsEnabled = !busy;
        SkipButton.IsEnabled = !busy;
        TmdbGetTokenButton.IsEnabled = !busy;
        TmdbConfigureButton.IsEnabled = !busy;
        BangumiLoginButton.IsEnabled = !busy && !_bangumi.IsConnected;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        CloseGuide();
        e.Handled = true;
    }

    private void CloseGuide()
    {
        Visibility = Visibility.Collapsed;
        _isBusy = false;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateGuideSize();

    private void UpdateGuideSize()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        GuideCard.Width = Math.Min(PreferredWidth, Math.Max(MinimumWidth, ActualWidth - 48));
        GuideCard.Height = Math.Min(PreferredHeight, Math.Max(MinimumHeight, ActualHeight - 48));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _bangumi.Changed -= Bangumi_Changed;
        _localization.LanguageChanged -= Localization_LanguageChanged;
        Unloaded -= OnUnloaded;
    }

    private static int LanguagePreferenceIndex(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "ja" => 1,
        "zh" => 2,
        "en" => 3,
        _ => 0
    };

    private static string LanguagePreferenceCode(int index) => index switch
    {
        1 => "ja",
        2 => "zh",
        3 => "en",
        _ => "auto"
    };
}
