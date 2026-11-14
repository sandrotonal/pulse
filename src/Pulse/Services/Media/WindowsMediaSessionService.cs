using System.IO;
using System.Text;
using Pulse.Core.Interfaces;
using Pulse.Core.Models;
using Pulse.Core.Services;
using Windows.Media.Control;

namespace Pulse.Services.Media;

public sealed class WindowsMediaSessionService : IMediaSessionService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private readonly IArtworkService _artworkService;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private string? _cachedArtworkKey;
    private byte[]? _cachedArtworkBytes;
    private volatile bool _disposed;

    public WindowsMediaSessionService(IArtworkService artworkService) => _artworkService = artworkService;

    public MediaTrack? CurrentTrack { get; private set; }

    public event EventHandler<MediaTrack?>? CurrentTrackChanged;

    public async Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        if (_disposed)
        {
            _manager = null;
            return;
        }

        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        _manager.SessionsChanged += OnSessionsChanged;
        await BindCurrentSessionAsync();
    }

    public async Task RefreshAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_manager is null)
        {
            try
            {
                await InitializeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                CrashLogger.Write("WindowsMediaSessionService.RefreshAsync", ex);
            }

            return;
        }

        await BindCurrentSessionAsync().ConfigureAwait(false);
    }

    public Task<bool> PlayAsync() => RunSessionCommandAsync(session => session.TryPlayAsync());

    public Task<bool> PauseAsync() => RunSessionCommandAsync(session => session.TryPauseAsync());

    public Task<bool> NextAsync() => RunSessionCommandAsync(session => session.TrySkipNextAsync());

    public Task<bool> PreviousAsync() => RunSessionCommandAsync(session => session.TrySkipPreviousAsync());

    public async Task<bool> TogglePlayPauseAsync()
    {
        var session = _session;
        if (_disposed || session is null)
        {
            return false;
        }

        GlobalSystemMediaTransportControlsSessionPlaybackInfo? playback;
        try
        {
            playback = session.GetPlaybackInfo();
        }
        catch (Exception)
        {
            await BindCurrentSessionAsync();
            return false;
        }

        return playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
            ? await PauseAsync()
            : await PlayAsync();
    }

    public async Task<bool> SeekAsync(TimeSpan position)
    {
        var session = _session;
        if (_disposed || session is null || CurrentTrack is not { Duration: { Ticks: > 0 } duration })
        {
            return false;
        }

        var boundedPosition = PlaybackPosition.Clamp(position, duration);
        try
        {
            return await session.TryChangePlaybackPositionAsync(boundedPosition.Ticks);
        }
        catch (Exception)
        {
            await BindCurrentSessionAsync();
            return false;
        }
    }

    public Task<bool> SeekForwardAsync(TimeSpan amount)
        => SeekAsync(PlaybackPosition.Offset(CurrentTrack?.Position ?? TimeSpan.Zero, Positive(amount), CurrentTrack?.Duration));

    public Task<bool> SeekBackwardAsync(TimeSpan amount)
        => SeekAsync(PlaybackPosition.Offset(CurrentTrack?.Position ?? TimeSpan.Zero, -Positive(amount), CurrentTrack?.Duration));

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        if (_manager is not null)
        {
            try
            {
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _manager.SessionsChanged -= OnSessionsChanged;
            }
            catch
            {
            }
        }

        DetachSessionEvents();
        _manager = null;
        _cachedArtworkKey = null;
        _cachedArtworkBytes = null;
        SetCurrentTrack(null);
        _sessionGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private static TimeSpan Positive(TimeSpan amount) => amount < TimeSpan.Zero ? TimeSpan.Zero : amount;

    private async Task<bool> RunSessionCommandAsync(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> command)
    {
        var session = _session;
        if (_disposed || session is null)
        {
            return false;
        }

        try
        {
            return await command(session);
        }
        catch (Exception)
        {
            await BindCurrentSessionAsync();
            return false;
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        => _ = BindCurrentSessionAsync();

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        => _ = BindCurrentSessionAsync();

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        => _ = RefreshCurrentTrackAsync();

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        => _ = RefreshCurrentTrackAsync();

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        if (_disposed || !ReferenceEquals(sender, _session) || CurrentTrack is not { } track)
        {
            return;
        }

        try
        {
            var position = sender.GetTimelineProperties().Position;
            if (_disposed || !ReferenceEquals(sender, _session))
            {
                return;
            }

            SetCurrentTrack(track with { Position = position });
        }
        catch (Exception)
        {
            _ = BindCurrentSessionAsync();
        }
    }

    private async Task BindCurrentSessionAsync()
    {
        if (_disposed || _manager is null)
        {
            return;
        }

        var gateEntered = false;
        try
        {
            await _sessionGate.WaitAsync().ConfigureAwait(false);
            gateEntered = true;
            if (_disposed || _manager is null)
            {
                return;
            }

            DetachSessionEvents();
            _session = _manager.GetCurrentSession();
            if (_session is null)
            {
                SetCurrentTrack(null);
                return;
            }

            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
            await RefreshCurrentTrackCoreAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception)
        {
            DetachSessionEvents();
            SetCurrentTrack(null);
        }
        finally
        {
            if (gateEntered)
            {
                try { _sessionGate.Release(); } catch (ObjectDisposedException) { }
            }
        }
    }

    private void DetachSessionEvents()
    {
        if (_session is not null)
        {
            try
            {
                _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            }
            catch
            {
            }
        }

        _session = null;
    }

    private async Task RefreshCurrentTrackAsync()
    {
        if (_disposed || _session is null)
        {
            return;
        }

        var gateEntered = false;
        try
        {
            await _sessionGate.WaitAsync().ConfigureAwait(false);
            gateEntered = true;
            await RefreshCurrentTrackCoreAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            if (gateEntered)
            {
                try { _sessionGate.Release(); } catch (ObjectDisposedException) { }
            }
        }
    }

    private async Task RefreshCurrentTrackCoreAsync()
    {
        var session = _session;
        if (_disposed || session is null)
        {
            return;
        }

        try
        {
            var media = await session.TryGetMediaPropertiesAsync();
            if (!ReferenceEquals(session, _session) || _disposed)
            {
                return;
            }

            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var status = playback?.PlaybackStatus switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackStatus.Playing,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackStatus.Paused,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => PlaybackStatus.Closed,
                _ => PlaybackStatus.Unknown
            };

            var artworkKey = string.Join("|", session.SourceAppUserModelId, media.Title, media.Artist, media.AlbumTitle);
            byte[]? artworkBytes;
            if (string.Equals(_cachedArtworkKey, artworkKey, StringComparison.Ordinal) && _cachedArtworkBytes is not null)
            {
                artworkBytes = _cachedArtworkBytes;
            }
            else
            {
                artworkBytes = await _artworkService.GetArtworkAsync(media.Thumbnail, artworkKey);
                if (artworkBytes is not null)
                {
                    _cachedArtworkKey = artworkKey;
                    _cachedArtworkBytes = artworkBytes;
                }
                else
                {
                    _cachedArtworkKey = null;
                    _cachedArtworkBytes = null;
                }
            }

            SetCurrentTrack(new MediaTrack
            {
                Title = string.IsNullOrWhiteSpace(media.Title) ? "Unknown title" : media.Title,
                Artist = string.IsNullOrWhiteSpace(media.Artist) ? "Unknown artist" : media.Artist,
                Album = media.AlbumTitle ?? string.Empty,
                SourceApplicationId = session.SourceAppUserModelId,
                PlaybackStatus = status,
                Position = timeline.Position,
                Duration = timeline.EndTime > TimeSpan.Zero ? timeline.EndTime : null,
                CanPlay = playback?.Controls.IsPlayEnabled ?? false,
                CanPause = playback?.Controls.IsPauseEnabled ?? false,
                CanNext = playback?.Controls.IsNextEnabled ?? false,
                CanPrevious = playback?.Controls.IsPreviousEnabled ?? false,
                // Some media providers report this capability incorrectly. A known timeline is
                // enough to let the user try; TryChangePlaybackPositionAsync is the final authority.
                CanSeek = timeline.EndTime > TimeSpan.Zero,
                Artwork = artworkBytes
            });
        }
        catch (Exception)
        {
            if (ReferenceEquals(session, _session))
            {
                SetCurrentTrack(null);
            }
        }
    }

    private void SetCurrentTrack(MediaTrack? track)
    {
        CurrentTrack = track;
        CurrentTrackChanged?.Invoke(this, track);
    }
}
