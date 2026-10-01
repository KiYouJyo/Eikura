using Eizo.Localization;
using Eizo.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Eizo.Views;

public sealed partial class SettingsView : UserControl
{
    private static readonly double[] PlaybackRates =
        [0.5d, 0.75d, 1d, 1.25d, 1.5d, 2d];
    private static readonly int[] FullscreenControlTimeouts =
        [2, 4, 6, 10];

    private readonly AppLocalizationService _localization =
        AppLocalizationService.Default;
    private readonly BangumiAccountService _bangumiAccount =
        BangumiAccountService.Default;
    private bool _isSynchronizing;
    private bool _metadataMaintenanceRunning;

    public SettingsView()
    {
        InitializeComponent();
        ApplyText();
        EnsureConfigBackupCard();
        Loaded += SettingsView_Loaded;
        Unloaded += SettingsView_Unloaded;
    }

    private string T(string key) => _localization.GetString(key);

    private string L(
        string zhCn,
        string jaJp,
        string enUs) =>
        _localization.CurrentLanguage switch
        {
            "ja-JP" => jaJp,
            "en-US" => enUs,
            _ => zhCn,
        };

    private async void SettingsView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        _bangumiAccount.Changed -= BangumiAccount_Changed;
        _bangumiAccount.Changed += BangumiAccount_Changed;
        AppSettingsStore.Changed -= AppSettingsStore_Changed;
        AppSettingsStore.Changed += AppSettingsStore_Changed;

        SyncControlsFromSettings();
        RefreshTmdbCredentialState();
        await RefreshBangumiAccountStateAsync(
            forceRefresh: false);
    }

    private void SettingsView_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        _bangumiAccount.Changed -= BangumiAccount_Changed;
        AppSettingsStore.Changed -= AppSettingsStore_Changed;
    }

    private void AppSettingsStore_Changed(
        object? sender,
        EventArgs e)
    {
        if (_isSynchronizing)
            return;

        DispatcherQueue.TryEnqueue(
            SyncControlsFromSettings);
    }

    private void SyncControlsFromSettings()
    {
        _isSynchronizing = true;
        try
        {
            var requestedTheme =
                (XamlRoot?.Content as FrameworkElement)?.RequestedTheme
                ?? ThemePreferenceStore.Load();

            AppearanceCombo.SelectedIndex = requestedTheme switch
            {
                ElementTheme.Light => 1,
                ElementTheme.Dark => 2,
                _ => 0
            };

            var settings = AppSettingsStore.Current;
            LanguagePicker.SelectedIndex = settings.Language switch
            {
                AppLanguagePreference.SimplifiedChinese => 1,
                AppLanguagePreference.Japanese => 2,
                AppLanguagePreference.English => 3,
                _ => 0
            };

            MetadataAutoScrapeToggle.IsOn =
                settings.MetadataAutoScrapeOnScan;
            MetadataArtworkToggle.IsOn =
                settings.MetadataArtworkEnrichment;
            AutoPlayNextToggle.IsOn =
                settings.AutoPlayNextEpisode;
            FullscreenControlsTimeoutCombo.SelectedIndex =
                FindFullscreenControlsTimeoutIndex(
                    settings.FullscreenControlsTimeoutSeconds);
            RememberPlaybackRateToggle.IsOn =
                settings.RememberPlaybackRate;
            RememberSubtitleTrackToggle.IsOn =
                settings.RememberSubtitleTrack;

            DefaultPlaybackRateCombo.SelectedIndex =
                FindPlaybackRateIndex(
                    settings.DefaultPlaybackRate);
            PreferredAudioLanguageCombo.SelectedIndex =
                LanguagePreferenceIndex(
                    settings.PreferredAudioLanguage);
            PreferredSubtitleLanguageCombo.SelectedIndex =
                LanguagePreferenceIndex(
                    settings.PreferredSubtitleLanguage);
            PreferredSecondarySubtitleLanguageCombo.SelectedIndex =
                LanguagePreferenceIndex(
                    settings.PreferredSecondarySubtitleLanguage);

            PrimarySubtitlePositionSettingsSlider.Value =
                Math.Clamp(
                    settings.PrimarySubtitleVerticalPosition,
                    0d,
                    90d);
            SecondarySubtitlePositionSettingsSlider.Value =
                Math.Clamp(
                    settings.SecondarySubtitleVerticalPosition,
                    0d,
                    90d);
            PrimarySubtitleOpacitySettingsSlider.Value =
                Math.Clamp(
                    settings.PrimarySubtitleBackgroundOpacity,
                    0d,
                    100d);
            SecondarySubtitleOpacitySettingsSlider.Value =
                Math.Clamp(
                    settings.SecondarySubtitleBackgroundOpacity,
                    0d,
                    100d);

            UpdateSubtitleSettingValueText();
            RefreshTmdbCredentialState();
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private void SettingsSectionList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (GeneralSettingsPanel is null)
            return;

        var index = Math.Max(
            0,
            SettingsSectionList.SelectedIndex);

        GeneralSettingsPanel.Visibility =
            index == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        MetadataSettingsPanel.Visibility =
            index == 1
                ? Visibility.Visible
                : Visibility.Collapsed;
        PlaybackSettingsPanel.Visibility =
            index == 2
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private async void BangumiAccount_Changed(
        object? sender,
        EventArgs e)
    {
        await RefreshBangumiAccountStateAsync(
            forceRefresh: false);
    }

    private async Task RefreshBangumiAccountStateAsync(
        bool forceRefresh)
    {
        if (!_bangumiAccount.IsConnected)
        {
            BangumiAccountStatusText.Text =
                T("Bangumi_AccountNotConnected");
            BangumiConnectButton.Visibility =
                Visibility.Visible;
            BangumiDisconnectButton.Visibility =
                Visibility.Collapsed;
            return;
        }

        BangumiAccountStatusText.Text =
            T("Bangumi_AccountChecking");

        try
        {
            var profile =
                await _bangumiAccount.GetProfileAsync(
                    forceRefresh);

            if (profile is null)
            {
                BangumiAccountStatusText.Text =
                    T("Bangumi_AccountNotConnected");
                BangumiConnectButton.Visibility =
                    Visibility.Visible;
                BangumiDisconnectButton.Visibility =
                    Visibility.Collapsed;
                return;
            }

            var displayName =
                string.IsNullOrWhiteSpace(profile.NickName)
                    ? profile.UserName
                    : profile.NickName;

            BangumiAccountStatusText.Text =
                string.Format(
                    T("Bangumi_AccountConnectedFormat"),
                    displayName,
                    profile.UserName);
            BangumiConnectButton.Visibility =
                Visibility.Collapsed;
            BangumiDisconnectButton.Visibility =
                Visibility.Visible;
        }
        catch
        {
            BangumiAccountStatusText.Text =
                T("Bangumi_AccountNetworkError");
            BangumiConnectButton.Visibility =
                Visibility.Collapsed;
            BangumiDisconnectButton.Visibility =
                Visibility.Visible;
        }
    }

    private async void BangumiConnectButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (XamlRoot is null)
            return;

        await BangumiAccountDialogService.ShowConnectAsync(
            XamlRoot);
    }

    private async void BangumiDisconnectButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (XamlRoot is null)
            return;

        await BangumiAccountDialogService.ConfirmDisconnectAsync(
            XamlRoot);
    }

    private void ReopenFirstRunGuideButton_Click(
        object sender,
        RoutedEventArgs e) =>
        App.MainWindow?.ShowFirstRunGuideFromSettings();

    private void AppearanceCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isSynchronizing ||
            AppearanceCombo.SelectedIndex < 0 ||
            XamlRoot?.Content is not FrameworkElement root)
        {
            return;
        }

        var theme = AppearanceCombo.SelectedIndex switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        root.RequestedTheme = theme;
        ThemePreferenceStore.Save(theme);
    }

    private async void LanguagePicker_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isSynchronizing ||
            LanguagePicker.SelectedIndex < 0)
        {
            return;
        }

        var preference = LanguagePicker.SelectedIndex switch
        {
            1 => AppLanguagePreference.SimplifiedChinese,
            2 => AppLanguagePreference.Japanese,
            3 => AppLanguagePreference.English,
            _ => AppLanguagePreference.System
        };

        LanguagePicker.IsEnabled = false;
        var switched =
            await _localization.SwitchLanguageAsync(
                preference);
        LanguagePicker.IsEnabled = true;
        if (switched)
            return;

        SyncControlsFromSettings();
    }

    private void RefreshTmdbCredentialState()
    {
        if (TmdbTokenStatusText is null)
            return;

        var source =
            MediaScanCoordinator.Default
                .TmdbConfigurationSource;
        var configured =
            !string.Equals(
                source,
                "None",
                StringComparison.Ordinal);

        TmdbTokenStatusText.Text =
            source switch
            {
                "Environment" => L(
                    "TMDB 已通过 EIZO_TMDB_READ_ACCESS_TOKEN 环境变量启用；环境变量优先于应用内保存的 Token。",
                    "TMDB は EIZO_TMDB_READ_ACCESS_TOKEN 環境変数で有効です。環境変数はアプリ内保存 Token より優先されます。",
                    "TMDB is enabled through EIZO_TMDB_READ_ACCESS_TOKEN; the environment value takes precedence over an in-app token."),
                "PasswordVault" => L(
                    "TMDB 已配置，将参与电影、电视剧与视觉元数据匹配。",
                    "TMDB は設定済みです。映画・ドラマ・ビジュアルメタデータの照合に使用されます。",
                    "TMDB is configured and will participate in movie, TV and visual metadata matching."),
                _ => L(
                    "TMDB 尚未配置；当前仅使用已可用的数据源。",
                    "TMDB は未設定です。現在は利用可能な他のデータソースのみを使用します。",
                    "TMDB is not configured; only other available providers are currently used."),
            };

        TmdbRemoveTokenButton.IsEnabled =
            string.Equals(
                source,
                "PasswordVault",
                StringComparison.Ordinal);
    }

    private async void TmdbGetTokenButton_Click(
        object sender,
        RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(
            new Uri(
                "https://www.themoviedb.org/settings/api"));

    private void TmdbSaveTokenButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var token = TmdbTokenBox.Password?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            TmdbTokenStatusText.Text =
                L(
                    "请输入 TMDB Read Access Token。",
                    "TMDB Read Access Token を入力してください。",
                    "Enter a TMDB Read Access Token.");
            return;
        }

        MediaCredentialStore.Default
            .SaveTmdbReadAccessToken(token);
        MediaScanCoordinator.Default
            .ReloadMetadataService();
        TmdbTokenBox.Password = string.Empty;
        RefreshTmdbCredentialState();
    }

    private void TmdbRemoveTokenButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        MediaCredentialStore.Default
            .RemoveTmdbReadAccessToken();
        MediaScanCoordinator.Default
            .ReloadMetadataService();
        TmdbTokenBox.Password = string.Empty;
        RefreshTmdbCredentialState();
    }

    private async void TmdbVerifyTokenButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var token =
            string.IsNullOrWhiteSpace(
                TmdbTokenBox.Password)
                ? MediaCredentialStore.Default
                    .GetTmdbReadAccessToken()
                : TmdbTokenBox.Password.Trim();

        TmdbGetTokenButton.IsEnabled = false;
        TmdbSaveTokenButton.IsEnabled = false;
        TmdbVerifyTokenButton.IsEnabled = false;
        TmdbRemoveTokenButton.IsEnabled = false;
        TmdbTokenStatusText.Text =
            L(
                "正在验证 TMDB 连接…",
                "TMDB 接続を確認しています…",
                "Checking TMDB connection…");

        try
        {
            var result =
                await TmdbConnectionVerifier.CheckAsync(
                    token);

            TmdbTokenStatusText.Text =
                result.State switch
                {
                    "Connected" => L(
                        "TMDB 连接正常。",
                        "TMDB に接続できました。",
                        "TMDB connection is working."),
                    "Unauthorized" => L(
                        "TMDB Token 无效或未获授权。",
                        "TMDB Token が無効、または権限がありません。",
                        "The TMDB token is invalid or unauthorized."),
                    "MissingToken" => L(
                        "请输入或保存 TMDB Read Access Token。",
                        "TMDB Read Access Token を入力または保存してください。",
                        "Enter or save a TMDB Read Access Token."),
                    "Timeout" => L(
                        "TMDB 连接超时。",
                        "TMDB 接続がタイムアウトしました。",
                        "TMDB connection timed out."),
                    _ => L(
                        "无法连接 TMDB，请检查网络后重试。",
                        "TMDB に接続できません。ネットワークを確認して再試行してください。",
                        "Unable to reach TMDB. Check your network and try again."),
                };
        }
        finally
        {
            TmdbGetTokenButton.IsEnabled = true;
            TmdbSaveTokenButton.IsEnabled = true;
            TmdbVerifyTokenButton.IsEnabled = true;
            TmdbRemoveTokenButton.IsEnabled =
                string.Equals(
                    MediaScanCoordinator.Default
                        .TmdbConfigurationSource,
                    "PasswordVault",
                    StringComparison.Ordinal);
        }
    }


    private void MetadataAutoScrapeToggle_Toggled(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSynchronizing)
            return;

        AppSettingsStore.Update(settings =>
            settings with
            {
                MetadataAutoScrapeOnScan =
                    MetadataAutoScrapeToggle.IsOn
            });
    }

    private void MetadataArtworkToggle_Toggled(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSynchronizing)
            return;

        AppSettingsStore.Update(settings =>
            settings with
            {
                MetadataArtworkEnrichment =
                    MetadataArtworkToggle.IsOn
            });
    }

    private async void MetadataRescrapeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_metadataMaintenanceRunning)
            return;

        var sources = MediaSourceStore.Default
            .Snapshot()
            .Where(static source => source.Enabled)
            .Where(source =>
                MediaCatalogStore.Default
                    .SnapshotForSource(source.Id)
                    .Count > 0)
            .ToArray();

        if (sources.Length == 0)
        {
            MetadataActionStatusText.Text =
                T("Settings_MetadataNoMedia");
            return;
        }

        SetMetadataMaintenanceBusy(true);
        var processed = 0;
        var failed = 0;

        try
        {
            foreach (var source in sources)
            {
                MetadataActionStatusText.Text =
                    string.Format(
                        T("Settings_MetadataRescrapeRunningFormat"),
                        source.DisplayName);

                try
                {
                    var result =
                        await MediaScanCoordinator.Default
                            .StartMetadataAsync(source);

                    if (result.Status == MediaScanStatus.Completed)
                    {
                        processed +=
                            Math.Max(
                                result.MetadataProcessed,
                                result.MetadataTotal);
                    }
                    else
                    {
                        failed++;
                    }
                }
                catch
                {
                    failed++;
                }
            }

            MetadataActionStatusText.Text =
                failed == 0
                    ? string.Format(
                        T("Settings_MetadataRescrapeCompleteFormat"),
                        processed)
                    : string.Format(
                        T("Settings_MetadataRescrapePartialFormat"),
                        processed,
                        failed);
        }
        finally
        {
            SetMetadataMaintenanceBusy(false);
        }
    }

    private async void MetadataClearCacheButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_metadataMaintenanceRunning)
            return;

        var scanning = MediaSourceStore.Default
            .Snapshot()
            .Any(source =>
                MediaScanCoordinator.Default.IsScanning(
                    source.Id));

        if (scanning)
        {
            MetadataActionStatusText.Text =
                T("Settings_MetadataBusy");
            return;
        }

        SetMetadataMaintenanceBusy(true);
        MetadataActionStatusText.Text =
            T("Settings_MetadataClearingCache");

        try
        {
            await MediaScanCoordinator
                .ClearMetadataCacheAsync();
            MetadataActionStatusText.Text =
                T("Settings_MetadataCacheCleared");
        }
        finally
        {
            SetMetadataMaintenanceBusy(false);
        }
    }

    private void SetMetadataMaintenanceBusy(bool busy)
    {
        _metadataMaintenanceRunning = busy;
        MetadataRescrapeButton.IsEnabled = !busy;
        MetadataClearCacheButton.IsEnabled = !busy;
        MetadataAutoScrapeToggle.IsEnabled = !busy;
        MetadataArtworkToggle.IsEnabled = !busy;
    }

    private void AutoPlayNextToggle_Toggled(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSynchronizing)
            return;

        AppSettingsStore.Update(settings =>
            settings with
            {
                AutoPlayNextEpisode =
                    AutoPlayNextToggle.IsOn
            });
    }

    private void FullscreenControlsTimeoutCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isSynchronizing ||
            FullscreenControlsTimeoutCombo.SelectedIndex < 0 ||
            FullscreenControlsTimeoutCombo.SelectedIndex >=
                FullscreenControlTimeouts.Length)
        {
            return;
        }

        AppSettingsStore.Update(settings =>
            settings with
            {
                FullscreenControlsTimeoutSeconds =
                    FullscreenControlTimeouts[
                        FullscreenControlsTimeoutCombo.SelectedIndex]
            });
    }

    private void RememberPlaybackRateToggle_Toggled(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSynchronizing)
            return;

        AppSettingsStore.Update(settings =>
            settings with
            {
                RememberPlaybackRate =
                    RememberPlaybackRateToggle.IsOn
            });
    }

    private void DefaultPlaybackRateCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isSynchronizing ||
            DefaultPlaybackRateCombo.SelectedIndex < 0 ||
            DefaultPlaybackRateCombo.SelectedIndex >=
                PlaybackRates.Length)
        {
            return;
        }

        var rate =
            PlaybackRates[
                DefaultPlaybackRateCombo.SelectedIndex];
        AppSettingsStore.Update(settings =>
            settings with
            {
                DefaultPlaybackRate = rate
            });
    }

    private void PreferredAudioLanguageCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isSynchronizing ||
            PreferredAudioLanguageCombo.SelectedIndex < 0)
        {
            return;
        }

        AppSettingsStore.Update(settings =>
            settings with
            {
                PreferredAudioLanguage =
                    LanguagePreferenceCode(
                        PreferredAudioLanguageCombo.SelectedIndex)
            });
    }

    private void PreferredSubtitleLanguageCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isSynchronizing ||
            PreferredSubtitleLanguageCombo.SelectedIndex < 0)
        {
            return;
        }

        AppSettingsStore.Update(settings =>
            settings with
            {
                PreferredSubtitleLanguage =
                    LanguagePreferenceCode(
                        PreferredSubtitleLanguageCombo.SelectedIndex)
            });
    }

    private void PreferredSecondarySubtitleLanguageCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isSynchronizing ||
            PreferredSecondarySubtitleLanguageCombo.SelectedIndex < 0)
        {
            return;
        }

        AppSettingsStore.Update(settings =>
            settings with
            {
                PreferredSecondarySubtitleLanguage =
                    LanguagePreferenceCode(
                        PreferredSecondarySubtitleLanguageCombo.SelectedIndex)
            });
    }

    private void RememberSubtitleTrackToggle_Toggled(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSynchronizing)
            return;

        AppSettingsStore.Update(settings =>
            settings with
            {
                RememberSubtitleTrack =
                    RememberSubtitleTrackToggle.IsOn
            });
    }

    private void PrimarySubtitlePositionSettingsSlider_ValueChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isSynchronizing)
            return;

        AppSettingsStore.Update(settings =>
            settings with
            {
                PrimarySubtitleVerticalPosition =
                    Math.Clamp(e.NewValue, 0d, 90d)
            });
        UpdateSubtitleSettingValueText();
    }

    private void SecondarySubtitlePositionSettingsSlider_ValueChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isSynchronizing)
            return;

        AppSettingsStore.Update(settings =>
            settings with
            {
                SecondarySubtitleVerticalPosition =
                    Math.Clamp(e.NewValue, 0d, 90d)
            });
        UpdateSubtitleSettingValueText();
    }

    private void PrimarySubtitleOpacitySettingsSlider_ValueChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isSynchronizing)
            return;

        AppSettingsStore.Update(settings =>
            settings with
            {
                PrimarySubtitleBackgroundOpacity =
                    Math.Clamp(e.NewValue, 0d, 100d)
            });
        UpdateSubtitleSettingValueText();
    }

    private void SecondarySubtitleOpacitySettingsSlider_ValueChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isSynchronizing)
            return;

        AppSettingsStore.Update(settings =>
            settings with
            {
                SecondarySubtitleBackgroundOpacity =
                    Math.Clamp(e.NewValue, 0d, 100d)
            });
        UpdateSubtitleSettingValueText();
    }

    private void UpdateSubtitleSettingValueText()
    {
        if (PrimarySubtitlePositionSettingsValue is null)
            return;

        PrimarySubtitlePositionSettingsValue.Text =
            $"{PrimarySubtitlePositionSettingsSlider.Value:0}%";
        SecondarySubtitlePositionSettingsValue.Text =
            $"{SecondarySubtitlePositionSettingsSlider.Value:0}%";
        PrimarySubtitleOpacitySettingsValue.Text =
            $"{PrimarySubtitleOpacitySettingsSlider.Value:0}%";
        SecondarySubtitleOpacitySettingsValue.Text =
            $"{SecondarySubtitleOpacitySettingsSlider.Value:0}%";
    }

    private static int FindFullscreenControlsTimeoutIndex(
        int value)
    {
        var normalized =
            AppSettingsStore.NormalizeFullscreenControlsTimeout(
                value);
        var index =
            Array.IndexOf(
                FullscreenControlTimeouts,
                normalized);
        return index >= 0 ? index : 1;
    }

    private static int FindPlaybackRateIndex(double value)
    {
        var bestIndex = 0;
        var bestDistance = double.MaxValue;

        for (var i = 0; i < PlaybackRates.Length; i++)
        {
            var distance =
                Math.Abs(PlaybackRates[i] - value);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
        }

        return bestIndex;
    }

    private static int LanguagePreferenceIndex(
        string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "ja" => 1,
            "zh" => 2,
            "en" => 3,
            _ => 0
        };

    private static string LanguagePreferenceCode(
        int index) =>
        index switch
        {
            1 => "ja",
            2 => "zh",
            3 => "en",
            _ => "auto"
        };

    private void ApplyText()
    {
        PageTitle.Text = T("Nav_Settings");
        PageSubtitle.Text = T("Settings_Subtitle");

        SettingsSectionList.ItemsSource = new[]
        {
            T("Settings_General"),
            T("Settings_Metadata"),
            T("Settings_Playback")
        };

        LanguageSectionTitle.Text =
            T("Settings_LanguageAppearance");
        LanguageTitle.Text = T("Settings_Language");
        LanguageDescription.Text =
            T("Settings_LanguageDescription");
        LanguagePicker.ItemsSource = new[]
        {
            T("Settings_LanguageSystem"),
            "简体中文",
            "日本語",
            "English"
        };

        AppearanceTitle.Text = T("Settings_Appearance");
        AppearanceDescription.Text =
            T("Settings_AppearanceDescription");
        AppearanceCombo.ItemsSource = new[]
        {
            T("Settings_AppearanceSystem"),
            T("Settings_AppearanceLight"),
            T("Settings_AppearanceDark")
        };

        OnboardingSectionTitle.Text =
            L("首次使用", "初回セットアップ", "First-run setup");
        OnboardingTitle.Text =
            L("重新打开首次使用向导", "初回ガイドをもう一度開く", "Open the first-run guide again");
        OnboardingDescription.Text =
            L(
                "重新查看功能介绍并调整媒体来源、TMDB、Bangumi 与播放偏好。不会重置现有媒体库或账户数据。",
                "機能紹介をもう一度確認し、メディアソース、TMDB、Bangumi、再生設定を調整できます。既存のライブラリやアカウントデータはリセットしません。",
                "Review the setup flow and adjust media sources, TMDB, Bangumi, and playback preferences without resetting your existing library or account data.");
        ReopenFirstRunGuideButton.Content =
            L("打开向导", "ガイドを開く", "Open guide");

        BangumiSectionTitle.Text =
            T("Bangumi_SettingsSection");
        BangumiAccountTitle.Text =
            T("Bangumi_Account");
        BangumiAccountDescription.Text =
            T("Bangumi_AccountSettingsDescription");
        BangumiConnectButton.Content =
            T("Bangumi_Connect");
        BangumiDisconnectButton.Content =
            T("Bangumi_Disconnect");

        TmdbSectionTitle.Text =
            L("TMDB 数据源", "TMDB データソース", "TMDB provider");
        TmdbTokenLabel.Text =
            L("Read Access Token", "Read Access Token", "Read Access Token");
        TmdbTokenDescription.Text =
            L(
                "用于 TMDB 电影、电视剧、季度、剧集、图片与演职人员元数据。Token 仅保存在 Windows PasswordVault 中。",
                "TMDB の映画・ドラマ・シーズン・エピソード・画像・キャスト情報に使用します。Token は Windows PasswordVault のみに保存されます。",
                "Used for TMDB movie, TV, season, episode, artwork and credits metadata. The token is stored only in Windows PasswordVault.");
        TmdbGetTokenButton.Content =
            L("获取 Token", "Token を取得", "Get token");
        TmdbSaveTokenButton.Content =
            L("保存", "保存", "Save");
        TmdbVerifyTokenButton.Content =
            L("验证", "確認", "Verify");
        TmdbRemoveTokenButton.Content =
            L("移除", "削除", "Remove");

        MetadataAutomationSectionTitle.Text =
            T("Settings_MetadataAutomation");
        MetadataAutoScrapeLabel.Text =
            T("Settings_MetadataAutoScrape");
        MetadataAutoScrapeDescription.Text =
            T("Settings_MetadataAutoScrapeDescription");
        MetadataArtworkLabel.Text =
            T("Settings_MetadataArtwork");
        MetadataArtworkDescription.Text =
            T("Settings_MetadataArtworkDescription");

        MetadataMaintenanceSectionTitle.Text =
            T("Settings_MetadataMaintenance");
        MetadataRescrapeLabel.Text =
            T("Settings_MetadataRescrape");
        MetadataRescrapeDescription.Text =
            T("Settings_MetadataRescrapeDescription");
        MetadataRescrapeButton.Content =
            T("Settings_MetadataRescrapeButton");
        MetadataClearCacheLabel.Text =
            T("Settings_MetadataClearCache");
        MetadataClearCacheDescription.Text =
            T("Settings_MetadataClearCacheDescription");
        MetadataClearCacheButton.Content =
            T("Settings_MetadataClearCacheButton");

        PlaybackBehaviorSectionTitle.Text =
            T("Settings_PlaybackBehavior");
        AutoPlayNextLabel.Text =
            T("Settings_AutoPlayNext");
        AutoPlayNextDescription.Text =
            T("Settings_AutoPlayNextDescription");
        FullscreenControlsTimeoutLabel.Text =
            L(
                "全屏控制区驻留时间",
                "全画面コントロール表示時間",
                "Fullscreen controls timeout");
        FullscreenControlsTimeoutDescription.Text =
            L(
                "鼠标停止移动后，播放控制区继续显示的时间。",
                "マウス操作を止めてから再生コントロールを表示し続ける時間です。",
                "How long playback controls stay visible after pointer activity stops.");
        FullscreenControlsTimeoutCombo.ItemsSource =
            FullscreenControlTimeouts
                .Select(seconds =>
                    L(
                        $"{seconds} 秒",
                        $"{seconds} 秒",
                        $"{seconds} sec"))
                .ToArray();
        RememberPlaybackRateLabel.Text =
            T("Settings_RememberPlaybackRate");
        RememberPlaybackRateDescription.Text =
            T("Settings_RememberPlaybackRateDescription");
        DefaultPlaybackRateLabel.Text =
            T("Settings_DefaultPlaybackRate");
        DefaultPlaybackRateDescription.Text =
            T("Settings_DefaultPlaybackRateDescription");
        DefaultPlaybackRateCombo.ItemsSource =
            PlaybackRates
                .Select(static rate => $"{rate:0.##}×")
                .ToArray();

        PlaybackTracksSectionTitle.Text =
            T("Settings_TracksAndSubtitles");
        PreferredAudioLanguageLabel.Text =
            T("Settings_PreferredAudioLanguage");
        PreferredAudioLanguageDescription.Text =
            T("Settings_PreferredAudioLanguageDescription");
        PreferredSubtitleLanguageLabel.Text =
            T("Settings_PreferredSubtitleLanguage");
        PreferredSubtitleLanguageDescription.Text =
            T("Settings_PreferredSubtitleLanguageDescription");
        PreferredSecondarySubtitleLanguageLabel.Text =
            T("Settings_PreferredSecondarySubtitleLanguage");
        PreferredSecondarySubtitleLanguageDescription.Text =
            T("Settings_PreferredSecondarySubtitleLanguageDescription");
        RememberSubtitleTrackLabel.Text =
            T("Settings_RememberSubtitleTrack");
        RememberSubtitleTrackDescription.Text =
            T("Settings_RememberSubtitleTrackDescription");

        var trackLanguages = new[]
        {
            T("Settings_LanguageAuto"),
            "日本語",
            "简体中文",
            "English"
        };
        PreferredAudioLanguageCombo.ItemsSource =
            trackLanguages;
        PreferredSubtitleLanguageCombo.ItemsSource =
            trackLanguages.ToArray();
        PreferredSecondarySubtitleLanguageCombo.ItemsSource =
            trackLanguages.ToArray();

        SubtitleStyleSectionTitle.Text =
            T("Settings_SubtitleStyle");
        PrimarySubtitlePositionSettingsLabel.Text =
            T("Playback_PrimarySubtitlePosition");
        SecondarySubtitlePositionSettingsLabel.Text =
            T("Playback_SecondarySubtitlePosition");
        PrimarySubtitleOpacitySettingsLabel.Text =
            T("Playback_PrimarySubtitleOpacity");
        SecondarySubtitleOpacitySettingsLabel.Text =
            T("Playback_SecondarySubtitleOpacity");
    }
}
