using Pulse.Core.Events;
using Pulse.Core.Interfaces;
using Pulse.Core.Models;
using Pulse.Core.Services;

namespace Pulse.Tests.Services;

public sealed class DownloadsWatcherServiceTests
{
    private sealed class StubSettingsService(AppSettings initial) : ISettingsService
    {
        public AppSettings Current { get; set; } = initial;
        public string? LastSaveError => null;
        public event EventHandler<AppSettings>? SettingsChanged;

        public Task LoadAsync() => Task.CompletedTask;

        public Task SaveAsync(AppSettings settings)
        {
            Current = settings;
            SettingsChanged?.Invoke(this, settings);
            return Task.CompletedTask;
        }

        public Task FlushAsync() => Task.CompletedTask;
    }

    [Fact]
    public async Task PublishesEventWhenSettledNonEmptyFileIsCreated()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PulseDownloadTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        using var router = new EventRouter();
        var settings = new StubSettingsService(new AppSettings { DownloadNotifications = true });
        using var watcher = new DownloadsWatcherService(
            router,
            settings,
            dir,
            TimeSpan.FromMilliseconds(40),
            maxLockRetries: 2);

        try
        {
            var filePath = Path.Combine(dir, "Setup.exe");
            await File.WriteAllBytesAsync(filePath, [1, 2, 3, 4]);

            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (router.CurrentActiveEvent is null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(30);
            }

            var evt = Assert.IsType<FileOperationEvent>(router.CurrentActiveEvent);
            Assert.Equal("Setup.exe", evt.FileName);
            Assert.Equal(FileOperationStatus.Completed, evt.Status);
            Assert.Equal(4, evt.TotalBytes);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task IgnoresTempExtensionsHiddenAndZeroByteFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PulseDownloadTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        using var router = new EventRouter();
        var settings = new StubSettingsService(new AppSettings { DownloadNotifications = true });
        using var watcher = new DownloadsWatcherService(
            router,
            settings,
            dir,
            TimeSpan.FromMilliseconds(35),
            maxLockRetries: 2);

        try
        {
            await File.WriteAllBytesAsync(Path.Combine(dir, "video.crdownload"), [1, 2, 3]);
            await File.WriteAllBytesAsync(Path.Combine(dir, "archive.part"), [1, 2, 3]);
            await File.WriteAllBytesAsync(Path.Combine(dir, ".hidden.zip"), [1, 2, 3]);
            await File.WriteAllBytesAsync(Path.Combine(dir, "empty.zip"), []);

            await Task.Delay(180);

            Assert.Null(router.CurrentActiveEvent);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SuppressesEventWhenDownloadNotificationsDisabled()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PulseDownloadTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        using var router = new EventRouter();
        var settings = new StubSettingsService(new AppSettings { DownloadNotifications = false });
        using var watcher = new DownloadsWatcherService(
            router,
            settings,
            dir,
            TimeSpan.FromMilliseconds(35),
            maxLockRetries: 2);

        try
        {
            await File.WriteAllBytesAsync(Path.Combine(dir, "doc.pdf"), [1, 2, 3, 4, 5]);
            await Task.Delay(160);

            Assert.Null(router.CurrentActiveEvent);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task StopsRetryingLockedFileAfterMaxLockRetries()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PulseDownloadTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        using var router = new EventRouter();
        var settings = new StubSettingsService(new AppSettings { DownloadNotifications = true });
        using var watcher = new DownloadsWatcherService(
            router,
            settings,
            dir,
            TimeSpan.FromMilliseconds(30),
            maxLockRetries: 2);

        try
        {
            var filePath = Path.Combine(dir, "locked.bin");
            using (var lockStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                await lockStream.WriteAsync(new byte[] { 10, 20, 30 });
                await lockStream.FlushAsync();

                // Wait longer than initial + 2 retries (~90ms) while still exclusively locked.
                await Task.Delay(250);
                Assert.Null(router.CurrentActiveEvent);
                Assert.Equal(0, watcher.PendingCount);
            }
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RapidTouchesFollowedByDisposeDoNotThrow()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PulseDownloadTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        using var router = new EventRouter();
        var watcher = new DownloadsWatcherService(
            router,
            settings: null,
            dir,
            TimeSpan.FromMilliseconds(25),
            maxLockRetries: 2);

        try
        {
            var path = Path.Combine(dir, "burst.dat");
            for (var i = 0; i < 15; i++)
            {
                await File.WriteAllBytesAsync(path, [(byte)(i + 1)]);
            }

            watcher.Dispose();
            await Task.Delay(100);
            Assert.Equal(0, watcher.PendingCount);
        }
        finally
        {
            watcher.Dispose();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
