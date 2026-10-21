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
    private static readonly string[] TempExtensions =
        [".crdownload", ".part", ".partial", ".opdownload", ".tmp", ".download"];

    private readonly IEventRouter _eventRouter;
    private readonly ISettingsService? _settings;
    private readonly FileSystemWatcher? _watcher;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = new();
    private bool _isDisposed;

    public DownloadsWatcherService(IEventRouter eventRouter, ISettingsService? settings = null)
    {
        _eventRouter = eventRouter ?? throw new ArgumentNullException(nameof(eventRouter));
        _settings = settings;

        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

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

    private void OnFileTouched(object sender, FileSystemEventArgs e) => ScheduleCheck(e.FullPath);

    private void OnFileRenamed(object sender, RenamedEventArgs e) => ScheduleCheck(e.FullPath);

    private void ScheduleCheck(string path)
    {
        if (_isDisposed || !IsCandidate(path))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _pending.AddOrUpdate(
            path,
            cts,
            (_, previous) =>
            {
                previous.Cancel();
                previous.Dispose();
                return cts;
            });

        _ = CompleteWhenSettledAsync(path, cts.Token);
    }

    private async Task CompleteWhenSettledAsync(string path, CancellationToken token)
    {
        try
        {
            await Task.Delay(1500, token).ConfigureAwait(false);
            if (token.IsCancellationRequested || _isDisposed || !IsCandidate(path))
            {
                return;
            }

            if (!File.Exists(path))
            {
                return;
            }

            if (IsLocked(path))
            {
                // Still being written — check again shortly.
                ScheduleCheck(path);
                return;
            }

            var info = new FileInfo(path);
            if (info.Length <= 0)
            {
                return;
            }

            _pending.TryRemove(path, out _);

            if (_settings is not null && !_settings.Current.DownloadNotifications)
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
    }

    private static bool IsCandidate(string path)
    {
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
            _watcher.Dispose();
        }

        foreach (var cts in _pending.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _pending.Clear();
    }
}
