namespace Pulse.Core.Interfaces;

public interface IPlaybackController
{
    Task<bool> PlayAsync();
    Task<bool> PauseAsync();
    Task<bool> TogglePlayPauseAsync();
    Task<bool> NextAsync();
    Task<bool> PreviousAsync();
    Task<bool> SeekAsync(TimeSpan position);
    Task<bool> SeekForwardAsync(TimeSpan amount);
    Task<bool> SeekBackwardAsync(TimeSpan amount);
}
