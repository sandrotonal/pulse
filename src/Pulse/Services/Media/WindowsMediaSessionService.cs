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
    private bool _disposed;

    public WindowsMediaSessionService(IArtworkService artworkService) => _artworkService = artworkService;

    public MediaTrack? CurrentTrack { get; private set; }

    public event EventHandler<MediaTrack?>? CurrentTrackChanged;

    public async Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        _manager.SessionsChanged += OnSessionsChanged;
        await BindCurrentSessionAsync();
    }

    public Task<bool> PlayAsync() => RunSessionCommandAsync(session => session.TryPlayAsync());

    public Task<bool> PauseAsync() => RunSessionCommandAsync(session => session.TryPauseAsync());

    public Task<bool> NextAsync() => RunSessionCommandAsync(session => session.TrySkipNextAsync());

    public Task<bool> PreviousAsync() => RunSessionCommandAsync(session => session.TrySkipPreviousAsync());

    public async Task<bool> TogglePlayPauseAsync()
    {
        if (_session is null)
        {
            return false;
        }

        var playback = _session.GetPlaybackInfo();
        return playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
            ? await PauseAsync()
            : await PlayAsync();
    }

    public async Task<bool> SeekAsync(TimeSpan position)
    {
        if (_session is null || CurrentTrack is not { Duration: { Ticks: > 0 } duration })
        {
            return false;
        }

        var boundedPosition = PlaybackPosition.Clamp(position, duration);
        try
        {
            return await _session.TryChangePlaybackPositionAsync(boundedPosition.Ticks);
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
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager.SessionsChanged -= OnSessionsChanged;
        }

        DetachSessionEvents();
        _manager = null;
        SetCurrentTrack(null);
        return ValueTask.CompletedTask;
    }

    private static TimeSpan Positive(TimeSpan amount) => amount < TimeSpan.Zero ? TimeSpan.Zero : amount;

    private async Task<bool> RunSessionCommandAsync(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> command)
    {
        if (_session is null)
        {
            return false;
        }

        try
        {
            return await command(_session);
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
        if (!ReferenceEquals(sender, _session) || CurrentTrack is not { } track)
        {
            return;
        }

        try
        {
            SetCurrentTrack(track with { Position = sender.GetTimelineProperties().Position });
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

        DetachSessionEvents();
        try
        {
            _session = _manager.GetCurrentSession();
            if (_session is null)
            {
                SetCurrentTrack(null);
                return;
            }

            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
            await RefreshCurrentTrackAsync();
        }
        catch (Exception)
        {
            SetCurrentTrack(null);
        }
    }

    private void DetachSessionEvents()
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }

        _session = null;
    }

    private async Task RefreshCurrentTrackAsync()
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
                Artwork = await _artworkService.GetArtworkAsync(
                    media.Thumbnail,
                    string.Join("|", session.SourceAppUserModelId, media.Title, media.Artist, media.AlbumTitle))
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
