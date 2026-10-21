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
                UseTransparency = true,
                TransparencyLevel = 34,
                ArtworkTintStrength = 72,
                MotionSpeed = 130,
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
    public async Task LoadedSettingsAreNormalized()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PulseTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), """
                {
                  "TopOffset": -12,
                  "IslandScale": 300,
                  "TransparencyLevel": 140,
                  "ArtworkTintStrength": -5,
                  "MotionSpeed": 10
                }
                """);

            var service = new SettingsService(directory);
            await service.LoadAsync();

            Assert.Equal(0, service.Current.TopOffset);
            Assert.Equal(125, service.Current.IslandScale);
            Assert.Equal(100, service.Current.TransparencyLevel);
            Assert.Equal(0, service.Current.ArtworkTintStrength);
            Assert.Equal(70, service.Current.MotionSpeed);
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
    public void DefaultsMatchDocumentedVisualValues()
    {
        var settings = new AppSettings();

        Assert.Equal(60, settings.TransparencyLevel);
        Assert.Equal(25, settings.ArtworkTintStrength);
        Assert.Equal(100, settings.MotionSpeed);
    }

    [Fact]
    public async Task SettingsValuesAreNormalizedBeforeSaving()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PulseTests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new SettingsService(directory);
            await service.SaveAsync(new AppSettings
            {
                TopOffset = 500,
                Theme = "Neon",
                AnimationIntensity = "Ultra",
                TransparencyLevel = 250,
                ArtworkTintStrength = -30,
                MotionSpeed = 900
            });

            Assert.Equal(200, service.Current.TopOffset);
            Assert.Equal("System", service.Current.Theme);
            Assert.Equal("Normal", service.Current.AnimationIntensity);
            Assert.Equal(100, service.Current.TransparencyLevel);
            Assert.Equal(0, service.Current.ArtworkTintStrength);
            Assert.Equal(140, service.Current.MotionSpeed);
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
