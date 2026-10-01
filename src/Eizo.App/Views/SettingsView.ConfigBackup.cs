using Eizo.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Eizo.Views;

public sealed partial class SettingsView
{
    private readonly ConfigBackupService _configBackupService = new();
    private bool _configBackupRunning;
    private Border? _configBackupCard;
    private TextBox? _configBackupUrlBox;
    private TextBox? _configBackupUserBox;
    private PasswordBox? _configBackupWebDavPasswordBox;
    private TextBlock? _configBackupStatusText;
    private Button? _configBackupNowButton;
    private Button? _configRestoreButton;

    private void EnsureConfigBackupCard()
    {
        if (_configBackupCard is not null || GeneralSettingsPanel is null) return;
        var section = new StackPanel { Spacing = 14 };
        section.Children.Add(new TextBlock
        {
            Text = L("配置备份与恢复", "設定のバックアップと復元", "Configuration backup and restore"),
            Style = FindAppStyle("SectionTitleText")
        });
        var content = new StackPanel { Padding = new Thickness(14), Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = L(
                "连接独立的 WebDAV 配置备份仓库。这里的地址不会加入媒体来源。备份自动使用 WebDAV 密码加密，无需设置额外密码；更换 WebDAV 密码后，请重新备份。",
                "設定バックアップ専用の WebDAV に接続します。このアドレスはメディアソースに追加されません。WebDAV パスワードで自動暗号化し、追加のパスワードは不要です。パスワード変更後は再バックアップしてください。",
                "Connect to a dedicated WebDAV backup repository. This address is never added as a media source. Backups automatically use the WebDAV password for encryption; no extra password is needed. Create a new backup after changing your WebDAV password."),
            TextWrapping = TextWrapping.Wrap,
            Style = FindAppStyle("SettingsDescriptionStyle")
        });
        _configBackupUrlBox = new TextBox
        {
            Header = L("配置备份 WebDAV 地址", "設定バックアップ WebDAV アドレス", "WebDAV backup location"),
            PlaceholderText = "https://example.com/dav/"
        };
        content.Children.Add(_configBackupUrlBox);
        _configBackupUserBox = new TextBox { Header = L("用户名", "ユーザー名", "Username") };
        content.Children.Add(_configBackupUserBox);
        _configBackupWebDavPasswordBox = new PasswordBox
        {
            Header = L("WebDAV 密码", "WebDAV パスワード", "WebDAV password"),
            PasswordRevealMode = PasswordRevealMode.Peek
        };
        content.Children.Add(_configBackupWebDavPasswordBox);
        content.Children.Add(new TextBlock
        {
            Text = L(
                "包含媒体来源及凭据、应用内 TMDB Token、元数据、播放/字幕和外观设置。不会备份播放历史、媒体数据库、缓存或备份仓库自身的密码。WebDAV 凭据只用于本次操作，结束后清除。",
                "メディアソースと認証情報、アプリ内 TMDB Token、メタデータ、再生・字幕、外観設定を含みます。再生履歴、メディアDB、キャッシュ、バックアップ先自身のパスワードは対象外です。認証情報は処理後に消去します。",
                "Includes media sources and credentials, the in-app TMDB token, metadata, playback/subtitle, and appearance settings. Excludes playback history, media database, cache, and the backup repository's own password. WebDAV credentials are cleared after each operation."),
            TextWrapping = TextWrapping.Wrap,
            Style = FindAppStyle("MetadataText")
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _configBackupNowButton = new Button { Content = L("立即备份", "今すぐバックアップ", "Back up now") };
        _configBackupNowButton.Click += ConfigBackupNowButton_Click;
        actions.Children.Add(_configBackupNowButton);
        _configRestoreButton = new Button { Content = L("从备份恢复", "バックアップから復元", "Restore backup") };
        _configRestoreButton.Click += ConfigRestoreButton_Click;
        actions.Children.Add(_configRestoreButton);
        content.Children.Add(actions);
        _configBackupStatusText = new TextBlock
        {
            Text = L("尚未执行备份或恢复。", "まだ処理を実行していません。", "No backup or restore has been run yet."),
            TextWrapping = TextWrapping.Wrap,
            Style = FindAppStyle("MetadataText")
        };
        content.Children.Add(_configBackupStatusText);
        section.Children.Add(new Border { Style = FindAppStyle("SettingsInnerCardStyle"), Child = content });
        _configBackupCard = new Border { Style = FindAppStyle("SettingsSectionCardStyle"), Child = section };
        GeneralSettingsPanel.Children.Insert(Math.Min(1, GeneralSettingsPanel.Children.Count), _configBackupCard);
    }

    private async void ConfigBackupNowButton_Click(object sender, RoutedEventArgs e) =>
        await RunConfigBackupOperationAsync(restore: false);

    private async void ConfigRestoreButton_Click(object sender, RoutedEventArgs e) =>
        await RunConfigBackupOperationAsync(restore: true);

    private async Task RunConfigBackupOperationAsync(bool restore)
    {
        if (_configBackupRunning || !TryGetConfigBackupInput(out var destination)) return;
        SetConfigBackupBusy(true);
        var credentials = MediaCredentialStore.Default;
        try
        {
            if (restore)
            {
                if (XamlRoot is null) return;
                var confirm = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = L("恢复 Eikura 配置？", "Eikura の設定を復元しますか？", "Restore Eikura configuration?"),
                    Content = L("恢复会替换媒体来源和可迁移设置。播放历史、媒体数据库和缓存不会被修改。",
                        "メディアソースと設定を置き換えます。再生履歴・メディアDB・キャッシュは変更しません。",
                        "Restore replaces media sources and portable settings. Playback history, media database, and cache are not changed."),
                    PrimaryButtonText = L("恢复", "復元", "Restore"),
                    CloseButtonText = L("取消", "キャンセル", "Cancel"),
                    DefaultButton = ContentDialogButton.Close
                };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            }
            var credentialKey = credentials.SaveWebDav(destination!.Id, destination.UserName, _configBackupWebDavPasswordBox!.Password);
            destination = destination with { CredentialKey = credentialKey };
            SetConfigBackupStatus(restore
                ? L("正在还原配置…", "設定を復元しています…", "Restoring configuration…")
                : L("正在备份配置…", "設定をバックアップしています…", "Backing up configuration…"));
            var result = restore
                ? await _configBackupService.RestoreAsync(destination)
                : await _configBackupService.BackupAsync(destination);
            if (restore)
            {
                MediaScanCoordinator.Default.ReloadMetadataService();
                SyncControlsFromSettings();
                RefreshTmdbCredentialState();
                if (XamlRoot?.Content is FrameworkElement root) root.RequestedTheme = ThemePreferenceStore.Load();
            }
            SetConfigBackupStatus(restore
                ? L($"恢复完成：备份包含 {result.SourceCount} 个媒体来源；已跳过 {result.SkippedLocalSources} 个不存在的本地目录。",
                    $"復元完了：メディアソース {result.SourceCount} 件。存在しないフォルダー {result.SkippedLocalSources} 件をスキップしました。",
                    $"Restore complete: {result.SourceCount} media sources in the backup; {result.SkippedLocalSources} missing local folders skipped.")
                : L($"备份完成：已保存 {result.SourceCount} 个媒体来源配置。",
                    $"バックアップ完了：メディアソース {result.SourceCount} 件を保存しました。",
                    $"Backup complete: {result.SourceCount} media source configurations saved."));
        }
        catch (ConfigBackupException exception) { SetConfigBackupStatus(ConfigBackupErrorText(exception)); }
        catch { SetConfigBackupStatus(L("操作失败，请检查 WebDAV 地址、权限和网络后重试。", "WebDAV のアドレス・権限・ネットワークを確認してください。", "Operation failed. Check the WebDAV address, permissions, and network.")); }
        finally
        {
            if (destination is not null) credentials.RemoveWebDav(destination.Id);
            if (_configBackupWebDavPasswordBox is not null) _configBackupWebDavPasswordBox.Password = string.Empty;
            SetConfigBackupBusy(false);
        }
    }

    private bool TryGetConfigBackupInput(out MediaSourceDefinition? destination)
    {
        destination = null;
        if (!Uri.TryCreate(_configBackupUrlBox?.Text?.Trim(), UriKind.Absolute, out var root) ||
            (root.Scheme != Uri.UriSchemeHttps && root.Scheme != Uri.UriSchemeHttp) || !string.IsNullOrEmpty(root.UserInfo))
        {
            SetConfigBackupStatus(L("请输入有效的 HTTP(S) WebDAV 地址。", "有効な HTTP(S) WebDAV アドレスを入力してください。", "Enter a valid HTTP(S) WebDAV address."));
            return false;
        }
        if (string.IsNullOrEmpty(_configBackupWebDavPasswordBox?.Password))
        {
            SetConfigBackupStatus(L("请输入 WebDAV 密码。", "WebDAV パスワードを入力してください。", "Enter the WebDAV password."));
            return false;
        }
        var builder = new UriBuilder(root);
        if (!builder.Path.EndsWith('/')) builder.Path += "/";
        var id = $"config-backup-{Guid.NewGuid():N}";
        destination = new MediaSourceDefinition(id, MediaSourceKind.WebDav, "Eikura configuration backup endpoint",
            builder.Uri.AbsoluteUri, UserName: _configBackupUserBox?.Text?.Trim(),
            CredentialKey: MediaCredentialStore.BuildWebDavCredentialKey(id));
        return true;
    }

    private void SetConfigBackupBusy(bool busy)
    {
        _configBackupRunning = busy;
        if (_configBackupUrlBox is not null) _configBackupUrlBox.IsEnabled = !busy;
        if (_configBackupUserBox is not null) _configBackupUserBox.IsEnabled = !busy;
        if (_configBackupWebDavPasswordBox is not null) _configBackupWebDavPasswordBox.IsEnabled = !busy;
        if (_configBackupNowButton is not null) _configBackupNowButton.IsEnabled = !busy;
        if (_configRestoreButton is not null) _configRestoreButton.IsEnabled = !busy;
    }

    private void SetConfigBackupStatus(string text)
    {
        if (_configBackupStatusText is not null) _configBackupStatusText.Text = text;
    }

    private string ConfigBackupErrorText(ConfigBackupException exception) => exception.Code switch
    {
        "BackupNotFound" => L("没有找到 Eikura 配置备份。", "設定バックアップが見つかりません。", "No Eikura configuration backup was found."),
        "AuthenticationFailed" => L("WebDAV 认证失败，请检查用户名和密码。", "WebDAV 認証に失敗しました。", "WebDAV authentication failed. Check your username and password."),
        "InvalidPasswordOrBackup" => L("WebDAV 密码与备份时不同，或备份已损坏。", "WebDAV パスワードがバックアップ時と異なるか、ファイルが破損しています。", "The WebDAV password differs from the one used for this backup, or the backup is damaged."),
        "LegacyBackupRequiresMigration" => L("找到旧版额外密码加密的备份，请先用旧版还原，再使用新版重新备份。", "旧バージョンで復元し、新バージョンで再バックアップしてください。", "This legacy backup uses a separate password. Restore it with the previous version, then create a new backup."),
        "MissingWebDavPassword" => L("请输入 WebDAV 密码。", "WebDAV パスワードを入力してください。", "Enter the WebDAV password."),
        "InvalidBackup" or "UnsupportedBackup" => L("备份文件已损坏或格式不受支持。", "未対応のバックアップ形式です。", "The backup is damaged or its format is unsupported."),
        "Timeout" => L("WebDAV 请求超时，请重试。", "WebDAV リクエストがタイムアウトしました。", "The WebDAV request timed out. Try again."),
        _ => L("操作失败，请检查 WebDAV 权限和网络。", "WebDAV の権限・ネットワークを確認してください。", "Operation failed. Check WebDAV permissions and network.")
    };

    private static Style? FindAppStyle(string key)
    {
        try { return Application.Current.Resources[key] as Style; }
        catch (KeyNotFoundException) { return null; }
    }
}
