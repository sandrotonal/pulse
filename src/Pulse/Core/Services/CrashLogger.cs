using System.Text;

namespace Pulse.Core.Services;

/// <summary>
/// Thread-safe, zero-throw diagnostic logger writing to %LOCALAPPDATA%\Pulse\crash.log.
/// </summary>
public static class CrashLogger
{
    private static readonly object SyncRoot = new();
    private const long MaxLogBytes = 1024 * 1024;
    private static readonly TimeSpan DuplicateBurstWindow = TimeSpan.FromSeconds(2);
    private static string? _lastKey;
    private static DateTimeOffset _lastWrittenUtc;

    public static string DefaultLogPath
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var baseDir = !string.IsNullOrWhiteSpace(localAppData) ? localAppData : Path.GetTempPath();
            return Path.Combine(baseDir, "Pulse", "crash.log");
        }
    }

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
                var now = DateTimeOffset.UtcNow;
                var key = $"{source}|{message}";
                if (string.Equals(_lastKey, key, StringComparison.Ordinal) &&
                    (now - _lastWrittenUtc) < DuplicateBurstWindow)
                {
                    return;
                }

                _lastKey = key;
                _lastWrittenUtc = now;

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

                var entry = $"[{now:O}] [{source}] {message}{Environment.NewLine}{Environment.NewLine}";
                using var stream = new FileStream(targetPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                writer.Write(entry);
            }
        }
        catch
        {
            // Logging must never throw into callers or crash handlers.
        }
    }
}

