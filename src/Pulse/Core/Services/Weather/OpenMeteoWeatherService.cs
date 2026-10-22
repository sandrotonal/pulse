using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pulse.Core.Interfaces;
using Pulse.Core.Models;

namespace Pulse.Core.Services.Weather;

internal sealed record WeatherCacheEntry(
    double Latitude,
    double Longitude,
    double TemperatureC,
    double ApparentTemperatureC,
    int WeatherCode,
    bool IsDay,
    string TimeZoneId,
    DateTimeOffset ObservedAt,
    DateTimeOffset RetrievedAtUtc);

[JsonSerializable(typeof(WeatherCacheEntry))]
internal sealed partial class WeatherCacheJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Open-Meteo client with a short-lived disk cache. No API key is required.
/// On a transient failure the last cached snapshot is returned as stale for up to six hours.
/// </summary>
public sealed class OpenMeteoWeatherService : IWeatherService
{
    private static readonly TimeSpan FreshTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StaleTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private const string CacheFileName = "weather-cache.json";

    private readonly HttpClient _httpClient;
    private readonly string _cachePath;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _cacheGate = new(1, 1);

    public OpenMeteoWeatherService(
        HttpClient httpClient,
        string? cacheDirectory = null,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        var directory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pulse");
        _cachePath = Path.Combine(directory, CacheFileName);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<WeatherResult> GetAsync(GeoLocation location, CancellationToken cancellationToken = default)
    {
        if (!IsValid(location))
        {
            return WeatherResult.Empty;
        }

        var now = _timeProvider.GetUtcNow();
        var cached = await ReadCacheAsync(cancellationToken).ConfigureAwait(false);
        var matches = cached is not null && Matches(cached, location);

        if (matches && now - cached!.RetrievedAtUtc <= FreshTtl)
        {
            return new WeatherResult(ToSnapshot(cached), false);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            var snapshot = await FetchAsync(location, now, timeout.Token).ConfigureAwait(false);
            if (snapshot is not null)
            {
                await WriteCacheAsync(new WeatherCacheEntry(
                    location.Latitude,
                    location.Longitude,
                    snapshot.TemperatureC,
                    snapshot.ApparentTemperatureC,
                    snapshot.WeatherCode,
                    snapshot.IsDay,
                    snapshot.TimeZoneId,
                    snapshot.ObservedAt,
                    snapshot.RetrievedAtUtc), cancellationToken).ConfigureAwait(false);

                return new WeatherResult(snapshot, false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Request timeout: fall through to the cached snapshot.
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException)
        {
            // Network or malformed payload: fall through to the cached snapshot.
        }

        if (matches && now - cached!.RetrievedAtUtc <= StaleTtl)
        {
            return new WeatherResult(ToSnapshot(cached), true);
        }

        return WeatherResult.Empty;
    }

    private async Task<WeatherSnapshot?> FetchAsync(
        GeoLocation location,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var latitude = location.Latitude.ToString("0.0000", CultureInfo.InvariantCulture);
        var longitude = location.Longitude.ToString("0.0000", CultureInfo.InvariantCulture);
        var url = "https://api.open-meteo.com/v1/forecast" +
                  $"?latitude={latitude}&longitude={longitude}" +
                  "&current=temperature_2m,apparent_temperature,is_day,weather_code" +
                  "&timezone=auto";

        using var response = await _httpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var root = document.RootElement;
        if (!root.TryGetProperty("current", out var current) || current.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!TryGetDouble(current, "temperature_2m", out var temperature) ||
            !TryGetInt(current, "weather_code", out var weatherCode))
        {
            return null;
        }

        var apparent = TryGetDouble(current, "apparent_temperature", out var apparentValue)
            ? apparentValue
            : temperature;
        var isDay = TryGetInt(current, "is_day", out var day) && day == 1;
        var timeZone = root.TryGetProperty("timezone", out var timezone) && timezone.ValueKind == JsonValueKind.String
            ? timezone.GetString() ?? string.Empty
            : string.Empty;

        return new WeatherSnapshot(
            temperature,
            apparent,
            weatherCode,
            isDay,
            timeZone,
            now,
            now);
    }

    private async Task<WeatherCacheEntry?> ReadCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_cachePath))
            {
                return null;
            }

            await using var stream = File.OpenRead(_cachePath);
            return await JsonSerializer
                .DeserializeAsync(stream, WeatherCacheJsonContext.Default.WeatherCacheEntry, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task WriteCacheAsync(WeatherCacheEntry entry, CancellationToken cancellationToken)
    {
        await _cacheGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            var temporaryPath = _cachePath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer
                    .SerializeAsync(stream, entry, WeatherCacheJsonContext.Default.WeatherCacheEntry, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, _cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Weather is best-effort; a cache write failure must never break the island.
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    private static bool Matches(WeatherCacheEntry cache, GeoLocation location) =>
        Math.Abs(cache.Latitude - location.Latitude) < 0.02 &&
        Math.Abs(cache.Longitude - location.Longitude) < 0.02;

    private static WeatherSnapshot ToSnapshot(WeatherCacheEntry cache) =>
        new(
            cache.TemperatureC,
            cache.ApparentTemperatureC,
            cache.WeatherCode,
            cache.IsDay,
            cache.TimeZoneId,
            cache.ObservedAt,
            cache.RetrievedAtUtc);

    private static bool IsValid(GeoLocation location) =>
        double.IsFinite(location.Latitude) &&
        double.IsFinite(location.Longitude) &&
        location.Latitude is >= -90 and <= 90 &&
        location.Longitude is >= -180 and <= 180;

    private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetDouble(out value);
    }

    private static bool TryGetInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out value);
    }
}
