using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Pulse.Core.Events;
using Pulse.Core.Interfaces;
using Pulse.Core.Models;
using Pulse.Core.Services;
using Pulse.Services.Lyrics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Pulse.ViewModels;

public sealed class MediaViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IMediaSessionService _mediaSessionService;
    private readonly IPlaybackController _playbackController;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ISettingsService _settingsService;
    private readonly IEventRouter? _eventRouter;
    private readonly LyricsService _lyricsService = new();
    private readonly IslandStateMachine _stateMachine = new();
    private readonly DispatcherQueueTimer _stateTimer;
    private readonly DispatcherQueueTimer _playbackTicker;
    private DateTimeOffset _lastTickTime = DateTimeOffset.UtcNow;
    private MediaTrack? _currentTrack;
    private BitmapImage? _artworkImage;
    private uint _artworkAccentColor = 0xFF007AFFu;
    private LyricsResult? _lyrics;
    private string _currentLyricLine = "Sözler aranıyor";
    private string _nextLyricLine = "";
    private string? _lyricsLookupKey;
    private double? _seekPreviewPercent;
    private string? _seekStatus;
    private bool _isInitialized;

    public MediaViewModel(
        IMediaSessionService mediaSessionService,
        IPlaybackController playbackController,
        ISettingsService settingsService,
        DispatcherQueue dispatcherQueue,
        IEventRouter? eventRouter = null)
    {
        _mediaSessionService = mediaSessionService;
        _playbackController = playbackController;
        _settingsService = settingsService;
        _dispatcherQueue = dispatcherQueue;
        _eventRouter = eventRouter;
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
        ToggleShuffleCommand = new RelayCommand(() => IsShuffleEnabled = !IsShuffleEnabled);
        ToggleFavoriteCommand = new RelayCommand(() => IsFavorite = !IsFavorite);
        ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);
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
    public string AppBadgeText => AppDisplayName switch
    {
        "Spotify" => "S",
        "YouTube Music" => "YT",
        "Apple Music" => "",
        "Google Chrome" => "C",
        "Microsoft Edge" => "E",
        _ => "P"
    };
    public bool IsSpotify => CurrentTrack?.SourceApplicationId?.Contains("Spotify", StringComparison.OrdinalIgnoreCase) == true;
    public IslandState CurrentIslandState => _stateMachine.CurrentState;
    public bool IsIdle => CurrentIslandState == IslandState.Idle;
    public bool IsExpanded => CurrentIslandState == IslandState.Expanded;
    public bool IsCompact => CurrentIslandState.IsCompactPill();
    /// <summary>
    /// Collapsing renders the compact layout while the card shrinks, so the compact
    /// content must be laid out for the whole shrink — otherwise the pill is blank.
    /// </summary>
    public bool ShowsCompactContent => CurrentIslandState.ShowsCompactContent();
    public bool ShowSeekControls => _settingsService.Current.ShowSeekControls;
    public bool NotificationsPaused => _settingsService.Current.NotificationsPaused;
    public Microsoft.UI.Xaml.Visibility IdleVisibility => !HasTrack && CurrentIslandState is (IslandState.Idle or IslandState.Hover)
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility CompactVisibility => ShowsCompactContent ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility ExpandedVisibility => IsExpanded ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility SeekPanelVisibility => ShowSeekControls ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public bool AutoCollapse => _settingsService.Current.AutoCollapse;
    public bool EnableHoverExpand => _settingsService.Current.EnableHoverExpand;
    public int HoverExpandDelayMs => _settingsService.Current.HoverExpandDelayMs;
    public bool AutoHideOnFullscreen => _settingsService.Current.AutoHideOnFullscreen;
    public bool EnableScrollWheelVolume => _settingsService.Current.EnableScrollWheelVolume;
    public bool DynamicColorTint => _settingsService.Current.DynamicColorTint;
    public bool ShowEqualizer => _settingsService.Current.ShowEqualizer;
    public bool UseTransparency => _settingsService.Current.UseTransparency;
    public int TransparencyLevel => _settingsService.Current.TransparencyLevel;
    public int ArtworkTintStrength => _settingsService.Current.ArtworkTintStrength;
    public int MotionSpeed => _settingsService.Current.MotionSpeed;
    public int TopOffset => _settingsService.Current.TopOffset;
    public double IslandScale => _settingsService.Current.IslandScale / 100d;
    public string AnimationIntensity => _settingsService.Current.AnimationIntensity;
    public string Theme => _settingsService.Current.Theme;
    public bool CanTogglePlayback => CurrentTrack is { CanPlay: true } or { CanPause: true };
    public string? SourceAppUserModelId => CurrentTrack?.SourceApplicationId;
    public bool CanGoNext => CurrentTrack?.CanNext == true;
    public bool CanGoPrevious => CurrentTrack?.CanPrevious == true;
    public BitmapImage? ArtworkImage => _artworkImage;
    public uint ArtworkAccentColor => _artworkAccentColor;
    public bool HasLyrics => _lyrics?.HasLyrics == true;
    public string CurrentLyricLine => _currentLyricLine;
    public string NextLyricLine => _nextLyricLine;
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
    public RelayCommand ToggleShuffleCommand { get; }
    public RelayCommand ToggleFavoriteCommand { get; }
    public RelayCommand ToggleMuteCommand { get; }

    private bool _isShuffleEnabled;
    public bool IsShuffleEnabled
    {
        get => _isShuffleEnabled;
        set
        {
            if (SetProperty(ref _isShuffleEnabled, value))
            {
                OnPropertyChanged(nameof(ShuffleBrush));
            }
        }
    }

    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (SetProperty(ref _isFavorite, value))
            {
                OnPropertyChanged(nameof(FavoriteGlyph));
                OnPropertyChanged(nameof(FavoriteBrush));
            }
        }
    }

    public string FavoriteGlyph => IsFavorite ? "\uEB52" : "\uEB51";

    public Microsoft.UI.Xaml.Media.SolidColorBrush ShuffleBrush => new(
        IsShuffleEnabled ? Microsoft.UI.ColorHelper.FromArgb(255, 0, 122, 255) : Microsoft.UI.ColorHelper.FromArgb(255, 164, 171, 182));

    public Microsoft.UI.Xaml.Media.SolidColorBrush FavoriteBrush => new(
        IsFavorite ? Microsoft.UI.ColorHelper.FromArgb(255, 255, 59, 48) : Microsoft.UI.ColorHelper.FromArgb(255, 164, 171, 182));

    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                OnPropertyChanged(nameof(MuteGlyph));
            }
        }
    }

    public string MuteGlyph => IsMuted ? "\uE74F" : "\uE767";

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
        if (_stateMachine.CurrentState == IslandState.Collapsing)
        {
            _stateMachine.SetHovered(true);
            return;
        }
        if (EnableHoverExpand)
        {
            _stateMachine.SetHovered(true);
        }
    }

    public void OnPointerExited()
    {
        _stateMachine.SetHovered(false);
    }

    public void ToggleExpanded()
    {
        _stateMachine.ToggleExpanded();
        _stateTimer.Stop();
        if (_stateMachine.CurrentState == IslandState.Expanded)
        {
            _eventRouter?.LockUserInteraction();
        }
        else
        {
            _eventRouter?.UnlockUserInteraction();
        }
    }

    public void Collapse()
    {
        if (CurrentIslandState == IslandState.Expanded)
        {
            _stateMachine.ToggleExpanded();
            _eventRouter?.UnlockUserInteraction();
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
        OnPropertyChanged(nameof(SourceAppUserModelId));
        if (changedTrack)
        {
            _ = UpdateLyricsAsync(track);
        }
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
        if (track is not null)
        {
            _eventRouter?.Publish(new MediaEvent(
                "active_media",
                track,
                track.PlaybackStatus == PlaybackStatus.Playing,
                DateTimeOffset.UtcNow));
        }
        else
        {
            _eventRouter?.Dismiss("active_media");
        }

        if (changedTrack && !NotificationsPaused && _stateMachine.ShowTrackChanged())
        {
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
        UpdateActiveLyric(newPos);
        OnPropertyChanged(nameof(PositionLabel));
        OnPropertyChanged(nameof(SeekPercent));
    }

    private void OnStateTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        // The timer only retires transient announcements. It must never collapse an
        // island the user opened: expanded presentations stay open until dismissed.
        if (CurrentIslandState is IslandState.TrackChanged or IslandState.Notification)
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
        OnPropertyChanged(nameof(EnableHoverExpand));
        OnPropertyChanged(nameof(HoverExpandDelayMs));
        OnPropertyChanged(nameof(AutoHideOnFullscreen));
        OnPropertyChanged(nameof(EnableScrollWheelVolume));
        OnPropertyChanged(nameof(DynamicColorTint));
        OnPropertyChanged(nameof(ShowEqualizer));
        OnPropertyChanged(nameof(UseTransparency));
        OnPropertyChanged(nameof(TransparencyLevel));
        OnPropertyChanged(nameof(ArtworkTintStrength));
        OnPropertyChanged(nameof(MotionSpeed));
        OnPropertyChanged(nameof(TopOffset));
        OnPropertyChanged(nameof(IslandScale));
        OnPropertyChanged(nameof(AnimationIntensity));
        OnPropertyChanged(nameof(Theme));
        if (!settings.AutoCollapse)
        {
            _stateTimer.Stop();
        }
    }

    private async Task UpdateLyricsAsync(MediaTrack? track)
    {
        var lookupKey = track is null ? null : string.Join("|", track.Title, track.Artist, track.Album, track.Duration?.TotalSeconds.ToString("0"));
        _lyricsLookupKey = lookupKey;
        _lyrics = null;
        SetLyricLines("Sözler aranıyor", string.Empty);
        OnPropertyChanged(nameof(HasLyrics));

        if (track is null || string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist))
        {
            SetLyricLines("Bu parça için söz bilgisi yok", string.Empty);
            return;
        }

        var lyrics = await _lyricsService.FindLyricsAsync(track.Title, track.Artist, track.Album, track.Duration);
        if (_lyricsLookupKey != lookupKey)
        {
            return;
        }

        _lyrics = lyrics;
        OnPropertyChanged(nameof(HasLyrics));
        UpdateActiveLyric(track.Position);
    }

    private void UpdateActiveLyric(TimeSpan position)
    {
        if (_lyrics is null)
        {
            SetLyricLines("Bu parça için söz bilgisi yok", string.Empty);
            return;
        }

        if (_lyrics.Lines.Count > 0)
        {
            var index = 0;
            for (var i = 0; i < _lyrics.Lines.Count; i++)
            {
                if (_lyrics.Lines[i].Time <= position)
                {
                    index = i;
                }
                else
                {
                    break;
                }
            }

            SetLyricLines(_lyrics.Lines[index].Text, index + 1 < _lyrics.Lines.Count ? _lyrics.Lines[index + 1].Text : string.Empty);
            return;
        }

        var plainLines = _lyrics.PlainText?
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .ToArray();
        SetLyricLines(plainLines?.FirstOrDefault() ?? "Söz bulundu", plainLines is { Length: > 1 } ? plainLines[1] : string.Empty);
    }

    private void SetLyricLines(string current, string next)
    {
        if (_currentLyricLine != current)
        {
            _currentLyricLine = current;
            OnPropertyChanged(nameof(CurrentLyricLine));
        }

        if (_nextLyricLine != next)
        {
            _nextLyricLine = next;
            OnPropertyChanged(nameof(NextLyricLine));
        }
    }

    private async Task UpdateArtworkAsync(byte[]? artwork)
    {
        if (artwork is null || artwork.Length == 0)
        {
            _artworkImage = null;
            _artworkAccentColor = 0xFF007AFFu;
            OnPropertyChanged(nameof(ArtworkImage));
            OnPropertyChanged(nameof(ArtworkAccentColor));
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
            _artworkAccentColor = await ExtractArtworkAccentColorAsync(artwork);
            OnPropertyChanged(nameof(ArtworkImage));
            OnPropertyChanged(nameof(ArtworkAccentColor));
        }
        catch (Exception)
        {
            _artworkImage = null;
            _artworkAccentColor = 0xFF007AFFu;
            OnPropertyChanged(nameof(ArtworkImage));
            OnPropertyChanged(nameof(ArtworkAccentColor));
        }
    }

    private static async Task<uint> ExtractArtworkAccentColorAsync(byte[] artwork)
    {
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
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var transform = new BitmapTransform
            {
                ScaledWidth = Math.Min(48u, decoder.PixelWidth),
                ScaledHeight = Math.Min(48u, decoder.PixelHeight),
                InterpolationMode = BitmapInterpolationMode.Linear
            };
            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Rgba8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);
            var data = pixels.DetachPixelData();
            if (data.Length < 4)
            {
                return 0xFF007AFFu;
            }

            double totalWeight = 0;
            double r = 0;
            double g = 0;
            double b = 0;
            for (var i = 0; i + 3 < data.Length; i += 4)
            {
                var pr = data[i];
                var pg = data[i + 1];
                var pb = data[i + 2];
                var max = Math.Max(pr, Math.Max(pg, pb));
                var min = Math.Min(pr, Math.Min(pg, pb));
                var saturation = (max - min) / 255d;
                var brightness = max / 255d;
                if (brightness < 0.12 || brightness > 0.95)
                {
                    continue;
                }

                var weight = 0.35 + saturation + (brightness * 0.35);
                r += pr * weight;
                g += pg * weight;
                b += pb * weight;
                totalWeight += weight;
            }

            if (totalWeight <= 0)
            {
                return 0xFF007AFFu;
            }

            var rr = NormalizeAccentChannel(r / totalWeight);
            var gg = NormalizeAccentChannel(g / totalWeight);
            var bb = NormalizeAccentChannel(b / totalWeight);
            return 0xFF000000u | ((uint)rr << 16) | ((uint)gg << 8) | bb;
        }
        catch
        {
            return 0xFF007AFFu;
        }
    }

    private static byte NormalizeAccentChannel(double value)
        => (byte)Math.Clamp(Math.Round((value * 0.86) + 24), 32, 232);

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

    public void SimulateDemoTrack(string title = "Blinding Lights", string artist = "The Weeknd", string album = "After Hours")
    {
        var track = new MediaTrack
        {
            SourceApplicationId = "Spotify",
            Title = title,
            Artist = artist,
            Album = album,
            Position = TimeSpan.FromSeconds(74),
            Duration = TimeSpan.FromSeconds(200),
            PlaybackStatus = PlaybackStatus.Playing,
            CanPause = true,
            CanPlay = true,
            CanNext = true,
            CanPrevious = true,
            CanSeek = true,
            Artwork = null
        };
        ApplyTrackUpdate(track);
        _artworkAccentColor = 0xFFFF0055u;
        OnPropertyChanged(nameof(ArtworkAccentColor));
        OnPropertyChanged(nameof(PositionLabel));
        OnPropertyChanged(nameof(DurationLabel));
        OnPropertyChanged(nameof(SeekPercent));
        OnPropertyChanged(nameof(HasTrack));
        OnPropertyChanged(nameof(IsPlaying));
    }

    public void ClearDemoTrack()
    {
        ApplyTrackUpdate(null);
        OnPropertyChanged(nameof(HasTrack));
        OnPropertyChanged(nameof(IsPlaying));
    }

    public void ToggleDemoTrack()
    {
        if (HasTrack)
        {
            ClearDemoTrack();
        }
        else
        {
            SimulateDemoTrack();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
