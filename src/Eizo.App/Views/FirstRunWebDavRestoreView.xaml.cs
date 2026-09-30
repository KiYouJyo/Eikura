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
            "如果你已将配置备份到 WebDAV，可以在这里直接还原。没有备份可直接进入下一步。",
            "WebDAV に設定バックアップがある場合は、ここで復元できます。バックアップがなければそのまま次へ進めます。",
            "If you backed up Eikura settings to WebDAV, restore them here. Otherwise, continue to the next step.");
        WebDavUrlBox.Header = L("WebDAV 地址", "WebDAV アドレス", "WebDAV address");
        WebDavUserBox.Header = L("用户名", "ユーザー名", "Username");
        WebDavPasswordBox.Header = L("WebDAV 密码", "WebDAV パスワード", "WebDAV password");
        BackupPassphraseBox.Header = L("配置备份密码", "設定バックアップのパスワード", "Configuration backup passphrase");
        PrivacyText.Text = L(
            "WebDAV 密码将保存到 Windows PasswordVault；配置备份密码只用于本次解密，不会保存。",
            "WebDAV パスワードは Windows PasswordVault に保存されます。バックアップ用パスワードは今回の復号だけに使い、保存しません。",
            "The WebDAV password is stored in Windows PasswordVault. The backup passphrase is used only for this restore and is never saved.");
        RestoreButton.Content = L("还原配置", "設定を復元", "Restore configuration");
    }

    private async void OnRestore(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;

        if (!TryBuildDestination(out var destination, out var error))
        {
            ShowResult(InfoBarSeverity.Error, error!);
            return;
        }

        var passphrase = BackupPassphraseBox.Password;
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            ShowResult(
                InfoBarSeverity.Error,
                L("请输入配置备份密码。", "設定バックアップのパスワードを入力してください。", "Enter the configuration backup passphrase."));
            return;
        }

        var previousSource = MediaSourceStore.Default.Find(destination!.Id);
        var previousCredential = previousSource is null
            ? null
            : _credentials.GetWebDav(previousSource);

        SetBusy(true);
        RestoreResultInfoBar.IsOpen = false;
        try
        {
            var credentialKey = _credentials.SaveWebDav(
                destination.Id,
                destination.UserName,
                WebDavPasswordBox.Password);
            destination = destination with { CredentialKey = credentialKey };

            var result = await _backupService.RestoreAsync(destination, passphrase);

            // The backup destination credential is deliberately excluded from the
            // encrypted backup. Re-save it locally after restore and ensure that
            // the destination remains a normal WebDAV media source.
            credentialKey = _credentials.SaveWebDav(
                destination.Id,
                destination.UserName,
                WebDavPasswordBox.Password);
            var persisted = MediaSourceStore.Default.Find(destination.Id);
            if (persisted is null)
            {
                MediaSourceStore.Default.AddWebDav(
                    destination.DisplayName,
                    new Uri(destination.RootLocation!, UriKind.Absolute),
                    destination.UserName,
                    credentialKey);
            }

            MediaScanCoordinator.Default.ReloadMetadataService();
            BackupPassphraseBox.Password = string.Empty;
            WebDavPasswordBox.Password = string.Empty;

            var skipped = result.SkippedLocalSources > 0
                ? L($"；另有 {result.SkippedLocalSources} 个本地目录在这台设备上不存在，已跳过。",
                    $"。この端末に存在しないローカルフォルダー {result.SkippedLocalSources} 件はスキップしました。",
                    $"; {result.SkippedLocalSources} local folder(s) missing on this device were skipped.")
                : string.Empty;
            ShowResult(
                InfoBarSeverity.Success,
                L($"配置已还原，共读取 {result.SourceCount} 个媒体来源{skipped}",
                    $"設定を復元しました。メディアソース {result.SourceCount} 件を読み込みました{skipped}",
                    $"Configuration restored. Loaded {result.SourceCount} media source(s){skipped}"));
        }
        catch (ConfigBackupException exception)
        {
            RestorePreviousCredential(destination.Id, previousSource, previousCredential);
            ShowResult(InfoBarSeverity.Error, DescribeFailure(exception));
        }
        catch
        {
            RestorePreviousCredential(destination.Id, previousSource, previousCredential);
            ShowResult(
                InfoBarSeverity.Error,
                L("还原失败，请检查 WebDAV 地址、凭据和网络后重试。",
                  "復元に失敗しました。WebDAV のアドレス、認証情報、ネットワークを確認してください。",
                  "Restore failed. Check the WebDAV address, credentials, and network, then try again."));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool TryBuildDestination(
        out MediaSourceDefinition? destination,
        out string? error)
    {
        destination = null;
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
        var sourceId = MediaSourceStore.BuildWebDavSourceId(root, userName);
        destination = new MediaSourceDefinition(
            sourceId,
            MediaSourceKind.WebDav,
            L("配置备份 WebDAV", "設定バックアップ WebDAV", "Configuration backup WebDAV"),
            root.AbsoluteUri,
            UserName: userName,
            CredentialKey: MediaCredentialStore.BuildWebDavCredentialKey(sourceId));
        return true;
    }

    private void RestorePreviousCredential(
        string sourceId,
        MediaSourceDefinition? previousSource,
        MediaCredentialSnapshot? previousCredential)
    {
        _credentials.RemoveWebDav(sourceId);
        if (previousSource is not null && previousCredential is not null)
        {
            _credentials.SaveWebDav(
                previousSource.Id,
                previousCredential.UserName,
                previousCredential.Password);
        }
    }

    private string DescribeFailure(ConfigBackupException exception) => exception.Code switch
    {
        "BackupNotFound" => L("该 WebDAV 位置没有找到 Eikura 配置备份。", "この WebDAV に Eikura 設定バックアップが見つかりません。", "No Eikura configuration backup was found at this WebDAV location."),
        "InvalidPasswordOrBackup" => L("备份密码错误，或备份文件已损坏。", "バックアップのパスワードが違うか、ファイルが破損しています。", "The backup passphrase is incorrect or the backup file is damaged."),
        "Unauthorized" or "WebDavReadFailed" => L("WebDAV 认证失败或服务器拒绝读取备份。", "WebDAV の認証に失敗したか、サーバーが読み取りを拒否しました。", "WebDAV authentication failed or the server refused the backup download."),
        "Timeout" => L("连接 WebDAV 超时，请稍后重试。", "WebDAV 接続がタイムアウトしました。", "The WebDAV connection timed out."),
        "NetworkError" => L("无法连接 WebDAV，请检查网络和地址。", "WebDAV に接続できません。ネットワークとアドレスを確認してください。", "Unable to reach WebDAV. Check the network and address."),
        "UnsupportedBackup" => L("此备份格式不受当前版本支持。", "このバックアップ形式は現在のバージョンではサポートされていません。", "This backup format is not supported by the current version."),
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
        BackupPassphraseBox.IsEnabled = !busy;
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
