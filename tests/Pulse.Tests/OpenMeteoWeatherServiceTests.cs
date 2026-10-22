using System.Globalization;
using System.Net;
using Pulse.Core.Models;
using Pulse.Core.Services.Weather;

namespace Pulse.Tests;

public sealed class OpenMeteoWeatherServiceTests
{
    private static readonly GeoLocation Istanbul = new(41.01, 28.95, "Istanbul", "TR", "Europe/Istanbul");

    [Fact]
    public async Task FetchesAndParsesCurrentConditions()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(WeatherJson(21.9, 3, 1)));
        using var http = new HttpClient(handler);
        var service = CreateService(http, out var directory);

        try
        {
            var result = await service.GetAsync(Istanbul);

            Assert.NotNull(result.Snapshot);
            Assert.False(result.IsStale);
            Assert.Equal(21.9, result.Snapshot!.TemperatureC);
            Assert.Equal(3, result.Snapshot.WeatherCode);
            Assert.True(result.Snapshot.IsDay);
            Assert.Equal("Europe/Istanbul", result.Snapshot.TimeZoneId);
            Assert.Equal(1, handler.CallCount);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task FreshCacheSkipsSecondHttpCall()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(WeatherJson(21.9, 3, 1)));
        using var http = new HttpClient(handler);
        var service = CreateService(http, out var directory);

        try
        {
            await service.GetAsync(Istanbul);
            var second = await service.GetAsync(Istanbul);

            Assert.NotNull(second.Snapshot);
            Assert.False(second.IsStale);
            Assert.Equal(1, handler.CallCount);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task RefetchesAfterFreshTtlExpires()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(WeatherJson(21.9, 3, 1)));
        using var http = new HttpClient(handler);
        var time = new MutableTimeProvider();
        var service = CreateService(http, out var directory, time);

        try
        {
            await service.GetAsync(Istanbul);
            time.UtcNow += TimeSpan.FromMinutes(16);
            await service.GetAsync(Istanbul);

            Assert.Equal(2, handler.CallCount);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task ReturnsStaleCacheWhenProviderFails()
    {
        var fail = false;
        var handler = new StubHttpMessageHandler(_ => fail
            ? StubHttpMessageHandler.Json("{}", HttpStatusCode.ServiceUnavailable)
            : StubHttpMessageHandler.Json(WeatherJson(21.9, 3, 1)));
        using var http = new HttpClient(handler);
        var time = new MutableTimeProvider();
        var service = CreateService(http, out var directory, time);

        try
        {
            await service.GetAsync(Istanbul);
            fail = true;
            time.UtcNow += TimeSpan.FromMinutes(16);

            var result = await service.GetAsync(Istanbul);

            Assert.NotNull(result.Snapshot);
            Assert.True(result.IsStale);
            Assert.Equal(21.9, result.Snapshot!.TemperatureC);
            Assert.Equal(2, handler.CallCount);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task EmptyResultWithoutCacheOnFailure()
    {
        var handler = new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.Json("{}", HttpStatusCode.ServiceUnavailable));
        using var http = new HttpClient(handler);
        var service = CreateService(http, out var directory);

        try
        {
            var result = await service.GetAsync(Istanbul);

            Assert.Null(result.Snapshot);
            Assert.False(result.IsStale);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task MalformedJsonReturnsEmpty()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("{"));
        using var http = new HttpClient(handler);
        var service = CreateService(http, out var directory);

        try
        {
            var result = await service.GetAsync(Istanbul);

            Assert.Null(result.Snapshot);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task DifferentCoordinatesDoNotReuseCache()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(WeatherJson(21.9, 3, 1)));
        using var http = new HttpClient(handler);
        var service = CreateService(http, out var directory);

        try
        {
            await service.GetAsync(Istanbul);
            await service.GetAsync(new GeoLocation(39.93, 32.86, "Ankara", "TR", "Europe/Istanbul"));

            Assert.Equal(2, handler.CallCount);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task CacheSurvivesServiceRestart()
    {
        var time = new MutableTimeProvider();
        var directory = NewDirectory();

        try
        {
            var firstHandler = new StubHttpMessageHandler(_ =>
                StubHttpMessageHandler.Json(WeatherJson(21.9, 3, 1)));
            using (var firstHttp = new HttpClient(firstHandler))
            {
                var first = new OpenMeteoWeatherService(firstHttp, directory, time);
                await first.GetAsync(Istanbul);
            }

            var secondHandler = new StubHttpMessageHandler(_ =>
                throw new InvalidOperationException("Fresh cache should avoid HTTP."));
            using var secondHttp = new HttpClient(secondHandler);
            var second = new OpenMeteoWeatherService(secondHttp, directory, time);
            var result = await second.GetAsync(Istanbul);

            Assert.NotNull(result.Snapshot);
            Assert.Equal(0, secondHandler.CallCount);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    private static OpenMeteoWeatherService CreateService(
        HttpClient httpClient,
        out string directory,
        TimeProvider? timeProvider = null)
    {
        directory = NewDirectory();
        return new OpenMeteoWeatherService(httpClient, directory, timeProvider);
    }

    private static string NewDirectory() =>
        Path.Combine(Path.GetTempPath(), "PulseTests", Guid.NewGuid().ToString("N"));

    private static void Cleanup(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string WeatherJson(double temperature, int code, int isDay) =>
        $$"""
        {
          "latitude": 41.01,
          "longitude": 28.95,
          "timezone": "Europe/Istanbul",
          "current": {
            "time": "2026-10-22T09:00",
            "temperature_2m": {{temperature.ToString(CultureInfo.InvariantCulture)}},
            "apparent_temperature": {{(temperature + 0.6).ToString(CultureInfo.InvariantCulture)}},
            "is_day": {{isDay}},
            "weather_code": {{code}}
          }
        }
        """;
}
