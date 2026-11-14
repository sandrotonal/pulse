namespace Pulse.Core.Services;

public static class AudioDeviceResolver
{
    public static string ResolveGlyph(string? deviceName, uint formFactor = 0)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return "\uE7F5"; // Default Speaker
        }

        var lower = deviceName.ToLowerInvariant();
        if (formFactor is 3 or 5 or 6 ||
            lower.Contains("headphone") ||
            lower.Contains("kulaklık") ||
            lower.Contains("headset") ||
            lower.Contains("airpods") ||
            lower.Contains("buds") ||
            lower.Contains("earphone"))
        {
            return "\uE7F6"; // Headphones
        }

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
        if (parenIdx > 0 && name.Length > 16)
        {
            var clean = name.Substring(0, parenIdx).Trim();
            if (!string.IsNullOrEmpty(clean))
            {
                return clean;
            }
        }

        return name.Length > 18 ? $"{name.Substring(0, 16)}…" : name;
    }
}
