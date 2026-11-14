namespace Pulse.Core.Services;

/// <summary>
/// Filters external screenshot files, tracks recently created files so delayed OneDrive/Snipping Tool
/// flushes via FileSystemWatcher.Changed still trigger safely, and deduplicates repeat events.
/// </summary>
public sealed class ScreenshotFilePolicy
{
    private static readonly TimeSpan RecentCreationWindow = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DedupeWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PruneAge = TimeSpan.FromSeconds(30);
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp"];

    private readonly object _syncRoot = new();
    private readonly Dictionary<string, DateTimeOffset> _recentObserved = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _reportedRecently = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsExternalScreenshotPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(name) ||
            name.StartsWith('.') ||
            name.StartsWith("Pulse_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var ext = Path.GetExtension(path);
        return ImageExtensions.Any(valid => string.Equals(valid, ext, StringComparison.OrdinalIgnoreCase));
    }

    public bool RecordObservedCreation(string path, DateTimeOffset nowUtc)
    {
        if (!IsExternalScreenshotPath(path))
        {
            return false;
        }

        lock (_syncRoot)
        {
            PruneOldEntries_NoLock(_recentObserved, nowUtc, PruneAge);
            _recentObserved[path] = nowUtc;
        }

        return true;
    }

    public bool ShouldInspectChange(string path, DateTimeOffset fileCreationTimeUtc, DateTimeOffset nowUtc)
    {
        if (!IsExternalScreenshotPath(path))
        {
            return false;
        }

        lock (_syncRoot)
        {
            if (_reportedRecently.TryGetValue(path, out var reportedAt) &&
                (nowUtc - reportedAt) <= RecentCreationWindow)
            {
                return false;
            }

            if (_recentObserved.TryGetValue(path, out var observedAt) &&
                (nowUtc - observedAt) <= RecentCreationWindow)
            {
                return true;
            }
        }

        return (nowUtc - fileCreationTimeUtc) <= RecentCreationWindow;
    }

    public bool TryMarkReported(string path, DateTimeOffset nowUtc)
    {
        if (!IsExternalScreenshotPath(path))
        {
            return false;
        }

        lock (_syncRoot)
        {
            PruneOldEntries_NoLock(_reportedRecently, nowUtc, PruneAge);
            if (_reportedRecently.TryGetValue(path, out var prevTime) &&
                (nowUtc - prevTime) < DedupeWindow)
            {
                return false;
            }

            _reportedRecently[path] = nowUtc;
            _recentObserved.Remove(path);
            return true;
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _recentObserved.Clear();
            _reportedRecently.Clear();
        }
    }

    private static void PruneOldEntries_NoLock(
        Dictionary<string, DateTimeOffset> map,
        DateTimeOffset nowUtc,
        TimeSpan maxAge)
    {
        if (map.Count <= 32)
        {
            return;
        }

        List<string>? staleKeys = null;
        foreach (var (key, stamp) in map)
        {
            if ((nowUtc - stamp) >= maxAge)
            {
                staleKeys ??= [];
                staleKeys.Add(key);
            }
        }

        if (staleKeys is not null)
        {
            foreach (var key in staleKeys)
            {
                map.Remove(key);
            }
        }
    }
}
