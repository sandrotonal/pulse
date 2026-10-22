namespace Pulse.Core.Services.Weather;

public readonly record struct WeatherVisual(string Glyph, string Description);

/// <summary>Maps WMO weather codes to Segoe Fluent Icons glyphs and Turkish labels.</summary>
public static class WeatherCodeMapper
{
    public static WeatherVisual Map(int weatherCode, bool isDay) => weatherCode switch
    {
        0 => new(isDay ? "\uE706" : "\uE70A", "Açık"),
        1 => new(isDay ? "\uE708" : "\uE709", "Az bulutlu"),
        2 => new(isDay ? "\uE708" : "\uE709", "Parçalı bulutlu"),
        3 => new("\uE707", "Kapalı"),
        45 or 48 => new("\uE710", "Sisli"),
        51 or 53 or 55 => new("\uE70D", "Çisenti"),
        56 or 57 => new("\uE70D", "Dondurucu çisenti"),
        61 or 63 or 65 => new("\uE70D", "Yağmurlu"),
        66 or 67 => new("\uE70D", "Dondurucu yağmur"),
        71 or 73 or 75 => new("\uE70E", "Karlı"),
        77 => new("\uE70E", "Kar taneleri"),
        80 or 81 or 82 => new(isDay ? "\uE70B" : "\uE70C", "Sağanak yağış"),
        85 or 86 => new("\uE70E", "Kar sağanağı"),
        95 => new("\uE70F", "Fırtınalı"),
        96 or 99 => new("\uE70F", "Dolu ile fırtınalı"),
        _ => new("\uE707", "Bilinmeyen")
    };
}
