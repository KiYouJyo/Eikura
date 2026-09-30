using Eizo.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Eizo.Views;

public sealed partial class SettingsView
{
    private readonly ConfigBackupService _configBackupService = new();
    private bool _configBackupUiHooked;
    private bool _configBackupRunning;
    private Border? _configBackupCard;
    private ComboBox? _configBackupDestinationCombo;
    private PasswordBox? _configBackupPasswordBox;
    private TextBlock? _configBackupStatusText;
    private TextBlock? _configBackupScopeText;
    private Button? _configBackupNowButton;
    private Button? _configRestoreButton;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_configBackupUiHooked)
            return;

        _configBackupUiHooked = true;
        Loaded += SettingsView_ConfigBackupLoaded;
        Unloaded += SettingsView_ConfigBackupUnloaded;
    }

    private void SettingsView_ConfigBackupLoaded(
        object sender,
        RoutedEventArgs e)
    {
        EnsureConfigBackupCard();
        MediaSourceStore.Default.Changed -= MediaSourceStore_ConfigBackupChanged;
        MediaSourceStore.Default.Changed += MediaSourceStore_ConfigBackupChanged;
        RefreshConfigBackupDestinations();
    }

    private void SettingsView_ConfigBackupUnloaded(
        object sender,
        RoutedEventArgs e)
    {
        MediaSourceStore.Default.Changed -= MediaSourceStore_ConfigBackupChanged;
    }

    private void MediaSourceStore_ConfigBackupChanged(
        object? sender,
        EventArgs e)
    {
        DispatcherQueue.TryEnqueue(RefreshConfigBackupDestinations);
    }

    private void EnsureConfigBackupCard()
    {
        if (_configBackupCard is not null ||
            GeneralSettingsPanel is null)
        {
            return;
        }

        var outer = new Border
        {
            Style = FindAppStyle("SettingsSectionCardStyle")
        };

        var section = new StackPanel
        {
            Spacing = 14
        };

        var sectionTitle = new TextBlock
        {
            Text = L(
                "配置备份与恢复",
                "設定のバックアップと復元",
                "Configuration backup and restore"),
            Style = FindAppStyle("SectionTitleText")
        };
        section.Children.Add(sectionTitle);

        var inner = new Border
        {
            Style = FindAppStyle("SettingsInnerCardStyle")
        };
        var content = new StackPanel
        {
            Padding = new Thickness(14),
            Spacing = 12
        };

        var description = new TextBlock
        {
            Text = L(
                "将可迁移的 Eikura 配置加密备份到一个已添加的 WebDAV 媒体来源。新设备只需先添加该 WebDAV 来源，即可从这里恢复其余配置。",
                "移行可能な Eikura 設定を、追加済みの WebDAV メディアソースへ暗号化して保存します。新しい端末では、まずその WebDAV ソースを追加すれば、ここから残りの設定を復元できます。",
                "Encrypt portable Eikura settings to an existing WebDAV media source. On a new device, add that WebDAV source first, then restore the remaining configuration here."),
            TextWrapping = TextWrapping.Wrap,
            Style = FindAppStyle("SettingsDescriptionStyle")
        };
        content.Children.Add(description);

        _configBackupDestinationCombo = new ComboBox
        {
            Header = L(
                "WebDAV 备份位置",
                "WebDAV バックアップ先",
                "WebDAV backup location"),
            MinWidth = 240,
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Left,
            DisplayMemberPath = nameof(MediaSourceDefinition.DisplayName)
        };
        content.Children.Add(_configBackupDestinationCombo);

        _configBackupPasswordBox = new PasswordBox
        {
            Header = L(
                "备份密码",
                "バックアップパスワード",
                "Backup password"),
            PlaceholderText = L(
                "至少 8 个字符",
                "8 文字以上",
                "At least 8 characters"),
            MinWidth = 240,
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Left,
            PasswordRevealMode = PasswordRevealMode.Peek
        };
        content.Children.Add(_configBackupPasswordBox);

        var passwordNote = new TextBlock
        {
            Text = L(
                "密码仅用于本次加密/解密，不会保存到应用或 WebDAV。忘记密码后备份无法恢复。",
                "パスワードは今回の暗号化・復号にのみ使用し、アプリや WebDAV には保存しません。忘れた場合はバックアップを復元できません。",
                "The password is used only for encryption/decryption and is not stored by Eikura or WebDAV. A forgotten password cannot be recovered."),
            TextWrapping = TextWrapping.Wrap,
            Style = FindAppStyle("SettingsDescriptionStyle")
        };
        content.Children.Add(passwordNote);

        _configBackupScopeText = new TextBlock
        {
            Text = L(
                "包含：媒体来源、应用内保存的 TMDB Token、其他 WebDAV 来源凭据、元数据设置、播放/字幕设置与外观。不会包含：当前备份目的地自身的 WebDAV 密码、播放历史、媒体数据库、缓存、窗口尺寸和本地文件访问令牌。",
                "対象：メディアソース、アプリ内保存の TMDB Token、他の WebDAV ソース認証情報、メタデータ設定、再生・字幕設定、外観。対象外：バックアップ先自身の WebDAV パスワード、再生履歴、メディアDB、キャッシュ、ウィンドウサイズ、ローカルファイルのアクセストークン。",
                "Includes media sources, the in-app TMDB token, credentials for other WebDAV sources, metadata settings, playback/subtitle settings, and appearance. Excludes the backup destination's own WebDAV password, playback history, media database, cache, window size, and local-file access tokens."),
            TextWrapping = TextWrapping.Wrap,
            Style = FindAppStyle("MetadataText")
        };
        content.Children.Add(_configBackupScopeText);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        _configBackupNowButton = new Button
        {
            Content = L(
                "立即备份",
                "今すぐバックアップ",
                "Back up now")
        };
        _configBackupNowButton.Click += ConfigBackupNowButton_Click;
        actions.Children.Add(_configBackupNowButton);

        _configRestoreButton = new Button
        {
            Content = L(
                "从备份恢复",
                "バックアップから復元",
                "Restore backup")
        };
        _configRestoreButton.Click += ConfigRestoreButton_Click;
        actions.Children.Add(_configRestoreButton);
        content.Children.Add(actions);

        _configBackupStatusText = new TextBlock
        {
            Text = L(
                "尚未执行备份或恢复。",
                "まだバックアップまたは復元を実行していません。",
                "No backup or restore has been run yet."),
            TextWrapping = TextWrapping.Wrap,
            Style = FindAppStyle("MetadataText")
        };
        content.Children.Add(_configBackupStatusText);

        inner.Child = content;
        section.Children.Add(inner);
        outer.Child = section;
        _configBackupCard = outer;

        // Keep backup beside the other General portability/preferences controls,
        // before account/onboarding cards.
        var insertionIndex = Math.Min(1, GeneralSettingsPanel.Children.Count);
        GeneralSettingsPanel.Children.Insert(insertionIndex, outer);
    }

    private void RefreshConfigBackupDestinations()
    {
        if (_configBackupDestinationCombo is null)
            return;

        var previousId =
            (_configBackupDestinationCombo.SelectedItem as MediaSourceDefinition)?.Id;
        var sources = MediaSourceStore.Default
            .Snapshot()
            .Where(static source =>
                source.Kind == MediaSourceKind.WebDav &&
                source.Enabled &&
                !string.IsNullOrWhiteSpace(source.RootLocation))
            .OrderBy(static source => source.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        _configBackupDestinationCombo.ItemsSource = sources;
        _configBackupDestinationCombo.SelectedItem = sources.FirstOrDefault(source =>
            string.Equals(source.Id, previousId, StringComparison.Ordinal));

        if (_configBackupDestinationCombo.SelectedItem is null &&
            sources.Length > 0)
        {
            _configBackupDestinationCombo.SelectedIndex = 0;
        }

        if (sources.Length == 0 && _configBackupStatusText is not null)
        {
            _configBackupStatusText.Text = L(
                "请先在“媒体来源”中添加一个 WebDAV 来源。",
                "まず「メディアソース」で WebDAV ソースを追加してください。",
                "Add a WebDAV source in Media Sources first.");
        }

        SetConfigBackupBusy(_configBackupRunning);
    }

    private async void ConfigBackupNowButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_configBackupRunning ||
            !TryGetConfigBackupInput(
                out var destination,
                out var passphrase))
        {
            return;
        }

        SetConfigBackupBusy(true);
        SetConfigBackupStatus(
            L(
                "正在加密并上传配置…",
                "設定を暗号化してアップロードしています…",
                "Encrypting and uploading configuration…"));

        try
        {
            var result = await _configBackupService.BackupAsync(
                destination!,
                passphrase!);

            SetConfigBackupStatus(
                string.Format(
                    L(
                        "备份完成：{0} 个媒体来源已写入加密备份。",
                        "バックアップ完了：{0} 件のメディアソースを暗号化バックアップに保存しました。",
                        "Backup complete: {0} media sources were written to the encrypted backup."),
                    result.SourceCount));
        }
        catch (ConfigBackupException exception)
        {
            SetConfigBackupStatus(ConfigBackupErrorText(exception));
        }
        catch (Exception)
        {
            SetConfigBackupStatus(
                L(
                    "备份失败。请检查 WebDAV 来源和网络后重试。",
                    "バックアップに失敗しました。WebDAV ソースとネットワークを確認して再試行してください。",
                    "Backup failed. Check the WebDAV source and network, then try again."));
        }
        finally
        {
            SetConfigBackupBusy(false);
        }
    }

    private async void ConfigRestoreButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_configBackupRunning ||
            XamlRoot is null ||
            !TryGetConfigBackupInput(
                out var destination,
                out var passphrase))
        {
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = L(
                "恢复 Eikura 配置？",
                "Eikura の設定を復元しますか？",
                "Restore Eikura configuration?"),
            Content = L(
                "恢复会用备份中的可迁移配置更新当前媒体来源、TMDB Token、元数据、播放/字幕和外观设置。当前备份目的地会保留；播放历史、媒体数据库和缓存不会被修改。",
                "バックアップ内の移行可能な設定で、現在のメディアソース、TMDB Token、メタデータ、再生・字幕、外観設定を更新します。現在のバックアップ先は保持され、再生履歴・メディアDB・キャッシュは変更しません。",
                "Portable settings from the backup will update current media sources, TMDB token, metadata, playback/subtitle, and appearance settings. The current backup destination is preserved; playback history, media database, and cache are not changed."),
            PrimaryButtonText = L("恢复", "復元", "Restore"),
            CloseButtonText = L("取消", "キャンセル", "Cancel"),
            DefaultButton = ContentDialogButton.Close
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            return;

        SetConfigBackupBusy(true);
        SetConfigBackupStatus(
            L(
                "正在下载、解密并恢复配置…",
                "バックアップをダウンロード・復号して復元しています…",
                "Downloading, decrypting, and restoring configuration…"));

        try
        {
            var result = await _configBackupService.RestoreAsync(
                destination!,
                passphrase!);

            MediaScanCoordinator.Default.ReloadMetadataService();
            SyncControlsFromSettings();
            RefreshTmdbCredentialState();
            ApplyRestoredTheme();
            RefreshConfigBackupDestinations();

            SetConfigBackupStatus(
                result.SkippedLocalSources == 0
                    ? string.Format(
                        L(
                            "恢复完成：已载入 {0} 个媒体来源配置。",
                            "復元完了：{0} 件のメディアソース設定を読み込みました。",
                            "Restore complete: {0} media source configurations were loaded."),
                        result.SourceCount)
                    : string.Format(
                        L(
                            "恢复完成：已载入备份；其中 {0} 个本地目录在此设备上不存在，已跳过。",
                            "復元完了：バックアップを読み込みました。この端末に存在しないローカルフォルダー {0} 件はスキップしました。",
                            "Restore complete; {0} local folders that do not exist on this device were skipped."),
                        result.SkippedLocalSources));
        }
        catch (ConfigBackupException exception)
        {
            SetConfigBackupStatus(ConfigBackupErrorText(exception));
        }
        catch (Exception)
        {
            SetConfigBackupStatus(
                L(
                    "恢复失败。当前播放历史、媒体数据库和缓存未被改动。",
                    "復元に失敗しました。再生履歴・メディアDB・キャッシュは変更されていません。",
                    "Restore failed. Playback history, media database, and cache were not changed."));
        }
        finally
        {
            SetConfigBackupBusy(false);
        }
    }

    private bool TryGetConfigBackupInput(
        out MediaSourceDefinition? destination,
        out string? passphrase)
    {
        destination =
            _configBackupDestinationCombo?.SelectedItem as MediaSourceDefinition;
        passphrase = _configBackupPasswordBox?.Password;

        if (destination is null)
        {
            SetConfigBackupStatus(
                L(
                    "请选择一个 WebDAV 备份位置。",
                    "WebDAV バックアップ先を選択してください。",
                    "Select a WebDAV backup location."));
            return false;
        }

        if (string.IsNullOrWhiteSpace(passphrase) ||
            passphrase.Length < 8)
        {
            SetConfigBackupStatus(
                L(
                    "备份密码至少需要 8 个字符。",
                    "バックアップパスワードは 8 文字以上必要です。",
                    "The backup password must contain at least 8 characters."));
            return false;
        }

        return true;
    }

    private void ApplyRestoredTheme()
    {
        if (XamlRoot?.Content is FrameworkElement root)
            root.RequestedTheme = ThemePreferenceStore.Load();
    }

    private void SetConfigBackupBusy(bool busy)
    {
        _configBackupRunning = busy;

        if (_configBackupDestinationCombo is not null)
            _configBackupDestinationCombo.IsEnabled = !busy &&
                _configBackupDestinationCombo.Items.Count > 0;
        if (_configBackupPasswordBox is not null)
            _configBackupPasswordBox.IsEnabled = !busy;
        if (_configBackupNowButton is not null)
            _configBackupNowButton.IsEnabled = !busy &&
                (_configBackupDestinationCombo?.Items.Count ?? 0) > 0;
        if (_configRestoreButton is not null)
            _configRestoreButton.IsEnabled = !busy &&
                (_configBackupDestinationCombo?.Items.Count ?? 0) > 0;
    }

    private void SetConfigBackupStatus(string text)
    {
        if (_configBackupStatusText is not null)
            _configBackupStatusText.Text = text;
    }

    private string ConfigBackupErrorText(ConfigBackupException exception) =>
        exception.Code switch
        {
            "InvalidPassphrase" => L(
                "备份密码至少需要 8 个字符。",
                "バックアップパスワードは 8 文字以上必要です。",
                "The backup password must contain at least 8 characters."),
            "BackupNotFound" => L(
                "所选 WebDAV 来源中没有找到 Eikura 配置备份。",
                "選択した WebDAV ソースに Eikura 設定バックアップが見つかりません。",
                "No Eikura configuration backup was found in the selected WebDAV source."),
            "AuthenticationFailed" => L(
                "WebDAV 身份验证失败。请检查该媒体来源的账号与密码。",
                "WebDAV の認証に失敗しました。メディアソースのユーザー名とパスワードを確認してください。",
                "WebDAV authentication failed. Check the media source username and password."),
            "InvalidPasswordOrBackup" => L(
                "备份密码不正确，或备份文件已被修改/损坏。",
                "バックアップパスワードが正しくないか、バックアップファイルが変更・破損しています。",
                "The backup password is incorrect, or the backup file was modified or damaged."),
            "InvalidBackup" or "UnsupportedBackup" => L(
                "该文件不是当前版本可读取的 Eikura 配置备份。",
                "このファイルは現在のバージョンで読み取れる Eikura 設定バックアップではありません。",
                "This file is not an Eikura configuration backup supported by this version."),
            "Timeout" => L(
                "WebDAV 请求超时，请稍后重试。",
                "WebDAV リクエストがタイムアウトしました。後でもう一度お試しください。",
                "The WebDAV request timed out. Try again later."),
            "NetworkError" => L(
                "无法连接 WebDAV，请检查网络后重试。",
                "WebDAV に接続できません。ネットワークを確認して再試行してください。",
                "Unable to reach WebDAV. Check the network and try again."),
            _ => L(
                "WebDAV 配置备份操作失败，请检查服务器权限后重试。",
                "WebDAV 設定バックアップ操作に失敗しました。サーバー権限を確認して再試行してください。",
                "The WebDAV configuration backup operation failed. Check server permissions and try again.")
        };

    private static Style? FindAppStyle(string key)
    {
        try
        {
            return Application.Current.Resources[key] as Style;
        }
        catch
        {
            return null;
        }
    }
}
