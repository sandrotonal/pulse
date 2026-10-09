using Pulse.Core.Models;
using Pulse.Services.Settings;

namespace Pulse.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task SettingsPersistAcrossServiceInstances()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PulseTests", Guid.NewGuid().ToString("N"));
        try
        {
            var first = new SettingsService(directory);
            var expected = new AppSettings
            {
                StartWithWindows = true,
                AutoCollapse = false,
                ShowSeekControls = false,
                Theme = "Light",
                AnimationIntensity = "Dynamic",
                TopOffset = 42
            };

            await first.SaveAsync(expected);
            var second = new SettingsService(directory);
            await second.LoadAsync();

            Assert.Equal(expected, second.Current);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SettingsValuesAreNormalizedBeforeSaving()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PulseTests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new SettingsService(directory);
            await service.SaveAsync(new AppSettings { TopOffset = 500, Theme = "Neon", AnimationIntensity = "Ultra" });

            Assert.Equal(200, service.Current.TopOffset);
            Assert.Equal("System", service.Current.Theme);
            Assert.Equal("Normal", service.Current.AnimationIntensity);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
