using Pulse.Core.Models;

namespace Pulse.Core.Interfaces;

public interface IDropShelfService : IDisposable
{
    IReadOnlyList<ShelfItem> Items { get; }
    int MaxItems { get; }

    event EventHandler? ItemsChanged;

    ShelfItem? AddPath(string path, bool isTemporaryStaging = false, string? displayNameOverride = null);
    Task<ShelfItem?> StageTextAsync(string text, CancellationToken cancellationToken = default);
    Task<ShelfItem?> StageBytesAsync(
        byte[] bytes,
        string fileNamePrefix,
        string extension,
        ShelfItemKind kind,
        CancellationToken cancellationToken = default);
    bool Remove(string itemId);
    bool TogglePin(string itemId);
    void Clear();
    Task<ShelfItem?> CreateZipArchiveAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<string> GetExistingPaths();
}
