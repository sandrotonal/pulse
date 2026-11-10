using System.Runtime.InteropServices;

namespace Pulse.Core.Services;

/// <summary>
/// Resolves user-relocatable Windows Known Folders (Downloads, Screenshots) via Shell API
/// with deterministic Environment fallbacks.
/// </summary>
public static class KnownFolderResolver
{
    private static readonly Guid FolderIdDownloads = new("374DE290-123F-4565-9164-39C4925E467B");
    private static readonly Guid FolderIdScreenshots = new("B7BEDE81-DF94-4682-A7D8-57A52620B86F");

    public static string GetDownloadsDirectory()
    {
        var resolved = TryGetKnownFolder(FolderIdDownloads);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
    }

    public static string GetScreenshotsDirectory()
    {
        var resolved = TryGetKnownFolder(FolderIdScreenshots);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Screenshots");
    }

    private static string? TryGetKnownFolder(Guid folderId)
    {
        var ptr = IntPtr.Zero;
        try
        {
            var hr = SHGetKnownFolderPath(ref folderId, 0, IntPtr.Zero, out ptr);
            if (hr == 0 && ptr != IntPtr.Zero)
            {
                return Marshal.PtrToStringUni(ptr);
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("KnownFolderResolver", ex, $"FolderId={folderId}");
        }
        finally
        {
            if (ptr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(ptr);
            }
        }

        return null;
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        ref Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        out IntPtr ppszPath);
}

