using Eizo.Localization;
using Eizo.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Eizo.Views;

public sealed class RestoreBusyChangedEventArgs(bool isBusy) : EventArgs
{
    public bool IsBusy { get; } = isBusy;
}

public sealed partial class FirstRunWebDavRestoreView : UserControl
{
    private readonly AppLocalizationService _localization = AppLocalizationService.Default;
    private readonly ConfigBackupService _backupService = new();
    private readonly MediaCredentialStore _credentials = MediaCredentialStore.Default;
    private bool _isBusy;

    public FirstRunWebDavRestoreView()
    {
        InitializeComponent();
        _localization.LanguageChanged += OnLanguageChanged;
        Unloaded += OnUnloaded;
        ApplyText();
    }

    public event EventHandler<RestoreBusyChangedEventArgs>? BusyChanged;

    private string L(string zh, string ja, string en) =>
        _localization.CurrentLanguage switch
        {
            "ja-JP" => ja,
            "en-US" => en,
            _ => zh
        };

    private void ApplyText()
    {
        IntroInfoBar.Title = L("已有 Eikura 配置？", "既存の Eikura 設定がありますか？", "Already have an Eikura configuration?");
        IntroInfoBar.Message = L(
            "如果你已将配置备份到 WebDAV，可以在这里直接还原。这里填写的是配置备份仓库，不会被添加为媒体来源。没有备份可直接进入下一步。",
            "WebDAV に設定バックアップがある場合は、ここで復元できます。ここで指定する WebDAV は設定バックアップ用で、メディアソースには追加されません。バックアップがなければそのまま次へ進めます。",
            "If you backed up Eikura settings to WebDAV, restore them here. This WebDAV location is only the configuration-backup repository and is never added as a media source. Otherwise, continue to the next step.");
        WebDavUrlBox.Header = L("配置备份 WebDAV 地址", "設定バックアップ WebDAV アドレス", "Configuration backup WebDAV address");
        WebDavUserBox.Header = L("用户名", "ユーザー名", "Username");
        WebDavPasswordBox.Header = L("WebDAV 密码", "WebDAV パスワード", "WebDAV password");
        PrivacyText.Text = L(
            "备份自动使用 WebDAV 密码加密，无需额外密码。凭据仅用于本次还原，操作结束后清除。更换 WebDAV 密码后，请在设置中重新备份。",
            "WebDAV パスワードで自動暗号化するため、追加のパスワードは不要です。認証情報は復元後に消去します。WebDAV パスワード変更後は、設定から再度バックアップしてください。",
            "Backups automatically use the WebDAV password for encryption; no extra password is needed. Credentials are cleared after restoration. Create a new backup after changing your WebDAV password.");
        RestoreButton.Content = L("还原配置", "設定を復元", "Restore configuration");
    }

    private async void OnRestore(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;

        if (!TryBuildBackupEndpointAdapter(out var endpoint, out var error))
        {
            ShowResult(InfoBarSeverity.Error, error!);
            return;
        }

        if (string.IsNullOrEmpty(WebDavPasswordBox.Password))
        {
            ShowResult(
                InfoBarSeverity.Error,
                L("请输入 WebDAV 密码。", "WebDAV パスワードを入力してください。", "Enter the WebDAV password."));
            return;
        }

        SetBusy(true);
        RestoreResultInfoBar.IsOpen = false;
        try
        {
            // ConfigBackupService currently accepts a WebDAV-shaped media definition.
            // This object is a transport adapter only: it receives a unique ephemeral
            // id, is never inserted into MediaSourceStore, and its credential is
            // removed in finally. Therefore the configuration repository can never
            // become or collide with a playback media source.
            var credentialKey = _credentials.SaveWebDav(
                endpoint!.Id,
                endpoint.UserName,
                WebDavPasswordBox.Password);
            endpoint = endpoint with { CredentialKey = credentialKey };

            var result = await _backupService.RestoreAsync(endpoint);

            MediaScanCoordinator.Default.ReloadMetadataService();
            WebDavPasswordBox.Password = string.Empty;

            var skipped = result.SkippedLocalSources > 0
                ? L($"；另有 {result.SkippedLocalSources} 个本地目录在这台设备上不存在，已跳过。",
                    $"。この端末に存在しないローカルフォルダー {result.SkippedLocalSources} 件はスキップしました。",
                    $"; {result.SkippedLocalSources} local folder(s) missing on this device were skipped.")
                : string.Empty;
            ShowResult(
                InfoBarSeverity.Success,
                L($"配置已还原，共恢复 {result.SourceCount} 个备份中的媒体来源{skipped}",
                    $"設定を復元しました。バックアップ内のメディアソース {result.SourceCount} 件を復元しました{skipped}",
                    $"Configuration restored. Restored {result.SourceCount} media source(s) from the backup{skipped}"));
        }
        catch (ConfigBackupException exception)
        {
            ShowResult(InfoBarSeverity.Error, DescribeFailure(exception));
        }
        catch
        {
            ShowResult(
                InfoBarSeverity.Error,
                L("还原失败，请检查配置备份 WebDAV 地址、凭据和网络后重试。",
                  "復元に失敗しました。設定バックアップ WebDAV のアドレス、認証情報、ネットワークを確認してください。",
                  "Restore failed. Check the configuration-backup WebDAV address, credentials, and network, then try again."));
        }
        finally
        {
            if (endpoint is not null)
                _credentials.RemoveWebDav(endpoint.Id);
            WebDavPasswordBox.Password = string.Empty;
            SetBusy(false);
        }
    }

    private bool TryBuildBackupEndpointAdapter(
        out MediaSourceDefinition? endpoint,
        out string? error)
    {
        endpoint = null;
        error = null;
        var raw = WebDavUrlBox.Text?.Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var root) ||
            (root.Scheme != Uri.UriSchemeHttp && root.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(root.UserInfo))
        {
            error = L("请输入有效的 HTTP(S) WebDAV 地址。",
                "有効な HTTP(S) WebDAV アドレスを入力してください。",
                "Enter a valid HTTP(S) WebDAV address.");
            return false;
        }

        var builder = new UriBuilder(root);
        if (!builder.Path.EndsWith('/')) builder.Path += "/";
        root = builder.Uri;
        var userName = string.IsNullOrWhiteSpace(WebDavUserBox.Text)
            ? null
            : WebDavUserBox.Text.Trim();

        // Never derive this id from the WebDAV URI. A unique id guarantees that
        // ConfigBackupService's legacy "preserve destination" behavior cannot
        // accidentally preserve a playback source with the same URI/user.
        var ephemeralId = $"config-backup-{Guid.NewGuid():N}";
        endpoint = new MediaSourceDefinition(
            ephemeralId,
            MediaSourceKind.WebDav,
            "Eikura configuration backup endpoint",
            root.AbsoluteUri,
            UserName: userName,
            CredentialKey: MediaCredentialStore.BuildWebDavCredentialKey(ephemeralId));
        return true;
    }

    private string DescribeFailure(ConfigBackupException exception) => exception.Code switch
    {
        "BackupNotFound" => L("该 WebDAV 位置没有找到 Eikura 配置备份。", "この WebDAV に Eikura 設定バックアップが見つかりません。", "No Eikura configuration backup was found at this WebDAV location."),
        "InvalidPasswordOrBackup" => L("WebDAV 密码与备份时不同，或备份文件已损坏。", "WebDAV パスワードがバックアップ時と異なるか、ファイルが破損しています。", "The WebDAV password differs from the one used for this backup, or the backup is damaged."),
        "LegacyBackupRequiresMigration" => L("找到旧版额外密码加密的备份，请先用旧版还原，再使用新版重新备份。", "旧形式のバックアップです。旧バージョンで復元し、新バージョンで再バックアップしてください。", "This legacy backup uses a separate password. Restore it with the previous version, then create a new backup."),
        "AuthenticationFailed" or "WebDavReadFailed" => L("WebDAV 认证失败或服务器拒绝读取备份。", "WebDAV の認証に失敗したか、サーバーが読み取りを拒否しました。", "WebDAV authentication failed or the server refused the backup download."),
        "Timeout" => L("连接 WebDAV 超时，请稍后重试。", "WebDAV 接続がタイムアウトしました。", "The WebDAV connection timed out."),
        "NetworkError" => L("无法连接 WebDAV，请检查网络和地址。", "WebDAV に接続できません。ネットワークとアドレスを確認してください。", "Unable to reach WebDAV. Check the network and address."),
        "UnsupportedBackup" => L("此备份格式不受当前版本支持。", "このバックアップ形式は現在のバージョンではサポートされていません。", "This backup format is not supported by the current version."),
        "MissingWebDavPassword" => L("请输入 WebDAV 密码。", "WebDAV パスワードを入力してください。", "Enter the WebDAV password."),
        _ => L("无法还原该配置备份。", "設定バックアップを復元できませんでした。", "The configuration backup could not be restored.")
    };

    private void ShowResult(InfoBarSeverity severity, string message)
    {
        RestoreResultInfoBar.Severity = severity;
        RestoreResultInfoBar.Title = severity == InfoBarSeverity.Success
            ? L("还原完成", "復元完了", "Restore complete")
            : L("无法还原", "復元できません", "Restore unavailable");
        RestoreResultInfoBar.Message = message;
        RestoreResultInfoBar.IsOpen = true;
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        RestoreButton.IsEnabled = !busy;
        WebDavUrlBox.IsEnabled = !busy;
        WebDavUserBox.IsEnabled = !busy;
        WebDavPasswordBox.IsEnabled = !busy;
        RestoreProgressRing.IsActive = busy;
        RestoreProgressRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        BusyChanged?.Invoke(this, new RestoreBusyChangedEventArgs(busy));
    }

    private void OnLanguageChanged(object? sender, AppLanguageChangedEventArgs e) =>
        DispatcherQueue.TryEnqueue(ApplyText);

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        Unloaded -= OnUnloaded;
    }
}
