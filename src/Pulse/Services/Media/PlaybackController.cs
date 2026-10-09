using Pulse.Core.Interfaces;

namespace Pulse.Services.Media;

public sealed class PlaybackController(IMediaSessionService mediaSessionService) : IPlaybackController
{
    public Task<bool> PlayAsync() => mediaSessionService.PlayAsync();
    public Task<bool> PauseAsync() => mediaSessionService.PauseAsync();
    public Task<bool> TogglePlayPauseAsync() => mediaSessionService.TogglePlayPauseAsync();
    public Task<bool> NextAsync() => mediaSessionService.NextAsync();
    public Task<bool> PreviousAsync() => mediaSessionService.PreviousAsync();
    public Task<bool> SeekAsync(TimeSpan position) => mediaSessionService.SeekAsync(position);
    public Task<bool> SeekForwardAsync(TimeSpan amount) => mediaSessionService.SeekForwardAsync(amount);
    public Task<bool> SeekBackwardAsync(TimeSpan amount) => mediaSessionService.SeekBackwardAsync(amount);
}
