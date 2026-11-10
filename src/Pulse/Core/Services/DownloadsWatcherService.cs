using System.Collections.Concurrent;
using Pulse.Core.Events;
using Pulse.Core.Interfaces;

namespace Pulse.Core.Services;

/// <summary>
/// Watches the user's Downloads folder and surfaces completed downloads as island events.
/// Temporary browser artifacts (.crdownload, .part, ...) are ignored and files must
/// settle (unlocked, non-zero size) before an event is published.
/// </summary>
public sealed class DownloadsWatcherService : IDisposable
{
    private const int DefaultMaxLockRetries = 12;

    private static readonly string[] TempExtensions =
        [".crdownload", ".part", ".partial", ".opdownload", ".tmp", ".download"];

    private readonly IEventRouter _eventRouter;
    private readonly ISettingsService? _settings;
    private readonly TimeSpan _settleDelay;
    private readonly int _maxLockRetries;
    private readonly FileSystemWatcher? _watcher;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = new();
    private readonly object _pendingSync = new();
    private volatile bool _isDisposed;

    public DownloadsWatcherService(
        IEventRouter eventRouter,
        ISettingsService? settings = null,
        string? downloadsDirectory = null)
        : this(eventRouter, settings, downloadsDirectory, TimeSpan.FromMilliseconds(1500), DefaultMaxLockRetries)
    {
    }

    public DownloadsWatcherService(
        IEventRouter eventRouter,
        ISettingsService? settings,
        string? downloadsDirectory,
        TimeSpan settleDelay,
        int maxLockRetries)
    {
        _eventRouter = eventRouter ?? throw new ArgumentNullException(nameof(eventRouter));
        _settings = settings;
        _settleDelay = settleDelay > TimeSpan.Zero ? settleDelay : TimeSpan.FromMilliseconds(25);
        _maxLockRetries = Math.Max(0, maxLockRetries);

        var downloads = string.IsNullOrWhiteSpace(downloadsDirectory)
            ? KnownFolderResolver.GetDownloadsDirectory()
            : downloadsDirectory;

        if (!Directory.Exists(downloads))
        {
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(downloads)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite
            };
            _watcher.Created += OnFileTouched;
            _watcher.Changed += OnFileTouched;
            _watcher.Renamed += OnFileRenamed;
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            _watcher = null;
        }
    }

    public int PendingCount => _pending.Count;

    private void OnFileTouched(object sender, FileSystemEventArgs e)
    {
        try
        {
            ScheduleCheck(e.FullPath, attempt: 0);
        }
        catch
        {
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        try
        {
            ScheduleCheck(e.FullPath, attempt: 0);
        }
        catch
        {
        }
    }

    private void ScheduleCheck(string path, int attempt)
    {
        if (_isDisposed ||
            !IsCandidate(path) ||
            (_settings is not null && !_settings.Current.DownloadNotifications))
        {
            return;
        }

        CancellationTokenSource? cts = null;
        CancellationToken token;

        lock (_pendingSync)
        {
            if (_isDisposed)
            {
                return;
            }

            cts = new CancellationTokenSource();
            token = cts.Token;
            if (_pending.TryGetValue(path, out var previous))
            {
                try { previous.Cancel(); } catch { }
                try { previous.Dispose(); } catch { }
            }

            _pending[path] = cts;
        }

        _ = CompleteWhenSettledAsync(path, cts, token, attempt);
    }

    private async Task CompleteWhenSettledAsync(
        string path,
        CancellationTokenSource cts,
        CancellationToken token,
        int attempt)
    {
        var rescheduled = false;
        try
        {
            await Task.Delay(_settleDelay, token).ConfigureAwait(false);
            if (token.IsCancellationRequested ||
                _isDisposed ||
                !IsCandidate(path) ||
                (_settings is not null && !_settings.Current.DownloadNotifications))
            {
                return;
            }

            if (!File.Exists(path))
            {
                return;
            }

            if (IsLocked(path))
            {
                if (attempt < _maxLockRetries && !_isDisposed && !token.IsCancellationRequested)
                {
                    rescheduled = true;
                    ScheduleCheck(path, attempt + 1);
                }

                return;
            }

            var info = new FileInfo(path);
            if (info.Length <= 0)
            {
                return;
            }

            _eventRouter.Publish(new FileOperationEvent(
                $"download_{path}",
                info.Name,
                info.Length,
                info.Length,
                FileOperationStatus.Completed,
                info.DirectoryName,
                DisplayDuration: TimeSpan.FromSeconds(3.5)));
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Best-effort watcher: ignore transient IO races.
        }
        finally
        {
            if (!rescheduled)
            {
                lock (_pendingSync)
                {
                    _pending.TryRemove(KeyValuePair.Create(path, cts));
                }
            }

            try { cts.Dispose(); } catch { }
        }
    }

    private static bool IsCandidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.StartsWith('.') ||
            fileName.StartsWith('~') ||
            fileName.EndsWith(":Zone.Identifier", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return !TempExtensions.Any(temp => string.Equals(temp, extension, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLocked(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileTouched;
            _watcher.Changed -= OnFileTouched;
            _watcher.Renamed -= OnFileRenamed;
            _watcher.Dispose();
        }

        lock (_pendingSync)
        {
            foreach (var cts in _pending.Values)
            {
                try { cts.Cancel(); } catch { }
                try { cts.Dispose(); } catch { }
            }

            _pending.Clear();
        }
    }
}
