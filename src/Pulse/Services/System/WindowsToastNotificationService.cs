using System.Runtime.InteropServices;
using Pulse.Core.Interfaces;
using Pulse.Core.Services;

namespace Pulse.Services.System;

/// <summary>
/// Surfaces real Windows Action Center / Toast notifications (WhatsApp, Discord, Chrome,
/// Windows system alerts) on the island without requiring MSIX package identity.
/// Reads isolated snapshot copies of %LOCALAPPDATA%\Microsoft\Windows\Notifications\wpndatabase.db.
/// </summary>
public sealed class WindowsToastNotificationService : IDisposable
{
    private const int SqliteOk = 0;
    private const int SqliteRow = 100;
    private const int SqliteOpenReadWrite = 0x00000002;
    private const int SqliteOpenReadOnly = 0x00000001;

    private readonly ISystemEventService _systemEvents;
    private readonly ISettingsService _settings;
    private readonly string _notificationsDir;
    private readonly string _dbPath;
    private readonly string _walPath;
    private readonly string _shmPath;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly FileSystemWatcher? _watcher;
    private readonly Timer _pollTimer;

    private CancellationTokenSource? _debounceCts;
    private long _lastSeenOrder;
    private bool _seeded;
    private DateTime _lastKnownStampUtc;
    private long _lastKnownWalLength = -1;
    private bool _disposed;

    public WindowsToastNotificationService(ISystemEventService systemEvents, ISettingsService settings)
    {
        _systemEvents = systemEvents ?? throw new ArgumentNullException(nameof(systemEvents));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        _notificationsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "Windows",
            "Notifications");
        _dbPath = Path.Combine(_notificationsDir, "wpndatabase.db");
        _walPath = _dbPath + "-wal";
        _shmPath = _dbPath + "-shm";

        if (Directory.Exists(_notificationsDir))
        {
            try
            {
                _watcher = new FileSystemWatcher(_notificationsDir, "wpndatabase.db*")
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
                };
                _watcher.Changed += OnDatabaseFileChanged;
                _watcher.Created += OnDatabaseFileChanged;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                CrashLogger.Write("WindowsToastNotificationService.WatcherInit", ex);
                _watcher = null;
            }
        }

        _ = Task.Run(SeedBaselineOrderAsync);

        // Low-overhead stat check catches WAL appends that Windows skips emitting FSW events for until flush.
        _pollTimer = new Timer(_ => CheckForMetadataChange(), null, TimeSpan.FromMilliseconds(1200), TimeSpan.FromMilliseconds(1500));
    }

    private void OnDatabaseFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_disposed || !_settings.Current.SystemToastNotifications)
        {
            return;
        }

        ScheduleScan();
    }

    private void CheckForMetadataChange()
    {
        if (_disposed || !_settings.Current.SystemToastNotifications || !File.Exists(_dbPath))
        {
            return;
        }

        try
        {
            var walInfo = File.Exists(_walPath) ? new FileInfo(_walPath) : null;
            var dbInfo = new FileInfo(_dbPath);
            var stamp = walInfo is not null && walInfo.LastWriteTimeUtc > dbInfo.LastWriteTimeUtc
                ? walInfo.LastWriteTimeUtc
                : dbInfo.LastWriteTimeUtc;
            var walLen = walInfo?.Length ?? 0;

            if (stamp != _lastKnownStampUtc || walLen != _lastKnownWalLength)
            {
                _lastKnownStampUtc = stamp;
                _lastKnownWalLength = walLen;
                ScheduleScan();
            }
        }
        catch
        {
            // Ignore transient lock/IO errors during stat check.
        }
    }

    private void ScheduleScan()
    {
        if (_disposed)
        {
            return;
        }

        var nextCts = new CancellationTokenSource();
        var prev = Interlocked.Exchange(ref _debounceCts, nextCts);
        if (prev is not null)
        {
            try { prev.Cancel(); } catch { }
            prev.Dispose();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(140, nextCts.Token).ConfigureAwait(false);
                await ScanNewToastsAsync(nextCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                CrashLogger.Write("WindowsToastNotificationService.ScheduleScan", ex);
            }
        });
    }

    private async Task SeedBaselineOrderAsync()
    {
        await _scanGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed || !File.Exists(_dbPath))
            {
                _seeded = true;
                return;
            }

            UpdateMetadataBaseline();
            if (TryQueryMaxOrder(out var maxOrder))
            {
                _lastSeenOrder = maxOrder;
            }

            _seeded = true;
        }
        catch (Exception ex)
        {
            CrashLogger.Write("WindowsToastNotificationService.SeedBaseline", ex);
            _seeded = true;
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private void UpdateMetadataBaseline()
    {
        try
        {
            var walInfo = File.Exists(_walPath) ? new FileInfo(_walPath) : null;
            var dbInfo = new FileInfo(_dbPath);
            _lastKnownStampUtc = walInfo is not null && walInfo.LastWriteTimeUtc > dbInfo.LastWriteTimeUtc
                ? walInfo.LastWriteTimeUtc
                : dbInfo.LastWriteTimeUtc;
            _lastKnownWalLength = walInfo?.Length ?? 0;
        }
        catch
        {
        }
    }

    private async Task ScanNewToastsAsync(CancellationToken cancellationToken)
    {
        if (_disposed || cancellationToken.IsCancellationRequested || !File.Exists(_dbPath))
        {
            return;
        }

        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!_seeded)
            {
                if (TryQueryMaxOrder(out var maxOrder))
                {
                    _lastSeenOrder = maxOrder;
                }

                _seeded = true;
                return;
            }

            if (!_settings.Current.SystemToastNotifications)
            {
                if (TryQueryMaxOrder(out var skippedMax))
                {
                    _lastSeenOrder = Math.Max(_lastSeenOrder, skippedMax);
                }

                return;
            }

            var toasts = ReadToastsAfterOrder(_lastSeenOrder);
            foreach (var (order, primaryId, payload) in toasts)
            {
                if (order > _lastSeenOrder)
                {
                    _lastSeenOrder = order;
                }

                var parsed = ToastNotificationParser.TryParse(primaryId, payload);
                if (parsed is null)
                {
                    continue;
                }

                _systemEvents.ReportSystemToast(
                    order.ToString(),
                    parsed.Title,
                    parsed.Subtitle,
                    parsed.IconGlyph,
                    parsed.IsHighPriority);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CrashLogger.Write("WindowsToastNotificationService.ScanNewToasts", ex);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private bool TryQueryMaxOrder(out long maxOrder)
    {
        maxOrder = 0;
        var tempDir = CreateSnapshotDirectory();
        if (tempDir is null)
        {
            return false;
        }

        IntPtr db = IntPtr.Zero;
        IntPtr stmt = IntPtr.Zero;
        try
        {
            var snapDb = Path.Combine(tempDir, "wpndatabase.db");
            var flags = File.Exists(snapDb + "-wal") ? SqliteOpenReadWrite : SqliteOpenReadOnly;
            if (sqlite3_open_v2(snapDb, out db, flags, IntPtr.Zero) != SqliteOk)
            {
                return false;
            }

            const string sql = "SELECT COALESCE(MAX([Order]), 0) FROM Notification;";
            if (sqlite3_prepare_v2(db, sql, -1, out stmt, out _) != SqliteOk)
            {
                return false;
            }

            if (sqlite3_step(stmt) == SqliteRow)
            {
                maxOrder = sqlite3_column_int64(stmt, 0);
            }

            return true;
        }
        finally
        {
            if (stmt != IntPtr.Zero) _ = sqlite3_finalize(stmt);
            if (db != IntPtr.Zero) _ = sqlite3_close_v2(db);
            TryDeleteDirectory(tempDir);
        }
    }

    private List<(long Order, string PrimaryId, byte[] Payload)> ReadToastsAfterOrder(long minOrder)
    {
        var results = new List<(long Order, string PrimaryId, byte[] Payload)>();
        var tempDir = CreateSnapshotDirectory();
        if (tempDir is null)
        {
            return results;
        }

        IntPtr db = IntPtr.Zero;
        IntPtr stmt = IntPtr.Zero;
        try
        {
            var snapDb = Path.Combine(tempDir, "wpndatabase.db");
            var flags = File.Exists(snapDb + "-wal") ? SqliteOpenReadWrite : SqliteOpenReadOnly;
            if (sqlite3_open_v2(snapDb, out db, flags, IntPtr.Zero) != SqliteOk)
            {
                return results;
            }

            const string sql = """
                SELECT n.[Order], COALESCE(h.PrimaryId, ''), n.Payload
                FROM Notification n
                LEFT JOIN NotificationHandler h ON n.HandlerId = h.RecordId
                WHERE n.[Order] > ?1 AND n.Type = 'toast' AND n.Payload IS NOT NULL
                ORDER BY n.[Order] ASC
                LIMIT 10;
                """;

            if (sqlite3_prepare_v2(db, sql, -1, out stmt, out _) != SqliteOk)
            {
                return results;
            }

            _ = sqlite3_bind_int64(stmt, 1, minOrder);

            while (sqlite3_step(stmt) == SqliteRow)
            {
                var order = sqlite3_column_int64(stmt, 0);
                var primaryIdPtr = sqlite3_column_text(stmt, 1);
                var primaryId = primaryIdPtr != IntPtr.Zero ? (Marshal.PtrToStringUTF8(primaryIdPtr) ?? string.Empty) : string.Empty;

                var blobPtr = sqlite3_column_blob(stmt, 2);
                var blobLength = sqlite3_column_bytes(stmt, 2);
                if (blobPtr == IntPtr.Zero || blobLength <= 0)
                {
                    continue;
                }

                var payload = new byte[blobLength];
                Marshal.Copy(blobPtr, payload, 0, blobLength);
                results.Add((order, primaryId, payload));
            }
        }
        finally
        {
            if (stmt != IntPtr.Zero) _ = sqlite3_finalize(stmt);
            if (db != IntPtr.Zero) _ = sqlite3_close_v2(db);
            TryDeleteDirectory(tempDir);
        }

        return results;
    }

    private string? CreateSnapshotDirectory()
    {
        if (!File.Exists(_dbPath))
        {
            return null;
        }

        var dir = Path.Combine(Path.GetTempPath(), "PulseToastSnap_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            CopyShared(_dbPath, Path.Combine(dir, "wpndatabase.db"));
            if (File.Exists(_walPath))
            {
                CopyShared(_walPath, Path.Combine(dir, "wpndatabase.db-wal"));
            }

            if (File.Exists(_shmPath))
            {
                CopyShared(_shmPath, Path.Combine(dir, "wpndatabase.db-shm"));
            }

            return dir;
        }
        catch
        {
            TryDeleteDirectory(dir);
            return null;
        }
    }

    private static void CopyShared(string source, string destination)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pollTimer.Dispose();

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnDatabaseFileChanged;
            _watcher.Created -= OnDatabaseFileChanged;
            _watcher.Dispose();
        }

        var cts = Interlocked.Exchange(ref _debounceCts, null);
        if (cts is not null)
        {
            try { cts.Cancel(); } catch { }
            cts.Dispose();
        }

        _scanGate.Dispose();
    }

    // ---- winsqlite3.dll UTF-8 P/Invoke ----

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
        out IntPtr ppDb,
        int flags,
        IntPtr zVfs);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close_v2(IntPtr db);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(
        IntPtr db,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string zSql,
        int nByte,
        out IntPtr ppStmt,
        out IntPtr pzTail);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_int64(IntPtr pStmt, int index, long value);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr pStmt);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern long sqlite3_column_int64(IntPtr pStmt, int iCol);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr pStmt, int iCol);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_blob(IntPtr pStmt, int iCol);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_bytes(IntPtr pStmt, int iCol);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr pStmt);
}

