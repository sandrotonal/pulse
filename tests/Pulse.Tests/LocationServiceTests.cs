using System.Net;
using Pulse.Core.Services.Weather;

namespace Pulse.Tests;

public sealed class LocationServiceTests
{
    private const string IpJson =
        """{"success":true,"country_code":"TR","city":"Istanbul","latitude":41.0138366,"longitude":28.9496622,"timezone":{"id":"Europe/Istanbul"}}""";

    private const string GeocodeJson =
        """{"results":[{"name":"İstanbul","latitude":41.01384,"longitude":28.94966,"country_code":"TR","timezone":"Europe/Istanbul"}]}""";

    [Fact]
    public async Task ManualCoordinatesDoNotUseHttp()
    {
        var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Manual coordinates should not call HTTP."));
        using var http = new HttpClient(handler);
        var service = CreateService(http, out var directory);

        try
        {
            var location = await service.ResolveAsync(true, "Istanbul", 41.01, 28.95);

            Assert.NotNull(location);
            Assert.True(location!.IsManual);
            Assert.Equal("Istanbul", location.City);
            Assert.Equal(41.01, location.Latitude);
            Assert.Equal(28.95, location.Longitude);
            Assert.Equal(0, handler.CallCount);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task AutomaticLocationUsesIpAndCachesFor24Hours()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(IpJson));
        using var http = new HttpClient(handler);
        var time = new MutableTimeProvider();
        var service = CreateService(http, out var directory, time);

        try
        {
            var first = await service.ResolveAsync(false, null, null, null);
            var second = await service.ResolveAsync(false, null, null, null);

            Assert.NotNull(first);
            Assert.Equal("Istanbul", first!.City);
            Assert.Equal("Europe/Istanbul", first.TimeZoneId);
            Assert.NotNull(second);
            Assert.Equal(1, handler.CallCount);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task AutomaticLocationRefreshesAfterCacheExpires()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(IpJson));
        using var http = new HttpClient(handler);
        var time = new MutableTimeProvider();
        var service = CreateService(http, out var directory, time);

        try
        {
            await service.ResolveAsync(false, null, null, null);
            time.UtcNow += TimeSpan.FromHours(25);
            await service.ResolveAsync(false, null, null, null);

            Assert.Equal(2, handler.CallCount);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task AutomaticLocationFallsBackToCacheOnFailure()
    {
        var fail = false;
        var handler = new StubHttpMessageHandler(_ => fail
            ? StubHttpMessageHandler.Json("{}", HttpStatusCode.ServiceUnavailable)
            : StubHttpMessageHandler.Json(IpJson));
        using var http = new HttpClient(handler);
        var time = new MutableTimeProvider();
        var service = CreateService(http, out var directory, time);

        try
        {
            await service.ResolveAsync(false, null, null, null);
            fail = true;
            time.UtcNow += TimeSpan.FromHours(25);

            var location = await service.ResolveAsync(false, null, null, null);

            Assert.NotNull(location);
            Assert.Equal("Istanbul", location!.City);
            Assert.Equal(2, handler.CallCount);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task GeocodeResolvesCityAndCachesInMemory()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(GeocodeJson));
        using var http = new HttpClient(handler);
        var service = CreateService(http, out var directory);

        try
        {
            var first = await service.GeocodeAsync("Istanbul");
            var second = await service.GeocodeAsync("Istanbul");

            Assert.NotNull(first);
            Assert.Equal("İstanbul", first!.City);
            Assert.Equal(41.01384, first.Latitude);
            Assert.NotNull(second);
            Assert.Equal(1, handler.CallCount);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task GeocodeReturnsNullWhenCityIsNotFound()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("""{"results":[]}"""));
        using var http = new HttpClient(handler);
        var service = CreateService(http, out var directory);

        try
        {
            var location = await service.GeocodeAsync("PulseOlmayanSehir");

            Assert.Null(location);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public async Task GeocodeReturnsNullOnProviderFailure()
    {
        var handler = new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.Json("{}", HttpStatusCode.ServiceUnavailable));
        using var http = new HttpClient(handler);
        var service = CreateService(http, out var directory);

        try
        {
            var location = await service.GeocodeAsync("Istanbul");

            Assert.Null(location);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    private static LocationService CreateService(
        HttpClient httpClient,
        out string directory,
        TimeProvider? timeProvider = null)
    {
        directory = Path.Combine(Path.GetTempPath(), "PulseTests", Guid.NewGuid().ToString("N"));
        return new LocationService(httpClient, directory, timeProvider);
    }

    private static void Cleanup(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
