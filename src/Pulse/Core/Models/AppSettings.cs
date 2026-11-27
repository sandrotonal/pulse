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
    public bool AutoAddScreenshotsToShelf { get; init; } = true;
    public bool FocusAutoDoNotDisturb { get; init; }
    public bool DoNotDisturb { get; init; }

    // Opt-in Developer Workspace (Git & GitHub Live Activity + 5th Panel)
    public bool EnableDeveloperWorkspace { get; init; }
    public List<string> DeveloperLocalRepositories { get; init; } = [];
    public List<string> DeveloperGitHubRepositories { get; init; } = [];
    public int DeveloperSelectedRepoIndex { get; init; }
    /// <summary>GitHub Actions/PR polling interval in seconds (30-600).</summary>
    public int DeveloperPollIntervalSeconds { get; init; } = 60;
    public bool NotifyOnGitCommits { get; init; } = true;
    public bool NotifyOnGitHubActions { get; init; } = true;
    public bool NotifyOnPullRequests { get; init; } = true;
    public bool ShowDeveloperLiveActivity { get; init; } = true;

    public bool Equals(AppSettings? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;

        return EqualityComparer<Type>.Default.Equals(EqualityContract, other.EqualityContract)
            && StartWithWindows == other.StartWithWindows
            && AutoCollapse == other.AutoCollapse
            && ShowSeekControls == other.ShowSeekControls
            && NotificationsPaused == other.NotificationsPaused
            && AudioNotifications == other.AudioNotifications
            && PowerNotifications == other.PowerNotifications
            && DownloadNotifications == other.DownloadNotifications
            && SystemToastNotifications == other.SystemToastNotifications
            && BluetoothNotifications == other.BluetoothNotifications
            && FocusCompletionSound == other.FocusCompletionSound
            && EnableHoverExpand == other.EnableHoverExpand
            && HoverExpandDelayMs == other.HoverExpandDelayMs
            && AutoHideOnFullscreen == other.AutoHideOnFullscreen
            && DynamicColorTint == other.DynamicColorTint
            && ShowEqualizer == other.ShowEqualizer
            && UseTransparency == other.UseTransparency
            && TransparencyLevel == other.TransparencyLevel
            && ArtworkTintStrength == other.ArtworkTintStrength
            && MotionSpeed == other.MotionSpeed
            && string.Equals(Theme, other.Theme, StringComparison.Ordinal)
            && string.Equals(AnimationIntensity, other.AnimationIntensity, StringComparison.Ordinal)
            && TopOffset == other.TopOffset
            && IslandScale == other.IslandScale
            && ShowIdleClock == other.ShowIdleClock
            && ShowIdleDate == other.ShowIdleDate
            && ShowIdleWeather == other.ShowIdleWeather
            && string.Equals(IdleLocationSource, other.IdleLocationSource, StringComparison.Ordinal)
            && string.Equals(IdleLocationCity, other.IdleLocationCity, StringComparison.Ordinal)
            && Nullable.Equals(IdleLocationLatitude, other.IdleLocationLatitude)
            && Nullable.Equals(IdleLocationLongitude, other.IdleLocationLongitude)
            && string.Equals(PreferredMonitorDeviceName, other.PreferredMonitorDeviceName, StringComparison.Ordinal)
            && HorizontalOffset == other.HorizontalOffset
            && AutoCollapseDelayMs == other.AutoCollapseDelayMs
            && string.Equals(CompactClickAction, other.CompactClickAction, StringComparison.Ordinal)
            && string.Equals(HoverAction, other.HoverAction, StringComparison.Ordinal)
            && Use24HourClock == other.Use24HourClock
            && string.Equals(TemperatureUnit, other.TemperatureUnit, StringComparison.Ordinal)
            && ShowLyrics == other.ShowLyrics
            && FocusDefaultMinutes == other.FocusDefaultMinutes
            && FocusBreakMinutes == other.FocusBreakMinutes
            && FocusCustomMinutes == other.FocusCustomMinutes
            && EnableGlobalHotkeys == other.EnableGlobalHotkeys
            && ScreenshotNotifications == other.ScreenshotNotifications
            && AutoAddScreenshotsToShelf == other.AutoAddScreenshotsToShelf
            && FocusAutoDoNotDisturb == other.FocusAutoDoNotDisturb
            && DoNotDisturb == other.DoNotDisturb
            && EnableDeveloperWorkspace == other.EnableDeveloperWorkspace
            && DeveloperLocalRepositories.SequenceEqual(other.DeveloperLocalRepositories, StringComparer.OrdinalIgnoreCase)
            && DeveloperGitHubRepositories.SequenceEqual(other.DeveloperGitHubRepositories, StringComparer.OrdinalIgnoreCase)
            && DeveloperSelectedRepoIndex == other.DeveloperSelectedRepoIndex
            && DeveloperPollIntervalSeconds == other.DeveloperPollIntervalSeconds
            && NotifyOnGitCommits == other.NotifyOnGitCommits
            && NotifyOnGitHubActions == other.NotifyOnGitHubActions
            && NotifyOnPullRequests == other.NotifyOnPullRequests
            && ShowDeveloperLiveActivity == other.ShowDeveloperLiveActivity;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(StartWithWindows);
        hash.Add(Theme, StringComparer.Ordinal);
        hash.Add(TopOffset);
        hash.Add(IslandScale);
        hash.Add(EnableDeveloperWorkspace);
        hash.Add(DeveloperLocalRepositories.Count);
        hash.Add(DeveloperGitHubRepositories.Count);
        hash.Add(DeveloperPollIntervalSeconds);
        return hash.ToHashCode();
    }
}
