namespace Pulse.Core.Models;

public sealed record MediaTrack
{
    public string Title { get; init; } = "Unknown title";
    public string Artist { get; init; } = "Unknown artist";
    public string Album { get; init; } = string.Empty;
    public string SourceApplicationId { get; init; } = string.Empty;
    public PlaybackStatus PlaybackStatus { get; init; }
    public TimeSpan Position { get; init; }
    public TimeSpan? Duration { get; init; }
    public bool CanPlay { get; init; }
    public bool CanPause { get; init; }
    public bool CanNext { get; init; }
    public bool CanPrevious { get; init; }
    public bool CanSeek { get; init; }
    public byte[]? Artwork { get; init; }
}
