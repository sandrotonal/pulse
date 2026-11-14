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
        Assert.True(settings.FocusCompletionSound);
        Assert.False(settings.NotificationsPaused);
        Assert.True(settings.SystemToastNotifications);
        Assert.True(settings.BluetoothNotifications);
    }

    [Fact]
    public async Task FlushAsyncWaitsForActiveSaveAndClearsLastSaveError()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PulseTests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new SettingsService(directory);
            await service.SaveAsync(new AppSettings
            {
                StartWithWindows = true,
                SystemToastNotifications = false,
                BluetoothNotifications = false
            });
            await service.FlushAsync();

            Assert.Null(service.LastSaveError);

            var reloaded = new SettingsService(directory);
            await reloaded.LoadAsync();
            Assert.True(reloaded.Current.StartWithWindows);
            Assert.False(reloaded.Current.SystemToastNotifications);
            Assert.False(reloaded.Current.BluetoothNotifications);
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
                FocusCompletionSound = false,
                IdleLocationSource = "Manual",
                IdleLocationCity = "  İstanbul  ",
                IdleLocationLatitude = 41.01384,
                IdleLocationLongitude = 28.94966
            });

            var second = new SettingsService(directory);
            await second.LoadAsync();

            Assert.False(second.Current.FocusCompletionSound);
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

    [Fact]
    public async Task MalformedSinglePropertyFallsBackOnlyThatFieldWithoutWipingValidSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PulseTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), """
                {
                  "Theme": "Light",
                  "IslandScale": 110,
                  "IdleLocationCity": "Ankara",
                  "TopOffset": "not-an-integer",
                  "UseTransparency": 12345,
                  "PowerNotifications": "invalid-bool"
                }
                """);

            using var service = new SettingsService(directory);
            await service.LoadAsync();

            Assert.Equal("Light", service.Current.Theme);
            Assert.Equal(110, service.Current.IslandScale);
            Assert.Equal("Ankara", service.Current.IdleLocationCity);
            Assert.Equal(14, service.Current.TopOffset);
            Assert.False(service.Current.UseTransparency);
            Assert.True(service.Current.PowerNotifications);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("dark", "Dark")]
    [InlineData("LIGHT", "Light")]
    [InlineData("system", "System")]
    [InlineData("Neon", "System")]
    public async Task ThemeNormalizationIsCaseInsensitive(string input, string expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), "PulseTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var service = new SettingsService(directory);
            await service.SaveAsync(new AppSettings { Theme = input });

            Assert.Equal(expected, service.Current.Theme);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("{bad")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("")]
    public async Task CorruptOrNonObjectJsonFallsBackToDefaults(string payload)
    {
        var directory = Path.Combine(Path.GetTempPath(), "PulseTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), payload);

            using var service = new SettingsService(directory);
            await service.LoadAsync();

            Assert.Equal(new AppSettings(), service.Current);
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
    public void PersonalizationDefaultsMatchExpectedValues()
    {
        var settings = new AppSettings();

        Assert.Equal(string.Empty, settings.PreferredMonitorDeviceName);
        Assert.Equal(0, settings.HorizontalOffset);
        Assert.Equal(600, settings.AutoCollapseDelayMs);
        Assert.Equal("Expand", settings.CompactClickAction);
        Assert.Equal("Preview", settings.HoverAction);
        Assert.True(settings.Use24HourClock);
        Assert.Equal("C", settings.TemperatureUnit);
        Assert.True(settings.ShowLyrics);
        Assert.Equal(25, settings.FocusDefaultMinutes);
        Assert.Equal(5, settings.FocusBreakMinutes);
        Assert.Equal(45, settings.FocusCustomMinutes);
        Assert.True(settings.EnableGlobalHotkeys);
        Assert.True(settings.ScreenshotNotifications);
        Assert.False(settings.DoNotDisturb);
    }

    [Fact]
    public async Task PersonalizationSettingsRoundTripAndNormalize()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PulseTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var first = new SettingsService(directory);
            await first.SaveAsync(new AppSettings
            {
                PreferredMonitorDeviceName = "  \\\\.\\DISPLAY2  ",
                HorizontalOffset = -120,
                AutoCollapseDelayMs = 1250,
                CompactClickAction = "activateapp",
                HoverAction = "expandcard",
                Use24HourClock = false,
                TemperatureUnit = "f",
                ShowLyrics = false,
                FocusDefaultMinutes = 50,
                FocusBreakMinutes = 10,
                FocusCustomMinutes = 65,
                EnableGlobalHotkeys = false,
                ScreenshotNotifications = false,
                DoNotDisturb = true
            });
            await first.FlushAsync();

            using var second = new SettingsService(directory);
            await second.LoadAsync();

            Assert.Equal(@"\\.\DISPLAY2", second.Current.PreferredMonitorDeviceName);
            Assert.Equal(-120, second.Current.HorizontalOffset);
            Assert.Equal(1250, second.Current.AutoCollapseDelayMs);
            Assert.Equal("ActivateApp", second.Current.CompactClickAction);
            Assert.Equal("ExpandCard", second.Current.HoverAction);
            Assert.False(second.Current.Use24HourClock);
            Assert.Equal("F", second.Current.TemperatureUnit);
            Assert.False(second.Current.ShowLyrics);
            Assert.Equal(50, second.Current.FocusDefaultMinutes);
            Assert.Equal(10, second.Current.FocusBreakMinutes);
            Assert.Equal(65, second.Current.FocusCustomMinutes);
            Assert.False(second.Current.EnableGlobalHotkeys);
            Assert.False(second.Current.ScreenshotNotifications);
            Assert.True(second.Current.DoNotDisturb);
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
    public async Task InvalidPersonalizationValuesAreClampedAndFallbackCleanly()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PulseTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var service = new SettingsService(directory);
            await service.SaveAsync(new AppSettings
            {
                PreferredMonitorDeviceName = new string('M', 200),
                HorizontalOffset = -999,
                AutoCollapseDelayMs = 50,
                CompactClickAction = "Unknown",
                HoverAction = "Unknown",
                TemperatureUnit = "K",
                FocusDefaultMinutes = 0,
                FocusBreakMinutes = 999,
                FocusCustomMinutes = 300
            });

            Assert.Equal(128, service.Current.PreferredMonitorDeviceName.Length);
            Assert.Equal(-400, service.Current.HorizontalOffset);
            Assert.Equal(300, service.Current.AutoCollapseDelayMs);
            Assert.Equal("Expand", service.Current.CompactClickAction);
            Assert.Equal("Preview", service.Current.HoverAction);
            Assert.Equal("C", service.Current.TemperatureUnit);
            Assert.Equal(1, service.Current.FocusDefaultMinutes);
            Assert.Equal(60, service.Current.FocusBreakMinutes);
            Assert.Equal(180, service.Current.FocusCustomMinutes);
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
    public async Task LegacyRemovedScrollWheelSettingIsIgnoredGracefully()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PulseTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var settingsPath = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(settingsPath, """
            {
              "TopOffset": 18,
              "ScrollWheelAction": "Volume",
              "Theme": "Dark"
            }
            """);

            using var service = new SettingsService(directory);
            await service.LoadAsync();

            Assert.Equal(18, service.Current.TopOffset);
            Assert.Equal("Dark", service.Current.Theme);
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
