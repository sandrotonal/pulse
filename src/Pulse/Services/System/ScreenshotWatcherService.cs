using Pulse.Core.Interfaces;
using Pulse.Core.Services;

namespace Pulse.Services.System;

/// <summary>
/// Watches the user's Known Screenshots folder (Pictures\Screenshots) so OS captures
/// (Win+Shift+S / PrintScreen) surface on the Dynamic Island with one-click open support.
/// Handles delayed OneDrive/Snipping Tool flushes by also tracking recent Changed events
/// and retrying while a freshly created file is still empty (0 bytes) or locked.
/// </summary>
public sealed class ScreenshotWatcherService : IDisposable
{
    private const int MaxLockRetries = 12;

    private readonly ISystemEventService _systemEvents;
    private readonly ScreenshotFilePolicy _policy = new();
    private readonly FileSystemWatcher? _watcher;
    private readonly object _pendingSync = new();
    private readonly Dictionary<string, CancellationTokenSource> _pending = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _disposed;

    public ScreenshotWatcherService(ISystemEventService systemEvents, string? screenshotsDirectory = null)
    {
        _systemEvents = systemEvents ?? throw new ArgumentNullException(nameof(systemEvents));

        var dir = string.IsNullOrWhiteSpace(screenshotsDirectory)
            ? KnownFolderResolver.GetScreenshotsDirectory()
            : screenshotsDirectory;

        if (!Directory.Exists(dir))
        {
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(dir)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size | NotifyFilters.LastWrite
            };
            _watcher.Created += OnFileTouched;
            _watcher.Renamed += OnFileRenamed;
            _watcher.Changed += OnFileChanged;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            CrashLogger.Write("ScreenshotWatcherService.Ctor", ex);
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    private void OnFileTouched(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (_policy.RecordObservedCreation(e.FullPath, DateTimeOffset.UtcNow))
            {
                ScheduleReport(e.FullPath);
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("ScreenshotWatcherService.OnFileTouched", ex);
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        try
        {
            if (_policy.RecordObservedCreation(e.FullPath, DateTimeOffset.UtcNow))
            {
                ScheduleReport(e.FullPath);
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("ScreenshotWatcherService.OnFileRenamed", ex);
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (_disposed || !ScreenshotFilePolicy.IsExternalScreenshotPath(e.FullPath))
            {
                return;
            }

            var creationUtc = DateTimeOffset.MinValue;
            try
            {
                if (File.Exists(e.FullPath))
                {
                    creationUtc = new DateTimeOffset(File.GetCreationTimeUtc(e.FullPath), TimeSpan.Zero);
                }
            }
            catch
            {
                // File may be locked during atomic write; recorded creation window still allows retry.
            }

            if (_policy.ShouldInspectChange(e.FullPath, creationUtc, DateTimeOffset.UtcNow))
            {
                ScheduleReport(e.FullPath);
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("ScreenshotWatcherService.OnFileChanged", ex);
        }
    }

    private void ScheduleReport(string path, int attempt = 0)
    {
        if (!ScreenshotFilePolicy.IsExternalScreenshotPath(path))
        {
            return;
        }

        CancellationTokenSource? prev = null;
        var cts = new CancellationTokenSource();
        lock (_pendingSync)
        {
            if (_disposed)
            {
                cts.Dispose();
                return;
            }

            if (_pending.TryGetValue(path, out prev))
            {
                _pending[path] = cts;
            }
            else
            {
                _pending.Add(path, cts);
            }
        }

        if (prev is not null)
        {
            try { prev.Cancel(); } catch (ObjectDisposedException) { }
        }

        _ = WaitAndReportAsync(path, cts, attempt);
    }

    private async Task WaitAndReportAsync(string path, CancellationTokenSource cts, int attempt)
    {
        var requeued = false;
        try
        {
            var token = cts.Token;
            await Task.Delay(260, token).ConfigureAwait(false);
            if (_disposed || token.IsCancellationRequested || !File.Exists(path))
            {
                return;
            }

            var info = new FileInfo(path);
            if (IsLocked(path) || info.Length <= 0)
            {
                if (attempt + 1 < MaxLockRetries)
                {
                    requeued = true;
                    ScheduleReport(path, attempt + 1);
                }

                return;
            }

            if (!_policy.TryMarkReported(path, DateTimeOffset.UtcNow))
            {
                return;
            }

            _systemEvents.ReportScreenshotSaved(path);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            CrashLogger.Write("ScreenshotWatcherService.WaitAndReportAsync", ex, $"Path={path}");
        }
        finally
        {
            if (!requeued)
            {
                lock (_pendingSync)
                {
                    if (_pending.TryGetValue(path, out var current) && ReferenceEquals(current, cts))
                    {
                        _pending.Remove(path);
                    }
                }
            }

            cts.Dispose();
        }
    }

    private static bool IsLocked(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch
        {
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileTouched;
            _watcher.Renamed -= OnFileRenamed;
            _watcher.Changed -= OnFileChanged;
            _watcher.Dispose();
        }

        List<CancellationTokenSource> toCancel;
        lock (_pendingSync)
        {
            toCancel = [.. _pending.Values];
            _pending.Clear();
        }

        foreach (var cts in toCancel)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        _policy.Clear();
    }
}
