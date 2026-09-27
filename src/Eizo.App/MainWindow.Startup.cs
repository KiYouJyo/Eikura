using System.Diagnostics;
using Eizo.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Eizo;

public sealed partial class MainWindow
{
    // Keep Eizo aligned with the startup contract already used by PageArc /
    // UrbanPlanToolbox: show the in-window splash for at least 500 ms, then
    // reveal the ready shell and fade the overlay out over 200 ms.
    private static readonly TimeSpan StartupMinimumVisibleDuration = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan StartupFadeOutDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan StartupFadeOutFallbackDuration = TimeSpan.FromMilliseconds(300);

    private bool _startupImageReady;
    private bool _startupSplashRenderRequested;
    private bool _startupSplashShown;
    private bool _startupMinimumDurationSatisfied;
    private bool _startupMainContentLoaded;
    private bool _startupDeferredInitializationStarted;
    private bool _startupDeferredInitializationCompleted;
    private bool _startupVisualCompleted;
    private bool _startupWatchdogStarted;
    private readonly Stopwatch _startupSplashVisibleClock = new();

    private void OnStartupWindowRootLoaded(object sender, RoutedEventArgs e)
    {
        StartupTrace.Mark("Startup.WindowRoot.Loaded");
        WindowRoot.Loaded -= OnStartupWindowRootLoaded;
        StartStartupSafetyNets();
    }

    private void OnStartupMainContentLoaded(object sender, RoutedEventArgs e)
    {
        StartupTrace.Mark("Startup.RootGrid.Loaded");
        RootGrid.Loaded -= OnStartupMainContentLoaded;
        _startupMainContentLoaded = true;
        TryCompleteStartupVisual();
    }

    private void StartStartupSafetyNets()
    {
        if (_startupWatchdogStarted) return;
        _startupWatchdogStarted = true;
        _ = EnsureStartupLogoGateAsync();
        _ = WatchStartupAsync();
    }

    private async Task EnsureStartupLogoGateAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(1));
        if (_startupImageReady || _startupVisualCompleted) return;

        _startupImageReady = true;
        StartMinimumSplashDurationAfterFirstRender();
        TryCompleteStartupVisual();
    }

    private async Task WatchStartupAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        if (_startupVisualCompleted) return;

        PresentMainContent();
    }

    private void OnStartupLogoImageOpened(object sender, RoutedEventArgs e)
    {
        _startupImageReady = true;
        StartMinimumSplashDurationAfterFirstRender();
        TryCompleteStartupVisual();
    }

    private void OnStartupLogoImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _startupImageReady = true;
        StartMinimumSplashDurationAfterFirstRender();
        TryCompleteStartupVisual();
    }

    private void StartMinimumSplashDurationAfterFirstRender()
    {
        if (_startupSplashRenderRequested) return;
        _startupSplashRenderRequested = true;
        CompositionTarget.Rendering += OnStartupSplashRendered;
    }

    private void OnStartupSplashRendered(object? sender, object e)
    {
        CompositionTarget.Rendering -= OnStartupSplashRendered;
        StartupTrace.Mark("Startup.FirstCompositionFrame");
        StartMinimumSplashDuration();
        StartDeferredStartupInitialization();
    }

    private void StartDeferredStartupInitialization()
    {
        if (_startupDeferredInitializationStarted)
            return;

        _startupDeferredInitializationStarted = true;
        _ = InitializeDeferredStartupAsync();
    }

    private async Task InitializeDeferredStartupAsync()
    {
        try
        {
            StartupTrace.Mark("Startup.DeferredInitialization:begin");

            // Cache maintenance used to be invoked before MainWindow.Activate().
            // Run it only after the first splash frame, and off the UI thread.
            _ = Task.Run(async () =>
                await CacheRuntime.RunStartupMaintenanceAsync());

            StartupTrace.Mark("Startup.CreateWorkspaceTab:begin");
            CreateWorkspaceTab(select: true);
            StartupTrace.Mark("Startup.CreateWorkspaceTab:end");

            if (_selectedTabKey is not null &&
                _tabs.TryGetValue(_selectedTabKey, out var state) &&
                state.View is HomeView home)
            {
                StartupTrace.Mark("Startup.HomeInitialContent:begin");
                await home.EnsureInitialContentAsync();
                StartupTrace.Mark("Startup.HomeInitialContent:end");
            }
        }
        catch (Exception exception)
        {
            StartupTrace.Mark($"Startup.DeferredInitialization:error {exception.GetType().Name}");
        }
        finally
        {
            _startupDeferredInitializationCompleted = true;
            StartupTrace.Mark("Startup.DeferredInitialization:end");
            StartupTrace.FlushSoon();
            TryCompleteStartupVisual();
        }
    }

    private void StartMinimumSplashDuration()
    {
        if (_startupSplashShown) return;
        _startupSplashShown = true;
        _startupSplashVisibleClock.Start();
        _ = CompleteMinimumSplashDurationAsync();
    }

    private async Task CompleteMinimumSplashDurationAsync()
    {
        var remaining = StartupMinimumVisibleDuration - _startupSplashVisibleClock.Elapsed;
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining);

        _startupMinimumDurationSatisfied = true;
        TryCompleteStartupVisual();
    }

    private void TryCompleteStartupVisual()
    {
        if (!_startupMainContentLoaded ||
            !_startupImageReady ||
            !_startupMinimumDurationSatisfied ||
            !_startupDeferredInitializationCompleted ||
            _startupVisualCompleted)
        {
            return;
        }

        PresentMainContent();
    }

    private void PresentMainContent()
    {
        if (_startupVisualCompleted) return;
        _startupVisualCompleted = true;

        StartupTrace.Mark("Startup.PresentMainContent");
        MainContent.Opacity = 1;
        _ = FadeOutStartupOverlayAsync();
    }

    private async Task FadeOutStartupOverlayAsync()
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var storyboard = new Storyboard();
            var fade = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = new Duration(StartupFadeOutDuration),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            Storyboard.SetTarget(fade, StartupOverlay);
            Storyboard.SetTargetProperty(fade, nameof(UIElement.Opacity));
            storyboard.Children.Add(fade);
            storyboard.Completed += (_, _) => completion.TrySetResult(true);
            storyboard.Begin();

            if (await Task.WhenAny(
                    completion.Task,
                    Task.Delay(StartupFadeOutFallbackDuration)) != completion.Task)
            {
                // Fail open: startup visuals must never block the actual app shell.
            }
        }
        finally
        {
            StartupProgressBar.IsIndeterminate = false;
            StartupOverlay.Visibility = Visibility.Collapsed;
            StartupOverlay.Opacity = 1;
            MainContent.IsHitTestVisible = true;

            Eizo.Models.FirstRunExperienceService.Default.PrepareForLaunch();
            if (Eizo.Models.FirstRunExperienceService.Default.ShouldShowAutomatically())
                FirstRunGuideHost.Show();
        }
    }
}
