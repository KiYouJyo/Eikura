using Eizo.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using Windows.System;

namespace Eizo.Views;

public sealed partial class AboutView : UserControl
{
    private static readonly Uri ProductRepositoryUri = new("https://github.com/KiYouJyo/Eikura");
    private static readonly Uri ReleasesUri = new("https://github.com/KiYouJyo/Eikura/releases");
    private static readonly Uri PrivacyUri = new("https://github.com/KiYouJyo/Eikura/blob/main/PRIVACY.md");
    private static readonly Uri TmdbUri = new("https://www.themoviedb.org");
    private readonly AppLocalizationService _localization = AppLocalizationService.Default;
    private readonly AboutUpdateSessionState _updates = AboutUpdateSessionState.Default;

    public AboutView()
    {
        InitializeComponent();
        ApplyText();
        PopulateApplicationInfo();
        RenderProductUpdate();
        Loaded += AboutView_Loaded;
        Unloaded += AboutView_Unloaded;
    }

    private string T(string key) => _localization.GetString(key);

    private string L(string zh, string ja, string en) => _localization.CurrentLanguage switch
    {
        "ja-JP" => ja,
        "en-US" => en,
        _ => zh
    };

    private void AboutView_Loaded(object sender, RoutedEventArgs e)
    {
        _updates.Changed -= Updates_Changed;
        _updates.Changed += Updates_Changed;
        PopulateApplicationInfo();
        RenderProductUpdate();
    }

    private void AboutView_Unloaded(object sender, RoutedEventArgs e) =>
        _updates.Changed -= Updates_Changed;

    private void Updates_Changed(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (XamlRoot is null) return;
            RenderProductUpdate();
            });
    }

    private void PopulateApplicationInfo()
    {
        DisplayVersionText.Text = AppVersionProvider.DisplayVersion;
        PackageVersionText.Text = AppVersionProvider.GetPackageVersion();
        ArchitectureText.Text = RuntimeInformation.ProcessArchitecture.ToString();
        CurrentAppVersionText.Text = AppVersionProvider.DisplayVersion;
        var store = ProductAppUpdateService.IsMicrosoftStoreBuild;
        ChannelText.Text = store ? "Microsoft Store" : L("GitHub 侧载", "GitHub サイドロード", "GitHub sideload");
        UpdateSourceText.Text = store ? "Microsoft Store" : "GitHub Releases";
        ReleaseNotesButton.Visibility = store ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var info = _updates.ProductInfo;
        if (info.IsReadyToInstall)
        {
            await _updates.InstallProductUpdateAsync();
            return;
        }

        if (info.IsUpdateAvailable)
        {
            await _updates.DownloadProductUpdateAsync();
            return;
        }

        await _updates.CheckProductUpdateAsync();
    }

    private async void ReleaseNotesButton_Click(object sender, RoutedEventArgs e)
    {
        var uri = _updates.ProductInfo.Release is { HtmlUrl.Length: > 0 } release
            ? new Uri(release.HtmlUrl)
            : ReleasesUri;
        await Launcher.LaunchUriAsync(uri);
    }

    private async void OpenRepositoryButton_Click(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(ProductRepositoryUri);

    private async void OpenReleasesButton_Click(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(ReleasesUri);

    private async void OpenPrivacyButton_Click(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(PrivacyUri);

    private async void OpenTmdbButton_Click(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(TmdbUri);

    private void RenderProductUpdate()
    {
        var info = _updates.ProductInfo;

        AvailableAppVersionText.Text = string.IsNullOrWhiteSpace(info.AvailableVersion)
            ? "—"
            : $"v{info.AvailableVersion}";

        AppUpdateStatusText.Text = ResolveProductUpdateStatus(info);
        ToolTipService.SetToolTip(
            AppUpdateStatusText,
            info.State == AppUpdateState.Failed && !string.IsNullOrWhiteSpace(info.Detail)
                ? info.Detail
                : null);

        CheckUpdateButton.Content = info.State switch
        {
            AppUpdateState.UpdateAvailable => ProductAppUpdateService.IsMicrosoftStoreBuild
                ? L("下载并安装", "ダウンロードしてインストール", "Download & install")
                : L("下载并验证", "ダウンロードして検証", "Download & verify"),
            AppUpdateState.ReadyToInstall =>
                RestartUpdateButtonText(),
            AppUpdateState.Downloading =>
                L("正在下载", "ダウンロード中", "Downloading"),
            AppUpdateState.Verifying =>
                L("正在验证", "検証中", "Verifying"),
            AppUpdateState.Installing or AppUpdateState.Restarting =>
                L("正在更新", "更新中", "Updating"),
            AppUpdateState.Failed or AppUpdateState.Cancelled =>
                T("Common_Retry"),
            _ => T("About_CheckUpdates")
        };

        var busy = info.State is
            AppUpdateState.Checking or
            AppUpdateState.Downloading or
            AppUpdateState.Verifying or
            AppUpdateState.Installing or
            AppUpdateState.Restarting;

        CheckUpdateButton.IsEnabled = _updates.CanOperateProductUpdate && !busy;

        var progressVisible = info.State is
            AppUpdateState.Downloading or
            AppUpdateState.Verifying or
            AppUpdateState.Installing or
            AppUpdateState.Restarting;
        AppUpdateProgressBar.Visibility = progressVisible ? Visibility.Visible : Visibility.Collapsed;
        AppUpdateProgressBar.IsIndeterminate =
            info.State is not AppUpdateState.Downloading || _updates.ProductProgress is null;
        if (_updates.ProductProgress is double progress)
            AppUpdateProgressBar.Value = progress * 100d;
    }

    private string RestartUpdateButtonText() =>
        L("重启并更新", "再起動して更新", "Restart to update");

    private string ResolveProductUpdateStatus(AppUpdateInfo info) => info.State switch
    {
        AppUpdateState.NotChecked => T("About_NotChecked"),
        AppUpdateState.Checking => L("正在检查更新…", "更新を確認しています…", "Checking for updates…"),
        AppUpdateState.UpToDate => L("已是最新版本", "最新バージョンです", "Up to date"),
        AppUpdateState.UpdateAvailable => L("发现新版本", "新しいバージョンがあります", "Update available"),
        AppUpdateState.Downloading => _updates.ProductProgress is double progress
            ? $"{L("正在下载…", "ダウンロード中…", "Downloading…")} {progress:P0}"
            : L("正在下载…", "ダウンロード中…", "Downloading…"),
        AppUpdateState.Verifying => L("正在验证安装包…", "パッケージを検証しています…", "Verifying package…"),
        AppUpdateState.ReadyToInstall => L(
            "安装包已验证，可安装并重启",
            "パッケージ検証済み。インストールして再起動できます",
            "Package verified; ready to install and restart"),
        AppUpdateState.Installing => L("正在安装更新…", "更新をインストールしています…", "Installing update…"),
        AppUpdateState.Restarting => L("正在重启以完成更新…", "更新完了のため再起動しています…", "Restarting to finish update…"),
        AppUpdateState.Completed => L("更新已完成", "更新が完了しました", "Update completed"),
        AppUpdateState.Cancelled => L("更新已取消", "更新はキャンセルされました", "Update cancelled"),
        AppUpdateState.Failed => ResolveProductUpdateError(info.ErrorCode),
        _ => T("About_NotChecked")
    };

    private string ResolveProductUpdateError(string? errorCode) => errorCode switch
    {
        "ReleaseNotFound" =>
            L("未找到可用发行版", "利用可能なリリースが見つかりません", "No release was found"),
        "UnableToContactGitHub" or "DownloadNetwork" or "DownloadTimeout" =>
            L("无法连接 GitHub", "GitHub に接続できません", "Unable to contact GitHub"),
        "BundleAssetNotFound" =>
            L("发行版缺少唯一的 MSIXBundle 或校验清单", "リリースに MSIXBundle またはチェックサム一覧がありません", "The release is missing the MSIXBundle or checksum manifest"),
        "ChecksumMissing" or "ChecksumMismatch" =>
            L("安装包 SHA-256 校验失败", "パッケージの SHA-256 検証に失敗しました", "Package SHA-256 verification failed"),
        "SignatureMissing" or "SignatureInvalid" or "SignatureReadFailed" or "SignerSubjectMismatch" or "SignerThumbprintMismatch" =>
            L("安装包签名验证失败", "パッケージ署名の検証に失敗しました", "Package signature verification failed"),
        "PendingStateWriteFailed" or "UpdateStorageFailed" =>
            L("无法保存已下载的更新文件", "ダウンロード済み更新ファイルを保存できません", "Unable to store the downloaded update"),
        "PackageDeploymentFailed" =>
            L("Windows 安装更新失败", "Windows で更新をインストールできませんでした", "Windows failed to install the update"),
        "NoPendingUpdate" =>
            L("没有已验证的待安装更新", "検証済みの更新がありません", "No verified update is ready to install"),
        "Cancelled" =>
            L("更新已取消", "更新はキャンセルされました", "Update cancelled"),
        null or "" =>
            L("更新失败", "更新に失敗しました", "Update failed"),
        _ when errorCode.StartsWith("0x", StringComparison.OrdinalIgnoreCase) =>
            $"{L("Windows 安装更新失败", "Windows で更新をインストールできませんでした", "Windows failed to install the update")} · {errorCode}",
        _ => $"{L("更新失败", "更新に失敗しました", "Update failed")} · {errorCode}"
    };

    private void ApplyText()
    {
        PageTitle.Text = T("Nav_About");
        AboutDescription.Text = T("App_Description");

        DisplayVersionLabel.Text = T("About_DisplayVersion");
        InternalVersionLabel.Text = T("About_InternalVersion");
        ArchitectureLabel.Text = T("About_Architecture");
        ChannelLabel.Text = T("About_Channel");
        PublisherLabel.Text = T("About_Publisher");
        TechLabel.Text = T("About_TechStack");

        UpdateTitle.Text = T("About_UpdateManagement");
        ApplicationUpdateLabel.Text = T("About_ApplicationUpdate");
        CurrentVersionLabel.Text = T("About_CurrentVersion");
        AvailableVersionLabel.Text = T("About_AvailableVersion");
        UpdateSourceLabel.Text = T("About_UpdateSource");
        UpdateStatusLabel.Text = T("About_Status");
        ReleaseNotesButton.Content = T("About_ReleaseNotes");
        CheckUpdateButton.Content = T("About_CheckUpdates");

        TmdbAttributionTitle.Text =
            L("数据来源", "データソース", "Data source");
        TmdbAttributionNotice.Text =
            "This product uses the TMDB API but is not endorsed or certified by TMDB.";
        OpenTmdbButton.Content =
            L("访问 TMDB", "TMDB を開く", "Open TMDB");

        ProjectTitle.Text = T("About_ProjectOpenSource");
        GitHubDescription.Text = T("About_GitHubDescription");
        ReleasesDescription.Text = T("About_ReleasesDescription");
        PrivacyTitle.Text = T("About_Privacy");
        PrivacyDescription.Text = T("About_PrivacyDescription");
        OpenRepositoryButton.Content = T("About_OpenRepository");
        OpenReleasesButton.Content = T("About_OpenReleases");
        OpenPrivacyButton.Content = T("About_OpenPrivacy");
    }
}
