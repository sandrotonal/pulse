using Pulse.Core.Models;
using Pulse.Core.Services;
using Pulse.Services.Settings;

namespace Pulse.Tests.Services;

public sealed class HardeningTests
{
    [Fact]
    public void KnownFolderResolverReturnsRootedNonEmptyDirectories()
    {
        var downloads = KnownFolderResolver.GetDownloadsDirectory();
        var screenshots = KnownFolderResolver.GetScreenshotsDirectory();

        Assert.False(string.IsNullOrWhiteSpace(downloads));
        Assert.False(string.IsNullOrWhiteSpace(screenshots));
        Assert.True(Path.IsPathRooted(downloads));
        Assert.True(Path.IsPathRooted(screenshots));
    }

    [Fact]
    public void CrashLoggerWritesSharedReadAndDeduplicatesRapidBurst()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PulseTests", Guid.NewGuid().ToString("N"));
        var logPath = Path.Combine(dir, "crash.log");

        try
        {
            Directory.CreateDirectory(dir);
            CrashLogger.WriteMessage("HardeningTests", "Unique burst message " + dir, logPath);

            // External reader holding FileShare.ReadWrite should not block subsequent writes.
            using (new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                // Identical burst within 2s should be deduplicated; a distinct message should be written.
                CrashLogger.WriteMessage("HardeningTests", "Unique burst message " + dir, logPath);
                CrashLogger.WriteMessage("HardeningTests", "Second distinct message", logPath);
            }

            var content = File.ReadAllText(logPath);
            Assert.Contains("Second distinct message", content);
            var firstIdx = content.IndexOf("Unique burst message", StringComparison.Ordinal);
            var lastIdx = content.LastIndexOf("Unique burst message", StringComparison.Ordinal);
            Assert.Equal(firstIdx, lastIdx);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SettingsServiceNormalizesDeveloperWorkspaceSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PulseTests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new SettingsService(dir);
            var raw = new AppSettings
            {
                EnableDeveloperWorkspace = true,
                DeveloperPollIntervalSeconds = 5,
                DeveloperGitHubRepositories =
                [
                    "https://github.com/sandrotonal/pulse.git",
                    "sandrotonal/pulse",
                    "invalid-entry"
                ],
                DeveloperLocalRepositories =
                [
                    " \"C:\\Repo\\One\" ",
                    "C:\\Repo\\One"
                ]
            };

            await service.SaveAsync(raw);
            var reloaded = new SettingsService(dir);
            await reloaded.LoadAsync();

            Assert.True(reloaded.Current.EnableDeveloperWorkspace);
            Assert.Equal(30, reloaded.Current.DeveloperPollIntervalSeconds);
            Assert.Equal(["sandrotonal/pulse"], reloaded.Current.DeveloperGitHubRepositories);
            Assert.Equal(["C:\\Repo\\One"], reloaded.Current.DeveloperLocalRepositories);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void SingleInstanceNameResolverIncludesDevExpandSignal()
    {
        var defaultNames = SingleInstanceNameResolver.Resolve("Default");
        var sandboxNames = SingleInstanceNameResolver.Resolve("Sandbox 1");

        Assert.Equal(SingleInstanceNameResolver.DefaultExpandDevEventName, defaultNames.ExpandDevEventName);
        Assert.StartsWith(SingleInstanceNameResolver.DefaultExpandDevEventName + "_", sandboxNames.ExpandDevEventName);
    }

    [Fact]
    public async Task SettingsServicePersistsEvenIfSettingsChangedSubscriberThrows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PulseTests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new SettingsService(dir);
            var secondCalled = false;
            service.SettingsChanged += (_, _) => throw new InvalidOperationException("Simulated UI handler error");
            service.SettingsChanged += (_, _) => secondCalled = true;

            await service.SaveAsync(new AppSettings { EnableDeveloperWorkspace = true });
            await service.FlushAsync();

            Assert.True(secondCalled);
            var reloaded = new SettingsService(dir);
            await reloaded.LoadAsync();
            Assert.True(reloaded.Current.EnableDeveloperWorkspace);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
