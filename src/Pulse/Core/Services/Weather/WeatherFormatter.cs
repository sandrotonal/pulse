using System.Globalization;

namespace Pulse.Core.Services.Weather;

/// <summary>Culture-stable formatting for the compact idle strip.</summary>
public static class WeatherFormatter
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string FormatClock(DateTimeOffset now) => now.ToString("HH:mm", Turkish);

    public static string FormatDate(DateTimeOffset now) => now.ToString("ddd, d MMM", Turkish);

    public static string FormatTemperature(double celsius) =>
        $"{Math.Round(celsius, MidpointRounding.AwayFromZero).ToString("0", Turkish)}°";

    public static string FormatTooltip(
        string city,
        string description,
        double temperatureC,
        double apparentTemperatureC,
        bool isStale,
        DateTimeOffset retrievedAtUtc)
    {
        var cityText = string.IsNullOrWhiteSpace(city) ? "Konum" : city.Trim();
        var text = $"{cityText} · {description} · {FormatTemperature(temperatureC)}, hissedilen {FormatTemperature(apparentTemperatureC)}";
        if (isStale)
        {
            text += $" · Son güncelleme {retrievedAtUtc.ToLocalTime().ToString("HH:mm", Turkish)}";
        }

        return text;
    }
}
