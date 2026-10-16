using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pulse.Core.Interfaces;
using Pulse.Core.Services;
using Pulse.Services.Artwork;
using Pulse.Services.Media;
using Pulse.Services.Settings;
using Pulse.Services.System;
using Pulse.ViewModels;
using Pulse.Views.Flyout;
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
    private readonly MediaViewModel _mediaViewModel;
    private readonly IslandViewModel _islandViewModel;
    private IslandWindow? _islandWindow;
    private TaskbarFlyoutWindow? _taskbarFlyoutWindow;
    private SettingsWindow? _settingsWindow;
    private TrayService? _trayService;

    public App()
    {
        InitializeComponent();
        _settingsService = new SettingsService();
        _settingsService.SettingsChanged += OnSettingsChanged;
        _startupService = new StartupService();
        _mediaSessionService = new WindowsMediaSessionService(new ArtworkCacheService());
        var playbackController = new PlaybackController(_mediaSessionService);
        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        _eventRouter = new EventRouter();
        _focusTimerService = new FocusTimerService(_eventRouter);
        _systemEventService = new SystemEventService(_eventRouter);
        _fileOperationService = new FileOperationService(_eventRouter);

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
            dispatcherQueue);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await _settingsService.LoadAsync();
        await _mediaViewModel.InitializeAsync();

        _islandWindow = new IslandWindow(_islandViewModel);
        _islandWindow.SettingsRequested += OnSettingsRequested;
        _islandWindow.TaskbarFlyoutRequested += (_, _) => _taskbarFlyoutWindow?.ToggleFlyout();
        _islandWindow.Activate();
        _islandWindow.ShowIsland();

        _taskbarFlyoutWindow = new TaskbarFlyoutWindow(_mediaViewModel);

        var startupEnabled = await _startupService.IsEnabledAsync();
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        _trayService = new TrayService(iconPath, startupEnabled);
        _trayService.ShowRequested += (_, _) =>
        {
            if (_islandWindow?.IsIslandHidden == true)
            {
                _islandWindow.UnhideIslandAnimated();
            }
            _taskbarFlyoutWindow?.ToggleFlyout();
        };
        _trayService.IslandToggleRequested += (_, _) => _islandWindow?.ToggleHideIsland();
        _trayService.SettingsRequested += OnSettingsRequested;
        _trayService.AboutRequested += OnAboutRequested;
        _trayService.ExitRequested += OnExitRequested;
        _trayService.StartupRequested += OnStartupRequested;
        _trayService.SetNotificationsPaused(_settingsService.Current.NotificationsPaused);
        _trayService.NotificationsPauseRequested += OnNotificationsPauseRequested;
    }

    private void OnSettingsRequested(object? sender, EventArgs args)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var viewModel = new SettingsViewModel(_settingsService, _startupService);
        _settingsWindow = new SettingsWindow(viewModel);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Activate();
    }

    private async void OnStartupRequested(object? sender, bool enabled)
    {
        var accepted = await _startupService.SetEnabledAsync(enabled);
        if (_trayService is not null)
        {
            _trayService.SetStartupEnabled(accepted);
        }

        if (accepted)
        {
            await _settingsService.SaveAsync(_settingsService.Current with { StartWithWindows = enabled });
        }
    }

    private async void OnNotificationsPauseRequested(object? sender, bool paused)
    {
        await _settingsService.SaveAsync(_settingsService.Current with { NotificationsPaused = paused });
    }

    private void OnSettingsChanged(object? sender, Pulse.Core.Models.AppSettings settings)
        => _trayService?.SetNotificationsPaused(settings.NotificationsPaused);

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
        _trayService?.Dispose();
        _settingsWindow?.Close();
        _taskbarFlyoutWindow?.Close();
        _islandWindow?.Close();
        _islandViewModel.Dispose();
        await _mediaViewModel.DisposeAsync();
        await _mediaSessionService.DisposeAsync();
        Exit();
    }
}
