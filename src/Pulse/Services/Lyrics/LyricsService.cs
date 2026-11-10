using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Pulse.Services.Lyrics;

public sealed class LyricsService
{
    private const int MaxCacheEntries = 100;
    private static readonly Uri BaseEndpoint = new("https://lrclib.net/");

    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, LyricsResult?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _cacheOrder = [];

    public LyricsService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    }

    public async Task<LyricsResult?> FindLyricsAsync(
        string title,
        string artist,
        string album,
        TimeSpan? duration,
        CancellationToken cancellationToken = default)
    {
        title = Clean(title);
        artist = Clean(artist);
        album = Clean(album);
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
        {
            return null;
        }

        var cacheKey = string.Join("|", title, artist, album, duration?.TotalSeconds.ToString("0", CultureInfo.InvariantCulture));
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var transientFailure = false;
        try
        {
            var baseQuery = $"api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
            var attempts = new List<string>();

            // lrclib's strict endpoint 404s when album/duration drift between sources,
            // so progressively loosen the query until something matches.
            if (!string.IsNullOrWhiteSpace(album) && duration is { TotalSeconds: > 0 })
            {
                attempts.Add($"{baseQuery}&album_name={Uri.EscapeDataString(album)}&duration={(int)Math.Round(duration.Value.TotalSeconds)}");
            }

            if (!string.IsNullOrWhiteSpace(album))
            {
                attempts.Add($"{baseQuery}&album_name={Uri.EscapeDataString(album)}");
            }

            if (duration is { TotalSeconds: > 0 })
            {
                attempts.Add($"{baseQuery}&duration={(int)Math.Round(duration.Value.TotalSeconds)}");
            }

            attempts.Add(baseQuery);

            foreach (var query in attempts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseEndpoint, query));
                    if (request.Headers.UserAgent.Count == 0)
                    {
                        request.Headers.UserAgent.ParseAdd("Pulse/1.0 (+https://github.com/pulse-dynamic-island)");
                    }

                    using var httpResponse = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests || (int)httpResponse.StatusCode >= 500)
                    {
                        transientFailure = true;
                        break;
                    }

                    if (!httpResponse.IsSuccessStatusCode)
                    {
                        continue;
                    }

                    var response = await httpResponse.Content.ReadFromJsonAsync(
                        LyricsJsonContext.Default.LrcLibResponse,
                        cancellationToken).ConfigureAwait(false);
                    var result = response is null
                        ? null
                        : new LyricsResult(ParseSyncedLyrics(response.SyncedLyrics), string.IsNullOrWhiteSpace(response.PlainLyrics) ? null : response.PlainLyrics.Trim());

                    if (result?.HasLyrics == true)
                    {
                        StoreCacheEntry(cacheKey, result);
                        return result;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    transientFailure = true;
                }
            }

            if (!transientFailure)
            {
                StoreCacheEntry(cacheKey, null);
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private void StoreCacheEntry(string key, LyricsResult? value)
    {
        if (_cache.ContainsKey(key))
        {
            _cacheOrder.Remove(key);
        }
        else if (_cache.Count >= MaxCacheEntries && _cacheOrder.Last is { } oldest)
        {
            _cacheOrder.RemoveLast();
            _cache.Remove(oldest.Value);
        }

        _cache[key] = value;
        _cacheOrder.AddFirst(key);
    }

    private static IReadOnlyList<LyricsLine> ParseSyncedLyrics(string? syncedLyrics)
    {
        if (string.IsNullOrWhiteSpace(syncedLyrics))
        {
            return Array.Empty<LyricsLine>();
        }

        var lines = new List<LyricsLine>();
        foreach (var rawLine in syncedLyrics.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawLine.Length < 11 || rawLine[0] != '[')
            {
                continue;
            }

            var end = rawLine.IndexOf(']');
            if (end <= 1)
            {
                continue;
            }

            var stamp = rawLine[1..end];
            var text = rawLine[(end + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var parts = stamp.Split(':');
            if (parts.Length != 2
                || !double.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
                || !double.TryParse(parts[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds))
            {
                continue;
            }

            lines.Add(new LyricsLine(TimeSpan.FromSeconds((minutes * 60) + seconds), text));
        }

        return lines.OrderBy(line => line.Time).ToArray();
    }

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;
}

internal sealed class LrcLibResponse
{
    [JsonPropertyName("plainLyrics")]
    public string? PlainLyrics { get; init; }

    [JsonPropertyName("syncedLyrics")]
    public string? SyncedLyrics { get; init; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(LrcLibResponse))]
internal sealed partial class LyricsJsonContext : JsonSerializerContext
{
}
