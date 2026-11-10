using Pulse.Core.Services;

namespace Pulse.Tests.Services;

public sealed class LiquidGlassBitmapProcessorTests
{
    [Fact]
    public void ProcessBgra8_SmoothsHighFrequencyStepAndPreservesDimensions()
    {
        const int width = 32;
        const int height = 16;
        var src = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var idx = (y * width + x) * 4;
                var val = (byte)(x < width / 2 ? 20 : 235);
                src[idx] = val;
                src[idx + 1] = val;
                src[idx + 2] = val;
                src[idx + 3] = 255;
            }
        }

        var blurred = LiquidGlassBitmapProcessor.ProcessBgra8(src, width, height, blurRadius: 6, vibrancyBoost: 1.0, addFrostGrain: false);

        Assert.Equal(src.Length, blurred.Length);
        // Step edge at center should now transition smoothly instead of jumping directly 20 -> 235.
        var leftCenter = blurred[(8 * width + 14) * 4];
        var rightCenter = blurred[(8 * width + 17) * 4];
        Assert.InRange(leftCenter, 40, 140);
        Assert.InRange(rightCenter, 115, 215);
    }

    [Fact]
    public void ProcessBgra8_VibrancyBoostEnrichesChromaWithoutOverflowAndHandlesTinyImages()
    {
        var tiny = new byte[] { 60, 100, 180, 255, 128, 128, 128, 255 };
        var result = LiquidGlassBitmapProcessor.ProcessBgra8(tiny, 2, 1, blurRadius: 3, vibrancyBoost: 1.35, addFrostGrain: false);

        Assert.Equal(8, result.Length);
        // With vibrancy > 1 and zero blur spread (wait: blur mixes, check single pixel preserves chroma).
        var single = new byte[] { 60, 100, 180, 255 };
        var boosted = LiquidGlassBitmapProcessor.ProcessBgra8(single, 1, 1, blurRadius: 4, vibrancyBoost: 1.35, addFrostGrain: false);
        var spreadBefore = 180 - 60;
        var spreadAfter = boosted[2] - boosted[0];
        Assert.True(spreadAfter > spreadBefore, $"Expected chroma spread {spreadAfter} > {spreadBefore}");
        Assert.Equal(255, boosted[3]);
    }
}

