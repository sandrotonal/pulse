using System.Text;

namespace Pulse.Core.Services;

public sealed record SingleInstanceNames(
    string MutexName,
    string ShowIslandEventName,
    string ShowSettingsEventName,
    string ExpandMediaEventName = SingleInstanceNameResolver.DefaultExpandMediaEventName,
    string ExpandFocusEventName = SingleInstanceNameResolver.DefaultExpandFocusEventName,
    string ExpandSystemEventName = SingleInstanceNameResolver.DefaultExpandSystemEventName,
    string CollapseEventName = SingleInstanceNameResolver.DefaultCollapseEventName,
    string ToggleSplitDemoEventName = SingleInstanceNameResolver.DefaultToggleSplitDemoEventName);

/// <summary>
/// Resolves kernel object names for Pulse's single-instance guard.
/// On the standard interactive desktop ("Default") we preserve the canonical names;
/// on isolated test/sandbox desktops we namespace by desktop name so background
/// harness processes cannot hijack the user's real desktop instance.
/// </summary>
public static class SingleInstanceNameResolver
{
    public const string DefaultMutexName = "Local\\PulseDynamicIslandInstance";
    public const string DefaultShowIslandEventName = "Local\\PulseShowIslandSignal";
    public const string DefaultShowSettingsEventName = "Local\\PulseShowSettingsSignal";
    public const string DefaultExpandMediaEventName = "Local\\PulseExpandMediaSignal";
    public const string DefaultExpandFocusEventName = "Local\\PulseExpandFocusSignal";
    public const string DefaultExpandSystemEventName = "Local\\PulseExpandSystemSignal";
    public const string DefaultCollapseEventName = "Local\\PulseCollapseSignal";
    public const string DefaultToggleSplitDemoEventName = "Local\\PulseToggleSplitDemoSignal";

    public static SingleInstanceNames Resolve(string? desktopName)
    {
        if (string.IsNullOrWhiteSpace(desktopName))
        {
            return new SingleInstanceNames(
                DefaultMutexName,
                DefaultShowIslandEventName,
                DefaultShowSettingsEventName,
                DefaultExpandMediaEventName,
                DefaultExpandFocusEventName,
                DefaultExpandSystemEventName,
                DefaultCollapseEventName,
                DefaultToggleSplitDemoEventName);
        }

        var trimmed = desktopName.Trim();
        if (string.Equals(trimmed, "Default", StringComparison.OrdinalIgnoreCase))
        {
            return new SingleInstanceNames(
                DefaultMutexName,
                DefaultShowIslandEventName,
                DefaultShowSettingsEventName,
                DefaultExpandMediaEventName,
                DefaultExpandFocusEventName,
                DefaultExpandSystemEventName,
                DefaultCollapseEventName,
                DefaultToggleSplitDemoEventName);
        }

        var suffix = SanitizeDesktopSuffix(trimmed);
        return new SingleInstanceNames(
            $"{DefaultMutexName}_{suffix}",
            $"{DefaultShowIslandEventName}_{suffix}",
            $"{DefaultShowSettingsEventName}_{suffix}",
            $"{DefaultExpandMediaEventName}_{suffix}",
            $"{DefaultExpandFocusEventName}_{suffix}",
            $"{DefaultExpandSystemEventName}_{suffix}",
            $"{DefaultCollapseEventName}_{suffix}",
            $"{DefaultToggleSplitDemoEventName}_{suffix}");
    }

    internal static string SanitizeDesktopSuffix(string rawDesktopName)
    {
        var sb = new StringBuilder(Math.Min(rawDesktopName.Length, 48));
        var lastWasUnderscore = false;

        foreach (var ch in rawDesktopName)
        {
            if (sb.Length >= 48)
            {
                break;
            }

            if ((ch is >= 'A' and <= 'Z') ||
                (ch is >= 'a' and <= 'z') ||
                (ch is >= '0' and <= '9') ||
                ch == '-')
            {
                sb.Append(ch);
                lastWasUnderscore = false;
            }
            else if (!lastWasUnderscore && sb.Length > 0)
            {
                sb.Append('_');
                lastWasUnderscore = true;
            }
        }

        var result = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(result) ? "isolated" : result;
    }
}

