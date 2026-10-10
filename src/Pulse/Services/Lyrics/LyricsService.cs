using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Pulse.Services.Lyrics;

public sealed class LyricsService
{
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("https://lrclib.net/"),
        Timeout = TimeSpan.FromSeconds(4)
    };
    private readonly Dictionary<string, LyricsResult?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<LyricsResult?> FindLyricsAsync(string title, string artist, string album, TimeSpan? duration)
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

        try
        {
            var query = $"api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
            if (!string.IsNullOrWhiteSpace(album))
            {
                query += $"&album_name={Uri.EscapeDataString(album)}";
            }

            if (duration is { TotalSeconds: > 0 })
            {
                query += $"&duration={(int)Math.Round(duration.Value.TotalSeconds)}";
            }

            var response = await _httpClient.GetFromJsonAsync<LrcLibResponse>(query);
            var result = response is null
                ? null
                : new LyricsResult(ParseSyncedLyrics(response.SyncedLyrics), string.IsNullOrWhiteSpace(response.PlainLyrics) ? null : response.PlainLyrics.Trim());
            if (result?.HasLyrics != true)
            {
                result = null;
            }

            _cache[cacheKey] = result;
            return result;
        }
        catch
        {
            _cache[cacheKey] = null;
            return null;
        }
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

    private sealed class LrcLibResponse
    {
        [JsonPropertyName("plainLyrics")]
        public string? PlainLyrics { get; init; }

        [JsonPropertyName("syncedLyrics")]
        public string? SyncedLyrics { get; init; }
    }
}
