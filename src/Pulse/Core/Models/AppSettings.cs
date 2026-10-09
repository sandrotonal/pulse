namespace Pulse.Core.Models;

public sealed record AppSettings
{
    public bool StartWithWindows { get; init; }
    public bool AutoCollapse { get; init; } = true;
    public bool ShowSeekControls { get; init; } = true;
    public bool NotificationsPaused { get; init; }
    public string Theme { get; init; } = "System";
    public string AnimationIntensity { get; init; } = "Normal";
    public int TopOffset { get; init; } = 18;
    public int IslandScale { get; init; } = 100;
}
