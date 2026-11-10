using System.Globalization;

namespace Pulse.Core.Services.Weather;

/// <summary>Culture-stable formatting for the compact idle strip.</summary>
public static class WeatherFormatter
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string FormatClock(DateTimeOffset now, bool use24HourClock = true) =>
        use24HourClock
            ? now.ToString("HH:mm", Turkish)
            : now.ToString("h:mm tt", CultureInfo.InvariantCulture);

    public static string FormatDate(DateTimeOffset now) => now.ToString("ddd, d MMM", Turkish);

    public static string FormatTemperature(double celsius, string temperatureUnit = "C")
    {
        var isFahrenheit = string.Equals(temperatureUnit, "F", StringComparison.OrdinalIgnoreCase);
        var value = isFahrenheit ? (celsius * 9.0 / 5.0) + 32.0 : celsius;
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", Turkish);
        return isFahrenheit ? $"{rounded}°F" : $"{rounded}°";
    }

    public static string FormatTooltip(
        string city,
        string description,
        double temperatureC,
        double apparentTemperatureC,
        bool isStale,
        DateTimeOffset retrievedAtUtc,
        string temperatureUnit = "C",
        bool use24HourClock = true)
    {
        var cityText = string.IsNullOrWhiteSpace(city) ? "Konum" : city.Trim();
        var text = $"{cityText} · {description} · {FormatTemperature(temperatureC, temperatureUnit)}, hissedilen {FormatTemperature(apparentTemperatureC, temperatureUnit)}";
        if (isStale)
        {
            text += $" · Son güncelleme {FormatClock(retrievedAtUtc.ToLocalTime(), use24HourClock)}";
        }

        return text;
    }
}
