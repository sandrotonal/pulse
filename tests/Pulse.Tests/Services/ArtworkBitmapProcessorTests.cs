using Pulse.Core.Services;

namespace Pulse.Tests.Services;

public sealed class ArtworkBitmapProcessorTests
{
    [Fact]
    public void ComputeRoundedSquareFeatherMask_FadesFromZeroEdgeToSolidCoreWithoutCornerSpill()
    {
        const int size = ArtworkBitmapProcessor.ExpandedTargetSizePx;

        // Outer border and sharp square corner outside the rounded arc must be completely transparent.
        Assert.Equal(0.0, ArtworkBitmapProcessor.ComputeRoundedSquareFeatherMask(
            0.5, 0.5, size,
            ArtworkBitmapProcessor.ExpandedCornerRadiusRatio,
            ArtworkBitmapProcessor.ExpandedFeatherRatio,
            ArtworkBitmapProcessor.ExpandedOuterInsetRatio));

        Assert.Equal(0.0, ArtworkBitmapProcessor.ComputeRoundedSquareFeatherMask(
            size * 0.5, 0.5, size,
            ArtworkBitmapProcessor.ExpandedCornerRadiusRatio,
            ArtworkBitmapProcessor.ExpandedFeatherRatio,
            ArtworkBitmapProcessor.ExpandedOuterInsetRatio));

        // Inner core must remain 100% opaque.
        Assert.Equal(1.0, ArtworkBitmapProcessor.ComputeRoundedSquareFeatherMask(
            size * 0.5, size * 0.5, size,
            ArtworkBitmapProcessor.ExpandedCornerRadiusRatio,
            ArtworkBitmapProcessor.ExpandedFeatherRatio,
            ArtworkBitmapProcessor.ExpandedOuterInsetRatio));

        // Samples moving inward along the top vertical centerline must increase monotonically from 0 to 1.
        var previous = 0.0;
        for (var y = 1; y <= size / 2; y++)
        {
            var alpha = ArtworkBitmapProcessor.ComputeRoundedSquareFeatherMask(
                size * 0.5, y + 0.5, size,
                ArtworkBitmapProcessor.ExpandedCornerRadiusRatio,
                ArtworkBitmapProcessor.ExpandedFeatherRatio,
                ArtworkBitmapProcessor.ExpandedOuterInsetRatio);
            Assert.True(alpha >= previous, $"Expected monotonic mask at y={y}: {alpha} < {previous}");
            previous = alpha;
        }
    }

    [Fact]
    public void CreateSoftEdgeSquarePremultipliedBgra8_CenterCropsWidescreenAndMaintainsPremultipliedInvariant()
    {
        const int srcWidth = 40;
        const int srcHeight = 20;
        var src = new byte[srcWidth * srcHeight * 4];
        for (var y = 0; y < srcHeight; y++)
        {
            for (var x = 0; x < srcWidth; x++)
            {
                var idx = ((y * srcWidth) + x) * 4;
                var isCenterSquare = x >= 10 && x < 30;
                src[idx] = isCenterSquare ? (byte)200 : (byte)0;     // B
                src[idx + 1] = isCenterSquare ? (byte)160 : (byte)0; // G
                src[idx + 2] = isCenterSquare ? (byte)20 : (byte)255; // R
                src[idx + 3] = 255;
            }
        }

        const int targetSize = 64;
        var dst = ArtworkBitmapProcessor.CreateSoftEdgeSquarePremultipliedBgra8(
            src,
            srcWidth,
            srcHeight,
            targetSize,
            ArtworkBitmapProcessor.ExpandedCornerRadiusRatio,
            ArtworkBitmapProcessor.ExpandedFeatherRatio,
            ArtworkBitmapProcessor.ExpandedOuterInsetRatio);

        Assert.Equal(targetSize * targetSize * 4, dst.Length);

        // Center pixel must reflect only the cropped teal center square with zero red wings bleed.
        var centerIdx = (((targetSize / 2) * targetSize) + (targetSize / 2)) * 4;
        Assert.Equal(200, dst[centerIdx]);
        Assert.Equal(160, dst[centerIdx + 1]);
        Assert.Equal(20, dst[centerIdx + 2]);
        Assert.Equal(255, dst[centerIdx + 3]);

        // Top-left corner pixel must be zeroed cleanly (no outline ring).
        Assert.Equal(0, dst[0]);
        Assert.Equal(0, dst[1]);
        Assert.Equal(0, dst[2]);
        Assert.Equal(0, dst[3]);

        // Every pixel must obey strict premultiplied BGRA invariant (B,G,R <= A).
        for (var i = 0; i < dst.Length; i += 4)
        {
            var a = dst[i + 3];
            Assert.True(dst[i] <= a && dst[i + 1] <= a && dst[i + 2] <= a,
                $"Invalid premultiplied pixel at byte {i}: B={dst[i]}, G={dst[i + 1]}, R={dst[i + 2]}, A={a}");
        }
    }

    [Fact]
    public void CreateSoftEdgeSquarePremultipliedBgra8_WhiteCoverFadesNeutrallyWithoutDarkFringe()
    {
        const int size = 96;
        var white = new byte[size * size * 4];
        Array.Fill<byte>(white, 255);

        var feathered = ArtworkBitmapProcessor.CreateSoftEdgeSquarePremultipliedBgra8(
            white,
            size,
            size,
            size,
            ArtworkBitmapProcessor.ExpandedCornerRadiusRatio,
            ArtworkBitmapProcessor.ExpandedFeatherRatio,
            ArtworkBitmapProcessor.ExpandedOuterInsetRatio);

        var hasPartialAlpha = false;
        for (var i = 0; i < feathered.Length; i += 4)
        {
            var b = feathered[i];
            var g = feathered[i + 1];
            var r = feathered[i + 2];
            var a = feathered[i + 3];
            Assert.Equal(a, b);
            Assert.Equal(a, g);
            Assert.Equal(a, r);
            if (a > 10 && a < 245)
            {
                hasPartialAlpha = true;
            }
        }

        Assert.True(hasPartialAlpha, "Expected smooth semi-transparent feather zone around white cover edge.");
    }

    [Fact]
    public void ExtractAccentColorFromBgra8_ExtractsSaturatedToneAndIgnoresTransparentRegions()
    {
        const int width = 32;
        const int height = 32;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var idx = ((y * width) + x) * 4;
                if (x < width / 2)
                {
                    // Transparent pixel should be ignored.
                    pixels[idx] = 255;
                    pixels[idx + 1] = 255;
                    pixels[idx + 2] = 255;
                    pixels[idx + 3] = 0;
                }
                else
                {
                    // Warm orange (B=20, G=140, R=220).
                    pixels[idx] = 20;
                    pixels[idx + 1] = 140;
                    pixels[idx + 2] = 220;
                    pixels[idx + 3] = 255;
                }
            }
        }

        var accent = ArtworkBitmapProcessor.ExtractAccentColorFromBgra8(pixels, width, height);
        var r = (accent >> 16) & 0xFF;
        var g = (accent >> 8) & 0xFF;
        var b = accent & 0xFF;

        Assert.True(r > g && g > b, $"Expected warm orange accent ordering R>G>B, got R={r}, G={g}, B={b}");
    }

    [Fact]
    public void ComputeDecodeDimensions_PreservesAspectAndValidatesEdgeCases()
    {
        Assert.Equal((683u, 384u), ArtworkBitmapProcessor.ComputeDecodeDimensions(1920, 1080, 384));
        Assert.Equal((300u, 300u), ArtworkBitmapProcessor.ComputeDecodeDimensions(300, 300, 384));
        Assert.Equal((0u, 0u), ArtworkBitmapProcessor.ComputeDecodeDimensions(0, 100, 384));
    }
}

