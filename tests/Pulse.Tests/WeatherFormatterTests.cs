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
}
