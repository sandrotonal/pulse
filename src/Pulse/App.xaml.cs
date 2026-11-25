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
    private readonly ArtworkCacheService _artworkCacheService;
    private readonly IMediaSessionService _mediaSessionService;
    private readonly EventRouter _eventRouter;
    private readonly FocusTimerService _focusTimerService;
    private readonly SystemEventService _systemEventService;
    private readonly FileOperationService _fileOperationService;
    private readonly DropShelfService _dropShelfService;
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

    private readonly SingleInstanceNames _instanceNames;
    private static Mutex? _instanceMutex;
    private EventWaitHandle? _showIslandSignal;
    private RegisteredWaitHandle? _showIslandRegistration;
    private EventWaitHandle? _showSettingsSignal;
    private RegisteredWaitHandle? _showSettingsRegistration;
    private EventWaitHandle? _expandMediaSignal;
    private RegisteredWaitHandle? _expandMediaRegistration;
    private EventWaitHandle? _expandFocusSignal;
    private RegisteredWaitHandle? _expandFocusRegistration;
    private EventWaitHandle? _expandSystemSignal;
    private RegisteredWaitHandle? _expandSystemRegistration;
    private EventWaitHandle? _expandShelfSignal;
    private RegisteredWaitHandle? _expandShelfRegistration;
    private EventWaitHandle? _collapseSignal;
    private RegisteredWaitHandle? _collapseRegistration;
    private EventWaitHandle? _toggleSplitDemoSignal;
    private RegisteredWaitHandle? _toggleSplitDemoRegistration;
    private int _pendingShowIslandSignal;
    private int _pendingShowSettingsSignal;

    private static bool HasLaunchArg(string argName)
        => Environment.GetCommandLineArgs().Any(arg =>
            string.Equals(arg, argName, StringComparison.OrdinalIgnoreCase));

    private static bool HasSettingsLaunchArg() => HasLaunchArg("--settings");

    private static string? TryGetCurrentDesktopName()
    {
        try
        {
            var desktop = GetThreadDesktop(GetCurrentThreadId());
            if (desktop == IntPtr.Zero)
            {
                return null;
            }

            const int uoiName = 2;
            var buffer = new char[256];
            if (!GetUserObjectInformation(desktop, uoiName, buffer, buffer.Length * sizeof(char), out var neededBytes) ||
                neededBytes <= 0)
            {
                return null;
            }

            var charCount = Math.Min(buffer.Length, neededBytes / sizeof(char));
            return new string(buffer, 0, charCount).TrimEnd('\0');
        }
        catch
        {
            return null;
        }
    }

    private static bool TrySignalExistingInstance(string targetSignal)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(targetSignal, out var signalEvent))
                {
                    signalEvent.Set();
                    signalEvent.Dispose();
                    return true;
                }
            }
            catch
            {
            }

            if (attempt < 7)
            {
                Thread.Sleep(40);
            }
        }

        return false;
    }

    public App()
    {
        _instanceNames = SingleInstanceNameResolver.Resolve(TryGetCurrentDesktopName());

        // Single-instance guard: a second launch must not boot a competing instance.
        // Note: on Windows, Local\ mutexes are session-wide across desktops, so resolving
        // by desktop keeps isolated sandbox runs from blocking the interactive desktop.
        _instanceMutex = new Mutex(initiallyOwned: true, _instanceNames.MutexName, out var createdNew);
        if (!createdNew)
        {
            // During a rapid restart the previous instance may still be finishing teardown;
            // wait briefly before assuming a live instance owns the mutex.
            var acquiredAfterWait = false;
            try
            {
                acquiredAfterWait = _instanceMutex.WaitOne(450);
            }
            catch (AbandonedMutexException)
            {
                acquiredAfterWait = true;
            }
            catch
            {
            }

            if (!acquiredAfterWait)
            {
                var targetSignal = HasLaunchArg("--settings")
                    ? _instanceNames.ShowSettingsEventName
                    : HasLaunchArg("--media")
                    ? _instanceNames.ExpandMediaEventName
                    : HasLaunchArg("--focus")
                    ? _instanceNames.ExpandFocusEventName
                    : HasLaunchArg("--system")
                    ? _instanceNames.ExpandSystemEventName
                    : (HasLaunchArg("--shelf") || HasLaunchArg("--raf"))
                    ? _instanceNames.ExpandShelfEventName
                    : HasLaunchArg("--collapse")
                    ? _instanceNames.CollapseEventName
                    : HasLaunchArg("--split")
                    ? _instanceNames.ToggleSplitDemoEventName
                    : _instanceNames.ShowIslandEventName;

                _ = TrySignalExistingInstance(targetSignal);

                try { _instanceMutex.Dispose(); } catch { }
                _instanceMutex = null;
                Environment.Exit(0);
                return;
            }
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
        _artworkCacheService = new ArtworkCacheService();
        _mediaSessionService = new WindowsMediaSessionService(_artworkCacheService);
        var playbackController = new PlaybackController(_mediaSessionService);
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        _eventRouter = new EventRouter();
        _focusTimerService = new FocusTimerService(_eventRouter);
        _systemEventService = new SystemEventService(_eventRouter, _settingsService);
        _fileOperationService = new FileOperationService(_eventRouter);
        _dropShelfService = new DropShelfService();
        _systemControlService = new WindowsSystemControlService(dispatcherQueue);
        _downloadsWatcherService = new DownloadsWatcherService(_eventRouter, _settingsService);
        _windowsToastService = new WindowsToastNotificationService(_systemEventService, _settingsService);
        _bluetoothWatcherService = new BluetoothWatcherService(_systemEventService, _settingsService);
        _screenshotWatcherService = new ScreenshotWatcherService(_systemEventService, dropShelf: _dropShelfService, settings: _settingsService);

        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        _weatherService = new OpenMeteoWeatherService(_httpClient);
        _locationService = new LocationService(_httpClient);

        _mediaViewModel = new MediaViewModel(
            _mediaSessionService,
            playbackController,
            _settingsService,
            dispatcherQueue,
            _eventRouter,
            _httpClient);

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
            _locationService,
            _dropShelfService);

        RegisterInstanceSignals(dispatcherQueue);
    }

    private void RegisterInstanceSignals(DispatcherQueue dispatcherQueue)
    {
        try
        {
            RegisterSignal(out _showIslandSignal, out _showIslandRegistration, _instanceNames.ShowIslandEventName, (_, _) =>
            {
                if (_exiting) return;

                if (_islandWindow is { } window)
                {
                    window.DispatcherQueue.TryEnqueue(() =>
                        SurfaceIsland(toggleIfAlreadyVisible: false, activateWindow: true));
                }
                else
                {
                    Interlocked.Exchange(ref _pendingShowIslandSignal, 1);
                    dispatcherQueue.TryEnqueue(() =>
                    {
                        if (_islandWindow is not null &&
                            Interlocked.Exchange(ref _pendingShowIslandSignal, 0) == 1)
                        {
                            SurfaceIsland(toggleIfAlreadyVisible: false, activateWindow: true);
                        }
                    });
                }
            });

            RegisterSignal(out _showSettingsSignal, out _showSettingsRegistration, _instanceNames.ShowSettingsEventName, (_, _) =>
            {
                if (_exiting) return;

                if (_islandWindow is { } window)
                {
                    window.DispatcherQueue.TryEnqueue(() => OnSettingsRequested(this, EventArgs.Empty));
                }
                else
                {
                    Interlocked.Exchange(ref _pendingShowSettingsSignal, 1);
                    dispatcherQueue.TryEnqueue(() =>
                    {
                        if (_islandWindow is not null &&
                            Interlocked.Exchange(ref _pendingShowSettingsSignal, 0) == 1)
                        {
                            OnSettingsRequested(this, EventArgs.Empty);
                        }
                    });
                }
            });

            RegisterIslandWindowSignal(out _expandMediaSignal, out _expandMediaRegistration, _instanceNames.ExpandMediaEventName, () =>
            {
                _islandViewModel.SelectedPageIndex = 0;
                if (!_islandViewModel.IsExpanded)
                {
                    _islandViewModel.ToggleExpanded();
                }
            });

            RegisterIslandWindowSignal(out _expandFocusSignal, out _expandFocusRegistration, _instanceNames.ExpandFocusEventName, () =>
            {
                _islandViewModel.SelectedPageIndex = 1;
                if (!_islandViewModel.IsExpanded)
                {
                    _islandViewModel.ToggleExpanded();
                }
            });

            RegisterIslandWindowSignal(out _expandSystemSignal, out _expandSystemRegistration, _instanceNames.ExpandSystemEventName, () =>
            {
                _islandViewModel.SelectedPageIndex = 2;
                if (!_islandViewModel.IsExpanded)
                {
                    _islandViewModel.ToggleExpanded();
                }
            });

            RegisterIslandWindowSignal(out _expandShelfSignal, out _expandShelfRegistration, _instanceNames.ExpandShelfEventName, () =>
            {
                _islandViewModel.SelectedPageIndex = 3;
                if (!_islandViewModel.IsExpanded)
                {
                    _islandViewModel.ToggleExpanded();
                }
            });

            RegisterIslandWindowSignal(out _collapseSignal, out _collapseRegistration, _instanceNames.CollapseEventName, () =>
            {
                if (_islandViewModel.IsExpanded)
                {
                    _islandViewModel.ToggleExpanded();
                }
            });

            RegisterIslandWindowSignal(out _toggleSplitDemoSignal, out _toggleSplitDemoRegistration, _instanceNames.ToggleSplitDemoEventName, () =>
            {
                _islandViewModel.IsSplitDemoForced = !_islandViewModel.IsSplitDemoForced;
            });
        }
        catch (Exception ex)
        {
            CrashLogger.Write("App.RegisterInstanceSignals", ex);
        }
    }

    private static void RegisterSignal(
        out EventWaitHandle handle,
        out RegisteredWaitHandle registration,
        string signalName,
        WaitOrTimerCallback callback)
    {
        handle = new EventWaitHandle(false, EventResetMode.AutoReset, signalName);
        registration = ThreadPool.RegisterWaitForSingleObject(
            handle,
            callback,
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    private void RegisterIslandWindowSignal(
        out EventWaitHandle handle,
        out RegisteredWaitHandle registration,
        string signalName,
        Action action)
    {
        RegisterSignal(out handle, out registration, signalName, (_, _) =>
        {
            if (_exiting || _islandWindow is not { } window) return;
            window.DispatcherQueue.TryEnqueue(() => action());
        });
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            await _settingsService.LoadAsync();
            if (_exiting)
            {
                return;
            }

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

            // Defer heavy OS/COM/WinRT watchers off the UI thread AFTER the island is visible
            // so Windows boot contention never delays the first painted frame.
            _ = _systemControlService.PrimeAsync();
            _ = Task.Run(() =>
            {
                if (_exiting)
                {
                    return;
                }

                try
                {
                    _windowsToastService.Start();
                }
                catch (Exception ex)
                {
                    CrashLogger.Write("App.WindowsToastService.Start", ex);
                }

                try
                {
                    _bluetoothWatcherService.Start();
                }
                catch (Exception ex)
                {
                    CrashLogger.Write("App.BluetoothWatcherService.Start", ex);
                }
            });

            var mediaInitTask = _mediaViewModel.InitializeAsync();
            var startupReconcileTask = ReconcileStartupAsync();

            var islandWindow = _islandWindow;
            if (Interlocked.Exchange(ref _pendingShowIslandSignal, 0) == 1)
            {
                islandWindow.DispatcherQueue.TryEnqueue(() =>
                    SurfaceIsland(toggleIfAlreadyVisible: false, activateWindow: true));
            }

            if (HasSettingsLaunchArg() || Interlocked.Exchange(ref _pendingShowSettingsSignal, 0) == 1)
            {
                islandWindow.DispatcherQueue.TryEnqueue(() => OnSettingsRequested(this, EventArgs.Empty));
            }

            if (HasLaunchArg("--demo-shelf") || HasLaunchArg("--sample-shelf"))
            {
                islandWindow.DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        var assetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");
                        var iconPath = Path.Combine(assetsDir, "AppIcon.png");
                        if (File.Exists(iconPath))
                        {
                            _dropShelfService.AddPath(iconPath);
                        }
                        await _dropShelfService.StageTextAsync("Pulse Drop Shelf v2.2\nApple NotchDrop experience on Windows 11.");

                        _islandViewModel.SelectedPageIndex = 3;
                        if (!_islandViewModel.IsExpanded)
                        {
                            _islandViewModel.ToggleExpanded();
                        }
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.Write("App.DemoShelf", ex);
                    }
                });
            }

            // Initialize tray icon immediately with current setting rather than waiting on slow boot I/O.
            try
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
                _trayService = new TrayService(iconPath, _settingsService.Current.StartWithWindows);
                _trayService.ShowRequested += (_, _) =>
                    SurfaceIsland(toggleIfAlreadyVisible: true, activateWindow: false);
                _trayService.IslandToggleRequested += (_, _) => _islandWindow?.ToggleHideIsland();
                _trayService.SettingsRequested += OnSettingsRequested;
                _trayService.AboutRequested += OnAboutRequested;
                _trayService.ExitRequested += OnExitRequested;
                _trayService.StartupRequested += OnStartupRequested;
                _trayService.SetNotificationsPaused(_settingsService.Current.NotificationsPaused);
                _trayService.SetDoNotDisturb(_settingsService.Current.DoNotDisturb);
                _trayService.SetHotkeysEnabled(_settingsService.Current.EnableGlobalHotkeys);
                _trayService.NotificationsPauseRequested += OnNotificationsPauseRequested;
                _trayService.DoNotDisturbRequested += OnDoNotDisturbRequested;
            }
            catch (Exception ex)
            {
                CrashLogger.Write("App.TrayService", ex);
            }

            var startupEnabled = await startupReconcileTask;
            if (!_exiting)
            {
                _trayService?.SetStartupEnabled(startupEnabled);
            }

            await mediaInitTask;
        }
        catch (Exception ex)
        {
            CrashLogger.Write("App.OnLaunched", ex);
        }
    }

    private async Task<bool> ReconcileStartupAsync()
    {
        try
        {
            var startupEnabled = await _startupService.ReconcileAsync(_settingsService.Current.StartWithWindows);
            if (!_exiting && startupEnabled != _settingsService.Current.StartWithWindows)
            {
                await _settingsService.SaveAsync(_settingsService.Current with { StartWithWindows = startupEnabled });
            }

            return startupEnabled;
        }
        catch (Exception ex)
        {
            CrashLogger.Write("App.ReconcileStartupAsync", ex);
            return _settingsService.Current.StartWithWindows;
        }
    }

    private void SurfaceIsland(bool toggleIfAlreadyVisible, bool activateWindow)
    {
        if (_exiting || _islandWindow is not { } islandWindow)
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
            else
            {
                islandWindow.EnsureNativeWindowVisible();
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
        if (_exiting)
        {
            return;
        }

        if (_settingsWindow is not null)
        {
            BringToFront(_settingsWindow);
            return;
        }

        var viewModel = new SettingsViewModel(
            _settingsService,
            _startupService,
            _locationService,
            () => _islandWindow?.HotkeyConflictWarning);
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

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint dwThreadId);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, [System.Runtime.InteropServices.Out] char[]? pvInfo, int nLength, out int lpnLengthNeeded);

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

    private async void OnDoNotDisturbRequested(object? sender, bool enabled)
    {
        try
        {
            await _settingsService.SaveAsync(_settingsService.Current with { DoNotDisturb = enabled });
            await _settingsService.FlushAsync();
        }
        catch (Exception ex)
        {
            CrashLogger.Write("App.OnDoNotDisturbRequested", ex);
        }
    }

    private void OnSettingsChanged(object? sender, Pulse.Core.Models.AppSettings settings)
    {
        _trayService?.SetNotificationsPaused(settings.NotificationsPaused);
        _trayService?.SetDoNotDisturb(settings.DoNotDisturb);
        _trayService?.SetHotkeysEnabled(settings.EnableGlobalHotkeys);
        _trayService?.SetStartupEnabled(settings.StartWithWindows);
    }

    private async void OnAboutRequested(object? sender, EventArgs args)
    {
        if (_exiting || _islandWindow is null)
        {
            return;
        }

        try
        {
            var dialog = new ContentDialog
            {
                Title = "Pulse",
                Content = "Windows için yerel medya denetimi. Medya oturumları cihazında işlenir.\nSürüm 1.0.0",
                CloseButtonText = "Kapat",
                XamlRoot = _islandWindow.IslandXamlRoot,
                RequestedTheme = _islandWindow.IslandRequestedTheme
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            CrashLogger.Write("App.OnAboutRequested", ex);
        }
    }

    private async void OnExitRequested(object? sender, EventArgs args)
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _settingsService.SettingsChanged -= OnSettingsChanged;

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

        try
        {
            _showIslandRegistration?.Unregister(null);
            _showIslandSignal?.Dispose();
            _showSettingsRegistration?.Unregister(null);
            _showSettingsSignal?.Dispose();
            _expandMediaRegistration?.Unregister(null);
            _expandMediaSignal?.Dispose();
            _expandFocusRegistration?.Unregister(null);
            _expandFocusSignal?.Dispose();
            _expandSystemRegistration?.Unregister(null);
            _expandSystemSignal?.Dispose();
            _expandShelfRegistration?.Unregister(null);
            _expandShelfSignal?.Dispose();
            _collapseRegistration?.Unregister(null);
            _collapseSignal?.Dispose();
            _toggleSplitDemoRegistration?.Unregister(null);
            _toggleSplitDemoSignal?.Dispose();
            _trayService?.Dispose();
            _settingsWindow?.Close();
            _islandWindow?.Close();

            // Release the single-instance mutex immediately so a rapid restart never collides with teardown.
            if (_instanceMutex is { } mutex)
            {
                _instanceMutex = null;
                try { mutex.ReleaseMutex(); } catch { }
                try { mutex.Dispose(); } catch { }
            }

            _islandViewModel.Dispose();
            _dropShelfService.Dispose();
            _downloadsWatcherService.Dispose();
            _windowsToastService.Dispose();
            _bluetoothWatcherService.Dispose();
            _screenshotWatcherService.Dispose();
            await Task.WhenAny(_mediaViewModel.DisposeAsync().AsTask(), Task.Delay(1200));
            await Task.WhenAny(_mediaSessionService.DisposeAsync().AsTask(), Task.Delay(1200));
            _artworkCacheService.Dispose();
            _systemControlService.Dispose();
            _weatherService.Dispose();
            _locationService.Dispose();
            (_settingsService as IDisposable)?.Dispose();
            _httpClient.Dispose();
        }
        catch (Exception ex)
        {
            CrashLogger.Write("App.OnExitRequested.Cleanup", ex);
        }

        // Watchdog ensures background COM/WinRT threads can never keep a zombie process alive after Exit().
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000).ConfigureAwait(false);
            Environment.Exit(0);
        });

        Exit();
    }
}
