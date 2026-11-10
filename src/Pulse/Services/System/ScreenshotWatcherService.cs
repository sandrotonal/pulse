using System.Collections.Concurrent;
using Pulse.Core.Interfaces;
using Pulse.Core.Services;

namespace Pulse.Services.System;

/// <summary>
/// Watches the user's Known Screenshots folder (Pictures\Screenshots) so OS captures
/// (Win+Shift+S / PrintScreen) surface on the Dynamic Island with one-click open support.
/// </summary>
public sealed class ScreenshotWatcherService : IDisposable
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp"];

    private readonly ISystemEventService _systemEvents;
    private readonly FileSystemWatcher? _watcher;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _reportedRecently = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

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
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size
            };
            _watcher.Created += OnFileTouched;
            _watcher.Renamed += OnFileRenamed;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            CrashLogger.Write("ScreenshotWatcherService.Ctor", ex);
            _watcher = null;
        }
    }

    private void OnFileTouched(object sender, FileSystemEventArgs e) => ScheduleReport(e.FullPath);

    private void OnFileRenamed(object sender, RenamedEventArgs e) => ScheduleReport(e.FullPath);

    private void ScheduleReport(string path)
    {
        if (_disposed || !IsExternalScreenshot(path))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _pending.AddOrUpdate(
            path,
            cts,
            (_, prev) =>
            {
                try { prev.Cancel(); } catch { }
                prev.Dispose();
                return cts;
            });

        _ = WaitAndReportAsync(path, cts.Token);
    }

    private async Task WaitAndReportAsync(string path, CancellationToken token)
    {
        try
        {
            await Task.Delay(260, token).ConfigureAwait(false);
            if (_disposed || token.IsCancellationRequested || !File.Exists(path))
            {
                return;
            }

            if (IsLocked(path))
            {
                ScheduleReport(path);
                return;
            }

            var info = new FileInfo(path);
            if (info.Length <= 0)
            {
                return;
            }

            _pending.TryRemove(path, out _);

            var now = DateTimeOffset.UtcNow;
            if (_reportedRecently.TryGetValue(path, out var prevTime) && (now - prevTime) < TimeSpan.FromSeconds(5))
            {
                return;
            }

            _reportedRecently[path] = now;
            _systemEvents.ReportScreenshotSaved(path);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CrashLogger.Write("ScreenshotWatcherService.WaitAndReportAsync", ex, $"Path={path}");
        }
    }

    private static bool IsExternalScreenshot(string path)
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
            _watcher.Dispose();
        }

        foreach (var cts in _pending.Values)
        {
            try { cts.Cancel(); } catch { }
            cts.Dispose();
        }

        _pending.Clear();
    }
}

