using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using Eizo.Models;

namespace Eizo;

public partial class App : Application
{
    private static readonly object ActivationGate = new();
    private static bool _redirectedActivationPending;
    private static Uri? _pendingBangumiAuth;
    private Window? _window;

    internal static MainWindow? MainWindow { get; private set; }

    public App()
    {
        try
        {
            StartupTrace.Mark("App.App:begin");
            StartupTrace.Mark("ApplyPersistedLanguage:begin");
            Localization.AppLocalizationService.Default.ApplyPersistedLanguage(AppSettingsStore.Current);
            StartupTrace.Mark("ApplyPersistedLanguage:end");
            StartupTrace.Mark("App.InitializeComponent:begin");
            InitializeComponent();
            StartupTrace.Mark("App.InitializeComponent:end");
            StartupTrace.Mark("App.App:end");
        }
        catch (Exception ex)
        {
            WriteStartupFailure("App.InitializeComponent", ex);
            throw;
        }
    }

    internal static void OnRedirectedActivation(AppActivationArguments activationArguments)
    {
        QueueProtocolActivation(activationArguments);
        MainWindow? existingWindow;
        lock (ActivationGate)
        {
            existingWindow = MainWindow;
            if (existingWindow is null)
            {
                _redirectedActivationPending = true;
                return;
            }
        }

        existingWindow.DispatcherQueue.TryEnqueue(() =>
        {
            existingWindow.RestoreAndActivate();
            _ = ProcessPendingBangumiAuthAsync();
        });
    }

    internal static void OnInitialActivation(AppActivationArguments activationArguments) =>
        QueueProtocolActivation(activationArguments);

    private static void QueueProtocolActivation(AppActivationArguments activationArguments)
    {
        if (activationArguments.Data is not IProtocolActivatedEventArgs protocol ||
            !BangumiOAuthService.IsBangumiAuthCallback(protocol.Uri)) return;
        lock (ActivationGate) _pendingBangumiAuth = protocol.Uri;
    }

    private static async Task ProcessPendingBangumiAuthAsync()
    {
        Uri? uri;
        lock (ActivationGate)
        {
            uri = _pendingBangumiAuth;
            _pendingBangumiAuth = null;
        }
        if (uri is not null) await BangumiOAuthService.Default.CompleteAsync(uri);
    }

    private static void ActivatePendingRedirectedWindow()
    {
        MainWindow? existingWindow;
        lock (ActivationGate)
        {
            if (!_redirectedActivationPending || MainWindow is null) return;
            _redirectedActivationPending = false;
            existingWindow = MainWindow;
        }

        existingWindow.DispatcherQueue.TryEnqueue(() =>
        {
            existingWindow.RestoreAndActivate();
            _ = ProcessPendingBangumiAuthAsync();
        });
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            StartupTrace.Mark("App.OnLaunched:begin");
            StartupTrace.Mark("MainWindow ctor:begin");
            _window = MainWindow = new MainWindow();
            StartupTrace.Mark("MainWindow ctor:end");

            // Cache maintenance is deferred until the Mica startup overlay has
            // painted its first compositor frame. Keep OnLaunched free of disk work.
            ActivatePendingRedirectedWindow();
            StartupTrace.Mark("MainWindow.Activate:begin");
            _window.Activate();
            StartupTrace.Mark("MainWindow.Activate:return");
            _ = ProcessPendingBangumiAuthAsync();
            StartupTrace.Mark("App.OnLaunched:return");
        }
        catch (Exception ex)
        {
            WriteStartupFailure("App.OnLaunched", ex);
            throw;
        }
    }

    private static void WriteStartupFailure(string stage, Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Eizo",
                "Logs");
            Directory.CreateDirectory(directory);

            var text =
                $"Stage: {stage}{Environment.NewLine}" +
                $"UTC: {DateTimeOffset.UtcNow:O}{Environment.NewLine}" +
                exception;

            File.WriteAllText(Path.Combine(directory, "startup-failure.log"), text);

            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "Eizo-startup-failure.log"), text);
            }
            catch
            {
            }

            try
            {
                File.WriteAllText(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Eizo-startup-failure.log"), text);
            }
            catch
            {
            }
        }
        catch
        {
        }
    }
}
