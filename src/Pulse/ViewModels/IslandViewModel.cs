using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Pulse.Core.Events;
using Pulse.Core.Interfaces;
using Pulse.Core.Models;

namespace Pulse.ViewModels;

public sealed class IslandViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly MediaViewModel _media;
    private readonly IEventRouter _eventRouter;
    private readonly IFocusTimerService _focusTimer;
    private readonly ISystemEventService _systemEvents;
    private readonly IFileOperationService _fileOperations;
    private readonly ISystemControlService? _systemControl;
    private readonly DispatcherQueue _dispatcherQueue;

    private IslandEvent? _activeEvent;
    private bool _systemMuted;
    private int _batteryPercent = -1;
    private bool _batteryIsCharging;
    private bool _batteryIsLow;

    public IslandViewModel(
        MediaViewModel media,
        IEventRouter eventRouter,
        IFocusTimerService focusTimer,
        ISystemEventService systemEvents,
        IFileOperationService fileOperations,
        DispatcherQueue dispatcherQueue,
        ISystemControlService? systemControl = null)
    {
        _media = media ?? throw new ArgumentNullException(nameof(media));
        _eventRouter = eventRouter ?? throw new ArgumentNullException(nameof(eventRouter));
        _focusTimer = focusTimer ?? throw new ArgumentNullException(nameof(focusTimer));
        _systemEvents = systemEvents ?? throw new ArgumentNullException(nameof(systemEvents));
        _fileOperations = fileOperations ?? throw new ArgumentNullException(nameof(fileOperations));
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        _systemControl = systemControl;

        _media.PropertyChanged += OnMediaPropertyChanged;
        _eventRouter.ActiveEventChanged += OnActiveEventChanged;
        _focusTimer.Tick += OnFocusTimerTick;

        StartFocusTimerCommand = new RelayCommand(() => _focusTimer.Start(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5)));
        ToggleFocusTimerCommand = new RelayCommand(() =>
        {
            if (_focusTimer.IsRunning)
            {
                _focusTimer.Pause();
            }
            else
            {
                _focusTimer.Resume();
            }
            OnPropertyChanged(nameof(FocusPlayPauseGlyph));
        });
        ResetFocusTimerCommand = new RelayCommand(() => _focusTimer.Reset());

        // Real system audio controls (endpoint volume), replacing the demo reporting path
        if (_systemControl is not null)
        {
            _masterVolume = _systemControl.GetMasterVolume();
            _systemMuted = _systemControl.IsMuted();
        }
        SystemToggleMuteCommand = new RelayCommand(ToggleSystemMute);

        TakeScreenshotCommand = new RelayCommand(() =>
        {
            var path = _systemControl?.CaptureScreenshot() ?? string.Empty;
            if (!string.IsNullOrEmpty(path))
            {
                _systemEvents.ReportScreenshotSaved(path);
            }
        });

        DismissActiveEventCommand = new RelayCommand(() =>
        {
            if (_activeEvent is not null && _activeEvent.CanDismiss)
            {
                _eventRouter.Dismiss(_activeEvent.Id);
            }
        });

        // Demo test commands for interactive verification
        TestBatteryNotificationCommand = new RelayCommand(() => _systemEvents.ReportBatteryStatus(15, false, true));
        TestBluetoothNotificationCommand = new RelayCommand(() => _systemEvents.ReportBluetoothStatus("AirPods Pro", true));
        TestScreenshotNotificationCommand = new RelayCommand(() => _systemEvents.ReportScreenshotSaved(@"C:\Screenshots\Ekran_01.png"));
        TestFileProgressCommand = new RelayCommand(() =>
        {
            var opId = "demo_file_" + Guid.NewGuid().ToString("N")[..6];
            _ = Task.Run(async () =>
            {
                try
                {
                    for (int pct = 10; pct <= 100; pct += 20)
                    {
                        _fileOperations.ReportProgress(opId, "PulseSetup_x64.msix", pct, 100);
                        await Task.Delay(400);
                    }
                    _fileOperations.ReportCompleted(opId, "PulseSetup_x64.msix", @"C:\Downloads\PulseSetup_x64.msix");
                }
                catch
                {
                    // Ignore demo task failures
                }
            });
        });

        // Presets for Focus Timer
        SetFocusTimer25Command = new RelayCommand(() => _focusTimer.Start(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5)));
        SetFocusTimer50Command = new RelayCommand(() => _focusTimer.Start(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(10)));
        SetBreakTimer5Command = new RelayCommand(() => _focusTimer.Start(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5)));
        SetBreakTimer15Command = new RelayCommand(() => _focusTimer.Start(TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(5)));

        _activeEvent = _eventRouter.CurrentActiveEvent;
        RefreshBattery();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private int _selectedPageIndex;
    public int SelectedPageIndex
    {
        get => _selectedPageIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, 2);
            if (_selectedPageIndex != clamped)
            {
                _selectedPageIndex = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsMediaPageSelected));
                OnPropertyChanged(nameof(IsFocusPageSelected));
                OnPropertyChanged(nameof(IsSystemPageSelected));
                OnPropertyChanged(nameof(MediaPageVisibility));
                OnPropertyChanged(nameof(FocusPageVisibility));
                OnPropertyChanged(nameof(SystemPageVisibility));
                OnPropertyChanged(nameof(ExpandedHeaderTitle));

                if (clamped == 2)
                {
                    RefreshBattery();
                    RefreshSystemAudio();
                }
            }
        }
    }

    public bool IsMediaPageSelected => _selectedPageIndex == 0;
    public bool IsFocusPageSelected => _selectedPageIndex == 1;
    public bool IsSystemPageSelected => _selectedPageIndex == 2;
    public Microsoft.UI.Xaml.Visibility MediaPageVisibility => SelectedPageIndex == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility FocusPageVisibility => SelectedPageIndex == 1 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility SystemPageVisibility => SelectedPageIndex == 2 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string ExpandedHeaderTitle => _selectedPageIndex switch
    {
        1 => "Odak Sayacı",
        2 => "Sistem & Denetim",
        _ => AppDisplayName
    };

    public void NextPage() => SelectedPageIndex = (SelectedPageIndex + 1) % 3;
    public void PreviousPage() => SelectedPageIndex = (SelectedPageIndex - 1 + 3) % 3;
    public void SelectPage(int index) => SelectedPageIndex = index;

    private int _masterVolume;
    public int MasterVolume
    {
        get => _masterVolume;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (_masterVolume != clamped)
            {
                _masterVolume = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MasterVolumeLabel));
                _systemControl?.SetMasterVolume(clamped);
                _systemEvents.ReportAudioStatus(_masterVolume, IsSystemMuted);
            }
        }
    }
    public string MasterVolumeLabel => $"%{_masterVolume}";

    public bool IsSystemMuted => _systemMuted;
    public string SystemMuteGlyph => _systemMuted ? "\uE74F" : "\uE995";

    public RelayCommand SystemToggleMuteCommand { get; }

    private void ToggleSystemMute()
    {
        _systemMuted = !_systemMuted;
        try
        {
            _systemControl?.SetMute(_systemMuted);
        }
        catch
        {
            // keep local state; endpoint stays authoritative next refresh
        }

        OnPropertyChanged(nameof(IsSystemMuted));
        OnPropertyChanged(nameof(SystemMuteGlyph));
        _systemEvents.ReportAudioStatus(_masterVolume, _systemMuted);
    }

    public bool HasBattery => _batteryPercent >= 0;
    public int BatteryPercent => _batteryPercent;
    public bool BatteryIsCharging => _batteryIsCharging;
    public string BatterySummary => _batteryIsCharging ? $"%{_batteryPercent} Şarj Oluyor" : $"%{_batteryPercent} Pil";
    public string BatteryGlyph => _batteryIsCharging
        ? "\uEBAA"
        : (_batteryIsLow ? "\uEBA0" : "\uEBA2");
    public Microsoft.UI.Xaml.Visibility BatteryVisibility => HasBattery
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;

    private void RefreshBattery()
    {
        var status = _systemControl?.GetBatteryStatus();
        if (status is null)
        {
            if (_batteryPercent >= 0)
            {
                _batteryPercent = -1;
                NotifyBatteryChanged();
            }
            return;
        }

        var (percent, isCharging, isLow) = status.Value;
        if (_batteryPercent != percent || _batteryIsCharging != isCharging || _batteryIsLow != isLow)
        {
            _batteryPercent = percent;
            _batteryIsCharging = isCharging;
            _batteryIsLow = isLow;
            NotifyBatteryChanged();
        }
    }

    private void NotifyBatteryChanged()
    {
        OnPropertyChanged(nameof(HasBattery));
        OnPropertyChanged(nameof(BatteryPercent));
        OnPropertyChanged(nameof(BatteryIsCharging));
        OnPropertyChanged(nameof(BatterySummary));
        OnPropertyChanged(nameof(BatteryGlyph));
        OnPropertyChanged(nameof(BatteryVisibility));
    }

    public MediaViewModel Media => _media;
    public IEventRouter EventRouter => _eventRouter;
    public IFocusTimerService FocusTimer => _focusTimer;
    public ISystemEventService SystemEvents => _systemEvents;
    public IFileOperationService FileOperations => _fileOperations;

    // Commands
    public RelayCommand StartFocusTimerCommand { get; }
    public RelayCommand ToggleFocusTimerCommand { get; }
    public RelayCommand ResetFocusTimerCommand { get; }
    public RelayCommand DismissActiveEventCommand { get; }
    public RelayCommand SetFocusTimer25Command { get; }
    public RelayCommand SetFocusTimer50Command { get; }
    public RelayCommand SetBreakTimer5Command { get; }
    public RelayCommand SetBreakTimer15Command { get; }
    public RelayCommand TakeScreenshotCommand { get; }
    public RelayCommand TestBatteryNotificationCommand { get; }
    public RelayCommand TestBluetoothNotificationCommand { get; }
    public RelayCommand TestScreenshotNotificationCommand { get; }
    public RelayCommand TestFileProgressCommand { get; }

    // Active Event Context
    public IslandEvent? ActiveEvent => _activeEvent;

    public bool IsNotificationActive => _activeEvent is SystemNotificationEvent;
    public string NotificationTitle => (_activeEvent as SystemNotificationEvent)?.Title ?? string.Empty;
    public string NotificationSubtitle => (_activeEvent as SystemNotificationEvent)?.Subtitle ?? string.Empty;
    public string NotificationGlyph => (_activeEvent as SystemNotificationEvent)?.IconGlyph ?? "\uE7E7";
    public bool IsNotificationWarning => (_activeEvent as SystemNotificationEvent)?.Priority == EventPriority.Warning;

    public bool IsFocusTimerActive => _activeEvent is FocusTimerEvent || _focusTimer.IsRunning;
    public string FocusTimerText
    {
        get
        {
            var evt = _activeEvent as FocusTimerEvent;
            var remaining = evt?.RemainingTime ?? _focusTimer.RemainingTime;
            return $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";
        }
    }
    public string FocusTimerModeText => (_activeEvent as FocusTimerEvent)?.IsBreak == true || _focusTimer.IsBreak ? "Mola" : "Odaklanma";
    public bool FocusTimerIsRunning => _focusTimer.IsRunning;
    public string FocusPlayPauseGlyph => _focusTimer.IsRunning ? "\uE769" : "\uE768";

    public bool IsFileOperationActive => _activeEvent is FileOperationEvent;
    public string FileOperationName => (_activeEvent as FileOperationEvent)?.FileName ?? string.Empty;
    public double FileOperationProgress => (_activeEvent as FileOperationEvent)?.ProgressPercentage ?? 0.0;
    public string FileOperationStatusText
    {
        get
        {
            if (_activeEvent is not FileOperationEvent evt) return string.Empty;
            return evt.Status switch
            {
                FileOperationStatus.InProgress => $"%{evt.ProgressPercentage:F0} Tamamlandı",
                FileOperationStatus.Completed => "İndirme Tamamlandı",
                FileOperationStatus.Failed => evt.ErrorMessage ?? "Başarısız Oldu",
                _ => string.Empty
            };
        }
    }

    public bool IsMediaActive => !IsNotificationActive && !IsFocusTimerActive && !IsFileOperationActive && (_media.HasTrack || _activeEvent is MediaEvent);

    public Microsoft.UI.Xaml.Visibility NotificationVisibility => IsNotificationActive ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility FocusTimerVisibility => (!IsNotificationActive && IsFocusTimerActive) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility FileOperationVisibility => (!IsNotificationActive && !IsFocusTimerActive && IsFileOperationActive) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility TrackContentVisibility => (IsMediaActive && HasTrack && !IsNotificationActive && !IsFocusTimerActive && !IsFileOperationActive && IsCompact) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility CustomIdleVisibility => (!HasTrack && !IsNotificationActive && !IsFocusTimerActive && !IsFileOperationActive && CurrentIslandState is (IslandState.Idle or IslandState.Hover)) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    // Delegated Media ViewModel properties for unified XAML binding compatibility
    public MediaTrack? CurrentTrack => _media.CurrentTrack;
    public bool HasTrack => _media.HasTrack;
    public bool IsPlaying => _media.IsPlaying;
    public string AppDisplayName => _media.AppDisplayName;
    public string AppBadgeText => _media.AppBadgeText;
    public bool IsSpotify => _media.IsSpotify;
    public IslandState CurrentIslandState => _media.CurrentIslandState;
    public bool IsIdle => _media.IsIdle;
    public bool IsExpanded => _media.IsExpanded;
    public bool IsCompact => _media.IsCompact;
    public bool ShowSeekControls => _media.ShowSeekControls;
    public bool NotificationsPaused => _media.NotificationsPaused;
    public Microsoft.UI.Xaml.Visibility IdleVisibility => _media.IdleVisibility;
    public Microsoft.UI.Xaml.Visibility CompactVisibility => _media.CompactVisibility;
    public Microsoft.UI.Xaml.Visibility ExpandedVisibility => _media.ExpandedVisibility;
    public Microsoft.UI.Xaml.Visibility SeekPanelVisibility => _media.SeekPanelVisibility;
    public bool AutoCollapse => _media.AutoCollapse;
    public bool EnableHoverExpand => _media.EnableHoverExpand;
    public bool DynamicColorTint => _media.DynamicColorTint;
    public bool ShowEqualizer => _media.ShowEqualizer;
    public bool UseTransparency => _media.UseTransparency;
    public int TopOffset => _media.TopOffset;
    public double IslandScale => _media.IslandScale;
    public string AnimationIntensity => _media.AnimationIntensity;
    public string Theme => _media.Theme;
    public bool CanTogglePlayback => _media.CanTogglePlayback;
    public bool CanGoNext => _media.CanGoNext;
    public bool CanGoPrevious => _media.CanGoPrevious;
    public BitmapImage? ArtworkImage => _media.ArtworkImage;
    public uint ArtworkAccentColor => _media.ArtworkAccentColor;
    public bool HasLyrics => _media.HasLyrics;
    public string CurrentLyricLine => _media.CurrentLyricLine;
    public string NextLyricLine => _media.NextLyricLine;
    public bool IsScrubbing => _media.IsScrubbing;
    public string PlaybackLabel => _media.PlaybackLabel;
    public string PlayPauseGlyph => _media.PlayPauseGlyph;
    public string PositionLabel => _media.PositionLabel;
    public string DurationLabel => _media.DurationLabel;
    public double SeekPercent => _media.SeekPercent;
    public string SeekStatus => _media.SeekStatus;
    public Microsoft.UI.Xaml.Visibility SeekControlsVisibility => _media.SeekControlsVisibility;
    public AsyncCommand PreviousCommand => _media.PreviousCommand;
    public AsyncCommand PlayPauseCommand => _media.PlayPauseCommand;
    public AsyncCommand NextCommand => _media.NextCommand;
    public AsyncCommand SeekBackwardCommand => _media.SeekBackwardCommand;
    public AsyncCommand SeekForwardCommand => _media.SeekForwardCommand;
    public RelayCommand ToggleShuffleCommand => _media.ToggleShuffleCommand;
    public RelayCommand ToggleFavoriteCommand => _media.ToggleFavoriteCommand;
    public RelayCommand ToggleMuteCommand => _media.ToggleMuteCommand;
    public bool IsShuffleEnabled => _media.IsShuffleEnabled;
    public bool IsFavorite => _media.IsFavorite;
    public string FavoriteGlyph => _media.FavoriteGlyph;
    public Microsoft.UI.Xaml.Media.SolidColorBrush ShuffleBrush => _media.ShuffleBrush;
    public Microsoft.UI.Xaml.Media.SolidColorBrush FavoriteBrush => _media.FavoriteBrush;
    public bool IsMuted => _media.IsMuted;
    public string MuteGlyph => _media.MuteGlyph;

    // Delegated actions
    public void OnPointerEntered() => _media.OnPointerEntered();
    public void OnPointerExited() => _media.OnPointerExited();
    public void ToggleExpanded() => _media.ToggleExpanded();
    public void Collapse() => _media.Collapse();
    public void CompleteCollapse() => _media.CompleteCollapse();
    public void BeginScrub(double percent) => _media.BeginScrub(percent);
    public void UpdateScrub(double percent) => _media.UpdateScrub(percent);
    public Task EndScrubAsync(double percent) => _media.EndScrubAsync(percent);

    private void OnActiveEventChanged(object? sender, IslandEvent? newEvent)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            ApplyActiveEvent(newEvent);
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => ApplyActiveEvent(newEvent));
        }
    }

    private void ApplyActiveEvent(IslandEvent? newEvent)
    {
        _activeEvent = newEvent;
        OnPropertyChanged(nameof(ActiveEvent));
        OnPropertyChanged(nameof(IsNotificationActive));
        OnPropertyChanged(nameof(NotificationTitle));
        OnPropertyChanged(nameof(NotificationSubtitle));
        OnPropertyChanged(nameof(NotificationGlyph));
        OnPropertyChanged(nameof(IsNotificationWarning));
        OnPropertyChanged(nameof(IsFocusTimerActive));
        OnPropertyChanged(nameof(FocusTimerText));
        OnPropertyChanged(nameof(FocusTimerModeText));
        OnPropertyChanged(nameof(FocusTimerIsRunning));
        OnPropertyChanged(nameof(FocusPlayPauseGlyph));
        OnPropertyChanged(nameof(IsFileOperationActive));
        OnPropertyChanged(nameof(FileOperationName));
        OnPropertyChanged(nameof(FileOperationProgress));
        OnPropertyChanged(nameof(FileOperationStatusText));
        OnPropertyChanged(nameof(IsMediaActive));
        OnPropertyChanged(nameof(NotificationVisibility));
        OnPropertyChanged(nameof(FocusTimerVisibility));
        OnPropertyChanged(nameof(FileOperationVisibility));
        OnPropertyChanged(nameof(TrackContentVisibility));
        OnPropertyChanged(nameof(CustomIdleVisibility));
    }

    private void OnFocusTimerTick(object? sender, FocusTimerEvent e)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            OnPropertyChanged(nameof(FocusTimerText));
            OnPropertyChanged(nameof(FocusTimerModeText));
            OnPropertyChanged(nameof(FocusTimerIsRunning));
            OnPropertyChanged(nameof(FocusPlayPauseGlyph));
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(FocusTimerText));
                OnPropertyChanged(nameof(FocusTimerModeText));
                OnPropertyChanged(nameof(FocusTimerIsRunning));
                OnPropertyChanged(nameof(FocusPlayPauseGlyph));
            });
        }
    }

    /// <summary>Re-reads endpoint volume and mute state from the real audio device.</summary>
    private void RefreshSystemAudio()
    {
        if (_systemControl is null)
        {
            return;
        }

        var volume = _systemControl.GetMasterVolume();
        var muted = _systemControl.IsMuted();
        if (_masterVolume != volume)
        {
            _masterVolume = volume;
            OnPropertyChanged(nameof(MasterVolume));
            OnPropertyChanged(nameof(MasterVolumeLabel));
        }

        if (_systemMuted != muted)
        {
            _systemMuted = muted;
            OnPropertyChanged(nameof(IsSystemMuted));
            OnPropertyChanged(nameof(SystemMuteGlyph));
        }
    }

    private void OnMediaPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName))
        {
            OnPropertyChanged(string.Empty);
            return;
        }

        OnPropertyChanged(e.PropertyName);

        if (e.PropertyName == nameof(MediaViewModel.HasTrack) || e.PropertyName == nameof(MediaViewModel.CurrentTrack))
        {
            OnPropertyChanged(nameof(IsMediaActive));
            OnPropertyChanged(nameof(TrackContentVisibility));
            OnPropertyChanged(nameof(CustomIdleVisibility));
        }
        else if (e.PropertyName is nameof(MediaViewModel.CurrentIslandState) or nameof(MediaViewModel.CompactVisibility) or nameof(MediaViewModel.IdleVisibility))
        {
            OnPropertyChanged(nameof(TrackContentVisibility));
            OnPropertyChanged(nameof(CustomIdleVisibility));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        _media.PropertyChanged -= OnMediaPropertyChanged;
        _eventRouter.ActiveEventChanged -= OnActiveEventChanged;
        _focusTimer.Tick -= OnFocusTimerTick;
        _focusTimer.Dispose();
        _systemEvents.Dispose();
        _fileOperations.Dispose();
    }
}
