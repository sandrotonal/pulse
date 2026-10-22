namespace Pulse.Core.Models;

/// <summary>Approximate location used for the idle weather strip.</summary>
public sealed record GeoLocation(
    double Latitude,
    double Longitude,
    string City,
    string CountryCode,
    string TimeZoneId,
    bool IsManual = false);

/// <summary>Current conditions returned by the weather provider.</summary>
public sealed record WeatherSnapshot(
    double TemperatureC,
    double ApparentTemperatureC,
    int WeatherCode,
    bool IsDay,
    string TimeZoneId,
    DateTimeOffset ObservedAt,
    DateTimeOffset RetrievedAtUtc);

/// <summary>
/// A weather result may carry stale cached data when the provider is unreachable.
/// An empty snapshot means the island should hide the weather segment.
/// </summary>
public sealed record WeatherResult(WeatherSnapshot? Snapshot, bool IsStale)
{
    public static WeatherResult Empty { get; } = new(null, false);
}
