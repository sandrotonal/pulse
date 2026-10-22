using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pulse.Core.Interfaces;
using Pulse.Core.Models;

namespace Pulse.Core.Services.Weather;

internal sealed record LocationCacheEntry(
    double Latitude,
    double Longitude,
    string City,
    string CountryCode,
    string TimeZoneId,
    DateTimeOffset RetrievedAtUtc);

[JsonSerializable(typeof(LocationCacheEntry))]
internal sealed partial class LocationCacheJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Approximate IP geolocation with a 24-hour disk cache, plus Open-Meteo geocoding
/// for the manual city override. No Windows location permission is required.
/// </summary>
public sealed class LocationService : ILocationService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private const string CacheFileName = "location-cache.json";

    private readonly HttpClient _httpClient;
    private readonly string _cachePath;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private readonly ConcurrentDictionary<string, GeoLocation> _geocodeCache = new(StringComparer.OrdinalIgnoreCase);

    public LocationService(
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

    public async Task<GeoLocation?> ResolveAsync(
        bool useManual,
        string? manualCity,
        double? manualLatitude,
        double? manualLongitude,
        CancellationToken cancellationToken = default)
    {
        if (useManual)
        {
            var manual = Create(manualLatitude, manualLongitude, manualCity, string.Empty, string.Empty, true);
            if (manual is not null)
            {
                return manual;
            }

            if (!string.IsNullOrWhiteSpace(manualCity))
            {
                var geocoded = await GeocodeAsync(manualCity, cancellationToken).ConfigureAwait(false);
                return geocoded is null ? null : geocoded with { IsManual = true };
            }

            return null;
        }

        var now = _timeProvider.GetUtcNow();
        var cached = await ReadCacheAsync(cancellationToken).ConfigureAwait(false);
        if (cached is not null && now - cached.RetrievedAtUtc <= CacheTtl)
        {
            return ToGeoLocation(cached);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            var fetched = await FetchIpLocationAsync(timeout.Token).ConfigureAwait(false);
            if (fetched is not null)
            {
                await WriteCacheAsync(new LocationCacheEntry(
                    fetched.Latitude,
                    fetched.Longitude,
                    fetched.City,
                    fetched.CountryCode,
                    fetched.TimeZoneId,
                    now), cancellationToken).ConfigureAwait(false);

                return fetched;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Request timeout: fall back to the last known location.
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException)
        {
            // Network or malformed payload: fall back to the last known location.
        }

        return cached is null ? null : ToGeoLocation(cached);
    }

    public async Task<GeoLocation?> GeocodeAsync(string city, CancellationToken cancellationToken = default)
    {
        var key = city?.Trim() ?? string.Empty;
        if (key.Length == 0)
        {
            return null;
        }

        if (_geocodeCache.TryGetValue(key, out var memoryCached))
        {
            return memoryCached;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            var url = "https://geocoding-api.open-meteo.com/v1/search" +
                      $"?name={Uri.EscapeDataString(key)}&count=1&language=tr&format=json";

            using var response = await _httpClient
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token)
                .ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array ||
                results.GetArrayLength() == 0)
            {
                return null;
            }

            var first = results[0];
            if (!TryGetDouble(first, "latitude", out var latitude) ||
                !TryGetDouble(first, "longitude", out var longitude))
            {
                return null;
            }

            var name = first.TryGetProperty("name", out var nameElement) &&
                       nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString() ?? key
                : key;
            var countryCode = first.TryGetProperty("country_code", out var countryElement) &&
                              countryElement.ValueKind == JsonValueKind.String
                ? countryElement.GetString() ?? string.Empty
                : string.Empty;
            var timeZone = first.TryGetProperty("timezone", out var timezoneElement) &&
                           timezoneElement.ValueKind == JsonValueKind.String
                ? timezoneElement.GetString() ?? string.Empty
                : string.Empty;

            var location = Create(latitude, longitude, name, countryCode, timeZone, true);
            if (location is not null)
            {
                _geocodeCache[key] = location;
            }

            return location;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException)
        {
            return null;
        }
    }

    private async Task<GeoLocation?> FetchIpLocationAsync(CancellationToken cancellationToken)
    {
        const string url = "https://ipwho.is/?fields=success,message,city,country_code,latitude,longitude,timezone";
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
        if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
        {
            return null;
        }

        if (!TryGetDouble(root, "latitude", out var latitude) ||
            !TryGetDouble(root, "longitude", out var longitude))
        {
            return null;
        }

        var city = root.TryGetProperty("city", out var cityElement) && cityElement.ValueKind == JsonValueKind.String
            ? cityElement.GetString() ?? string.Empty
            : string.Empty;
        var countryCode = root.TryGetProperty("country_code", out var countryElement) &&
                          countryElement.ValueKind == JsonValueKind.String
            ? countryElement.GetString() ?? string.Empty
            : string.Empty;

        var timeZone = string.Empty;
        if (root.TryGetProperty("timezone", out var timezoneElement))
        {
            if (timezoneElement.ValueKind == JsonValueKind.String)
            {
                timeZone = timezoneElement.GetString() ?? string.Empty;
            }
            else if (timezoneElement.ValueKind == JsonValueKind.Object &&
                     timezoneElement.TryGetProperty("id", out var idElement) &&
                     idElement.ValueKind == JsonValueKind.String)
            {
                timeZone = idElement.GetString() ?? string.Empty;
            }
        }

        return Create(latitude, longitude, city, countryCode, timeZone, false);
    }

    private async Task<LocationCacheEntry?> ReadCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_cachePath))
            {
                return null;
            }

            await using var stream = File.OpenRead(_cachePath);
            return await JsonSerializer
                .DeserializeAsync(stream, LocationCacheJsonContext.Default.LocationCacheEntry, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task WriteCacheAsync(LocationCacheEntry entry, CancellationToken cancellationToken)
    {
        await _cacheGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            var temporaryPath = _cachePath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer
                    .SerializeAsync(stream, entry, LocationCacheJsonContext.Default.LocationCacheEntry, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, _cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Location is best-effort; a cache write failure must never break the island.
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    private static GeoLocation ToGeoLocation(LocationCacheEntry cache) =>
        new(
            cache.Latitude,
            cache.Longitude,
            string.IsNullOrWhiteSpace(cache.City) ? "Konum" : cache.City,
            cache.CountryCode,
            cache.TimeZoneId,
            false);

    private static GeoLocation? Create(
        double? latitude,
        double? longitude,
        string? city,
        string countryCode,
        string timeZoneId,
        bool isManual)
    {
        if (latitude is not { } lat ||
            longitude is not { } lon ||
            !double.IsFinite(lat) ||
            !double.IsFinite(lon) ||
            lat is < -90 or > 90 ||
            lon is < -180 or > 180)
        {
            return null;
        }

        var cityName = string.IsNullOrWhiteSpace(city) ? "Konum" : city.Trim();
        return new GeoLocation(lat, lon, cityName, countryCode, timeZoneId, isManual);
    }

    private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetDouble(out value);
    }
}
