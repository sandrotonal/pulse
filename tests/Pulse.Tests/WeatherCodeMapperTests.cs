using Pulse.Core.Services.Weather;

namespace Pulse.Tests;

public sealed class WeatherCodeMapperTests
{
    [Theory]
    [InlineData(0, true, "\uE706", "Açık")]
    [InlineData(0, false, "\uE70A", "Açık")]
    [InlineData(1, true, "\uE708", "Az bulutlu")]
    [InlineData(2, false, "\uE709", "Parçalı bulutlu")]
    [InlineData(3, true, "\uE707", "Kapalı")]
    [InlineData(45, true, "\uE710", "Sisli")]
    [InlineData(48, false, "\uE710", "Sisli")]
    [InlineData(51, true, "\uE70D", "Çisenti")]
    [InlineData(57, true, "\uE70D", "Dondurucu çisenti")]
    [InlineData(61, true, "\uE70D", "Yağmurlu")]
    [InlineData(67, true, "\uE70D", "Dondurucu yağmur")]
    [InlineData(71, true, "\uE70E", "Karlı")]
    [InlineData(77, true, "\uE70E", "Kar taneleri")]
    [InlineData(80, true, "\uE70B", "Sağanak yağış")]
    [InlineData(80, false, "\uE70C", "Sağanak yağış")]
    [InlineData(85, true, "\uE70E", "Kar sağanağı")]
    [InlineData(95, true, "\uE70F", "Fırtınalı")]
    [InlineData(96, true, "\uE70F", "Dolu ile fırtınalı")]
    [InlineData(999, true, "\uE707", "Bilinmeyen")]
    public void MapsWmoCodesToGlyphAndDescription(
        int code,
        bool isDay,
        string expectedGlyph,
        string expectedDescription)
    {
        var visual = WeatherCodeMapper.Map(code, isDay);

        Assert.Equal(expectedGlyph, visual.Glyph);
        Assert.Equal(expectedDescription, visual.Description);
    }
}
