using Microsoft.UI.Xaml.Media.Imaging;
using Pulse.Core.Interfaces;
using Pulse.Core.Models;
using Pulse.Core.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Pulse.Services.System;

/// <summary>
/// WinUI 3 / WinRT shell interop bridge for the Drop Shelf:
/// ingests dropped OLE DataPackageViews (files, folders, bitmaps, links, text),
/// builds outgoing OLE drag packages (DragStarting) and Clipboard packages, and decodes
/// memory-bounded image thumbnails.
/// </summary>
public static class ShelfShellService
{
    public static bool CanAcceptDrop(DataPackageView dataView)
    {
        try
        {
            return dataView.Contains(StandardDataFormats.StorageItems)
                || dataView.Contains(StandardDataFormats.Bitmap)
                || dataView.Contains(StandardDataFormats.WebLink)
                || dataView.Contains(StandardDataFormats.Text);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<int> IngestDropAsync(DataPackageView dataView, IDropShelfService shelf)
    {
        if (dataView is null || shelf is null)
        {
            return 0;
        }

        var addedCount = 0;

        try
        {
            if (dataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await dataView.GetStorageItemsAsync();
                foreach (var storageItem in items)
                {
                    if (!string.IsNullOrWhiteSpace(storageItem.Path))
                    {
                        if (shelf.AddPath(storageItem.Path) is not null)
                        {
                            addedCount++;
                        }
                    }
                }

                if (addedCount > 0)
                {
                    return addedCount;
                }
            }

            if (dataView.Contains(StandardDataFormats.Bitmap))
            {
                var bitmapRef = await dataView.GetBitmapAsync();
                if (bitmapRef is not null)
                {
                    using var stream = await bitmapRef.OpenReadAsync();
                    var bytes = await ReadAllBytesAsync(stream).ConfigureAwait(false);
                    if (bytes.Length > 0)
                    {
                        var ext = DetectImageExtension(bytes);
                        if (await shelf.StageBytesAsync(bytes, "Gorsel", ext, ShelfItemKind.Image).ConfigureAwait(false) is not null)
                        {
                            return 1;
                        }
                    }
                }
            }

            if (dataView.Contains(StandardDataFormats.WebLink))
            {
                var uri = await dataView.GetWebLinkAsync();
                if (uri is not null && await shelf.StageTextAsync(uri.AbsoluteUri).ConfigureAwait(false) is not null)
                {
                    return 1;
                }
            }

            if (dataView.Contains(StandardDataFormats.Text))
            {
                var text = await dataView.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text) &&
                    await shelf.StageTextAsync(text).ConfigureAwait(false) is not null)
                {
                    return 1;
                }
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("ShelfShellService.IngestDropAsync", ex);
        }

        return addedCount;
    }

    public static async Task<bool> PopulateOutgoingDataPackageAsync(DataPackage data, IReadOnlyList<string> paths)
    {
        if (data is null || paths is null || paths.Count == 0)
        {
            return false;
        }

        var storageItems = new List<IStorageItem>(paths.Count);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            try
            {
                if (File.Exists(path))
                {
                    storageItems.Add(await StorageFile.GetFileFromPathAsync(path));
                }
                else if (Directory.Exists(path))
                {
                    storageItems.Add(await StorageFolder.GetFolderFromPathAsync(path));
                }
            }
            catch (Exception ex)
            {
                CrashLogger.Write("ShelfShellService.ResolveStorageItem", ex, path);
            }
        }

        if (storageItems.Count == 0)
        {
            return false;
        }

        try
        {
            data.RequestedOperation = DataPackageOperation.Copy;
            data.SetStorageItems(storageItems, readOnly: true);

            // If dragging a single small staged .txt snippet, also expose plain text
            // so chat inputs / editors that prefer text drops accept it seamlessly.
            if (paths.Count == 1 &&
                File.Exists(paths[0]) &&
                string.Equals(Path.GetExtension(paths[0]), ".txt", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var fi = new FileInfo(paths[0]);
                    if (fi.Length is > 0 and <= 65_536)
                    {
                        var text = await File.ReadAllTextAsync(paths[0]).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            data.SetText(text);
                        }
                    }
                }
                catch
                {
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            CrashLogger.Write("ShelfShellService.PopulateOutgoingDataPackageAsync", ex);
            return false;
        }
    }

    public static async Task<bool> CopyPathsToClipboardAsync(IReadOnlyList<string> paths)
    {
        if (paths is null || paths.Count == 0)
        {
            return false;
        }

        try
        {
            var package = new DataPackage();
            var ok = await PopulateOutgoingDataPackageAsync(package, paths);
            if (!ok)
            {
                return false;
            }

            Clipboard.SetContent(package);
            Clipboard.Flush();
            return true;
        }
        catch (Exception ex)
        {
            CrashLogger.Write("ShelfShellService.CopyPathsToClipboardAsync", ex);
            return false;
        }
    }

    public static async Task<int> PasteFromClipboardAsync(IDropShelfService shelf)
    {
        if (shelf is null)
        {
            return 0;
        }

        try
        {
            var content = Clipboard.GetContent();
            if (content is null || !CanAcceptDrop(content))
            {
                return 0;
            }

            return await IngestDropAsync(content, shelf);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("ShelfShellService.PasteFromClipboardAsync", ex);
            return 0;
        }
    }

    public static BitmapImage? TryCreateThumbnail(ShelfItem item)
    {
        if (item.Kind != ShelfItemKind.Image || !File.Exists(item.FilePath))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage
            {
                DecodePixelWidth = 192,
                CreateOptions = BitmapCreateOptions.IgnoreImageCache
            };
            bitmap.UriSource = new Uri(item.FilePath, UriKind.Absolute);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(IRandomAccessStream stream)
    {
        var size = (int)Math.Min(stream.Size, 25 * 1024 * 1024); // 25 MB safety cap
        if (size <= 0)
        {
            return [];
        }

        var bytes = new byte[size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        var loaded = await reader.LoadAsync((uint)size);
        if (loaded == 0)
        {
            return [];
        }

        var actual = loaded == size ? bytes : new byte[loaded];
        reader.ReadBytes(actual);
        return actual;
    }

    private static string DetectImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return ".png";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ".jpg";
        }

        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            return ".bmp";
        }

        return ".png";
    }

    public static async Task<string?> ExtractTextFromImageAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            using var stream = await file.OpenAsync(FileAccessMode.Read);
            var decoder = await global::Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                global::Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);

            var engine = global::Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()
                ?? global::Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new global::Windows.Globalization.Language("en-US"));

            if (engine is null)
            {
                return null;
            }

            var result = await engine.RecognizeAsync(softwareBitmap);
            var text = result?.Text?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex)
        {
            CrashLogger.Write("ShelfShellService.ExtractTextFromImageAsync", ex, $"Path={filePath}");
            return null;
        }
    }

    public static bool CopyTextToClipboard(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            var package = new DataPackage
            {
                RequestedOperation = DataPackageOperation.Copy
            };
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            return true;
        }
        catch (Exception ex)
        {
            CrashLogger.Write("ShelfShellService.CopyTextToClipboard", ex);
            return false;
        }
    }
}
