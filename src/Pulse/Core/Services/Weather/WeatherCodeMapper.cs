namespace Pulse.Core.Services.Weather;

public enum WeatherIconKind
{
    ClearDay,
    ClearNight,
    PartlyCloudyDay,
    PartlyCloudyNight,
    Cloudy,
    Fog,
    Rain,
    HeavyRain,
    Snow,
    Thunderstorm
}

public readonly record struct WeatherVisual(
    string Glyph,
    string Description,
    WeatherIconKind IconKind = WeatherIconKind.Cloudy);

/// <summary>Maps WMO weather codes to semantic icon kinds, safe fallback glyphs, and Turkish labels.</summary>
public static class WeatherCodeMapper
{
    private const string SunGlyph = "\uE706";
    private const string MoonGlyph = "\uE708";
    private const string CloudGlyph = "\uE753";

    public static WeatherVisual Map(int weatherCode, bool isDay) => weatherCode switch
    {
        0 => isDay
            ? new(SunGlyph, "Açık", WeatherIconKind.ClearDay)
            : new(MoonGlyph, "Açık", WeatherIconKind.ClearNight),
        1 => isDay
            ? new(SunGlyph, "Az bulutlu", WeatherIconKind.PartlyCloudyDay)
            : new(MoonGlyph, "Az bulutlu", WeatherIconKind.PartlyCloudyNight),
        2 => isDay
            ? new(CloudGlyph, "Parçalı bulutlu", WeatherIconKind.PartlyCloudyDay)
            : new(CloudGlyph, "Parçalı bulutlu", WeatherIconKind.PartlyCloudyNight),
        3 => new(CloudGlyph, "Kapalı", WeatherIconKind.Cloudy),
        45 or 48 => new(CloudGlyph, "Sisli", WeatherIconKind.Fog),
        51 or 53 or 55 => new(CloudGlyph, "Çisenti", WeatherIconKind.Rain),
        56 or 57 => new(CloudGlyph, "Dondurucu çisenti", WeatherIconKind.Rain),
        61 or 63 or 65 => new(CloudGlyph, "Yağmurlu", WeatherIconKind.Rain),
        66 or 67 => new(CloudGlyph, "Dondurucu yağmur", WeatherIconKind.Rain),
        71 or 73 or 75 => new(CloudGlyph, "Karlı", WeatherIconKind.Snow),
        77 => new(CloudGlyph, "Kar taneleri", WeatherIconKind.Snow),
        80 or 81 or 82 => new(CloudGlyph, "Sağanak yağış", WeatherIconKind.HeavyRain),
        85 or 86 => new(CloudGlyph, "Kar sağanağı", WeatherIconKind.Snow),
        95 => new(CloudGlyph, "Fırtınalı", WeatherIconKind.Thunderstorm),
        96 or 99 => new(CloudGlyph, "Dolu ile fırtınalı", WeatherIconKind.Thunderstorm),
        _ => new(CloudGlyph, "Bilinmeyen", WeatherIconKind.Cloudy)
    };
}
