using Pulse.Core.Services.Weather;

namespace Pulse.Tests;

public sealed class WeatherCodeMapperTests
{
    [Theory]
    [InlineData(0, true, "\uE706", "Açık", WeatherIconKind.ClearDay)]
    [InlineData(0, false, "\uE708", "Açık", WeatherIconKind.ClearNight)]
    [InlineData(1, true, "\uE706", "Az bulutlu", WeatherIconKind.PartlyCloudyDay)]
    [InlineData(1, false, "\uE708", "Az bulutlu", WeatherIconKind.PartlyCloudyNight)]
    [InlineData(2, true, "\uE753", "Parçalı bulutlu", WeatherIconKind.PartlyCloudyDay)]
    [InlineData(2, false, "\uE753", "Parçalı bulutlu", WeatherIconKind.PartlyCloudyNight)]
    [InlineData(3, true, "\uE753", "Kapalı", WeatherIconKind.Cloudy)]
    [InlineData(45, true, "\uE753", "Sisli", WeatherIconKind.Fog)]
    [InlineData(48, false, "\uE753", "Sisli", WeatherIconKind.Fog)]
    [InlineData(51, true, "\uE753", "Çisenti", WeatherIconKind.Rain)]
    [InlineData(57, true, "\uE753", "Dondurucu çisenti", WeatherIconKind.Rain)]
    [InlineData(61, true, "\uE753", "Yağmurlu", WeatherIconKind.Rain)]
    [InlineData(67, true, "\uE753", "Dondurucu yağmur", WeatherIconKind.Rain)]
    [InlineData(71, true, "\uE753", "Karlı", WeatherIconKind.Snow)]
    [InlineData(77, true, "\uE753", "Kar taneleri", WeatherIconKind.Snow)]
    [InlineData(80, true, "\uE753", "Sağanak yağış", WeatherIconKind.HeavyRain)]
    [InlineData(80, false, "\uE753", "Sağanak yağış", WeatherIconKind.HeavyRain)]
    [InlineData(85, true, "\uE753", "Kar sağanağı", WeatherIconKind.Snow)]
    [InlineData(95, true, "\uE753", "Fırtınalı", WeatherIconKind.Thunderstorm)]
    [InlineData(96, true, "\uE753", "Dolu ile fırtınalı", WeatherIconKind.Thunderstorm)]
    [InlineData(999, true, "\uE753", "Bilinmeyen", WeatherIconKind.Cloudy)]
    public void MapsWmoCodesToGlyphDescriptionAndIconKind(
        int code,
        bool isDay,
        string expectedGlyph,
        string expectedDescription,
        WeatherIconKind expectedIconKind)
    {
        var visual = WeatherCodeMapper.Map(code, isDay);

        Assert.Equal(expectedGlyph, visual.Glyph);
        Assert.Equal(expectedDescription, visual.Description);
        Assert.Equal(expectedIconKind, visual.IconKind);
    }

    [Fact]
    public void NeverEmitsAirplaneOrSystemControlGlyphsAcrossAllWmoCodes()
    {
        string[] forbidden = ["\uE707", "\uE709", "\uE70A", "\uE70B", "\uE70C", "\uE70D", "\uE70E", "\uE70F", "\uE710"];

        for (int code = 0; code <= 99; code++)
        {
            foreach (bool isDay in new[] { false, true })
            {
                var visual = WeatherCodeMapper.Map(code, isDay);
                Assert.DoesNotContain(visual.Glyph, forbidden);
                Assert.True(Enum.IsDefined(visual.IconKind));
                Assert.False(string.IsNullOrWhiteSpace(visual.Description));
            }
        }
    }
}
