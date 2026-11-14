using System.Text;

namespace Pulse.Core.Services;

public sealed record SingleInstanceNames(
    string MutexName,
    string ShowIslandEventName,
    string ShowSettingsEventName);

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

    public static SingleInstanceNames Resolve(string? desktopName)
    {
        if (string.IsNullOrWhiteSpace(desktopName))
        {
            return new SingleInstanceNames(
                DefaultMutexName,
                DefaultShowIslandEventName,
                DefaultShowSettingsEventName);
        }

        var trimmed = desktopName.Trim();
        if (string.Equals(trimmed, "Default", StringComparison.OrdinalIgnoreCase))
        {
            return new SingleInstanceNames(
                DefaultMutexName,
                DefaultShowIslandEventName,
                DefaultShowSettingsEventName);
        }

        var suffix = SanitizeDesktopSuffix(trimmed);
        return new SingleInstanceNames(
            $"{DefaultMutexName}_{suffix}",
            $"{DefaultShowIslandEventName}_{suffix}",
            $"{DefaultShowSettingsEventName}_{suffix}");
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

