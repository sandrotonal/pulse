namespace Pulse.Core.Services;

public sealed record GlassSurfacePalette(
    bool IsLightTheme,
    uint SurfaceBackgroundArgb,
    uint FallbackBackgroundArgb,
    uint FrostTintRgb,
    double FrostTintOpacity,
    uint BorderRimTopArgb,
    uint BorderRimBottomArgb,
    uint SheenTopArgb,
    uint PrimaryTextArgb,
    uint SecondaryTextArgb,
    uint PillControlBackgroundArgb,
    uint AccentArgb,
    uint PlayButtonBackgroundArgb,
    uint PlayButtonForegroundArgb,
    uint DetachedTintArgb,
    uint HiddenAnchorArgb);

/// <summary>
/// Pure color, opacity, contrast, and Liquid Glass material engine shared by
/// IslandWindow and DetachedPill.
/// </summary>
public static class LiquidGlassEngine
{
    public const uint DefaultAccentArgb = 0xFF007AFFu;
    public const double MinTextContrastRatio = 4.5;

    public static bool ResolveIsLightTheme(string? theme, bool systemIsLightTheme)
    {
        if (string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return systemIsLightTheme;
    }

    public static GlassSurfacePalette ComputeIslandPalette(
        bool isLightTheme,
        bool useTransparency,
        int transparencyLevel,
        bool dynamicColorTint,
        uint artworkAccentColor,
        int artworkTintStrength,
        bool hasFrostBitmap)
    {
        var clampedTransparency = Math.Clamp(transparencyLevel, 0, 100);
        var clampedTintStrength = Math.Clamp(artworkTintStrength, 0, 100);
        var rawAccent = dynamicColorTint && (artworkAccentColor & 0x00FFFFFFu) != 0
            ? (0xFF000000u | (artworkAccentColor & 0x00FFFFFFu))
            : DefaultAccentArgb;

        // Keep UI accents vivid without washing out white glyphs on bright covers.
        var uiAccent = EnsureAccentReadability(rawAccent, isLightTheme);

        // Liquid glass surface base (pure RGB, alpha applied separately so backdrop and frost never double-stack).
        var baseSurfaceRgb = isLightTheme ? 0xFFF4F6FAu : 0xFF0C1018u;

        // Cap tint mix so extreme ArtworkTintStrength preserves crisp Dark/Light surface hierarchy & WCAG contrast.
        var effectiveTintMix = dynamicColorTint
            ? (clampedTintStrength / 100.0) * (isLightTheme ? 0.28 : 0.46)
            : 0.0;
        var mixedSurfaceRgb = BlendRgb(baseSurfaceRgb, uiAccent, effectiveTintMix);

        // Smooth non-linear transparency curve:
        // Solid mode (Glass OFF): rich 95% opaque material.
        // Glass ON 0%: ~94% frosted satin; 60%: ~54% Apple Liquid Glass; 100%: ~24-28% crystal glass.
        double tintOpacity;
        if (!useTransparency)
        {
            tintOpacity = 0.95;
        }
        else
        {
            var t = clampedTransparency / 100.0;
            // Ease slightly so 40-85% slider feels responsive without losing text contrast at 100%.
            var eased = Math.Pow(t, 0.92);
            var minOpacity = isLightTheme ? 0.24 : 0.28;
            tintOpacity = 0.94 - (0.94 - minOpacity) * eased;
        }

        var alphaByte = (uint)Math.Clamp(Math.Round(tintOpacity * 255.0), 0, 255);
        var fallbackBackground = (alphaByte << 24) | (mixedSurfaceRgb & 0x00FFFFFFu);

        // Critical anti-murk rule: once the real FrostLayer blurred snapshot is rendered,
        // keep PillBackdrop.Background translucent/minimal underneath so two alpha layers never stack.
        uint surfaceBackground;
        if (!useTransparency)
        {
            surfaceBackground = fallbackBackground;
        }
        else if (hasFrostBitmap)
        {
            // Keep minimal base luminance inside clip to avoid edge fringing without muddying glass.
            surfaceBackground = (0x14u << 24) | (mixedSurfaceRgb & 0x00FFFFFFu);
        }
        else
        {
            surfaceBackground = fallbackBackground;
        }

        var primaryText = isLightTheme ? 0xFF141518u : 0xFFFFFFFFu;

        // Guarantee primary text meets >= 4.5:1 even if tint shifts luminance on solid/frosted surface.
        mixedSurfaceRgb = EnforceTextContrastAgainstSurface(mixedSurfaceRgb, primaryText, isLightTheme);
        uiAccent = EnsureContrastAgainstSurface(uiAccent, mixedSurfaceRgb, isLightTheme, 3.5);
        var secondaryText = EnsureSecondaryTextContrast(
            isLightTheme ? 0xFF52555Du : 0xCCEBEBF5u,
            mixedSurfaceRgb,
            isLightTheme);

        fallbackBackground = (alphaByte << 24) | (mixedSurfaceRgb & 0x00FFFFFFu);
        if (!useTransparency)
        {
            surfaceBackground = fallbackBackground;
        }
        else if (hasFrostBitmap)
        {
            surfaceBackground = (0x14u << 24) | (mixedSurfaceRgb & 0x00FFFFFFu);
        }
        else
        {
            surfaceBackground = fallbackBackground;
        }

        // Specular Fresnel rim highlight (stronger top light catch, gentle fade toward bottom).
        uint rimTop;
        uint rimBottom;
        uint sheenTop;
        if (isLightTheme)
        {
            rimTop = useTransparency ? 0xC8FFFFFFu : 0x24000000u;
            rimBottom = useTransparency ? 0x33000000u : 0x16000000u;
            sheenTop = useTransparency ? 0x56FFFFFFu : 0x18FFFFFFu;
        }
        else
        {
            rimTop = useTransparency ? BlendArgb(0x46FFFFFFu, uiAccent, 0.22) : 0x28FFFFFFu;
            rimBottom = useTransparency ? 0x14FFFFFFu : 0x10FFFFFFu;
            sheenTop = useTransparency ? 0x2EFFFFFFu : 0x12FFFFFFu;
        }

        var pillControlBg = isLightTheme
            ? (useTransparency ? 0x1A000000u : 0x14000000u)
            : BlendArgb(useTransparency ? 0x28FFFFFFu : 0x22FFFFFFu, uiAccent, dynamicColorTint ? 0.14 : 0.0);

        var playBg = uiAccent;
        var playFg = PickContrastingForeground(playBg);

        var detachedAlpha = useTransparency
            ? (uint)Math.Clamp(Math.Round(Math.Max(0.42, tintOpacity) * 255.0), 0, 255)
            : 0xF2u;
        var detachedTint = (detachedAlpha << 24) | (mixedSurfaceRgb & 0x00FFFFFFu);
        var hiddenAnchor = isLightTheme ? 0x991C1C1Eu : 0x99FFFFFFu;

        return new GlassSurfacePalette(
            IsLightTheme: isLightTheme,
            SurfaceBackgroundArgb: surfaceBackground,
            FallbackBackgroundArgb: fallbackBackground,
            FrostTintRgb: 0xFF000000u | (mixedSurfaceRgb & 0x00FFFFFFu),
            FrostTintOpacity: tintOpacity,
            BorderRimTopArgb: rimTop,
            BorderRimBottomArgb: rimBottom,
            SheenTopArgb: sheenTop,
            PrimaryTextArgb: primaryText,
            SecondaryTextArgb: secondaryText,
            PillControlBackgroundArgb: pillControlBg,
            AccentArgb: uiAccent,
            PlayButtonBackgroundArgb: playBg,
            PlayButtonForegroundArgb: playFg,
            DetachedTintArgb: detachedTint,
            HiddenAnchorArgb: hiddenAnchor);
    }

    public static double ComputeContrastRatio(uint colorA, uint colorB)
    {
        var lumA = RelativeLuminance(colorA);
        var lumB = RelativeLuminance(colorB);
        var lighter = Math.Max(lumA, lumB);
        var darker = Math.Min(lumA, lumB);
        return (lighter + 0.05) / (darker + 0.05);
    }

    public static double ComputeCompositedContrastRatio(uint foregroundArgb, uint backgroundRgb)
    {
        var composited = CompositeOver(foregroundArgb, backgroundRgb);
        return ComputeContrastRatio(composited, 0xFF000000u | (backgroundRgb & 0x00FFFFFFu));
    }

    public static uint CompositeOver(uint foregroundArgb, uint backgroundRgb)
    {
        var alpha = ((foregroundArgb >> 24) & 0xFF) / 255.0;
        if (alpha >= 0.999)
        {
            return 0xFF000000u | (foregroundArgb & 0x00FFFFFFu);
        }

        return BlendRgb(0xFF000000u | (backgroundRgb & 0x00FFFFFFu), foregroundArgb, alpha);
    }

    public static uint PickContrastingForeground(uint backgroundArgb)
    {
        var whiteContrast = ComputeContrastRatio(0xFFFFFFFFu, backgroundArgb);
        var darkContrast = ComputeContrastRatio(0xFF111215u, backgroundArgb);
        return whiteContrast >= darkContrast ? 0xFFFFFFFFu : 0xFF111215u;
    }

    public static uint BlendRgb(uint baseRgb, uint overlayRgb, double amount)
    {
        var t = Math.Clamp(amount, 0.0, 1.0);
        var br = (baseRgb >> 16) & 0xFF;
        var bg = (baseRgb >> 8) & 0xFF;
        var bb = baseRgb & 0xFF;

        var or = (overlayRgb >> 16) & 0xFF;
        var og = (overlayRgb >> 8) & 0xFF;
        var ob = overlayRgb & 0xFF;

        var r = (uint)Math.Round(br + (or - (double)br) * t);
        var g = (uint)Math.Round(bg + (og - (double)bg) * t);
        var b = (uint)Math.Round(bb + (ob - (double)bb) * t);
        return 0xFF000000u | (r << 16) | (g << 8) | b;
    }

    public static uint BlendArgb(uint baseArgb, uint overlayRgb, double amount)
    {
        var alpha = (baseArgb >> 24) & 0xFF;
        var mixedRgb = BlendRgb(baseArgb, overlayRgb, amount) & 0x00FFFFFFu;
        return (alpha << 24) | mixedRgb;
    }

    private static uint EnsureAccentReadability(uint accentArgb, bool isLightTheme)
    {
        var baseSurface = isLightTheme ? 0xFFF4F6FAu : 0xFF0C1018u;
        return EnsureContrastAgainstSurface(accentArgb, baseSurface, isLightTheme, 3.5);
    }

    private static uint EnsureContrastAgainstSurface(uint colorArgb, uint surfaceRgb, bool isLightTheme, double minContrastRatio)
    {
        var opaqueColor = 0xFF000000u | (colorArgb & 0x00FFFFFFu);
        var opaqueSurface = 0xFF000000u | (surfaceRgb & 0x00FFFFFFu);
        if (ComputeContrastRatio(opaqueColor, opaqueSurface) >= minContrastRatio)
        {
            return opaqueColor;
        }

        var anchor = isLightTheme ? 0xFF0F172Au : 0xFFFFFFFFu;
        for (var step = 1; step <= 20; step++)
        {
            var candidate = BlendRgb(opaqueColor, anchor, step * 0.05);
            if (ComputeContrastRatio(candidate, opaqueSurface) >= minContrastRatio)
            {
                return candidate;
            }
        }

        return anchor;
    }

    private static uint EnsureSecondaryTextContrast(uint secondaryArgb, uint surfaceRgb, bool isLightTheme)
    {
        var opaqueSurface = 0xFF000000u | (surfaceRgb & 0x00FFFFFFu);
        if (ComputeCompositedContrastRatio(secondaryArgb, opaqueSurface) >= MinTextContrastRatio)
        {
            return secondaryArgb;
        }

        var alpha = (secondaryArgb >> 24) & 0xFF;
        var anchor = isLightTheme ? 0xFF141518u : 0xFFFFFFFFu;
        for (var step = 1; step <= 20; step++)
        {
            var rgbCandidate = BlendRgb(0xFF000000u | (secondaryArgb & 0x00FFFFFFu), anchor, step * 0.05);
            var candidateAlpha = Math.Clamp((uint)Math.Round(alpha + (255 - (double)alpha) * (step * 0.05)), 0u, 255u);
            var candidate = (candidateAlpha << 24) | (rgbCandidate & 0x00FFFFFFu);
            if (ComputeCompositedContrastRatio(candidate, opaqueSurface) >= MinTextContrastRatio)
            {
                return candidate;
            }
        }

        return anchor;
    }

    private static uint EnforceTextContrastAgainstSurface(uint surfaceRgb, uint primaryText, bool isLightTheme)
    {
        var current = 0xFF000000u | (surfaceRgb & 0x00FFFFFFu);
        if (ComputeContrastRatio(primaryText, current) >= MinTextContrastRatio)
        {
            return current;
        }

        var anchor = isLightTheme ? 0xFFF4F6FAu : 0xFF0B0E14u;
        for (var step = 1; step <= 10; step++)
        {
            var adjusted = BlendRgb(current, anchor, step * 0.1);
            if (ComputeContrastRatio(primaryText, adjusted) >= MinTextContrastRatio)
            {
                return adjusted;
            }
        }

        return anchor;
    }

    private static double RelativeLuminance(uint argb)
    {
        var r = Linearize(((argb >> 16) & 0xFF) / 255.0);
        var g = Linearize(((argb >> 8) & 0xFF) / 255.0);
        var b = Linearize((argb & 0xFF) / 255.0);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static double Linearize(double channel) =>
        channel <= 0.03928
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
}

