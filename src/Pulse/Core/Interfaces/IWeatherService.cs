using Pulse.Core.Models;

namespace Pulse.Core.Interfaces;

public interface IWeatherService
{
    Task<WeatherResult> GetAsync(GeoLocation location, CancellationToken cancellationToken = default);
}
