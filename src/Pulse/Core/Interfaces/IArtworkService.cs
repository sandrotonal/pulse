using Windows.Storage.Streams;

namespace Pulse.Core.Interfaces;

public interface IArtworkService
{
    Task<byte[]?> GetArtworkAsync(IRandomAccessStreamReference? thumbnail, string cacheKey);
}
