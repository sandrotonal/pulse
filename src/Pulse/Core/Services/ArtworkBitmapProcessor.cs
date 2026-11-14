namespace Pulse.Core.Services;

/// <summary>
/// Pure BGRA8 artwork processor: center-square crops widescreen/non-square covers,
/// resamples cleanly, applies a quintic rounded-rectangle SDF alpha feather so covers
/// melt softly into the island backdrop without harsh borders, and extracts accent colors.
/// Zero WinUI/GDI+ dependencies and deterministically testable.
/// </summary>
public static class ArtworkBitmapProcessor
{
    public const int ExpandedTargetSizePx = 192;
    public const double ExpandedCornerRadiusRatio = 18.0 / 96.0;
    public const double ExpandedFeatherRatio = 0.105;
    public const double ExpandedOuterInsetRatio = 0.006;

    public const int CompactTargetSizePx = 64;
    public const double CompactCornerRadiusRatio = 7.0 / 26.0;
    public const double CompactFeatherRatio = 0.065;
    public const double CompactOuterInsetRatio = 0.016;

    public static (uint ScaledWidth, uint ScaledHeight) ComputeDecodeDimensions(
        uint pixelWidth,
        uint pixelHeight,
        uint maxShortSide = 384)
    {
        if (pixelWidth == 0 || pixelHeight == 0 || maxShortSide == 0)
        {
            return (0u, 0u);
        }

        var shortSide = Math.Min(pixelWidth, pixelHeight);
        if (shortSide <= maxShortSide)
        {
            return (pixelWidth, pixelHeight);
        }

        var scale = (double)maxShortSide / shortSide;
        var scaledW = Math.Max(1u, (uint)Math.Round(pixelWidth * scale));
        var scaledH = Math.Max(1u, (uint)Math.Round(pixelHeight * scale));
        return (scaledW, scaledH);
    }

    public static uint ExtractAccentColorFromBgra8(ReadOnlySpan<byte> straightBgra, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return LiquidGlassEngine.DefaultAccentArgb;
        }

        var requiredLength = checked(width * height * 4);
        if (straightBgra.Length < requiredLength)
        {
            return LiquidGlassEngine.DefaultAccentArgb;
        }

        var stepX = Math.Max(1, width / 48);
        var stepY = Math.Max(1, height / 48);

        double totalWeight = 0;
        double r = 0;
        double g = 0;
        double b = 0;

        for (var y = 0; y < height; y += stepY)
        {
            var rowOffset = y * width * 4;
            for (var x = 0; x < width; x += stepX)
            {
                var idx = rowOffset + x * 4;
                if (straightBgra[idx + 3] < 128)
                {
                    continue;
                }

                var pb = straightBgra[idx];
                var pg = straightBgra[idx + 1];
                var pr = straightBgra[idx + 2];

                var max = Math.Max(pr, Math.Max(pg, pb));
                var min = Math.Min(pr, Math.Min(pg, pb));
                var saturation = (max - min) / 255d;
                var brightness = max / 255d;
                if (brightness < 0.12 || brightness > 0.95)
                {
                    continue;
                }

                var weight = 0.35 + saturation + (brightness * 0.35);
                r += pr * weight;
                g += pg * weight;
                b += pb * weight;
                totalWeight += weight;
            }
        }

        if (totalWeight <= 0)
        {
            return LiquidGlassEngine.DefaultAccentArgb;
        }

        var rr = NormalizeAccentChannel(r / totalWeight);
        var gg = NormalizeAccentChannel(g / totalWeight);
        var bb = NormalizeAccentChannel(b / totalWeight);
        return 0xFF000000u | ((uint)rr << 16) | ((uint)gg << 8) | bb;
    }

    public static byte[] CreateSoftEdgeSquarePremultipliedBgra8(
        ReadOnlySpan<byte> sourceStraightBgra,
        int sourceWidth,
        int sourceHeight,
        int targetSize,
        double cornerRadiusRatio,
        double featherRatio,
        double outerInsetRatio = ExpandedOuterInsetRatio)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || targetSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetSize), "Bitmap dimensions must be positive.");
        }

        var requiredLength = checked(sourceWidth * sourceHeight * 4);
        if (sourceStraightBgra.Length < requiredLength)
        {
            throw new ArgumentException("Source buffer is smaller than sourceWidth * sourceHeight * 4.", nameof(sourceStraightBgra));
        }

        var output = new byte[checked(targetSize * targetSize * 4)];

        var cropSize = Math.Min(sourceWidth, sourceHeight);
        var cropOriginX = (sourceWidth - cropSize) * 0.5;
        var cropOriginY = (sourceHeight - cropSize) * 0.5;

        var stepScale = (double)cropSize / targetSize;
        var subSamples = Math.Clamp((int)Math.Ceiling(stepScale), 1, 4);
        var invSub = 1.0 / subSamples;
        var invTarget = 1.0 / targetSize;
        var invSampleCount = 1.0 / (subSamples * subSamples);

        for (var y = 0; y < targetSize; y++)
        {
            var dstRowOffset = y * targetSize * 4;
            var py = y + 0.5;

            for (var x = 0; x < targetSize; x++)
            {
                var px = x + 0.5;
                var maskAlpha = ComputeRoundedSquareFeatherMask(
                    px,
                    py,
                    targetSize,
                    cornerRadiusRatio,
                    featherRatio,
                    outerInsetRatio);

                var dstIdx = dstRowOffset + x * 4;
                if (maskAlpha <= 0.0)
                {
                    // Transparent premultiplied pixel; zero-initialized array already has 0,0,0,0.
                    continue;
                }

                double sumB = 0;
                double sumG = 0;
                double sumR = 0;
                double sumWeightA = 0;
                double sumA = 0;

                for (var sy = 0; sy < subSamples; sy++)
                {
                    var v = (y + (sy + 0.5) * invSub) * invTarget;
                    var srcY = cropOriginY + v * cropSize - 0.5;

                    for (var sx = 0; sx < subSamples; sx++)
                    {
                        var u = (x + (sx + 0.5) * invSub) * invTarget;
                        var srcX = cropOriginX + u * cropSize - 0.5;

                        SampleBilinearStraight(
                            sourceStraightBgra,
                            sourceWidth,
                            sourceHeight,
                            srcX,
                            srcY,
                            out var b,
                            out var g,
                            out var r,
                            out var a);

                        sumB += b * a;
                        sumG += g * a;
                        sumR += r * a;
                        sumWeightA += a;
                        sumA += a;
                    }
                }

                var avgA = sumA * invSampleCount;
                var finalAlpha01 = Math.Clamp((avgA / 255.0) * maskAlpha, 0.0, 1.0);
                var outA = (byte)Math.Clamp((int)Math.Round(finalAlpha01 * 255.0), 0, 255);
                if (outA == 0 || sumWeightA <= 0.0)
                {
                    continue;
                }

                var straightB = sumB / sumWeightA;
                var straightG = sumG / sumWeightA;
                var straightR = sumR / sumWeightA;
                var alphaScale = outA / 255.0;

                output[dstIdx] = (byte)Math.Clamp((int)Math.Round(straightB * alphaScale), 0, outA);
                output[dstIdx + 1] = (byte)Math.Clamp((int)Math.Round(straightG * alphaScale), 0, outA);
                output[dstIdx + 2] = (byte)Math.Clamp((int)Math.Round(straightR * alphaScale), 0, outA);
                output[dstIdx + 3] = outA;
            }
        }

        return output;
    }

    public static double ComputeRoundedSquareFeatherMask(
        double px,
        double py,
        int size,
        double cornerRadiusRatio,
        double featherRatio,
        double outerInsetRatio)
    {
        if (size <= 0)
        {
            return 0.0;
        }

        var half = size * 0.5;
        var inset = Math.Max(0.0, size * Math.Clamp(outerInsetRatio, 0.0, 0.25));
        var boxHalf = Math.Max(1.0, half - inset);
        var radius = Math.Clamp(size * Math.Clamp(cornerRadiusRatio, 0.0, 0.5), 0.0, boxHalf);
        var innerHalf = boxHalf - radius;

        var qx = Math.Abs(px - half) - innerHalf;
        var qy = Math.Abs(py - half) - innerHalf;
        var ox = Math.Max(qx, 0.0);
        var oy = Math.Max(qy, 0.0);

        var sdf = Math.Sqrt((ox * ox) + (oy * oy)) + Math.Min(Math.Max(qx, qy), 0.0) - radius;
        var distInside = -sdf;
        if (distInside <= 0.0)
        {
            return 0.0;
        }

        var featherPx = Math.Max(0.0, size * Math.Clamp(featherRatio, 0.0, 0.45));
        if (featherPx <= 0.001)
        {
            return Math.Clamp(distInside, 0.0, 1.0);
        }

        var t = Math.Clamp(distInside / featherPx, 0.0, 1.0);
        // C2 quintic smootherstep eliminates gradient banding / Mach rings around edges.
        return t * t * t * (t * ((t * 6.0) - 15.0) + 10.0);
    }

    private static void SampleBilinearStraight(
        ReadOnlySpan<byte> src,
        int width,
        int height,
        double fx,
        double fy,
        out double b,
        out double g,
        out double r,
        out double a)
    {
        var cx = Math.Clamp(fx, 0.0, width - 1);
        var cy = Math.Clamp(fy, 0.0, height - 1);

        var x0 = (int)Math.Floor(cx);
        var y0 = (int)Math.Floor(cy);
        var x1 = Math.Min(x0 + 1, width - 1);
        var y1 = Math.Min(y0 + 1, height - 1);

        var tx = cx - x0;
        var ty = cy - y0;
        var w00 = (1.0 - tx) * (1.0 - ty);
        var w10 = tx * (1.0 - ty);
        var w01 = (1.0 - tx) * ty;
        var w11 = tx * ty;

        var i00 = ((y0 * width) + x0) * 4;
        var i10 = ((y0 * width) + x1) * 4;
        var i01 = ((y1 * width) + x0) * 4;
        var i11 = ((y1 * width) + x1) * 4;

        var a00 = src[i00 + 3] * w00;
        var a10 = src[i10 + 3] * w10;
        var a01 = src[i01 + 3] * w01;
        var a11 = src[i11 + 3] * w11;

        var totalAlphaWeight = a00 + a10 + a01 + a11;
        a = totalAlphaWeight;
        if (totalAlphaWeight <= 0.0)
        {
            b = 0;
            g = 0;
            r = 0;
            return;
        }

        var invAlphaWeight = 1.0 / totalAlphaWeight;
        b = ((src[i00] * a00) + (src[i10] * a10) + (src[i01] * a01) + (src[i11] * a11)) * invAlphaWeight;
        g = ((src[i00 + 1] * a00) + (src[i10 + 1] * a10) + (src[i01 + 1] * a01) + (src[i11 + 1] * a11)) * invAlphaWeight;
        r = ((src[i00 + 2] * a00) + (src[i10 + 2] * a10) + (src[i01 + 2] * a01) + (src[i11 + 2] * a11)) * invAlphaWeight;
    }

    private static byte NormalizeAccentChannel(double value)
        => (byte)Math.Clamp(Math.Round((value * 0.86) + 24), 32, 232);
}

