using Eizo.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using System.Runtime.InteropServices;
using Windows.System;

namespace Eizo.Views;

public sealed partial class AboutView : UserControl
{
    private static readonly Uri ProductRepositoryUri = new("https://github.com/KiYouJyo/Eikura");
    private static readonly Uri ReleasesUri = new("https://github.com/KiYouJyo/Eikura/releases");
    private static readonly Uri MetadataReleasesUri = new("https://github.com/KiYouJyo/Eikura.Metadata/releases");
    private static readonly Uri PrivacyUri = new("https://github.com/KiYouJyo/Eikura/blob/main/PRIVACY.md");
    private static readonly Uri TmdbUri = new("https://www.themoviedb.org");
    private readonly AppLocalizationService _localization = AppLocalizationService.Default;
    private readonly AboutUpdateSessionState _updates = AboutUpdateSessionState.Default;
    private double? _recognitionProgress;

    public AboutView()
    {
        InitializeComponent();
        ApplyText();
        PopulateApplicationInfo();
        RenderProductUpdate();
        RenderComponentUpdates();
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
        RenderComponentUpdates();
    }

    private void AboutView_Unloaded(object sender, RoutedEventArgs e) =>
        _updates.Changed -= Updates_Changed;

    private void Updates_Changed(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (XamlRoot is null) return;
            RenderProductUpdate();
            RenderComponentUpdates();
        });
    }

    private void PopulateApplicationInfo()
    {
        DisplayVersionText.Text = AppVersionProvider.DisplayVersion;
        PackageVersionText.Text = AppVersionProvider.GetPackageVersion();
        ArchitectureText.Text = RuntimeInformation.ProcessArchitecture.ToString();
        CurrentAppVersionText.Text = AppVersionProvider.DisplayVersion;
        ChannelText.Text = L("GitHub 侧载", "GitHub サイドロード", "GitHub sideload");
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

    private async void CheckRecognitionUpdateButton_Click(object sender, RoutedEventArgs e) =>
        await HandleComponentUpdateAsync(
            _updates.RecognitionUpdateService,
            () => _updates.RecognitionResult,
            value => _updates.RecognitionResult = value,
            value => _recognitionProgress = value);

    private async Task HandleComponentUpdateAsync(
        ComponentUpdateService service,
        Func<ComponentUpdateResult> getResult,
        Action<ComponentUpdateResult> setResult,
        Action<double?> setProgress)
    {
        var result = getResult();
        if (result.State == ComponentUpdateState.ReadyForRestart)
        {
            RestartToApplyComponentUpdates(setResult, result);
            return;
        }

        if (result.State == ComponentUpdateState.UpdateAvailable)
        {
            setProgress(null);
            var progress = new Progress<ComponentUpdateProgress>(value =>
            {
                setProgress(value.Fraction);
                var current = getResult();
                setResult(current with { State = value.State, ErrorCode = null, ErrorDetail = null });
            });

            try
            {
                setResult(await service.DownloadAndStageAsync(progress));
            }
            catch (OperationCanceledException)
            {
                setResult(getResult() with
                {
                    State = ComponentUpdateState.Failed,
                    ErrorCode = "Cancelled",
                    ErrorDetail = null
                });
            }
            finally
            {
                setProgress(null);
                RenderComponentUpdates();
            }
            return;
        }

        setProgress(null);
        setResult(result with
        {
            State = ComponentUpdateState.Checking,
            ErrorCode = null,
            ErrorDetail = null
        });

        try
        {
            setResult(await service.CheckForUpdatesAsync());
        }
        catch (OperationCanceledException)
        {
            setResult(getResult() with
            {
                State = ComponentUpdateState.Failed,
                ErrorCode = "Cancelled",
                ErrorDetail = null
            });
        }
    }

    private void RestartToApplyComponentUpdates(
        Action<ComponentUpdateResult> setResult,
        ComponentUpdateResult result)
    {
        var failureReason = AppInstance.Restart(string.Empty);
        setResult(result with
        {
            State = ComponentUpdateState.Failed,
            ErrorCode = "RestartFailed",
            ErrorDetail = $"Windows App SDK AppInstance.Restart failed: {failureReason}."
        });
    }

    private async void ReleaseNotesButton_Click(object sender, RoutedEventArgs e)
    {
        var uri = _updates.ProductInfo.Release is { HtmlUrl.Length: > 0 } release
            ? new Uri(release.HtmlUrl)
            : ReleasesUri;
        await Launcher.LaunchUriAsync(uri);
    }

    private async void RecognitionReleaseNotesButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var uri = _updates.RecognitionUpdateService.LatestRelease is { HtmlUrl.Length: > 0 } release
            ? new Uri(release.HtmlUrl)
            : MetadataReleasesUri;
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
            AppUpdateState.UpdateAvailable =>
                L("下载并验证", "ダウンロードして検証", "Download & verify"),
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

    private void RenderComponentUpdates()
    {
        RenderComponentUpdate(
            _updates.RecognitionResult,
            _recognitionProgress,
            RecognitionCurrentVersionText,
            RecognitionAvailableVersionText,
            RecognitionUpdateStatusText,
            CheckRecognitionUpdateButton,
            RecognitionUpdateProgressBar);
    }

    private void RenderComponentUpdate(
        ComponentUpdateResult result,
        double? progress,
        TextBlock currentVersionText,
        TextBlock availableVersionText,
        TextBlock statusText,
        Button actionButton,
        ProgressBar progressBar)
    {
        ToolTipService.SetToolTip(statusText, null);
        currentVersionText.Text = $"v{ComponentRuntimeBootstrapper.FormatVersion(result.CurrentVersion)}";
        availableVersionText.Text = result.AvailableVersion is null
            ? "—"
            : $"v{ComponentRuntimeBootstrapper.FormatVersion(result.AvailableVersion)}";

        statusText.Text = result.State switch
        {
            ComponentUpdateState.NotChecked => T("About_NotChecked"),
            ComponentUpdateState.Checking => L("正在检查更新…", "更新を確認しています…", "Checking for updates…"),
            ComponentUpdateState.UpToDate => L("已是最新版本", "最新バージョンです", "Up to date"),
            ComponentUpdateState.UpdateAvailable => L("发现新版本", "新しいバージョンがあります", "Update available"),
            ComponentUpdateState.Downloading => progress is double fraction
                ? $"{L("正在下载…", "ダウンロード中…", "Downloading…")} {fraction:P0}"
                : L("正在下载…", "ダウンロード中…", "Downloading…"),
            ComponentUpdateState.Verifying => L("正在验证组件…", "コンポーネントを検証しています…", "Verifying component…"),
            ComponentUpdateState.ReadyForRestart => L("已下载，等待重启", "ダウンロード済み。再起動待ち", "Downloaded; restart required"),
            ComponentUpdateState.Failed => ResolveComponentUpdateError(result.ErrorCode),
            _ => T("About_NotChecked")
        };

        if (result.State == ComponentUpdateState.Failed && !string.IsNullOrWhiteSpace(result.ErrorDetail))
            ToolTipService.SetToolTip(statusText, result.ErrorDetail);

        actionButton.Content = result.State switch
        {
            ComponentUpdateState.UpdateAvailable => L("下载并验证", "ダウンロードして検証", "Download & verify"),
            ComponentUpdateState.ReadyForRestart => RestartUpdateButtonText(),
            ComponentUpdateState.Downloading => L("正在下载", "ダウンロード中", "Downloading"),
            ComponentUpdateState.Verifying => L("正在验证", "検証中", "Verifying"),
            ComponentUpdateState.Failed => T("Common_Retry"),
            _ => T("About_CheckUpdates")
        };

        actionButton.IsEnabled = result.State is not (ComponentUpdateState.Checking or ComponentUpdateState.Downloading or ComponentUpdateState.Verifying);

        var showProgress = result.State is ComponentUpdateState.Downloading or ComponentUpdateState.Verifying;
        progressBar.Opacity = showProgress ? 1 : 0;
        progressBar.IsIndeterminate = result.State != ComponentUpdateState.Downloading || progress is null;
        if (progress is double value) progressBar.Value = value * 100d;
    }

    private string ResolveComponentUpdateError(string? errorCode) => errorCode switch
    {
        "NoRelease" => L("未找到组件发行版", "コンポーネントのリリースが見つかりません", "No component release was found"),
        "InvalidVersion" => L("组件版本号无效", "コンポーネントのバージョンが無効です", "The component version is invalid"),
        "MissingAsset" => L("发行版缺少组件包", "リリースにコンポーネントパッケージがありません", "The release is missing the component package"),
        "MissingManifestAsset" => L("发行版缺少组件清单", "リリースにコンポーネントマニフェストがありません", "The release is missing the component manifest"),
        "ManifestValidation" or "ManifestVersionMismatch" => L("组件清单验证失败", "コンポーネントマニフェストの検証に失敗しました", "Component manifest validation failed"),
        "IncompatibleHostContract" => L("该组件版本与当前 Eikura 不兼容", "このコンポーネントは現在の Eikura と互換性がありません", "This component is incompatible with the current Eikura version"),
        "MissingDigest" or "DigestMismatch" => L("组件 SHA-256 校验失败", "コンポーネントの SHA-256 検証に失敗しました", "Component SHA-256 verification failed"),
        "Timeout" or "DownloadTimeout" or "Network" or "DownloadNetwork" => L("无法连接 GitHub", "GitHub に接続できません", "Unable to contact GitHub"),
        "PackageValidation" or "PackageInvalid" or "VersionMismatch" or "ManifestMismatch" => L("组件包验证失败", "コンポーネントパッケージの検証に失敗しました", "Component package validation failed"),
        "StorageAccess" or "StorageIo" => L("无法保存组件更新", "コンポーネント更新を保存できません", "Unable to store the component update"),
        "NoPendingUpdate" => L("没有可下载的组件更新", "ダウンロード可能なコンポーネント更新がありません", "No component update is ready to download"),
        "RestartFailed" => L("无法重启 Eikura", "Eikura を再起動できません", "Unable to restart Eikura"),
        "Cancelled" => L("更新已取消", "更新はキャンセルされました", "Update cancelled"),
        null or "" => L("更新失败", "更新に失敗しました", "Update failed"),
        _ => $"{L("更新失败", "更新に失敗しました", "Update failed")} · {errorCode}"
    };

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

        ComponentsTitle.Text = L("可独立更新组件", "個別更新可能なコンポーネント", "Independently updateable components");
        RecognitionDescriptionText.Text = L(
            "识别与元数据内核",
            "認識・メタデータコア",
            "Recognition & metadata runtime");
        RecognitionReleaseNotesButton.Content = T("About_ReleaseNotes");
        CheckRecognitionUpdateButton.Content = T("About_CheckUpdates");

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
