namespace Pulse.Core.Models;

public enum ShelfItemKind
{
    File,
    Folder,
    Image,
    Text
}

public sealed record ShelfItem(
    string Id,
    string DisplayName,
    string FilePath,
    ShelfItemKind Kind,
    string ExtensionBadge,
    string IconGlyph,
    long FileSizeBytes,
    bool IsTemporaryStaging,
    DateTimeOffset AddedAtUtc,
    bool IsPinned = false)
{
    public string SizeLabel => Kind == ShelfItemKind.Folder
        ? "Klasör"
        : FormatByteSize(FileSizeBytes);

    public string TooltipText => $"{DisplayName} ({SizeLabel})";

    public static string FormatByteSize(long bytes)
    {
        if (bytes < 0)
        {
            return "0 B";
        }

        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double kb = bytes / 1024.0;
        if (kb < 1024.0)
        {
            return $"{kb:0.#} KB";
        }

        double mb = kb / 1024.0;
        if (mb < 1024.0)
        {
            return $"{mb:0.#} MB";
        }

        double gb = mb / 1024.0;
        return $"{gb:0.##} GB";
    }
}
