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
                HoverExpandDelayMs = 350,
                AutoHideOnFullscreen = false,
                EnableScrollWheelVolume = false,
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
    public async Task LoadingLegacySettingsKeepsNewFieldDefaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PulseTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "settings.json"),
                """{ "AutoCollapse": true }""");

            var service = new SettingsService(directory);
            await service.LoadAsync();

            Assert.Equal(200, service.Current.HoverExpandDelayMs);
            Assert.True(service.Current.AutoHideOnFullscreen);
            Assert.True(service.Current.EnableScrollWheelVolume);
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
                  "MotionSpeed": 10,
                  "HoverExpandDelayMs": 5000
                }
                """);

            var service = new SettingsService(directory);
            await service.LoadAsync();

            Assert.Equal(0, service.Current.TopOffset);
            Assert.Equal(125, service.Current.IslandScale);
            Assert.Equal(100, service.Current.TransparencyLevel);
            Assert.Equal(0, service.Current.ArtworkTintStrength);
            Assert.Equal(70, service.Current.MotionSpeed);
            Assert.Equal(1000, service.Current.HoverExpandDelayMs);
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
        Assert.Equal(200, settings.HoverExpandDelayMs);
        Assert.True(settings.AutoHideOnFullscreen);
        Assert.True(settings.EnableScrollWheelVolume);
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
                MotionSpeed = 900,
                HoverExpandDelayMs = 5000
            });

            Assert.Equal(200, service.Current.TopOffset);
            Assert.Equal("System", service.Current.Theme);
            Assert.Equal("Normal", service.Current.AnimationIntensity);
            Assert.Equal(100, service.Current.TransparencyLevel);
            Assert.Equal(0, service.Current.ArtworkTintStrength);
            Assert.Equal(140, service.Current.MotionSpeed);
            Assert.Equal(1000, service.Current.HoverExpandDelayMs);
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
    public void IdleDefaultsAreEnabled()
    {
        var settings = new AppSettings();

        Assert.True(settings.ShowIdleClock);
        Assert.True(settings.ShowIdleDate);
        Assert.True(settings.ShowIdleWeather);
        Assert.Equal("Auto", settings.IdleLocationSource);
        Assert.Equal(string.Empty, settings.IdleLocationCity);
        Assert.Null(settings.IdleLocationLatitude);
        Assert.Null(settings.IdleLocationLongitude);
    }

    [Fact]
    public async Task IdleSettingsRoundTripWithNormalization()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PulseTests", Guid.NewGuid().ToString("N"));
        try
        {
            var first = new SettingsService(directory);
            await first.SaveAsync(new AppSettings
            {
                ShowIdleClock = false,
                ShowIdleDate = false,
                ShowIdleWeather = false,
                IdleLocationSource = "Manual",
                IdleLocationCity = "  İstanbul  ",
                IdleLocationLatitude = 41.01384,
                IdleLocationLongitude = 28.94966
            });

            var second = new SettingsService(directory);
            await second.LoadAsync();

            Assert.False(second.Current.ShowIdleClock);
            Assert.False(second.Current.ShowIdleDate);
            Assert.False(second.Current.ShowIdleWeather);
            Assert.Equal("Manual", second.Current.IdleLocationSource);
            Assert.Equal("İstanbul", second.Current.IdleLocationCity);
            Assert.Equal(41.01384, second.Current.IdleLocationLatitude);
            Assert.Equal(28.94966, second.Current.IdleLocationLongitude);
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
    public async Task InvalidIdleLocationIsNormalized()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PulseTests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new SettingsService(directory);
            await service.SaveAsync(new AppSettings
            {
                IdleLocationSource = "Neon",
                IdleLocationCity = new string('a', 120),
                IdleLocationLatitude = 200,
                IdleLocationLongitude = double.NaN
            });

            Assert.Equal("Auto", service.Current.IdleLocationSource);
            Assert.Equal(80, service.Current.IdleLocationCity.Length);
            Assert.Null(service.Current.IdleLocationLatitude);
            Assert.Null(service.Current.IdleLocationLongitude);
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
