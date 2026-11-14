using Pulse.Core.Services;

namespace Pulse.Tests.Services;

public sealed class LiquidGlassEngineTests
{
    [Theory]
    [InlineData("Light", false, true)]
    [InlineData("Dark", true, false)]
    [InlineData("System", true, true)]
    [InlineData("System", false, false)]
    [InlineData(null, true, true)]
    public void ResolveIsLightTheme_HandlesExplicitAndSystemModes(string? setting, bool osLight, bool expected)
    {
        Assert.Equal(expected, LiquidGlassEngine.ResolveIsLightTheme(setting, osLight));
    }

    [Fact]
    public void ComputeIslandPalette_DecouplesBackdropWhenFrostLoaded_AndIncreasesClarityFrom0To100()
    {
        var opaqueGlass = LiquidGlassEngine.ComputeIslandPalette(
            isLightTheme: false,
            useTransparency: true,
            transparencyLevel: 0,
            dynamicColorTint: false,
            artworkAccentColor: 0xFF007AFFu,
            artworkTintStrength: 25,
            hasFrostBitmap: true);

        var midGlass = LiquidGlassEngine.ComputeIslandPalette(
            isLightTheme: false,
            useTransparency: true,
            transparencyLevel: 60,
            dynamicColorTint: false,
            artworkAccentColor: 0xFF007AFFu,
            artworkTintStrength: 25,
            hasFrostBitmap: true);

        var crystalGlass = LiquidGlassEngine.ComputeIslandPalette(
            isLightTheme: false,
            useTransparency: true,
            transparencyLevel: 100,
            dynamicColorTint: false,
            artworkAccentColor: 0xFF007AFFu,
            artworkTintStrength: 25,
            hasFrostBitmap: true);

        Assert.True(opaqueGlass.FrostTintOpacity > midGlass.FrostTintOpacity);
        Assert.True(midGlass.FrostTintOpacity > crystalGlass.FrostTintOpacity);
        Assert.True(crystalGlass.FrostTintOpacity <= 0.30);

        // Backdrop must stay minimal underneath an active FrostLayer so two opaque fills don't double-murk.
        var backdropAlpha = (crystalGlass.SurfaceBackgroundArgb >> 24) & 0xFF;
        Assert.True(backdropAlpha <= 0x20, $"Expected minimal backdrop alpha behind frost, got 0x{backdropAlpha:X2}");
    }

    [Fact]
    public void ComputeIslandPalette_EnforcesMinimumTextContrastWithVibrantYellowCover()
    {
        var paletteDark = LiquidGlassEngine.ComputeIslandPalette(
            isLightTheme: false,
            useTransparency: false,
            transparencyLevel: 100,
            dynamicColorTint: true,
            artworkAccentColor: 0xFFFFFF33u,
            artworkTintStrength: 100,
            hasFrostBitmap: false);

        var darkRatio = LiquidGlassEngine.ComputeContrastRatio(paletteDark.PrimaryTextArgb, paletteDark.FrostTintRgb);
        Assert.True(darkRatio >= LiquidGlassEngine.MinTextContrastRatio, $"Dark contrast ratio too low: {darkRatio:F2}");

        var paletteLight = LiquidGlassEngine.ComputeIslandPalette(
            isLightTheme: true,
            useTransparency: false,
            transparencyLevel: 100,
            dynamicColorTint: true,
            artworkAccentColor: 0xFFFFFF33u,
            artworkTintStrength: 100,
            hasFrostBitmap: false);

        var lightRatio = LiquidGlassEngine.ComputeContrastRatio(paletteLight.PrimaryTextArgb, paletteLight.FrostTintRgb);
        Assert.True(lightRatio >= LiquidGlassEngine.MinTextContrastRatio, $"Light contrast ratio too low: {lightRatio:F2}");
    }

    [Fact]
    public void PickContrastingForeground_SelectsDarkGlyphOnBrightPlayButtonAndWhiteOnDarkButton()
    {
        Assert.Equal(0xFF111215u, LiquidGlassEngine.PickContrastingForeground(0xFFE6FF00u));
        Assert.Equal(0xFFFFFFFFu, LiquidGlassEngine.PickContrastingForeground(0xFF0A2463u));
    }

    [Theory]
    [InlineData(0xFFFFFFFFu, true)]
    [InlineData(0xFFFFFFFFu, false)]
    [InlineData(0xFF000000u, true)]
    [InlineData(0xFF000000u, false)]
    [InlineData(0xFF30D158u, false)]
    [InlineData(0xFFFFD60Au, true)]
    public void ComputeIslandPalette_MaintainsPlayButtonAndTextContrastAcrossExtremeCovers(uint coverColor, bool isLight)
    {
        var palette = LiquidGlassEngine.ComputeIslandPalette(
            isLightTheme: isLight,
            useTransparency: true,
            transparencyLevel: 65,
            dynamicColorTint: true,
            artworkAccentColor: coverColor,
            artworkTintStrength: 100,
            hasFrostBitmap: false);

        var playRatio = LiquidGlassEngine.ComputeContrastRatio(palette.PlayButtonForegroundArgb, palette.PlayButtonBackgroundArgb);
        Assert.True(playRatio >= LiquidGlassEngine.MinTextContrastRatio, $"Play button glyph contrast {playRatio:F2} < 4.5");

        var textRatio = LiquidGlassEngine.ComputeContrastRatio(palette.PrimaryTextArgb, palette.FrostTintRgb);
        Assert.True(textRatio >= LiquidGlassEngine.MinTextContrastRatio, $"Surface text contrast {textRatio:F2} < 4.5");
    }

    [Fact]
    public void ComputeIslandPalette_SwitchesFromFallbackAlphaToMinimalBackdropWhenFrostArrives()
    {
        var beforeFrost = LiquidGlassEngine.ComputeIslandPalette(
            isLightTheme: true,
            useTransparency: true,
            transparencyLevel: 60,
            dynamicColorTint: false,
            artworkAccentColor: 0xFF007AFFu,
            artworkTintStrength: 25,
            hasFrostBitmap: false);

        var afterFrost = LiquidGlassEngine.ComputeIslandPalette(
            isLightTheme: true,
            useTransparency: true,
            transparencyLevel: 60,
            dynamicColorTint: false,
            artworkAccentColor: 0xFF007AFFu,
            artworkTintStrength: 25,
            hasFrostBitmap: true);

        var alphaBefore = (beforeFrost.SurfaceBackgroundArgb >> 24) & 0xFF;
        var alphaAfter = (afterFrost.SurfaceBackgroundArgb >> 24) & 0xFF;

        Assert.True(alphaBefore > 0x70, $"Expected readable fallback opacity before frost, got 0x{alphaBefore:X2}");
        Assert.Equal(0x14u, alphaAfter);
    }

    [Theory]
    [InlineData(0xFFFFFF33u, true)]
    [InlineData(0xFF30D158u, true)]
    [InlineData(0xFF7CE8FFu, true)]
    [InlineData(0xFF121826u, false)]
    [InlineData(0xFF0A1428u, false)]
    public void ComputeIslandPalette_EnsuresAccentAndSecondaryTextContrastAcrossLightAndDark(uint coverColor, bool isLight)
    {
        var palette = LiquidGlassEngine.ComputeIslandPalette(
            isLightTheme: isLight,
            useTransparency: false,
            transparencyLevel: 60,
            dynamicColorTint: true,
            artworkAccentColor: coverColor,
            artworkTintStrength: 100,
            hasFrostBitmap: false);

        var accentSurfaceRatio = LiquidGlassEngine.ComputeContrastRatio(palette.AccentArgb, palette.FrostTintRgb);
        Assert.True(accentSurfaceRatio >= 3.5, $"Accent contrast {accentSurfaceRatio:F2} < 3.5 against surface");

        var secondaryRatio = LiquidGlassEngine.ComputeCompositedContrastRatio(palette.SecondaryTextArgb, palette.FrostTintRgb);
        Assert.True(secondaryRatio >= LiquidGlassEngine.MinTextContrastRatio, $"Secondary text contrast {secondaryRatio:F2} < 4.5");
    }

    [Theory]
    [InlineData("light", false, true)]
    [InlineData("DARK", true, false)]
    [InlineData("", true, true)]
    public void ResolveIsLightTheme_HandlesCaseInsensitiveAndEmptyInputs(string setting, bool osLight, bool expected)
    {
        Assert.Equal(expected, LiquidGlassEngine.ResolveIsLightTheme(setting, osLight));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComputeIslandPalette_PreservesDefaultBlueAccentOnNeutralSurface(bool isLight)
    {
        var palette = LiquidGlassEngine.ComputeIslandPalette(
            isLightTheme: isLight,
            useTransparency: true,
            transparencyLevel: 60,
            dynamicColorTint: false,
            artworkAccentColor: 0x00000000u,
            artworkTintStrength: 0,
            hasFrostBitmap: false);

        Assert.Equal(LiquidGlassEngine.DefaultAccentArgb, palette.AccentArgb);
    }
}
