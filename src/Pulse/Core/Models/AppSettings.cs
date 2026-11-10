namespace Pulse.Core.Models;

public sealed record AppSettings
{
    public bool StartWithWindows { get; init; }
    public bool AutoCollapse { get; init; } = true;
    public bool ShowSeekControls { get; init; } = true;
    public bool NotificationsPaused { get; init; }
    public bool AudioNotifications { get; init; } = true;
    public bool PowerNotifications { get; init; } = true;
    public bool DownloadNotifications { get; init; } = true;
    public bool SystemToastNotifications { get; init; } = true;
    public bool BluetoothNotifications { get; init; } = true;
    /// <summary>Play a short system sound when a focus or break session ends.</summary>
    public bool FocusCompletionSound { get; init; } = true;
    public bool EnableHoverExpand { get; init; } = true;
    /// <summary>How long the pointer must rest on the island before hover expansion (0-1000 ms).</summary>
    public int HoverExpandDelayMs { get; init; } = 200;
    /// <summary>Hide the island automatically while a fullscreen app, game or video is active.</summary>
    public bool AutoHideOnFullscreen { get; init; } = true;
    public bool DynamicColorTint { get; init; } = true;
    public bool ShowEqualizer { get; init; } = true;
    public bool UseTransparency { get; init; }
    /// <summary>0 = fully opaque, 100 = maximum see-through glass.</summary>
    public int TransparencyLevel { get; init; } = 60;
    /// <summary>How strongly the album artwork accent tints the island surface (0-100).</summary>
    public int ArtworkTintStrength { get; init; } = 25;
    /// <summary>Animation speed percentage (70-140); 100 = the profile's native speed.</summary>
    public int MotionSpeed { get; init; } = 100;
    public string Theme { get; init; } = "System";
    public string AnimationIntensity { get; init; } = "Normal";
    public int TopOffset { get; init; } = 14;
    public int IslandScale { get; init; } = 100;

    // Idle smart strip
    public bool ShowIdleClock { get; init; } = true;
    public bool ShowIdleDate { get; init; } = true;
    public bool ShowIdleWeather { get; init; } = true;
    /// <summary>"Auto" uses an approximate IP lookup; "Manual" uses the stored city/coordinates.</summary>
    public string IdleLocationSource { get; init; } = "Auto";
    public string IdleLocationCity { get; init; } = string.Empty;
    public double? IdleLocationLatitude { get; init; }
    public double? IdleLocationLongitude { get; init; }

    // Paket 3: Display placement & interaction personalization
    /// <summary>"" = Primary monitor, "FollowCursor" = monitor under pointer, or explicit MONITORINFOEX device name.</summary>
    public string PreferredMonitorDeviceName { get; init; } = string.Empty;
    /// <summary>Horizontal pixel offset (-400 to 400 DIP) from monitor horizontal center.</summary>
    public int HorizontalOffset { get; init; }
    /// <summary>Delay in ms (300-3000) before the expanded island auto-collapses on pointer exit.</summary>
    public int AutoCollapseDelayMs { get; init; } = 600;
    /// <summary>Short-tap action in compact mode: "Expand", "ActivateApp", or "PlayPause".</summary>
    public string CompactClickAction { get; init; } = "Expand";
    /// <summary>Hover action in compact mode: "Preview" or "ExpandCard".</summary>
    public string HoverAction { get; init; } = "Preview";
    /// <summary>Scroll wheel behavior: "Smart", "Volume", or "PageSwitch".</summary>
    public string ScrollWheelAction { get; init; } = "Smart";
    public bool Use24HourClock { get; init; } = true;
    /// <summary>"C" for Celsius, "F" for Fahrenheit.</summary>
    public string TemperatureUnit { get; init; } = "C";
    public bool ShowLyrics { get; init; } = true;

    // Focus timer presets (minutes)
    public int FocusDefaultMinutes { get; init; } = 25;
    public int FocusBreakMinutes { get; init; } = 5;
    public int FocusCustomMinutes { get; init; } = 45;

    // Global hotkeys & notification silencing
    public bool EnableGlobalHotkeys { get; init; } = true;
    public bool ScreenshotNotifications { get; init; } = true;
    public bool DoNotDisturb { get; init; }
}
