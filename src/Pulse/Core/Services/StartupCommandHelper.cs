namespace Pulse.Core.Services;

/// <summary>
/// Pure helper for building, parsing, and reconciling unpackaged Windows Registry
/// Run commands and Task Manager StartupApproved status flags.
/// </summary>
public static class StartupCommandHelper
{
    public static string BuildRunCommand(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Executable path cannot be empty.", nameof(executablePath));
        }

        return $"\"{Path.GetFullPath(executablePath.Trim().Trim('"'))}\"";
    }

    public static string ExtractExecutablePath(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return string.Empty;
        }

        var trimmed = commandLine.Trim();
        if (trimmed.StartsWith('"'))
        {
            var endQuote = trimmed.IndexOf('"', 1);
            if (endQuote > 1)
            {
                return trimmed[1..endQuote].Trim();
            }
        }

        var exeIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex >= 0)
        {
            return trimmed[..(exeIndex + 4)].Trim();
        }

        var spaceIndex = trimmed.IndexOf(' ');
        return (spaceIndex > 0 ? trimmed[..spaceIndex] : trimmed).Trim();
    }

    public static bool IsMatchingCommand(string? existingCommand, string executablePath)
    {
        var extracted = ExtractExecutablePath(existingCommand);
        if (string.IsNullOrWhiteSpace(extracted) || string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(extracted),
                Path.GetFullPath(executablePath.Trim().Trim('"')),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(extracted, executablePath.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool IsStartupApprovedEnabled(byte[]? approvalBytes)
    {
        if (approvalBytes is null || approvalBytes.Length == 0)
        {
            return true;
        }

        // In HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run,
        // even low bit values (0x02, 0x06) indicate enabled; odd values (0x01, 0x03, 0x07) indicate disabled.
        return (approvalBytes[0] & 0x01) == 0;
    }

    public static byte[] CreateEnabledApprovalValue() =>
        [0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
}

