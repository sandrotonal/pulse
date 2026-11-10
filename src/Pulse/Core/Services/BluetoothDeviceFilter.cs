namespace Pulse.Core.Services;

/// <summary>
/// Filters Windows PnP Bluetooth endpoint noise names while keeping user-facing device names intact.
/// </summary>
public static class BluetoothDeviceFilter
{
    private static readonly string[] AudioProfileSuffixes =
    [
        " Hands-Free AG Audio",
        " Hands-Free",
        " Stereo",
        " Avrcp Transport"
    ];

    public static string NormalizeName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return string.Empty;
        }

        var trimmed = rawName.Trim();
        if (trimmed.StartsWith("Bluetooth ", StringComparison.OrdinalIgnoreCase) &&
            (trimmed.Contains("Device", StringComparison.OrdinalIgnoreCase) ||
             trimmed.Contains("Attribute Service", StringComparison.OrdinalIgnoreCase) ||
             trimmed.Contains("Generic", StringComparison.OrdinalIgnoreCase)))
        {
            return string.Empty;
        }

        return trimmed;
    }

    /// <summary>
    /// Extracts the underlying device name from a Windows audio render endpoint display name,
    /// e.g. "Kulaklıklar (AirPods)" or "Headset (JBL TUNE500BT Hands-Free AG Audio)".
    /// </summary>
    public static string TryExtractAudioEndpointDeviceName(string? endpointName)
    {
        if (string.IsNullOrWhiteSpace(endpointName))
        {
            return string.Empty;
        }

        var trimmed = endpointName.Trim();
        var closeIndex = trimmed.LastIndexOf(')');
        if (closeIndex <= 0)
        {
            return string.Empty;
        }

        var openIndex = FindMatchingOpenParenthesis(trimmed, closeIndex);
        if (openIndex < 0 || openIndex >= closeIndex - 1)
        {
            return string.Empty;
        }

        var inner = trimmed[(openIndex + 1)..closeIndex].Trim();
        inner = StripNumericEndpointPrefix(inner);
        inner = StripAudioProfileSuffixes(inner);
        return NormalizeName(inner);
    }

    public static bool MatchesKnownDevice(string? audioCandidate, string knownDeviceName)
    {
        if (string.IsNullOrWhiteSpace(audioCandidate) || string.IsNullOrWhiteSpace(knownDeviceName))
        {
            return false;
        }

        return string.Equals(audioCandidate, knownDeviceName, StringComparison.OrdinalIgnoreCase) ||
               audioCandidate.StartsWith(knownDeviceName + " ", StringComparison.OrdinalIgnoreCase);
    }

    private static int FindMatchingOpenParenthesis(string text, int closeIndex)
    {
        var depth = 0;
        for (var i = closeIndex; i >= 0; i--)
        {
            if (text[i] == ')')
            {
                depth++;
            }
            else if (text[i] == '(')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static string StripNumericEndpointPrefix(string value)
    {
        var dashIndex = value.IndexOf("- ", StringComparison.Ordinal);
        if (dashIndex <= 0)
        {
            return value;
        }

        for (var i = 0; i < dashIndex; i++)
        {
            if (!char.IsDigit(value[i]))
            {
                return value;
            }
        }

        return value[(dashIndex + 2)..].Trim();
    }

    private static string StripAudioProfileSuffixes(string value)
    {
        var current = value.Trim();
        bool stripped;
        do
        {
            stripped = false;
            foreach (var suffix in AudioProfileSuffixes)
            {
                if (current.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    current = current[..^suffix.Length].Trim();
                    stripped = true;
                    break;
                }
            }
        } while (stripped && current.Length > 0);

        return current;
    }
}
