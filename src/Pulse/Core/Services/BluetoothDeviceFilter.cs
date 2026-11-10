namespace Pulse.Core.Services;

/// <summary>
/// Filters Windows PnP Bluetooth endpoint noise names while keeping user-facing device names intact.
/// </summary>
public static class BluetoothDeviceFilter
{
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
}
