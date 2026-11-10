using System.Text;

namespace Pulse.Core.Services;

/// <summary>
/// Thread-safe, zero-throw diagnostic logger writing to %LOCALAPPDATA%\Pulse\crash.log.
/// </summary>
public static class CrashLogger
{
    private static readonly object SyncRoot = new();
    private const long MaxLogBytes = 1024 * 1024;

    public static string DefaultLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Pulse",
        "crash.log");

    public static void Write(string source, Exception exception, string? context = null, string? logPath = null)
    {
        var details = string.IsNullOrWhiteSpace(context)
            ? exception.ToString()
            : $"{context}{Environment.NewLine}{exception}";
        WriteMessage(source, details, logPath);
    }

    public static void WriteMessage(string source, string message, string? logPath = null)
    {
        var targetPath = string.IsNullOrWhiteSpace(logPath) ? DefaultLogPath : logPath;
        try
        {
            lock (SyncRoot)
            {
                var directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(targetPath) && new FileInfo(targetPath).Length > MaxLogBytes)
                {
                    var archivePath = targetPath + ".1";
                    File.Move(targetPath, archivePath, overwrite: true);
                }

                var entry = $"[{DateTimeOffset.UtcNow:O}] [{source}] {message}{Environment.NewLine}{Environment.NewLine}";
                File.AppendAllText(targetPath, entry, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never throw into callers or crash handlers.
        }
    }
}

