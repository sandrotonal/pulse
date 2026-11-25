using System.IO.Compression;
using System.Text;
using Pulse.Core.Interfaces;
using Pulse.Core.Models;

namespace Pulse.Core.Services;

/// <summary>
/// Manages temporary Drop Shelf items (files, folders, images, staged text snippets)
/// with deduplication, bounded capacity, safe ZIP archiving, and strict temp-file-only cleanup.
/// Never deletes or mutates original user files outside its isolated staging directory.
/// </summary>
public sealed class DropShelfService : IDropShelfService
{
    public const int DefaultMaxItems = 12;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".ico", ".tif", ".tiff", ".heic", ".avif"
    };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".xz"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".avi", ".webm", ".wmv"
    };

    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".json", ".xml", ".xaml", ".html", ".css", ".md", ".yml", ".yaml", ".sql", ".ps1"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".csv", ".ini", ".cfg", ".rtf"
    };

    private readonly object _sync = new();
    private readonly List<ShelfItem> _items = [];
    private readonly string _stagingDirectory;
    private readonly int _maxItems;
    private bool _disposed;

    public DropShelfService(string? stagingDirectory = null, int maxItems = DefaultMaxItems)
    {
        _maxItems = Math.Clamp(maxItems, 1, 64);
        var defaultStaging = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Pulse",
            "ShelfTemp");
        _stagingDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(stagingDirectory) ? defaultStaging : stagingDirectory);
    }

    public int MaxItems => _maxItems;

    public IReadOnlyList<ShelfItem> Items
    {
        get
        {
            lock (_sync)
            {
                return _items.ToArray();
            }
        }
    }

    public event EventHandler? ItemsChanged;

    public ShelfItem? AddPath(string path, bool isTemporaryStaging = false, string? displayNameOverride = null)
    {
        if (_disposed || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string normalizedPath;
        try
        {
            normalizedPath = NormalizePath(path);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("DropShelfService.AddPath.Normalize", ex);
            return null;
        }

        var isDirectory = Directory.Exists(normalizedPath);
        var isFile = !isDirectory && File.Exists(normalizedPath);
        if (!isDirectory && !isFile)
        {
            return null;
        }

        long sizeBytes = 0;
        if (isFile)
        {
            try
            {
                sizeBytes = new FileInfo(normalizedPath).Length;
            }
            catch
            {
                sizeBytes = 0;
            }
        }

        var kind = ClassifyKind(normalizedPath, isDirectory);
        var displayName = !string.IsNullOrWhiteSpace(displayNameOverride)
            ? displayNameOverride.Trim()
            : ExtractDisplayName(normalizedPath);
        var badge = ResolveBadge(normalizedPath, kind);
        var glyph = ResolveGlyph(normalizedPath, kind);

        ShelfItem? addedOrExisting;
        ShelfItem? evicted = null;
        var changed = false;

        lock (_sync)
        {
            if (_disposed)
            {
                return null;
            }

            var existingIndex = _items.FindIndex(item =>
                string.Equals(NormalizePathSafe(item.FilePath), normalizedPath, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                // Move existing item to front (most recent) and refresh metadata.
                var existing = _items[existingIndex];
                var updated = existing with
                {
                    DisplayName = !string.IsNullOrWhiteSpace(displayNameOverride) ? displayName : existing.DisplayName,
                    FileSizeBytes = sizeBytes,
                    AddedAtUtc = DateTimeOffset.UtcNow
                };
                _items.RemoveAt(existingIndex);
                _items.Insert(0, updated);
                addedOrExisting = updated;
                changed = true;
            }
            else
            {
                var created = new ShelfItem(
                    Id: Guid.NewGuid().ToString("N"),
                    DisplayName: displayName,
                    FilePath: normalizedPath,
                    Kind: kind,
                    ExtensionBadge: badge,
                    IconGlyph: glyph,
                    FileSizeBytes: sizeBytes,
                    IsTemporaryStaging: isTemporaryStaging && IsInsideStagingDirectory(normalizedPath),
                    AddedAtUtc: DateTimeOffset.UtcNow);

                _items.Insert(0, created);
                if (_items.Count > _maxItems)
                {
                    // Prefer evicting the oldest unpinned item so pinned items stay protected.
                    var evictIdx = _items.FindLastIndex(i => !i.IsPinned);
                    if (evictIdx < 0)
                    {
                        evictIdx = _items.Count - 1;
                    }

                    evicted = _items[evictIdx];
                    _items.RemoveAt(evictIdx);
                }

                addedOrExisting = created;
                changed = true;
            }
        }

        if (evicted is not null)
        {
            TryDeleteStagingFile(evicted);
        }

        if (changed)
        {
            ItemsChanged?.Invoke(this, EventArgs.Empty);
        }

        return addedOrExisting;
    }

    public async Task<ShelfItem?> StageTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            var trimmed = text.Trim();
            var bytes = Encoding.UTF8.GetBytes(trimmed);
            var previewName = BuildTextPreviewFileName(trimmed);
            return await StageBytesAsync(bytes, previewName, ".txt", ShelfItemKind.Text, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLogger.Write("DropShelfService.StageTextAsync", ex);
            return null;
        }
    }

    public async Task<ShelfItem?> StageBytesAsync(
        byte[] bytes,
        string fileNamePrefix,
        string extension,
        ShelfItemKind kind,
        CancellationToken cancellationToken = default)
    {
        if (_disposed || bytes is null || bytes.Length == 0)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(_stagingDirectory);
            var safePrefix = SanitizeFileName(string.IsNullOrWhiteSpace(fileNamePrefix) ? "Pulse-Oge" : fileNamePrefix);
            var safeExt = string.IsNullOrWhiteSpace(extension)
                ? ".bin"
                : (extension.StartsWith('.') ? extension : "." + extension);
            var uniqueSuffix = DateTime.Now.ToString("HHmmss") + "-" + Guid.NewGuid().ToString("N")[..4];
            var fileName = $"{safePrefix}-{uniqueSuffix}{safeExt}";
            var fullPath = Path.Combine(_stagingDirectory, fileName);

            await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken).ConfigureAwait(false);

            var displayName = $"{safePrefix}{safeExt}";
            var item = AddPath(fullPath, isTemporaryStaging: true, displayNameOverride: displayName);
            if (item is not null && item.Kind != kind)
            {
                lock (_sync)
                {
                    var idx = _items.FindIndex(i => i.Id == item.Id);
                    if (idx >= 0)
                    {
                        var overridden = _items[idx] with
                        {
                            Kind = kind,
                            ExtensionBadge = ResolveBadge(fullPath, kind),
                            IconGlyph = ResolveGlyph(fullPath, kind)
                        };
                        _items[idx] = overridden;
                        item = overridden;
                    }
                }
                ItemsChanged?.Invoke(this, EventArgs.Empty);
            }

            return item;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLogger.Write("DropShelfService.StageBytesAsync", ex);
            return null;
        }
    }

    public bool Remove(string itemId)
    {
        if (_disposed || string.IsNullOrWhiteSpace(itemId))
        {
            return false;
        }

        ShelfItem? removed = null;
        lock (_sync)
        {
            var idx = _items.FindIndex(i => string.Equals(i.Id, itemId, StringComparison.Ordinal));
            if (idx >= 0)
            {
                removed = _items[idx];
                _items.RemoveAt(idx);
            }
        }

        if (removed is null)
        {
            return false;
        }

        TryDeleteStagingFile(removed);
        ItemsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool TogglePin(string itemId)
    {
        if (_disposed || string.IsNullOrWhiteSpace(itemId))
        {
            return false;
        }

        lock (_sync)
        {
            var idx = _items.FindIndex(i => string.Equals(i.Id, itemId, StringComparison.Ordinal));
            if (idx < 0)
            {
                return false;
            }

            _items[idx] = _items[idx] with { IsPinned = !_items[idx].IsPinned };
        }

        ItemsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Clear()
    {
        List<ShelfItem> removedItems;
        lock (_sync)
        {
            if (_items.Count == 0)
            {
                return;
            }

            removedItems = _items.Where(i => !i.IsPinned).ToList();
            if (removedItems.Count == 0)
            {
                return;
            }

            _items.RemoveAll(i => !i.IsPinned);
        }

        foreach (var item in removedItems)
        {
            TryDeleteStagingFile(item);
        }

        ItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<ShelfItem?> CreateZipArchiveAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return null;
        }

        var snapshot = Items;
        if (snapshot.Count == 0)
        {
            return null;
        }

        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(_stagingDirectory);

                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var zipName = $"Pulse-Raf-{stamp}.zip";
                var zipPath = Path.Combine(_stagingDirectory, zipName);
                var usedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var addedAny = false;

                using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
                {
                    foreach (var item in snapshot)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (string.Equals(NormalizePathSafe(item.FilePath), NormalizePathSafe(zipPath), StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (File.Exists(item.FilePath))
                        {
                            var entryName = EnsureUniqueEntryName(Path.GetFileName(item.FilePath), usedEntries);
                            archive.CreateEntryFromFile(item.FilePath, entryName, CompressionLevel.Fastest);
                            addedAny = true;
                        }
                        else if (Directory.Exists(item.FilePath))
                        {
                            var rootFolderName = ExtractDisplayName(item.FilePath);
                            foreach (var file in Directory.EnumerateFiles(item.FilePath, "*", SearchOption.AllDirectories))
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var rel = Path.GetRelativePath(item.FilePath, file).Replace('\\', '/');
                                if (rel.StartsWith("..", StringComparison.Ordinal))
                                {
                                    continue;
                                }

                                var entryName = EnsureUniqueEntryName($"{rootFolderName}/{rel}", usedEntries);
                                archive.CreateEntryFromFile(file, entryName, CompressionLevel.Fastest);
                                addedAny = true;
                            }
                        }
                    }
                }

                if (!addedAny)
                {
                    try { File.Delete(zipPath); } catch { }
                    return null;
                }

                return AddPath(zipPath, isTemporaryStaging: true, displayNameOverride: zipName);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLogger.Write("DropShelfService.CreateZipArchiveAsync", ex);
            return null;
        }
    }

    public IReadOnlyList<string> GetExistingPaths()
    {
        var snapshot = Items;
        var existing = new List<string>(snapshot.Count);
        foreach (var item in snapshot)
        {
            if (File.Exists(item.FilePath) || Directory.Exists(item.FilePath))
            {
                existing.Add(item.FilePath);
            }
        }

        return existing;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        List<ShelfItem> snapshot;
        lock (_sync)
        {
            snapshot = new List<ShelfItem>(_items);
            _items.Clear();
        }

        foreach (var item in snapshot)
        {
            TryDeleteStagingFile(item);
        }

        TryCleanEmptyStagingDirectory();
    }

    private bool IsInsideStagingDirectory(string normalizedPath)
    {
        var root = _stagingDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? _stagingDirectory
            : _stagingDirectory + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private void TryDeleteStagingFile(ShelfItem item)
    {
        if (!item.IsTemporaryStaging)
        {
            return;
        }

        try
        {
            var normalized = NormalizePathSafe(item.FilePath);
            if (!IsInsideStagingDirectory(normalized))
            {
                return;
            }

            if (File.Exists(normalized))
            {
                File.Delete(normalized);
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("DropShelfService.TryDeleteStagingFile", ex);
        }
    }

    private void TryCleanEmptyStagingDirectory()
    {
        try
        {
            if (Directory.Exists(_stagingDirectory) &&
                !Directory.EnumerateFileSystemEntries(_stagingDirectory).Any())
            {
                Directory.Delete(_stagingDirectory, recursive: false);
            }
        }
        catch
        {
        }
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string NormalizePathSafe(string path)
    {
        try
        {
            return NormalizePath(path);
        }
        catch
        {
            return path;
        }
    }

    private static ShelfItemKind ClassifyKind(string normalizedPath, bool isDirectory)
    {
        if (isDirectory)
        {
            return ShelfItemKind.Folder;
        }

        var ext = Path.GetExtension(normalizedPath);
        if (ImageExtensions.Contains(ext))
        {
            return ShelfItemKind.Image;
        }

        if (TextExtensions.Contains(ext))
        {
            return ShelfItemKind.Text;
        }

        return ShelfItemKind.File;
    }

    private static string ExtractDisplayName(string normalizedPath)
    {
        var name = Path.GetFileName(normalizedPath);
        return string.IsNullOrWhiteSpace(name) ? normalizedPath : name;
    }

    private static string ResolveBadge(string normalizedPath, ShelfItemKind kind)
    {
        if (kind == ShelfItemKind.Folder)
        {
            return "KLASÖR";
        }

        var ext = Path.GetExtension(normalizedPath).TrimStart('.');
        if (string.IsNullOrWhiteSpace(ext))
        {
            return kind == ShelfItemKind.Text ? "METİN" : "DOSYA";
        }

        return ext.Length > 5 ? ext[..5].ToUpperInvariant() : ext.ToUpperInvariant();
    }

    private static string ResolveGlyph(string normalizedPath, ShelfItemKind kind)
    {
        if (kind == ShelfItemKind.Folder)
        {
            return "\uE8B7"; // Folder
        }

        if (kind == ShelfItemKind.Image)
        {
            return "\uE91B"; // Photo
        }

        var ext = Path.GetExtension(normalizedPath);
        if (ArchiveExtensions.Contains(ext))
        {
            return "\uF012"; // ZipFolder
        }

        if (AudioExtensions.Contains(ext))
        {
            return "\uE8D6"; // MusicNote
        }

        if (VideoExtensions.Contains(ext))
        {
            return "\uE714"; // Video
        }

        if (CodeExtensions.Contains(ext))
        {
            return "\uE943"; // Code
        }

        if (kind == ShelfItemKind.Text)
        {
            return "\uE8A5"; // Document
        }

        return "\uE7C3"; // Page / File
    }

    private static string BuildTextPreviewFileName(string trimmedText)
    {
        var firstLine = trimmedText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "Metin";
        firstLine = firstLine.Trim();
        if (firstLine.Length > 22)
        {
            firstLine = firstLine[..22].Trim();
        }

        var safe = SanitizeFileName(firstLine);
        return string.IsNullOrWhiteSpace(safe) ? "Metin" : safe;
    }

    private static string SanitizeFileName(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (Array.IndexOf(invalid, ch) >= 0 || char.IsControl(ch))
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(ch);
            }
        }

        var cleaned = sb.ToString().Trim(' ', '.', '_');
        return string.IsNullOrWhiteSpace(cleaned) ? "Oge" : cleaned;
    }

    private static string EnsureUniqueEntryName(string candidate, HashSet<string> usedEntries)
    {
        if (usedEntries.Add(candidate))
        {
            return candidate;
        }

        var dir = Path.GetDirectoryName(candidate)?.Replace('\\', '/');
        var nameWithoutExt = Path.GetFileNameWithoutExtension(candidate);
        var ext = Path.GetExtension(candidate);
        for (int i = 2; i < 1000; i++)
        {
            var nextFile = $"{nameWithoutExt} ({i}){ext}";
            var nextPath = string.IsNullOrEmpty(dir) ? nextFile : $"{dir}/{nextFile}";
            if (usedEntries.Add(nextPath))
            {
                return nextPath;
            }
        }

        var fallback = $"{Guid.NewGuid():N}{ext}";
        usedEntries.Add(fallback);
        return fallback;
    }
}
