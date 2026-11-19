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
               lower.Contains("head-set") ||
               lower.Contains("airpod") ||
               lower.Contains("buds") ||
               lower.Contains("earbud") ||
               lower.Contains("earphone") ||
               lower.Contains("freebuds") ||
               lower.Contains("in-ear") ||
               lower.Contains("inear") ||
               lower.Contains("beats") ||
               lower.Contains("bose") ||
               lower.Contains("jabra") ||
               lower.Contains("sennheiser") ||
               lower.Contains("soundcore") ||
               lower.Contains("hyperx") ||
               lower.Contains("steelseries") ||
               lower.Contains("tws") ||
               lower.Contains("hands-free") ||
               lower.Contains("handsfree") ||
               lower.Contains("wh-") ||
               lower.Contains("wf-");
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
