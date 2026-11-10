using System.Globalization;
using Pulse.Core.Services.Weather;

namespace Pulse.Tests;

public sealed class WeatherFormatterTests
{
    [Fact]
    public void FormatsClockWithTurkishCulture()
    {
        var now = new DateTimeOffset(2026, 10, 22, 9, 5, 0, TimeSpan.FromHours(3));

        Assert.Equal("09:05", WeatherFormatter.FormatClock(now));
    }

    [Fact]
    public void FormatsDateWithTurkishCulture()
    {
        var now = new DateTimeOffset(2026, 10, 22, 9, 5, 0, TimeSpan.FromHours(3));

        Assert.Equal("Per, 22 Eki", WeatherFormatter.FormatDate(now));
    }

    [Theory]
    [InlineData(21.9, "22°")]
    [InlineData(-3.6, "-4°")]
    [InlineData(0, "0°")]
    [InlineData(22.5, "23°")]
    public void RoundsTemperaturesAwayFromZero(double celsius, string expected)
    {
        Assert.Equal(expected, WeatherFormatter.FormatTemperature(celsius));
    }

    [Fact]
    public void TooltipIncludesCityConditionAndFeelsLike()
    {
        var tooltip = WeatherFormatter.FormatTooltip(
            "İstanbul",
            "Parçalı bulutlu",
            21.9,
            23.1,
            isStale: false,
            retrievedAtUtc: DateTimeOffset.UtcNow);

        Assert.Equal("İstanbul · Parçalı bulutlu · 22°, hissedilen 23°", tooltip);
    }

    [Fact]
    public void StaleTooltipAddsLastUpdateTime()
    {
        var retrievedAt = new DateTimeOffset(2026, 10, 22, 11, 5, 0, TimeSpan.Zero);
        var localTime = retrievedAt.ToLocalTime().ToString("HH:mm", CultureInfo.GetCultureInfo("tr-TR"));

        var tooltip = WeatherFormatter.FormatTooltip(
            "İstanbul",
            "Kapalı",
            21.9,
            22.1,
            isStale: true,
            retrievedAtUtc: retrievedAt);

        Assert.Equal($"İstanbul · Kapalı · 22°, hissedilen 22° · Son güncelleme {localTime}", tooltip);
    }

    [Fact]
    public void BlankCityFallsBackToGenericLocation()
    {
        var tooltip = WeatherFormatter.FormatTooltip(
            " ",
            "Açık",
            10,
            9,
            isStale: false,
            retrievedAtUtc: DateTimeOffset.UtcNow);

        Assert.StartsWith("Konum · Açık", tooltip);
    }

    [Theory]
    [InlineData(9, 5, false, "9:05 AM")]
    [InlineData(14, 5, false, "2:05 PM")]
    [InlineData(0, 0, false, "12:00 AM")]
    [InlineData(14, 5, true, "14:05")]
    public void FormatsClockRespecting12And24HourPreferences(int hour, int minute, bool use24Hour, string expected)
    {
        var now = new DateTimeOffset(2026, 10, 22, hour, minute, 0, TimeSpan.FromHours(3));

        Assert.Equal(expected, WeatherFormatter.FormatClock(now, use24Hour));
    }

    [Theory]
    [InlineData(0.0, "F", "32°F")]
    [InlineData(21.9, "F", "71°F")]
    [InlineData(-3.6, "f", "26°F")]
    [InlineData(21.9, "C", "22°")]
    public void FormatsTemperaturesInCelsiusOrFahrenheit(double celsius, string unit, string expected)
    {
        Assert.Equal(expected, WeatherFormatter.FormatTemperature(celsius, unit));
    }

    [Fact]
    public void TooltipHonorsFahrenheitAnd12HourPreferencesWhenStale()
    {
        var retrievedAt = new DateTimeOffset(2026, 10, 22, 11, 5, 0, TimeSpan.Zero);
        var local12h = WeatherFormatter.FormatClock(retrievedAt.ToLocalTime(), use24HourClock: false);

        var tooltip = WeatherFormatter.FormatTooltip(
            "İstanbul",
            "Açık",
            20.0,
            25.0,
            isStale: true,
            retrievedAtUtc: retrievedAt,
            temperatureUnit: "F",
            use24HourClock: false);

        Assert.Equal($"İstanbul · Açık · 68°F, hissedilen 77°F · Son güncelleme {local12h}", tooltip);
    }
}
