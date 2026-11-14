using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Pulse.Core.Interfaces;
using Windows.Storage.Streams;

namespace Pulse.Services.Artwork;

public sealed class ArtworkCacheService : IArtworkService, IDisposable
{
    private const int MaxMemoryEntries = 24;
    private const int MaxDiskFiles = 150;

    private readonly string _cacheDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, byte[]> _memoryCache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lruOrder = [];
    private volatile bool _disposed;

    public ArtworkCacheService(string? cacheDirectory = null)
    {
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pulse", "Cache", "Artwork");
    }

    public async Task<byte[]?> GetArtworkAsync(IRandomAccessStreamReference? thumbnail, string cacheKey)
    {
        if (_disposed || thumbnail is null)
        {
            return null;
        }

        var safeKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey)));
        var cachedPath = Path.Combine(_cacheDirectory, safeKey + ".img");
        var gateEntered = false;
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            gateEntered = true;
            if (_disposed)
            {
                return null;
            }

            if (_memoryCache.TryGetValue(safeKey, out var cachedBytes))
            {
                TouchMemoryEntry(safeKey, cachedBytes);
                return cachedBytes;
            }

            if (File.Exists(cachedPath))
            {
                var diskBytes = await File.ReadAllBytesAsync(cachedPath).ConfigureAwait(false);
                if (diskBytes.Length > 0)
                {
                    TouchMemoryEntry(safeKey, diskBytes);
                    return diskBytes;
                }
            }

            using var input = await thumbnail.OpenReadAsync();
            using var readStream = input.AsStreamForRead();
            using var output = new MemoryStream();
            await readStream.CopyToAsync(output).ConfigureAwait(false);
            var bytes = output.ToArray();
            if (bytes.Length == 0)
            {
                return null;
            }

            TouchMemoryEntry(safeKey, bytes);

            Directory.CreateDirectory(_cacheDirectory);
            var temporaryPath = cachedPath + ".tmp";
            await File.WriteAllBytesAsync(temporaryPath, bytes).ConfigureAwait(false);
            File.Move(temporaryPath, cachedPath, overwrite: true);
            PruneDiskCacheIfNeeded();
            return bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or ObjectDisposedException or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            if (gateEntered)
            {
                try { _gate.Release(); } catch (ObjectDisposedException) { }
            }
        }
    }

    private void TouchMemoryEntry(string key, byte[] bytes)
    {
        if (_memoryCache.ContainsKey(key))
        {
            _lruOrder.Remove(key);
        }
        else if (_memoryCache.Count >= MaxMemoryEntries && _lruOrder.Last is { } oldest)
        {
            _lruOrder.RemoveLast();
            _memoryCache.Remove(oldest.Value);
        }

        _memoryCache[key] = bytes;
        _lruOrder.AddFirst(key);
    }

    private void PruneDiskCacheIfNeeded()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                return;
            }

            var files = new DirectoryInfo(_cacheDirectory).GetFiles("*.img");
            if (files.Length <= MaxDiskFiles)
            {
                return;
            }

            Array.Sort(files, static (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
            var removeCount = files.Length - MaxDiskFiles;
            for (var i = 0; i < removeCount; i++)
            {
                try { files[i].Delete(); } catch { }
            }
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _memoryCache.Clear();
        _lruOrder.Clear();
        _gate.Dispose();
    }
}
