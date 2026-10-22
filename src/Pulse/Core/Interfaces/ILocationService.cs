using Pulse.Core.Models;

namespace Pulse.Core.Interfaces;

public interface ILocationService
{
    /// <summary>
    /// Resolves the configured location. Automatic mode uses an approximate IP lookup;
    /// manual mode uses the stored coordinates, or geocodes the supplied city.
    /// </summary>
    Task<GeoLocation?> ResolveAsync(
        bool useManual,
        string? manualCity,
        double? manualLatitude,
        double? manualLongitude,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves a city name to coordinates for the settings screen.</summary>
    Task<GeoLocation?> GeocodeAsync(string city, CancellationToken cancellationToken = default);
}
