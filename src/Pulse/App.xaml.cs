using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pulse.Core.Interfaces;
using Pulse.Core.Services;
using Pulse.Core.Services.Weather;
using Pulse.Services.Artwork;
using Pulse.Services.Media;
using Pulse.Services.Settings;
using Pulse.Services.System;
using Pulse.ViewModels;
using Pulse.Views.Island;
using Pulse.Views.Settings;

namespace Pulse;

public partial class App : Application
{
    private readonly ISettingsService _settingsService;
    private readonly IStartupService _startupService;
    private readonly IMediaSessionService _mediaSessionService;
    private readonly EventRouter _eventRouter;
    private readonly FocusTimerService _focusTimerService;
    private readonly SystemEventService _systemEventService;
    private readonly FileOperationService _fileOperationService;
    private readonly WindowsSystemControlService _systemControlService;
    private readonly DownloadsWatcherService _downloadsWatcherService;
    private readonly WindowsToastNotificationService _windowsToastService;
    private readonly BluetoothWatcherService _bluetoothWatcherService;
    private readonly ScreenshotWatcherService _screenshotWatcherService;
    private readonly HttpClient _httpClient;
    private readonly OpenMeteoWeatherService _weatherService;
    private readonly LocationService _locationService;
    private readonly MediaViewModel _mediaViewModel;
    private readonly IslandViewModel _islandViewModel;
    private IslandWindow? _islandWindow;
    private SettingsWindow? _settingsWindow;
    private SettingsViewModel? _activeSettingsViewModel;
    private TrayService? _trayService;
    private bool _exiting;

    private const string InstanceMutexName = "Local\\PulseDynamicIslandInstance";
    private const string ShowIslandEventName = "Local\\PulseShowIslandSignal";
    private const string ShowSettingsEventName = "Local\\PulseShowSettingsSignal";
    private static Mutex? _instanceMutex;
    private EventWaitHandle? _showIslandSignal;
    private RegisteredWaitHandle? _showIslandRegistration;
    private EventWaitHandle? _showSettingsSignal;
    private RegisteredWaitHandle? _showSettingsRegistration;

    private static bool HasSettingsLaunchArg()
        => Environment.GetCommandLineArgs().Any(arg =>
            string.Equals(arg, "--settings", StringComparison.OrdinalIgnoreCase));

    public App()
    {
        // Single-instance guard: a second launch must not boot a competing instance.
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                var targetSignal = HasSettingsLaunchArg() ? ShowSettingsEventName : ShowIslandEventName;
                if (EventWaitHandle.TryOpenExisting(targetSignal, out var signalEvent))
                {
                    signalEvent.Set();
                    signalEvent.Dispose();
                }
            }
            catch
            {
                // Existing instance may be shutting down; nothing to signal.
            }

            Environment.Exit(0);
            return;
        }

        InitializeComponent();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                CrashLogger.Write("AppDomain.UnhandledException", ex);
            }
            else
            {
                CrashLogger.WriteMessage("AppDomain.UnhandledException", e.ExceptionObject?.ToString() ?? "Unknown exception");
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLogger.Write("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
        UnhandledException += (_, args) =>
        {
            if (args.Exception is Exception ex)
            {
                CrashLogger.Write("Application.UnhandledException", ex, args.Message);
            }
            else
            {
                CrashLogger.WriteMessage("Application.UnhandledException", args.Message);
            }
            args.Handled = true;
        };

        _settingsService = new SettingsService();
        _settingsService.SettingsChanged += OnSettingsChanged;
        _startupService = new StartupService();
        _mediaSessionService = new WindowsMediaSessionService(new ArtworkCacheService());
        var playbackController = new PlaybackController(_mediaSessionService);
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        _eventRouter = new EventRouter();
        _focusTimerService = new FocusTimerService(_eventRouter);
        _systemEventService = new SystemEventService(_eventRouter, _settingsService);
        _fileOperationService = new FileOperationService(_eventRouter);
        _systemControlService = new WindowsSystemControlService();
        _downloadsWatcherService = new DownloadsWatcherService(_eventRouter, _settingsService);
        _windowsToastService = new WindowsToastNotificationService(_systemEventService, _settingsService);
        _bluetoothWatcherService = new BluetoothWatcherService(_systemEventService, _settingsService);
        _screenshotWatcherService = new ScreenshotWatcherService(_systemEventService);

        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        _weatherService = new OpenMeteoWeatherService(_httpClient);
        _locationService = new LocationService(_httpClient);

        _mediaViewModel = new MediaViewModel(
            _mediaSessionService,
            playbackController,
            _settingsService,
            dispatcherQueue,
            _eventRouter);

        _islandViewModel = new IslandViewModel(
            _mediaViewModel,
            _eventRouter,
            _focusTimerService,
            _systemEventService,
            _fileOperationService,
            dispatcherQueue,
            _systemControlService,
            _settingsService,
            _weatherService,
            _locationService);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await _settingsService.LoadAsync();
        var startupEnabled = await _startupService.ReconcileAsync(_settingsService.Current.StartWithWindows);
        if (startupEnabled != _settingsService.Current.StartWithWindows)
        {
            await _settingsService.SaveAsync(_settingsService.Current with { StartWithWindows = startupEnabled });
        }

        await _mediaViewModel.InitializeAsync();

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (Environment.ExitCode != 0)
            {
                CrashLogger.WriteMessage("AppDomain.ProcessExit", $"Abnormal exit code={Environment.ExitCode}");
            }
        };

        _islandWindow = new IslandWindow(_islandViewModel);
        _islandWindow.Closed += (_, _) =>
        {
            if (!_exiting)
            {
                CrashLogger.WriteMessage("IslandWindow.Closed", "IslandWindow closed unexpectedly before application exit.");
            }
        };
        _islandWindow.SettingsRequested += OnSettingsRequested;
        _islandWindow.Activate();
        _islandWindow.ShowIsland();
        _islandViewModel.StartIdleMonitoring();

        // When a user launches Pulse again, surface the running instance instead of crashing.
        var islandWindow = _islandWindow;
        _showIslandSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowIslandEventName);
        _showIslandRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showIslandSignal,
            (_, _) => islandWindow.DispatcherQueue.TryEnqueue(() =>
                SurfaceIsland(toggleIfAlreadyVisible: false, activateWindow: true)),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        _showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        _showSettingsRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showSettingsSignal,
            (_, _) => islandWindow.DispatcherQueue.TryEnqueue(() => OnSettingsRequested(this, EventArgs.Empty)),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        if (HasSettingsLaunchArg())
        {
            islandWindow.DispatcherQueue.TryEnqueue(() => OnSettingsRequested(this, EventArgs.Empty));
        }

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            _trayService = new TrayService(iconPath, startupEnabled);
            _trayService.ShowRequested += (_, _) =>
                SurfaceIsland(toggleIfAlreadyVisible: true, activateWindow: false);
            _trayService.IslandToggleRequested += (_, _) => _islandWindow?.ToggleHideIsland();
            _trayService.SettingsRequested += OnSettingsRequested;
            _trayService.AboutRequested += OnAboutRequested;
            _trayService.ExitRequested += OnExitRequested;
            _trayService.StartupRequested += OnStartupRequested;
            _trayService.SetNotificationsPaused(_settingsService.Current.NotificationsPaused);
            _trayService.NotificationsPauseRequested += OnNotificationsPauseRequested;
        }
        catch (Exception ex)
        {
            CrashLogger.Write("App.TrayService", ex);
        }
    }

    private void SurfaceIsland(bool toggleIfAlreadyVisible, bool activateWindow)
    {
        if (_islandWindow is not { } islandWindow)
        {
            return;
        }

        try
        {
            islandWindow.ReassertTopmost();

            var wasHidden = islandWindow.IsIslandHidden;
            if (wasHidden)
            {
                islandWindow.ShowIsland();
            }

            if (wasHidden || !toggleIfAlreadyVisible)
            {
                if (!_islandViewModel.IsExpanded)
                {
                    _islandViewModel.ToggleExpanded();
                }
            }
            else
            {
                _islandViewModel.ToggleExpanded();
            }

            if (activateWindow)
            {
                islandWindow.Activate();
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("App.SurfaceIsland", ex);
        }
    }

    private void OnSettingsRequested(object? sender, EventArgs args)
    {
        if (_settingsWindow is not null)
        {
            BringToFront(_settingsWindow);
            return;
        }

        var viewModel = new SettingsViewModel(_settingsService, _startupService, _locationService);
        _activeSettingsViewModel = viewModel;
        try
        {
            var window = new SettingsWindow(viewModel);
            _settingsWindow = window;
            window.Closed += (_, _) =>
            {
                _settingsWindow = null;
                if (ReferenceEquals(_activeSettingsViewModel, viewModel))
                {
                    _activeSettingsViewModel = null;
                }
            };
            BringToFront(window);
        }
        catch (Exception ex)
        {
            _settingsWindow = null;
            if (ReferenceEquals(_activeSettingsViewModel, viewModel))
            {
                _activeSettingsViewModel = null;
            }
            viewModel.Dispose();
            CrashLogger.Write("App.OnSettingsRequested", ex);
        }
    }

    /// <summary>
    /// The island is a no-activate window, so the app never owns the foreground and a plain
    /// Activate() leaves the settings window behind whatever the user has open.
    /// </summary>
    private static void BringToFront(Window window)
    {
        window.Activate();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero) return;

        const uint noMoveNoSizeNoActivate = 0x0001 | 0x0002 | 0x0010;
        ShowWindow(hwnd, SwRestore);
        // Stay above the always-on-top island, which would otherwise cover the settings header.
        SetWindowPos(hwnd, HwndTopMost, 0, 0, 0, 0, noMoveNoSizeNoActivate);
        SetForegroundWindow(hwnd);
    }

    private const int SwRestore = 9;
    private static readonly IntPtr HwndTopMost = new(-1);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private async void OnStartupRequested(object? sender, bool enabled)
    {
        try
        {
            var accepted = await _startupService.SetEnabledAsync(enabled);
            var actual = await _startupService.IsEnabledAsync();
            _trayService?.SetStartupEnabled(actual);

            if (accepted || actual != _settingsService.Current.StartWithWindows)
            {
                await _settingsService.SaveAsync(_settingsService.Current with { StartWithWindows = actual });
                await _settingsService.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("App.OnStartupRequested", ex);
        }
    }

    private async void OnNotificationsPauseRequested(object? sender, bool paused)
    {
        try
        {
            await _settingsService.SaveAsync(_settingsService.Current with { NotificationsPaused = paused });
            await _settingsService.FlushAsync();
        }
        catch (Exception ex)
        {
            CrashLogger.Write("App.OnNotificationsPauseRequested", ex);
        }
    }

    private void OnSettingsChanged(object? sender, Pulse.Core.Models.AppSettings settings)
    {
        _trayService?.SetNotificationsPaused(settings.NotificationsPaused);
        _trayService?.SetStartupEnabled(settings.StartWithWindows);
    }

    private async void OnAboutRequested(object? sender, EventArgs args)
    {
        if (_islandWindow is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Pulse",
            Content = "Windows için yerel medya denetimi. Medya oturumları cihazında işlenir.\nSürüm 1.0.0",
            CloseButtonText = "Kapat",
            XamlRoot = _islandWindow.IslandXamlRoot
        };
        await dialog.ShowAsync();
    }

    private async void OnExitRequested(object? sender, EventArgs args)
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        try
        {
            if (_activeSettingsViewModel is { } activeVm)
            {
                await activeVm.FlushPendingSaveAsync();
            }

            await _settingsService.FlushAsync();
        }
        catch (Exception ex)
        {
            CrashLogger.Write("App.OnExitRequested.Flush", ex);
        }

        _showIslandRegistration?.Unregister(null);
        _showIslandSignal?.Dispose();
        _showSettingsRegistration?.Unregister(null);
        _showSettingsSignal?.Dispose();
        _trayService?.Dispose();
        _settingsWindow?.Close();
        _islandWindow?.Close();
        _islandViewModel.Dispose();
        _httpClient.Dispose();
        _downloadsWatcherService.Dispose();
        _windowsToastService.Dispose();
        _bluetoothWatcherService.Dispose();
        _screenshotWatcherService.Dispose();
        await _mediaViewModel.DisposeAsync();
        await _mediaSessionService.DisposeAsync();
        _systemControlService.Dispose();
        Exit();
    }
}
