namespace Pulse.Core.Services;

/// <summary>
/// Pure BGRA8 Liquid Glass filter: downsampled 3-pass separable sliding-window Gaussian
/// approximation with edge replication, saturation/vibrancy enhancement, and anti-banding dither.
/// Zero native dependencies, O(W*H) independent of radius, and deterministically testable.
/// </summary>
public static class LiquidGlassBitmapProcessor
{
    public static byte[] ProcessBgra8(
        ReadOnlySpan<byte> sourceBgra,
        int width,
        int height,
        int blurRadius = 11,
        double vibrancyBoost = 1.24,
        bool addFrostGrain = true)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Bitmap dimensions must be positive.");
        }

        var expectedLength = checked(width * height * 4);
        if (sourceBgra.Length < expectedLength)
        {
            throw new ArgumentException("Source buffer is smaller than width * height * 4.", nameof(sourceBgra));
        }

        var working = new byte[expectedLength];
        sourceBgra[..expectedLength].CopyTo(working);

        var clampedRadius = Math.Clamp(blurRadius, 0, Math.Max(width, height));
        if (clampedRadius > 0 && width > 1 && height > 1)
        {
            var temp = new byte[expectedLength];
            // Three box-blur passes approximate a smooth Gaussian bell curve without blocky grid artifacts.
            for (var pass = 0; pass < 3; pass++)
            {
                var r = Math.Max(1, clampedRadius - pass);
                BoxBlurHorizontal(working, temp, width, height, r);
                BoxBlurVertical(temp, working, width, height, r);
            }
        }

        if (Math.Abs(vibrancyBoost - 1.0) > 0.01 || addFrostGrain)
        {
            ApplyVibrancyAndGrain(working, width, height, Math.Clamp(vibrancyBoost, 0.5, 1.8), addFrostGrain);
        }

        return working;
    }

    private static void BoxBlurHorizontal(byte[] src, byte[] dst, int width, int height, int radius)
    {
        var window = radius * 2 + 1;
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * width * 4;
            int sumB = 0, sumG = 0, sumR = 0, sumA = 0;

            for (var i = -radius; i <= radius; i++)
            {
                var x = Math.Clamp(i, 0, width - 1);
                var idx = rowOffset + x * 4;
                sumB += src[idx];
                sumG += src[idx + 1];
                sumR += src[idx + 2];
                sumA += src[idx + 3];
            }

            for (var x = 0; x < width; x++)
            {
                var dstIdx = rowOffset + x * 4;
                dst[dstIdx] = (byte)(sumB / window);
                dst[dstIdx + 1] = (byte)(sumG / window);
                dst[dstIdx + 2] = (byte)(sumR / window);
                dst[dstIdx + 3] = (byte)(sumA / window);

                var removeX = Math.Clamp(x - radius, 0, width - 1);
                var addX = Math.Clamp(x + radius + 1, 0, width - 1);
                var removeIdx = rowOffset + removeX * 4;
                var addIdx = rowOffset + addX * 4;

                sumB += src[addIdx] - src[removeIdx];
                sumG += src[addIdx + 1] - src[removeIdx + 1];
                sumR += src[addIdx + 2] - src[removeIdx + 2];
                sumA += src[addIdx + 3] - src[removeIdx + 3];
            }
        }
    }

    private static void BoxBlurVertical(byte[] src, byte[] dst, int width, int height, int radius)
    {
        var window = radius * 2 + 1;
        var stride = width * 4;

        for (var x = 0; x < width; x++)
        {
            var colOffset = x * 4;
            int sumB = 0, sumG = 0, sumR = 0, sumA = 0;

            for (var i = -radius; i <= radius; i++)
            {
                var y = Math.Clamp(i, 0, height - 1);
                var idx = y * stride + colOffset;
                sumB += src[idx];
                sumG += src[idx + 1];
                sumR += src[idx + 2];
                sumA += src[idx + 3];
            }

            for (var y = 0; y < height; y++)
            {
                var dstIdx = y * stride + colOffset;
                dst[dstIdx] = (byte)(sumB / window);
                dst[dstIdx + 1] = (byte)(sumG / window);
                dst[dstIdx + 2] = (byte)(sumR / window);
                dst[dstIdx + 3] = (byte)(sumA / window);

                var removeY = Math.Clamp(y - radius, 0, height - 1);
                var addY = Math.Clamp(y + radius + 1, 0, height - 1);
                var removeIdx = removeY * stride + colOffset;
                var addIdx = addY * stride + colOffset;

                sumB += src[addIdx] - src[removeIdx];
                sumG += src[addIdx + 1] - src[removeIdx + 1];
                sumR += src[addIdx + 2] - src[removeIdx + 2];
                sumA += src[addIdx + 3] - src[removeIdx + 3];
            }
        }
    }

    private static void ApplyVibrancyAndGrain(byte[] pixels, int width, int height, double vibrancyBoost, bool addGrain)
    {
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var idx = rowOffset + x * 4;
                double b = pixels[idx];
                double g = pixels[idx + 1];
                double r = pixels[idx + 2];

                var luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                r = luma + (r - luma) * vibrancyBoost;
                g = luma + (g - luma) * vibrancyBoost;
                b = luma + (b - luma) * vibrancyBoost;

                if (addGrain)
                {
                    // Deterministic low-amplitude spatial hash (-2..+2) prevents 8-bit blur banding.
                    var hash = ((x * 73856093) ^ (y * 19349663)) & 0x7;
                    var dither = (hash - 3.5) * 0.55;
                    r += dither;
                    g += dither;
                    b += dither;
                }

                pixels[idx] = (byte)Math.Clamp((int)Math.Round(b), 0, 255);
                pixels[idx + 1] = (byte)Math.Clamp((int)Math.Round(g), 0, 255);
                pixels[idx + 2] = (byte)Math.Clamp((int)Math.Round(r), 0, 255);
            }
        }
    }
}

