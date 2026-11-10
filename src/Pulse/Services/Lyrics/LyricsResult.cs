namespace Pulse.Services.Lyrics;

public sealed record LyricsResult(IReadOnlyList<LyricsLine> Lines, string? PlainText)
{
    public bool HasLyrics => Lines.Count > 0 || !string.IsNullOrWhiteSpace(PlainText);

    public IReadOnlyList<string> PlainLines { get; } = string.IsNullOrWhiteSpace(PlainText)
        ? Array.Empty<string>()
        : PlainText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
