using Pulse.Core.Models;

namespace Pulse.Core.Interfaces;

public interface IMediaSessionService : IAsyncDisposable
{
    MediaTrack? CurrentTrack { get; }
    event EventHandler<MediaTrack?>? CurrentTrackChanged;

    Task InitializeAsync();
    Task<bool> PlayAsync();
    Task<bool> PauseAsync();
    Task<bool> TogglePlayPauseAsync();
    Task<bool> NextAsync();
    Task<bool> PreviousAsync();
    Task<bool> SeekAsync(TimeSpan position);
    Task<bool> SeekForwardAsync(TimeSpan amount);
    Task<bool> SeekBackwardAsync(TimeSpan amount);
}
