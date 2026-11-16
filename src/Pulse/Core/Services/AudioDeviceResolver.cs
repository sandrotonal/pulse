namespace Pulse.Core.Services;

public static class AudioDeviceResolver
{
    public static bool IsHeadphones(string? deviceName, uint formFactor = 0)
    {
        // Windows Core Audio FormFactor enum:
        // 3 = Headphones, 5 = Headset, 6 = Handset
        if (formFactor is 3 or 5 or 6)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return false;
        }

        var lower = deviceName.ToLowerInvariant();
        return lower.Contains("headphone") ||
               lower.Contains("kulaklık") ||
               lower.Contains("kulaklik") ||
               lower.Contains("headset") ||
               lower.Contains("airpods") ||
               lower.Contains("buds") ||
               lower.Contains("earphone") ||
               lower.Contains("freebuds") ||
               lower.Contains("wh-") ||
               lower.Contains("wf-") ||
               lower.Contains("pixel buds");
    }

    public static string ResolveGlyph(string? deviceName, uint formFactor = 0)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return "\uE7F5"; // Default Speaker
        }

        if (IsHeadphones(deviceName, formFactor))
        {
            return "\uE7F6"; // Headphones
        }

        var lower = deviceName.ToLowerInvariant();
        if (lower.Contains("bluetooth") || lower.Contains("bt"))
        {
            return "\uE702"; // Bluetooth
        }

        return "\uE7F5"; // Speaker
    }

    public static string FormatShortName(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return "Hoparlör";
        }

        var name = deviceName.Trim();
        var parenIdx = name.IndexOf('(');
        if (parenIdx > 0)
        {
            var clean = name.Substring(0, parenIdx).Trim();
            if (!string.IsNullOrEmpty(clean))
            {
                name = clean;
            }
        }

        return name.Length > 18 ? $"{name.Substring(0, 16)}…" : name;
    }
}
