using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Windows.ApplicationModel;
using Windows.Management.Deployment;
using Windows.Services.Store;

namespace Eizo;

/// <summary>
/// GitHub application updater ported from UrbanPlanToolbox: check -> download ->
/// checksum/signature verification -> ready-to-install -> Windows package deployment.
/// </summary>
internal sealed class ProductAppUpdateService(IBundleSignatureVerifier? signatureVerifier = null)
{
    internal const string ExpectedSignerSubject = "CN=AppPublisher";
    internal const string ExpectedSignerThumbprint = "BD85AD77A651C86CA01A480C8E9BC64952993F98";
    private const string Repository = "KiYouJyo/Eikura";
    private readonly IBundleSignatureVerifier _signatureVerifier = signatureVerifier ?? new MsixBundleSignatureVerifier();
    private GitHubReleaseInfo? _pendingRelease;
    private string? _pendingBundlePath;
    private bool _updateAvailable;
    private IReadOnlyList<StorePackageUpdate> _storeUpdates = Array.Empty<StorePackageUpdate>();

    internal static bool IsMicrosoftStoreBuild
    {
        get
        {
            try { return !string.Equals(Package.Current.Id.Name, "Eizo", StringComparison.Ordinal); }
            catch { return false; }
        }
    }

    private static string CacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Eizo",
        "Cache");

    private static string PendingStatePath => Path.Combine(CacheRoot, "github-pending-update.json");

    public async Task<AppUpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (IsMicrosoftStoreBuild)
            return await CheckMicrosoftStoreUpdatesAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var currentVersion = AppVersionProvider.GetCurrentVersion();
            var release = await GitHubUpdateService.GetLatestReleaseAsync(Repository, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (release is null) return Fail("ReleaseNotFound");
            if (!GitHubUpdateService.TryParseVersionTag(release.TagName, out var remoteVersion)) return Fail("InvalidReleaseResponse", release: release);

            var displayVersion = release.DisplayVersion;
            _pendingRelease = release;
            _updateAvailable = remoteVersion > currentVersion;
            if (!_updateAvailable)
                return new(AppUpdateState.UpToDate, displayVersion, Release: release);

            LoadPendingState();
            if (!string.IsNullOrWhiteSpace(_pendingBundlePath) && File.Exists(_pendingBundlePath))
                return new(AppUpdateState.ReadyToInstall, displayVersion, "Verified; ready to install", Release: release);

            return new(AppUpdateState.UpdateAvailable, displayVersion, Release: release);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail("UnableToContactGitHub", "Timeout");
        }
        catch (HttpRequestException exception)
        {
            return Fail("UnableToContactGitHub", exception.Message);
        }
    }

    public async Task<AppUpdateResult> DownloadAndPrepareAsync(
        IProgress<AppUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsMicrosoftStoreBuild)
            return await DownloadAndInstallMicrosoftStoreUpdatesAsync(progress, cancellationToken).ConfigureAwait(false);

        if (!_updateAvailable || _pendingRelease is null) return new(AppUpdateState.Failed, "NoPendingUpdate");

        var release = _pendingRelease;
        var expectedBundleName = $"Eikura_{release.DisplayVersion}.0_x64.msixbundle";
        var bundleAssets = release.Assets.Where(asset => asset.Name.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase)).ToArray();
        var bundleAsset = bundleAssets.SingleOrDefault(asset => string.Equals(asset.Name, expectedBundleName, StringComparison.Ordinal));
        var checksumAsset = release.Assets.SingleOrDefault(asset => string.Equals(asset.Name, "SHA256SUMS.txt", StringComparison.Ordinal));
        if (bundleAssets.Length != 1 || bundleAsset is null || checksumAsset is null)
            return new(AppUpdateState.Failed, "BundleAssetNotFound");

        var tempRoot = Path.Combine(Path.GetTempPath(), $"Eikura-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var checksumPath = Path.Combine(tempRoot, "SHA256SUMS.txt");
        var bundlePath = Path.Combine(tempRoot, bundleAsset.Name);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(AppUpdateState.Downloading, Detail: "Checksum"));
            await GitHubUpdateService.DownloadAssetAsync(checksumAsset, checksumPath, cancellationToken: cancellationToken).ConfigureAwait(false);
            var checksumText = await File.ReadAllTextAsync(checksumPath, cancellationToken).ConfigureAwait(false);
            var expectedHash = ParseChecksum(checksumText, bundleAsset.Name);
            if (expectedHash is null) return new(AppUpdateState.Failed, "ChecksumMissing");

            var bundleProgress = new Progress<double>(value => progress?.Report(new(AppUpdateState.Downloading, value, bundleAsset.Name)));
            await GitHubUpdateService.DownloadAssetAsync(bundleAsset, bundlePath, bundleProgress, cancellationToken).ConfigureAwait(false);

            progress?.Report(new(AppUpdateState.Verifying, Detail: "SHA-256"));
            await using (var bundleStream = File.OpenRead(bundlePath))
            {
                var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(bundleStream, cancellationToken).ConfigureAwait(false));
                if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    return new(AppUpdateState.Failed, "ChecksumMismatch");
            }

            progress?.Report(new(AppUpdateState.Verifying, Detail: "MSIX signature"));
            var signature = _signatureVerifier.Verify(bundlePath);
            if (!signature.IsValid) return new(AppUpdateState.Failed, signature.FailureCode);
            if (!ExpectedSignerSubject.Equals(signature.SignerSubject, StringComparison.Ordinal))
                return new(AppUpdateState.Failed, "SignerSubjectMismatch");
            if (!ExpectedSignerThumbprint.Equals(signature.SignerThumbprint, StringComparison.OrdinalIgnoreCase))
                return new(AppUpdateState.Failed, "SignerThumbprintMismatch");

            _pendingBundlePath = bundlePath;
            try
            {
                SavePendingState(release.TagName, bundlePath);
            }
            catch (IOException exception)
            {
                _pendingBundlePath = null;
                return new(
                    AppUpdateState.Failed,
                    "PendingStateWriteFailed",
                    exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                _pendingBundlePath = null;
                return new(
                    AppUpdateState.Failed,
                    "PendingStateWriteFailed",
                    exception.Message);
            }

            progress?.Report(new(AppUpdateState.ReadyToInstall, 1d, "Verified; ready to install"));
            return new(AppUpdateState.ReadyToInstall, "ReadyToInstall", "Verified; ready to install");
        }
        catch (OperationCanceledException)
        {
            return new(AppUpdateState.Cancelled, "Cancelled");
        }
        catch (GitHubAssetDownloadException exception)
        {
            return new(AppUpdateState.Failed, exception.Code, exception.Message);
        }
        catch (IOException exception)
        {
            return new(AppUpdateState.Failed, "UpdateStorageFailed", exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new(AppUpdateState.Failed, "UpdateStorageFailed", exception.Message);
        }
        finally
        {
            TryDeleteFile(checksumPath);
            if (!string.Equals(_pendingBundlePath, bundlePath, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteFile(bundlePath);
                TryDeleteDirectory(tempRoot);
            }
        }
    }

    public async Task<AppUpdateResult> InstallPendingAsync(
        IProgress<AppUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsMicrosoftStoreBuild)
            return new(AppUpdateState.Completed);

        if (_pendingRelease is null || string.IsNullOrWhiteSpace(_pendingBundlePath) || !File.Exists(_pendingBundlePath))
            return new(AppUpdateState.Failed, "NoPendingUpdate");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bundlePath = _pendingBundlePath;
            if (!GitHubUpdateService.TryParseVersionTag(_pendingRelease.TagName, out var targetVersion))
                return new(AppUpdateState.Failed, "InvalidReleaseResponse");

            var current = Package.Current.Id;
            var bundleInfo = new FileInfo(bundlePath);
            string bundleHash;
            await using (var bundleStream = File.OpenRead(bundlePath))
                bundleHash = Convert.ToHexString(await SHA256.HashDataAsync(bundleStream, cancellationToken).ConfigureAwait(false));

            Debug.WriteLine($"Eikura update deployment starting: Current={current.FullName}; Target={targetVersion}; Bundle={bundlePath}; Bytes={bundleInfo.Length}; SHA256={bundleHash}; Publisher={current.Publisher}");
            progress?.Report(new(AppUpdateState.Installing, Detail: "Verified; deployment queued"));

            using var restart = ApplicationRestartRegistration.Register(out var restartHresult);
            Debug.WriteLine($"RegisterApplicationRestart HRESULT=0x{restartHresult:X8}");

            var manager = new PackageManager();
            var operation = manager.AddPackageAsync(new Uri(bundlePath), null, DeploymentOptions.ForceApplicationShutdown);
            var deploymentProgress = new Progress<DeploymentProgress>(value =>
            {
                var state = value.state == DeploymentProgressState.Queued ? AppUpdateState.Downloading : AppUpdateState.Installing;
                double? percentage = value.percentage is >= 0 and <= 100 ? value.percentage / 100d : null;
                progress?.Report(new(state, percentage, $"Deployment {value.percentage}%"));
            });

            var started = Stopwatch.GetTimestamp();
            var result = await operation.AsTask(cancellationToken, deploymentProgress);
            Debug.WriteLine($"Eikura update deployment returned: Registered={result.IsRegistered}; Error={result.ExtendedErrorCode}; Text={result.ErrorText}; ElapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:0}");
            if (!result.IsRegistered) return new(AppUpdateState.Failed, "PackageDeploymentFailed", result.ErrorText);

            _pendingBundlePath = null;
            TryDeletePendingState();
            progress?.Report(new(AppUpdateState.Restarting));
            return new(AppUpdateState.Restarting);
        }
        catch (OperationCanceledException)
        {
            return new(AppUpdateState.Cancelled, "Cancelled");
        }
        catch (COMException exception)
        {
            return new(AppUpdateState.Failed, $"0x{exception.HResult:X8}", exception.Message);
        }
        catch (Exception exception)
        {
            return new(AppUpdateState.Failed, "PackageDeploymentFailed", exception.Message);
        }
    }

    private async Task<AppUpdateInfo> CheckMicrosoftStoreUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = StoreContext.GetDefault();
            _storeUpdates = await context.GetAppAndOptionalStorePackageUpdatesAsync().AsTask(cancellationToken);
            if (_storeUpdates.Count == 0)
            {
                _updateAvailable = false;
                return new(AppUpdateState.UpToDate);
            }

            _updateAvailable = true;
            var newest = _storeUpdates.Select(update => update.Package.Id.Version)
                .OrderByDescending(v => new Version(v.Major, v.Minor, v.Build, v.Revision)).First();
            return new(AppUpdateState.UpdateAvailable, $"{newest.Major}.{newest.Minor}.{newest.Build}", "Microsoft Store");
        }
        catch (OperationCanceledException) { return new(AppUpdateState.Cancelled, ErrorCode: "Cancelled"); }
        catch (Exception exception) { return new(AppUpdateState.Failed, Detail: exception.Message, ErrorCode: "MicrosoftStoreUnavailable"); }
    }

    private async Task<AppUpdateResult> DownloadAndInstallMicrosoftStoreUpdatesAsync(
        IProgress<AppUpdateProgress>? progress, CancellationToken cancellationToken)
    {
        if (!_updateAvailable || _storeUpdates.Count == 0)
            return new(AppUpdateState.Failed, "NoPendingUpdate");

        try
        {
            progress?.Report(new(AppUpdateState.Downloading, Detail: "Microsoft Store"));
            var operation = StoreContext.GetDefault().RequestDownloadAndInstallStorePackageUpdatesAsync(_storeUpdates);
            operation.Progress = (_, value) =>
            {
                var state = value.PackageUpdateState == StorePackageUpdateState.Installing
                    ? AppUpdateState.Installing : AppUpdateState.Downloading;
                progress?.Report(new(state, value.PackageDownloadProgress, "Microsoft Store"));
            };
            var result = await operation.AsTask(cancellationToken);
            if (result.OverallState == StorePackageUpdateState.Completed)
            {
                _storeUpdates = Array.Empty<StorePackageUpdate>();
                _updateAvailable = false;
                progress?.Report(new(AppUpdateState.Completed, 1d, "Microsoft Store"));
                return new(AppUpdateState.Completed, Detail: "Microsoft Store");
            }
            return new(AppUpdateState.Failed, "MicrosoftStoreUpdateFailed", $"Store update state: {result.OverallState}");
        }
        catch (OperationCanceledException) { return new(AppUpdateState.Cancelled, "Cancelled"); }
        catch (Exception exception) { return new(AppUpdateState.Failed, "MicrosoftStoreUpdateFailed", exception.Message); }
    }

    private static string? ParseChecksum(string content, string fileName)
    {
        foreach (var rawLine in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var separator = line.IndexOfAny([' ', '\t']);
            if (separator <= 0) continue;
            var hash = line[..separator].Trim();
            var name = line[separator..].Trim().TrimStart('*');
            if (hash.Length == 64 && name.Equals(fileName, StringComparison.Ordinal) && hash.All(Uri.IsHexDigit))
                return hash.ToUpperInvariant();
        }
        return null;
    }

    private static AppUpdateInfo Fail(string code, string? detail = null, GitHubReleaseInfo? release = null) =>
        new(AppUpdateState.Failed, release?.DisplayVersion, detail, code, release);

    private void LoadPendingState()
    {
        _pendingBundlePath = null;
        try
        {
            if (!File.Exists(PendingStatePath)) return;
            var state = JsonSerializer.Deserialize<PendingUpdateState>(File.ReadAllText(PendingStatePath));
            var bundlePath = state?.BundlePath;
            if (state?.TagName == _pendingRelease?.TagName && !string.IsNullOrWhiteSpace(bundlePath) && File.Exists(bundlePath))
                _pendingBundlePath = bundlePath;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Eikura pending update state load failed: {exception.Message}");
        }
    }

    private static void SavePendingState(string tagName, string bundlePath)
    {
        Directory.CreateDirectory(CacheRoot);
        File.WriteAllText(PendingStatePath, JsonSerializer.Serialize(new PendingUpdateState(tagName, bundlePath)));
    }

    private static void TryDeletePendingState()
    {
        try
        {
            if (File.Exists(PendingStatePath)) File.Delete(PendingStatePath);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Eikura pending update state cleanup failed: {exception.Message}");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private sealed record PendingUpdateState(string TagName, string BundlePath);
}

internal static class ApplicationRestartRegistration
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterApplicationRestart(string? commandLine, uint flags);

    public static IDisposable Register(out int hresult)
    {
        var result = RegisterApplicationRestart(null, 0);
        hresult = result;
        if (result != 0) Marshal.ThrowExceptionForHR(result);
        return new Registration();
    }

    private sealed class Registration : IDisposable
    {
        public void Dispose()
        {
            var result = RegisterApplicationRestart(string.Empty, 0);
            if (result != 0) Debug.WriteLine($"RegisterApplicationRestart cleanup returned 0x{result:X8}.");
        }
    }
}
