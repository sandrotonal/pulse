using System.Security.Cryptography;
using System.Text;
using Pulse.Core.Interfaces;
using Windows.Storage.Streams;

namespace Pulse.Services.Artwork;

public sealed class ArtworkCacheService : IArtworkService
{
    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ArtworkCacheService(string? cacheDirectory = null)
    {
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pulse", "Cache", "Artwork");
    }

    public async Task<byte[]?> GetArtworkAsync(IRandomAccessStreamReference? thumbnail, string cacheKey)
    {
        if (thumbnail is null)
        {
            return null;
        }

        var safeKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey)));
        var cachedPath = Path.Combine(_cacheDirectory, safeKey + ".img");
        await _gate.WaitAsync();
        try
        {
            if (File.Exists(cachedPath))
            {
                return await File.ReadAllBytesAsync(cachedPath);
            }

            using var input = await thumbnail.OpenReadAsync();
            using var output = new MemoryStream();
            await input.AsStreamForRead().CopyToAsync(output);
            var bytes = output.ToArray();
            if (bytes.Length == 0)
            {
                return null;
            }

            Directory.CreateDirectory(_cacheDirectory);
            var temporaryPath = cachedPath + ".tmp";
            await File.WriteAllBytesAsync(temporaryPath, bytes);
            File.Move(temporaryPath, cachedPath, overwrite: true);
            return bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or global::System.Runtime.InteropServices.COMException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
