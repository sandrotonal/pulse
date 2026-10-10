namespace Pulse.Services.Lyrics;

public sealed record LyricsResult(IReadOnlyList<LyricsLine> Lines, string? PlainText)
{
    public bool HasLyrics => Lines.Count > 0 || !string.IsNullOrWhiteSpace(PlainText);
}
