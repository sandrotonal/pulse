using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Pulse.Core.Services;
using Windows.Storage.Streams;
using Windows.UI.ViewManagement;

namespace Pulse.Services.Glass;

/// <summary>
/// Shared WinUI + GDI+ Liquid Glass utility: desktop region capture, smooth multi-pass
/// Gaussian frost + vibrancy encoding, theme resolution, and in-place brush updates.
/// </summary>
internal static class DesktopFrostService
{
    private static readonly UISettings SystemUiSettings = new();

    public static bool IsSystemLightTheme()
    {
        try
        {
            var background = SystemUiSettings.GetColorValue(UIColorType.Background);
            return (background.R * 0.299 + background.G * 0.587 + background.B * 0.114) > 128;
        }
        catch
        {
            return false;
        }
    }

    public static bool ResolveIsLightTheme(string? theme)
        => LiquidGlassEngine.ResolveIsLightTheme(theme, IsSystemLightTheme());

    public static Windows.UI.Color ToColor(uint argb)
        => Windows.UI.Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb);

    public static SolidColorBrush CreateBrush(uint argb)
        => new(ToColor(argb));

    public static void UpdateBrush(ResourceDictionary resources, string key, uint argb)
    {
        var color = ToColor(argb);
        if (resources.TryGetValue(key, out var existing) && existing is SolidColorBrush brush)
        {
            brush.Color = color;
        }
        else
        {
            resources[key] = new SolidColorBrush(color);
        }
    }

    public static Microsoft.UI.Xaml.Media.LinearGradientBrush CreateVerticalGradientBrush(uint topArgb, uint bottomArgb)
    {
        var brush = new Microsoft.UI.Xaml.Media.LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1)
        };
        brush.GradientStops.Add(new GradientStop { Color = ToColor(topArgb), Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = ToColor(bottomArgb), Offset = 1 });
        return brush;
    }

    public static CompositionLinearGradientBrush CreateCompositionVerticalGradient(
        Compositor compositor,
        uint topArgb,
        uint bottomArgb)
    {
        var brush = compositor.CreateLinearGradientBrush();
        brush.MappingMode = CompositionMappingMode.Relative;
        brush.StartPoint = new Vector2(0f, 0f);
        brush.EndPoint = new Vector2(0f, 1f);
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0f, ToColor(topArgb)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(1f, ToColor(bottomArgb)));
        return brush;
    }

    public static Bitmap? CaptureScreenRegion(int screenX, int screenY, int width, int height)
    {
        if (width < 4 || height < 4)
        {
            return null;
        }

        Bitmap? bitmap = null;
        try
        {
            bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(
                screenX,
                screenY,
                0,
                0,
                new Size(width, height),
                CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch
        {
            bitmap?.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Converts a captured screen bitmap into a DIP-scaled Liquid Glass PNG byte array
    /// using 2x downsampling + separable 3-pass Gaussian blur + vibrancy boost + anti-banding dither.
    /// </summary>
    public static byte[] ProcessCapturedFrostToPng(
        Bitmap captured,
        double dpiScale,
        int blurRadius = 11,
        double vibrancyBoost = 1.24)
    {
        var safeW = Math.Max(4, captured.Width);
        var safeH = Math.Max(4, captured.Height);
        var smallW = Math.Max(8, safeW / 2);
        var smallH = Math.Max(8, safeH / 2);

        using var small = new Bitmap(smallW, smallH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var attrs = new ImageAttributes();
            attrs.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(
                captured,
                new Rectangle(0, 0, smallW, smallH),
                0,
                0,
                safeW,
                safeH,
                GraphicsUnit.Pixel,
                attrs);
        }

        ApplyLiquidGlassFilterInPlace(small, blurRadius, vibrancyBoost);

        var safeDpi = dpiScale > 0.05 ? dpiScale : 1.0;
        var targetDipW = Math.Max(16, (int)Math.Round(safeW / safeDpi));
        var targetDipH = Math.Max(16, (int)Math.Round(safeH / safeDpi));

        using var dipBitmap = new Bitmap(targetDipW, targetDipH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dipBitmap))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var attrs = new ImageAttributes();
            attrs.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(
                small,
                new Rectangle(0, 0, targetDipW, targetDipH),
                0,
                0,
                smallW,
                smallH,
                GraphicsUnit.Pixel,
                attrs);
        }

        using var ms = new MemoryStream();
        dipBitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    public static async Task<BitmapImage> DecodePngToBitmapImageAsync(byte[] pngBytes)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(pngBytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        return image;
    }

    private static void ApplyLiquidGlassFilterInPlace(Bitmap bitmap, int blurRadius, double vibrancyBoost)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var rect = new Rectangle(0, 0, width, height);
        var locked = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var absStride = Math.Abs(locked.Stride);
            var pixelBytes = new byte[checked(width * height * 4)];
            var rowStride = width * 4;

            for (var y = 0; y < height; y++)
            {
                var srcPtr = IntPtr.Add(locked.Scan0, y * locked.Stride);
                Marshal.Copy(srcPtr, pixelBytes, y * rowStride, Math.Min(absStride, rowStride));
            }

            var processed = LiquidGlassBitmapProcessor.ProcessBgra8(
                pixelBytes,
                width,
                height,
                blurRadius: blurRadius,
                vibrancyBoost: vibrancyBoost,
                addFrostGrain: true);

            for (var y = 0; y < height; y++)
            {
                var dstPtr = IntPtr.Add(locked.Scan0, y * locked.Stride);
                Marshal.Copy(processed, y * rowStride, dstPtr, Math.Min(absStride, rowStride));
            }
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }
    }
}
