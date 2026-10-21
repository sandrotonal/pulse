using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
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
    private bool _batteryOnAc;

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

        StartFocusTimerCommand = new RelayCommand(() =>
        {
            _focusTimer.Start(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5));
            NotifySplitStateChanged();
        });
        ToggleFocusTimerCommand = new RelayCommand(() =>
        {
            if (_focusTimer.IsRunning)
            {
                _focusTimer.Pause();
            }
            else if (_focusTimer.State == FocusTimerState.Completed)
            {
                // Completed sessions restart as a fresh work session instead of doing nothing.
                _focusTimer.Restart();
            }
            else
            {
                _focusTimer.Resume();
            }
            NotifyFocusTick();
            NotifySplitStateChanged();
        });
        ResetFocusTimerCommand = new RelayCommand(() =>
        {
            _focusTimer.Reset();
            NotifyFocusTick();
            NotifySplitStateChanged();
        });

        ToggleDemoTrackCommand = new RelayCommand(() =>
        {
            _media.ToggleDemoTrack();
            NotifySplitStateChanged();
        });

        ToggleSplitIslandDemoCommand = new RelayCommand(() =>
        {
            if (!_isSplitDemoForced && !IsSplitModeActive)
            {
                if (!_media.HasTrack)
                {
                    _media.SimulateDemoTrack();
                }
                if (!_focusTimer.IsRunning && _focusTimer.State != FocusTimerState.Running)
                {
                    _focusTimer.Start(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5));
                }
                _isSplitDemoForced = true;
                _media.Collapse();
            }
            else
            {
                _isSplitDemoForced = false;
                _focusTimer.Reset();
                _media.ClearDemoTrack();
                _media.Collapse();
            }
            NotifySplitStateChanged();
        });

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

        // Presets for Focus Timer (keyed so the UI can highlight the active preset)
        SetFocusTimer25Command = new RelayCommand(() => StartFocusPreset("25", TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5)));
        SetFocusTimer50Command = new RelayCommand(() => StartFocusPreset("50", TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(10)));
        SetBreakTimer5Command = new RelayCommand(() => StartFocusPreset("5", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5)));
        SetBreakTimer15Command = new RelayCommand(() => StartFocusPreset("15", TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(5)));

        _activeEvent = _eventRouter.CurrentActiveEvent;
        RefreshBattery();
        StartSystemPolling();
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
        OnPropertyChanged(nameof(SystemMuteBrush));
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

    private void RefreshBattery(bool notify = false)
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

        var (percent, isCharging, isLow, isOnAc) = status.Value;
        if (_batteryPercent != percent || _batteryIsCharging != isCharging || _batteryIsLow != isLow || _batteryOnAc != isOnAc)
        {
            var wasPrimed = _batteryPercent >= 0;
            var wasLow = _batteryIsLow;
            var wasOnAc = _batteryOnAc;
            _batteryPercent = percent;
            _batteryIsCharging = isCharging;
            _batteryIsLow = isLow;
            _batteryOnAc = isOnAc;
            NotifyBatteryChanged();

            if (notify && wasPrimed)
            {
                if (!wasOnAc && isOnAc)
                {
                    // Plugging in must be visible even when the battery is full and Windows
                    // reports the pack as idle rather than charging.
                    _systemEvents.ReportPowerConnected(percent);
                }
                else if (!wasLow && isLow)
                {
                    _systemEvents.ReportBatteryStatus(percent, isCharging, true);
                }
            }
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
        OnPropertyChanged(nameof(BatteryBrush));
        OnPropertyChanged(nameof(SystemMuteBrush));
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
    public RelayCommand ToggleDemoTrackCommand { get; }
    public RelayCommand ToggleSplitIslandDemoCommand { get; }

    private bool _isSplitDemoForced;
    public bool IsSplitDemoForced
    {
        get => _isSplitDemoForced;
        set
        {
            if (_isSplitDemoForced != value)
            {
                _isSplitDemoForced = value;
                OnPropertyChanged();
                NotifySplitStateChanged();
            }
        }
    }

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

    private string _focusPresetKey = "25";
    public double FocusProgress => _focusTimer.TotalDuration.TotalSeconds <= 0
        ? 0
        : Math.Clamp(1.0 - (_focusTimer.RemainingTime.TotalSeconds / _focusTimer.TotalDuration.TotalSeconds), 0, 1);
    public bool PreserveFocusPanel => _focusTimer.IsRunning && _selectedPageIndex == 1;

    private void StartFocusPreset(string key, TimeSpan focus, TimeSpan breakTime)
    {
        _focusPresetKey = key;
        NotifyFocusBrushes();
        _focusTimer.Start(focus, breakTime);
        OnPropertyChanged(nameof(PreserveFocusPanel));
        NotifySplitStateChanged();
    }

    public Microsoft.UI.Xaml.Media.SolidColorBrush FocusPreset25Brush => FocusTipBrush("25");
    public Microsoft.UI.Xaml.Media.SolidColorBrush FocusPreset50Brush => FocusTipBrush("50");
    public Microsoft.UI.Xaml.Media.SolidColorBrush BreakPreset5Brush => FocusTipBrush("5");
    public Microsoft.UI.Xaml.Media.SolidColorBrush BreakPreset15Brush => FocusTipBrush("15");

    private Microsoft.UI.Xaml.Media.SolidColorBrush FocusTipBrush(string key)
    {
        if (key == _focusPresetKey)
        {
            var accent = _media.Theme == "Light"
                ? 0xFF007AFFu
                : (_media.DynamicColorTint ? _media.ArtworkAccentColor : 0xFF007AFFu);
            return MakeBrush(accent);
        }

        var idle = _media.Theme == "Light" ? 0x16000000u : 0x1FFFFFFFu;
        return MakeBrush(idle);
    }

    public Microsoft.UI.Xaml.Media.SolidColorBrush SystemMuteBrush => _systemMuted
        ? MakeBrush(0xFFFF453Au)
        : (_media.Theme == "Light" ? MakeBrush(0x14000000u) : MakeBrush(0x22FFFFFFu));

    public Microsoft.UI.Xaml.Media.SolidColorBrush BatteryBrush => MakeBrush(_batteryIsCharging
        ? 0xFF30D158u
        : _batteryIsLow ? 0xFFFF453Au : 0xFF007AFFu);

    private static Microsoft.UI.Xaml.Media.SolidColorBrush MakeBrush(uint value)
    {
        var color = Windows.UI.Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
    }

    private void NotifyFocusBrushes()
    {
        OnPropertyChanged(nameof(FocusPreset25Brush));
        OnPropertyChanged(nameof(FocusPreset50Brush));
        OnPropertyChanged(nameof(BreakPreset5Brush));
        OnPropertyChanged(nameof(BreakPreset15Brush));
        OnPropertyChanged(nameof(SystemMuteBrush));
        OnPropertyChanged(nameof(BatteryBrush));
        OnPropertyChanged(nameof(FocusProgress));
    }

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

    public bool IsMediaActive => !IsNotificationActive && !IsFocusTimerActive && !IsFileOperationActive && (_media.HasTrack || _isSplitDemoForced || _activeEvent is MediaEvent);

    public bool IsSplitModeActive => IsCompact && (
        _isSplitDemoForced ||
        (HasTrack && (IsFocusTimerActive || IsNotificationActive || IsFileOperationActive)) ||
        (IsFocusTimerActive && (IsNotificationActive || IsFileOperationActive))
    );
    public Microsoft.UI.Xaml.Visibility SplitModeVisibility => IsSplitModeActive ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string DetachedGlyph => IsFocusTimerActive ? "\uE916" : (IsNotificationActive ? NotificationGlyph : (_isSplitDemoForced ? "\uE916" : "\uE896"));
    public string DetachedBadgeText => IsFocusTimerActive ? FocusTimerText : (IsNotificationActive ? "!" : (_isSplitDemoForced ? "25:00" : $"{FileOperationProgress:F0}%"));
    public string DetachedToolTip => IsFocusTimerActive ? "Odak Sayacı" : (IsNotificationActive ? NotificationTitle : (_isSplitDemoForced ? "Odak Sayacı (Demo)" : FileOperationName));

    public void OnDetachedPillClicked()
    {
        if (IsFocusTimerActive || _isSplitDemoForced)
        {
            SelectedPageIndex = 1;
        }
        else if (IsNotificationActive || IsFileOperationActive)
        {
            SelectedPageIndex = 2;
        }
        ToggleExpanded();
    }

    public Microsoft.UI.Xaml.Visibility NotificationVisibility => (!IsSplitModeActive && IsNotificationActive) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility FocusTimerVisibility => (!IsSplitModeActive && !IsNotificationActive && IsFocusTimerActive) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility FileOperationVisibility => (!IsSplitModeActive && !IsNotificationActive && !IsFocusTimerActive && IsFileOperationActive) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility TrackContentVisibility => ((HasTrack || _isSplitDemoForced) && (!IsNotificationActive && !IsFocusTimerActive && !IsFileOperationActive || IsSplitModeActive) && ShowsCompactContent) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility CustomIdleVisibility => (!HasTrack && !_isSplitDemoForced && !IsNotificationActive && !IsFocusTimerActive && !IsFileOperationActive && CurrentIslandState is (IslandState.Idle or IslandState.Hover)) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

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
    public bool ShowsCompactContent => _media.ShowsCompactContent;
    public bool IsCollapsing => CurrentIslandState == IslandState.Collapsing;
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
    public int TransparencyLevel => _media.TransparencyLevel;
    public int ArtworkTintStrength => _media.ArtworkTintStrength;
    public int MotionSpeed => _media.MotionSpeed;
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
    public Microsoft.UI.Xaml.Visibility LyricsVisibility => HasLyrics
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;
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

    /// <summary>Launches (or foregrounds) the app that is currently providing the media session.</summary>
    public void ActivateMediaSourceApp()
    {
        var appId = _media.SourceAppUserModelId;
        if (!string.IsNullOrWhiteSpace(appId))
        {
            _systemControl?.TryActivateApp(appId);
        }
    }

    /// <summary>Opens the destination related to the active event (screenshot file, download folder...).</summary>
    public void ActivateActiveEventTarget()
    {
        var target = _activeEvent switch
        {
            SystemNotificationEvent notification => notification.LaunchTarget,
            FileOperationEvent fileOperation => fileOperation.DestinationPath,
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(target))
        {
            _systemControl?.TryOpenPath(target);
        }
    }

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
        var prevEvent = _activeEvent;
        _activeEvent = newEvent;
        OnPropertyChanged(nameof(ActiveEvent));

        bool stateChanged = (prevEvent is null) != (newEvent is null) ||
                            (prevEvent?.GetType() != newEvent?.GetType()) ||
                            (prevEvent is FocusTimerEvent pF && newEvent is FocusTimerEvent nF && pF.State != nF.State) ||
                            (prevEvent is FileOperationEvent pO && newEvent is FileOperationEvent nO && pO.Status != nO.Status);

        if (stateChanged)
        {
            OnPropertyChanged(nameof(IsNotificationActive));
            OnPropertyChanged(nameof(IsFocusTimerActive));
            OnPropertyChanged(nameof(IsFileOperationActive));
            OnPropertyChanged(nameof(IsMediaActive));
            OnPropertyChanged(nameof(NotificationVisibility));
            OnPropertyChanged(nameof(FocusTimerVisibility));
            OnPropertyChanged(nameof(FileOperationVisibility));
            OnPropertyChanged(nameof(TrackContentVisibility));
            OnPropertyChanged(nameof(CustomIdleVisibility));
            NotifySplitStateChanged();
        }

        OnPropertyChanged(nameof(NotificationTitle));
        OnPropertyChanged(nameof(NotificationSubtitle));
        OnPropertyChanged(nameof(NotificationGlyph));
        OnPropertyChanged(nameof(IsNotificationWarning));
        OnPropertyChanged(nameof(FocusTimerText));
        OnPropertyChanged(nameof(FocusTimerModeText));
        OnPropertyChanged(nameof(FocusTimerIsRunning));
        OnPropertyChanged(nameof(FocusPlayPauseGlyph));
        OnPropertyChanged(nameof(FileOperationName));
        OnPropertyChanged(nameof(FileOperationProgress));
        OnPropertyChanged(nameof(FileOperationStatusText));
        OnPropertyChanged(nameof(DetachedGlyph));
        OnPropertyChanged(nameof(DetachedBadgeText));
        OnPropertyChanged(nameof(DetachedToolTip));
    }

    private void OnFocusTimerTick(object? sender, FocusTimerEvent e)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            NotifyFocusTick();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(NotifyFocusTick);
        }
    }

    private void NotifyFocusTick()
    {
        OnPropertyChanged(nameof(FocusTimerText));
        OnPropertyChanged(nameof(FocusTimerModeText));
        OnPropertyChanged(nameof(FocusTimerIsRunning));
        OnPropertyChanged(nameof(FocusPlayPauseGlyph));
        OnPropertyChanged(nameof(FocusProgress));
        OnPropertyChanged(nameof(PreserveFocusPanel));
        OnPropertyChanged(nameof(DetachedBadgeText));
    }

    private bool _lastSplitModeActive;

    private void NotifySplitStateChanged()
    {
        var isSplitNow = IsSplitModeActive;
        if (_lastSplitModeActive != isSplitNow)
        {
            _lastSplitModeActive = isSplitNow;
            OnPropertyChanged(nameof(IsSplitModeActive));
            OnPropertyChanged(nameof(SplitModeVisibility));
        }

        OnPropertyChanged(nameof(DetachedGlyph));
        OnPropertyChanged(nameof(DetachedBadgeText));
        OnPropertyChanged(nameof(DetachedToolTip));
        OnPropertyChanged(nameof(TrackContentVisibility));
        OnPropertyChanged(nameof(NotificationVisibility));
        OnPropertyChanged(nameof(FocusTimerVisibility));
        OnPropertyChanged(nameof(FileOperationVisibility));
        OnPropertyChanged(nameof(CustomIdleVisibility));
        OnPropertyChanged(nameof(IsMediaActive));
    }

    private DispatcherTimer? _systemPollTimer;

    private void StartSystemPolling()
    {
        if (_systemControl is null)
        {
            return;
        }

        _systemPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _systemPollTimer.Tick += (_, _) => PollSystemState();
        _systemPollTimer.Start();
    }

    private void PollSystemState()
    {
        if (_systemControl is null)
        {
            return;
        }

        try
        {
            var volume = _systemControl.GetMasterVolume();
            var muted = _systemControl.IsMuted();
            if (volume != _masterVolume || muted != _systemMuted)
            {
                // Changed outside the island (hardware keys, other apps) — mirror it and show a HUD capsule.
                _masterVolume = volume;
                _systemMuted = muted;
                OnPropertyChanged(nameof(MasterVolume));
                OnPropertyChanged(nameof(MasterVolumeLabel));
                OnPropertyChanged(nameof(IsSystemMuted));
                OnPropertyChanged(nameof(SystemMuteGlyph));
                OnPropertyChanged(nameof(SystemMuteBrush));
                _systemEvents.ReportAudioStatus(volume, muted);
            }
        }
        catch
        {
            // endpoint may be temporarily unavailable; next tick retries
        }

        RefreshBattery(notify: true);
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
            OnPropertyChanged(nameof(SystemMuteBrush));
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

        if (e.PropertyName == nameof(MediaViewModel.HasLyrics))
        {
            OnPropertyChanged(nameof(LyricsVisibility));
        }
        else if (e.PropertyName == nameof(MediaViewModel.HasTrack) || e.PropertyName == nameof(MediaViewModel.CurrentTrack))
        {
            OnPropertyChanged(nameof(IsMediaActive));
            OnPropertyChanged(nameof(TrackContentVisibility));
            OnPropertyChanged(nameof(CustomIdleVisibility));
            NotifySplitStateChanged();
        }
        else if (e.PropertyName is nameof(MediaViewModel.CurrentIslandState) or nameof(MediaViewModel.CompactVisibility) or nameof(MediaViewModel.IdleVisibility))
        {
            OnPropertyChanged(nameof(TrackContentVisibility));
            OnPropertyChanged(nameof(CustomIdleVisibility));
            NotifySplitStateChanged();
        }
        else if (e.PropertyName is nameof(MediaViewModel.Theme) or nameof(MediaViewModel.ArtworkAccentColor) or nameof(MediaViewModel.DynamicColorTint))
        {
            NotifyFocusBrushes();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        _systemPollTimer?.Stop();
        _systemPollTimer = null;
        _media.PropertyChanged -= OnMediaPropertyChanged;
        _eventRouter.ActiveEventChanged -= OnActiveEventChanged;
        _focusTimer.Tick -= OnFocusTimerTick;
        _focusTimer.Dispose();
        _systemEvents.Dispose();
        _fileOperations.Dispose();
    }
}
