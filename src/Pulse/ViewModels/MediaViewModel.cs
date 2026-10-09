using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Pulse.Core.Interfaces;
using Pulse.Core.Models;
using Pulse.Core.Services;
using Windows.Storage.Streams;

namespace Pulse.ViewModels;

public sealed class MediaViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IMediaSessionService _mediaSessionService;
    private readonly IPlaybackController _playbackController;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ISettingsService _settingsService;
    private readonly IslandStateMachine _stateMachine = new();
    private readonly DispatcherQueueTimer _stateTimer;
    private readonly DispatcherQueueTimer _playbackTicker;
    private DateTimeOffset _lastTickTime = DateTimeOffset.UtcNow;
    private MediaTrack? _currentTrack;
    private BitmapImage? _artworkImage;
    private double? _seekPreviewPercent;
    private string? _seekStatus;
    private bool _isInitialized;

    public MediaViewModel(IMediaSessionService mediaSessionService, IPlaybackController playbackController, ISettingsService settingsService, DispatcherQueue dispatcherQueue)
    {
        _mediaSessionService = mediaSessionService;
        _playbackController = playbackController;
        _settingsService = settingsService;
        _dispatcherQueue = dispatcherQueue;
        _stateTimer = dispatcherQueue.CreateTimer();
        _stateTimer.Interval = TimeSpan.FromSeconds(2.4);
        _stateTimer.Tick += OnStateTimerTick;
        _playbackTicker = dispatcherQueue.CreateTimer();
        _playbackTicker.Interval = TimeSpan.FromMilliseconds(250);
        _playbackTicker.Tick += OnPlaybackTickerTick;
        _stateMachine.StateChanged += OnIslandStateChanged;
        _settingsService.SettingsChanged += OnSettingsChanged;
        PreviousCommand = new AsyncCommand(() => _playbackController.PreviousAsync());
        PlayPauseCommand = new AsyncCommand(() => _playbackController.TogglePlayPauseAsync());
        NextCommand = new AsyncCommand(() => _playbackController.NextAsync());
        SeekBackwardCommand = new AsyncCommand(async () =>
        {
            if (CurrentTrack is { Position: var pos, Duration: var dur })
            {
                var target = PlaybackPosition.Offset(pos, TimeSpan.FromSeconds(-15), dur);
                _currentTrack = CurrentTrack with { Position = target };
                OnPropertyChanged(nameof(PositionLabel));
                OnPropertyChanged(nameof(SeekPercent));
            }
            await ExecuteSeekAsync(() => _playbackController.SeekBackwardAsync(TimeSpan.FromSeconds(15)));
        });
        SeekForwardCommand = new AsyncCommand(async () =>
        {
            if (CurrentTrack is { Position: var pos, Duration: var dur })
            {
                var target = PlaybackPosition.Offset(pos, TimeSpan.FromSeconds(15), dur);
                _currentTrack = CurrentTrack with { Position = target };
                OnPropertyChanged(nameof(PositionLabel));
                OnPropertyChanged(nameof(SeekPercent));
            }
            await ExecuteSeekAsync(() => _playbackController.SeekForwardAsync(TimeSpan.FromSeconds(15)));
        });
        _mediaSessionService.CurrentTrackChanged += OnCurrentTrackChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private string? _lastArtworkKey;

    public MediaTrack? CurrentTrack
    {
        get => _currentTrack;
        private set
        {
            var artworkChanged = HasArtworkChanged(value);
            if (SetProperty(ref _currentTrack, value))
            {
                OnPropertyChanged(nameof(HasTrack));
                OnPropertyChanged(nameof(IsPlaying));
                OnPropertyChanged(nameof(CanTogglePlayback));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(PlaybackLabel));
                OnPropertyChanged(nameof(PlayPauseGlyph));
                OnPropertyChanged(nameof(AppDisplayName));
                OnPropertyChanged(nameof(IsSpotify));
                OnPropertyChanged(nameof(PositionLabel));
                OnPropertyChanged(nameof(DurationLabel));
                OnPropertyChanged(nameof(SeekControlsVisibility));
                OnPropertyChanged(nameof(SeekPercent));
                if (artworkChanged)
                {
                    _ = UpdateArtworkAsync(value?.Artwork);
                }
            }
        }
    }

    private bool HasArtworkChanged(MediaTrack? newTrack)
    {
        var newKey = newTrack is null
            ? null
            : $"{newTrack.SourceApplicationId}|{newTrack.Title}|{newTrack.Artist}|{newTrack.Album}|{newTrack.Artwork?.Length ?? 0}";
        if (_lastArtworkKey == newKey)
        {
            return false;
        }
        _lastArtworkKey = newKey;
        return true;
    }

    public bool HasTrack => CurrentTrack is not null;
    public bool IsPlaying => CurrentTrack?.PlaybackStatus == PlaybackStatus.Playing;
    public string AppDisplayName
    {
        get
        {
            var id = CurrentTrack?.SourceApplicationId ?? string.Empty;
            if (id.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
            if (id.Contains("Chrome", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
            if (id.Contains("Edge", StringComparison.OrdinalIgnoreCase)) return "Microsoft Edge";
            if (id.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase)) return "Apple Music";
            if (id.Contains("YouTube", StringComparison.OrdinalIgnoreCase)) return "YouTube Music";
            return !string.IsNullOrWhiteSpace(id) ? id : "Pulse";
        }
    }
    public bool IsSpotify => CurrentTrack?.SourceApplicationId?.Contains("Spotify", StringComparison.OrdinalIgnoreCase) == true;
    public IslandState CurrentIslandState => _stateMachine.CurrentState;
    public bool IsIdle => CurrentIslandState == IslandState.Idle;
    public bool IsExpanded => CurrentIslandState == IslandState.Expanded;
    public bool IsCompact => CurrentIslandState is IslandState.Compact or IslandState.Hover or IslandState.TrackChanged or IslandState.Notification;
    public bool ShowSeekControls => _settingsService.Current.ShowSeekControls;
    public bool NotificationsPaused => _settingsService.Current.NotificationsPaused;
    public Microsoft.UI.Xaml.Visibility IdleVisibility => !HasTrack && CurrentIslandState is (IslandState.Idle or IslandState.Hover)
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility CompactVisibility => IsCompact ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility ExpandedVisibility => IsExpanded ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility SeekPanelVisibility => ShowSeekControls ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public bool AutoCollapse => _settingsService.Current.AutoCollapse;
    public int TopOffset => _settingsService.Current.TopOffset;
    public double IslandScale => _settingsService.Current.IslandScale / 100d;
    public string AnimationIntensity => _settingsService.Current.AnimationIntensity;
    public string Theme => _settingsService.Current.Theme;
    public bool CanTogglePlayback => CurrentTrack is { CanPlay: true } or { CanPause: true };
    public bool CanGoNext => CurrentTrack?.CanNext == true;
    public bool CanGoPrevious => CurrentTrack?.CanPrevious == true;
    public BitmapImage? ArtworkImage => _artworkImage;
    public bool IsScrubbing { get; private set; }
    public string PlaybackLabel => CurrentTrack?.PlaybackStatus == PlaybackStatus.Playing ? "Pause" : "Play";
    public string PlayPauseGlyph => CurrentTrack?.PlaybackStatus == PlaybackStatus.Playing ? "\uE769" : "\uE768";
    public string PositionLabel
    {
        get
        {
            if (IsScrubbing && _seekPreviewPercent is { } pct && CurrentTrack?.Duration is { } duration)
            {
                var previewPos = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * pct / 100);
                return FormatTime(previewPos);
            }
            return FormatTime(CurrentTrack?.Position);
        }
    }
    public string DurationLabel => FormatTime(CurrentTrack?.Duration);
    public double SeekPercent
    {
        get => _seekPreviewPercent ?? (CurrentTrack?.Duration is { Ticks: > 0 } duration
            ? Math.Clamp(CurrentTrack.Position.TotalMilliseconds / duration.TotalMilliseconds * 100, 0, 100)
            : 0);
        set
        {
            var bounded = Math.Clamp(value, 0, 100);
            if (_seekPreviewPercent != bounded)
            {
                _seekPreviewPercent = bounded;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PositionLabel));
            }
        }
    }
    public string SeekStatus => _seekStatus ?? "WINDOWS MEDIA OTURUMU";
    public Microsoft.UI.Xaml.Visibility SeekControlsVisibility => CurrentTrack?.CanSeek == true
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;

    public AsyncCommand PreviousCommand { get; }
    public AsyncCommand PlayPauseCommand { get; }
    public AsyncCommand NextCommand { get; }
    public AsyncCommand SeekBackwardCommand { get; }
    public AsyncCommand SeekForwardCommand { get; }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        try
        {
            await _mediaSessionService.InitializeAsync();
            CurrentTrack = _mediaSessionService.CurrentTrack;
            _stateMachine.SetHasMedia(CurrentTrack is not null);
        }
        catch (Exception)
        {
            CurrentTrack = null;
        }
    }

    public Task<bool> SeekToPercentAsync(double percent)
    {
        if (CurrentTrack?.Duration is not { } duration)
        {
            return Task.FromResult(false);
        }

        var boundedPercent = Math.Clamp(percent, 0, 100);
        var position = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * boundedPercent / 100);
        if (CurrentTrack is not null)
        {
            _currentTrack = CurrentTrack with { Position = position };
            OnPropertyChanged(nameof(PositionLabel));
            OnPropertyChanged(nameof(SeekPercent));
        }
        return ExecuteSeekAsync(() => _playbackController.SeekAsync(position));
    }

    public void BeginScrub(double percent)
    {
        IsScrubbing = true;
        _seekPreviewPercent = Math.Clamp(percent, 0, 100);
        OnPropertyChanged(nameof(IsScrubbing));
        OnPropertyChanged(nameof(SeekPercent));
        OnPropertyChanged(nameof(PositionLabel));
    }

    public void UpdateScrub(double percent)
    {
        if (!IsScrubbing)
        {
            return;
        }

        _seekPreviewPercent = Math.Clamp(percent, 0, 100);
        OnPropertyChanged(nameof(SeekPercent));
        OnPropertyChanged(nameof(PositionLabel));
    }

    public async Task<bool> EndScrubAsync(double percent)
    {
        var bounded = Math.Clamp(percent, 0, 100);
        _seekPreviewPercent = bounded;
        bool result = false;
        try
        {
            if (CurrentTrack?.Duration is { } duration)
            {
                var targetPosition = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * bounded / 100);
                if (CurrentTrack is not null)
                {
                    _currentTrack = CurrentTrack with { Position = targetPosition };
                }
                result = await ExecuteSeekAsync(() => _playbackController.SeekAsync(targetPosition));
            }
        }
        catch (Exception)
        {
            result = false;
        }
        finally
        {
            _seekPreviewPercent = null;
            IsScrubbing = false;
            _lastTickTime = DateTimeOffset.UtcNow;
            OnPropertyChanged(nameof(IsScrubbing));
            OnPropertyChanged(nameof(SeekPercent));
            OnPropertyChanged(nameof(PositionLabel));
        }
        return result;
    }

    public void OnPointerEntered()
    {
        _stateTimer.Stop();
        _stateMachine.SetHovered(true);
    }

    public void OnPointerExited()
    {
        _stateMachine.SetHovered(false);
        if (IsExpanded && AutoCollapse)
        {
            _stateTimer.Interval = TimeSpan.FromSeconds(4.2);
            _stateTimer.Start();
        }
    }

    public void ToggleExpanded()
    {
        _stateMachine.ToggleExpanded();
        if (IsExpanded && AutoCollapse)
        {
            _stateTimer.Interval = TimeSpan.FromSeconds(5.5);
            _stateTimer.Start();
        }
        else
        {
            _stateTimer.Stop();
        }
    }

    public void Collapse()
    {
        if (IsExpanded)
        {
            _stateMachine.ToggleExpanded();
        }
    }

    public void CompleteCollapse() => _stateMachine.CompleteCollapse();

    public void ShowNotificationState()
    {
        _stateMachine.ShowNotification();
        _stateTimer.Interval = TimeSpan.FromSeconds(2.4);
        _stateTimer.Start();
    }

    public ValueTask DisposeAsync()
    {
        _mediaSessionService.CurrentTrackChanged -= OnCurrentTrackChanged;
        _settingsService.SettingsChanged -= OnSettingsChanged;
        _stateMachine.StateChanged -= OnIslandStateChanged;
        _stateTimer.Stop();
        _playbackTicker.Stop();
        return ValueTask.CompletedTask;
    }

    private void OnCurrentTrackChanged(object? sender, MediaTrack? track)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            ApplyTrackUpdate(track);
            return;
        }

        _dispatcherQueue.TryEnqueue(() => ApplyTrackUpdate(track));
    }

    private void ApplyTrackUpdate(MediaTrack? track)
    {
        var oldTrack = CurrentTrack;
        var changedTrack = track is not null && (oldTrack is null || track.Title != oldTrack.Title || track.Artist != oldTrack.Artist || track.SourceApplicationId != oldTrack.SourceApplicationId);
        CurrentTrack = track;
        _lastTickTime = DateTimeOffset.UtcNow;
        if (track?.PlaybackStatus == PlaybackStatus.Playing)
        {
            _playbackTicker.Start();
        }
        else
        {
            _playbackTicker.Stop();
        }

        _stateMachine.SetHasMedia(track is not null);
        if (changedTrack && !NotificationsPaused)
        {
            _stateMachine.ShowTrackChanged();
            _stateTimer.Interval = TimeSpan.FromSeconds(2.4);
            _stateTimer.Start();
        }
    }

    private void OnPlaybackTickerTick(DispatcherQueueTimer sender, object args)
    {
        if (IsScrubbing || CurrentTrack is not { PlaybackStatus: PlaybackStatus.Playing, Duration: { Ticks: > 0 } duration } track)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var elapsed = now - _lastTickTime;
        _lastTickTime = now;

        if (elapsed <= TimeSpan.Zero || elapsed > TimeSpan.FromSeconds(5))
        {
            return;
        }

        var newPos = track.Position + elapsed;
        if (newPos > duration)
        {
            newPos = duration;
        }

        _currentTrack = track with { Position = newPos };
        OnPropertyChanged(nameof(PositionLabel));
        OnPropertyChanged(nameof(SeekPercent));
    }

    private void OnStateTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (CurrentIslandState == IslandState.Expanded)
        {
            _stateMachine.ToggleExpanded();
        }
        else
        {
            _stateMachine.ReturnToCompact(HasTrack);
        }
    }

    private async Task<bool> ExecuteSeekAsync(Func<Task<bool>> execute)
    {
        var succeeded = await execute();
        _seekPreviewPercent = null;
        OnPropertyChanged(nameof(SeekPercent));
        _seekStatus = succeeded ? "KONUM GÜNCELLENDİ" : "BU İÇERİK SARMA İŞLEMİNİ DESTEKLEMİYOR";
        OnPropertyChanged(nameof(SeekStatus));
        return succeeded;
    }

    private void OnIslandStateChanged(object? sender, IslandState state)
    {
        OnPropertyChanged(nameof(CurrentIslandState));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(IsCompact));
        OnPropertyChanged(nameof(IdleVisibility));
        OnPropertyChanged(nameof(CompactVisibility));
        OnPropertyChanged(nameof(ExpandedVisibility));
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        OnPropertyChanged(nameof(ShowSeekControls));
        OnPropertyChanged(nameof(NotificationsPaused));
        OnPropertyChanged(nameof(SeekPanelVisibility));
        OnPropertyChanged(nameof(AutoCollapse));
        OnPropertyChanged(nameof(TopOffset));
        OnPropertyChanged(nameof(IslandScale));
        OnPropertyChanged(nameof(AnimationIntensity));
        OnPropertyChanged(nameof(Theme));
        if (!settings.AutoCollapse)
        {
            _stateTimer.Stop();
        }
    }

    private async Task UpdateArtworkAsync(byte[]? artwork)
    {
        if (artwork is null || artwork.Length == 0)
        {
            _artworkImage = null;
            OnPropertyChanged(nameof(ArtworkImage));
            return;
        }

        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(artwork);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            _artworkImage = image;
            OnPropertyChanged(nameof(ArtworkImage));
        }
        catch (Exception)
        {
            _artworkImage = null;
            OnPropertyChanged(nameof(ArtworkImage));
        }
    }

    private static string FormatTime(TimeSpan? value)
    {
        if (value is null || value < TimeSpan.Zero)
        {
            return "--:--";
        }

        return value.Value.TotalHours >= 1 ? value.Value.ToString(@"h\:mm\:ss") : value.Value.ToString(@"m\:ss");
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
