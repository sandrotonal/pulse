namespace Pulse.Core.Animation;

public static class IslandSurfaceLayout
{
    public const double DefaultHostWidthDip = 560.0;
    public const double DefaultHostHeightDip = 300.0;
    public const double SurfaceTopInsetDip = 3.0;
    public const double RegionAntialiasMarginDip = 3.0;
    public const double DefaultHostWindowHeightDip = DefaultHostHeightDip + (SurfaceTopInsetDip * 2.0);
    public const double SplitGapDip = 10.0;
    public const double DetachedPillWidthDip = 66.0;
    public const double DetachedPillHeightDip = 40.0;
    public const double HiddenAnchorWidthDip = 48.0;
    public const double HiddenAnchorHeightDip = 16.0;
    public const double HitTestPaddingDip = 4.0;
    public const double ExpansionOvershootPaddingDip = 40.0;

    public const double MinCompactScale = 0.80;
    public const double MaxCompactScale = 1.25;

    public readonly record struct PixelRgnRect(int Left, int Top, int Right, int Bottom, int CornerDiameter);

    public sealed record CompactMetrics(
        double Scale,
        double PillHeight,
        double PillCornerRadius,
        double SplitGap,
        double CompactHorizontalPadding,
        double IdleInnerMarginH,
        double IdleBlockMinWidth,
        double IdleItemSpacing,
        double IdleClockFontSize,
        double IdleClockLineHeight,
        double IdleDateFontSize,
        double IdleDateLineHeight,
        double IdleDividerWidth,
        double IdleDividerHeight,
        double IdleWeatherRowHeight,
        double IdleWeatherHeaderSpacing,
        double IdleWeatherIconSize,
        double IdleWeatherTempFontSize,
        double IdleWeatherTempLineHeight,
        double IdleWeatherCityFontSize,
        double IdleWeatherCityLineHeight,
        double IdleWeatherCityMaxWidth,
        double IdleBrandSpacing,
        double IdleBrandIconSize,
        double IdleBrandIconCornerRadius,
        double IdleBrandFontSize,
        double CompactArtSize,
        double CompactArtCornerRadius,
        double CompactArtMarginRight,
        double CompactArtFallbackGlyphSize,
        double CompactTrackTitleFontSize,
        double CompactTrackArtistFontSize,
        double CompactTrackRightSpacing,
        double CompactTrackRightMarginLeft,
        double CompactWaveSpacing,
        double CompactWaveBarWidth,
        double CompactWaveBarHeight,
        double CompactWaveBarCornerRadius,
        double CompactActionButtonSize,
        double CompactActionButtonRadius,
        double CompactActionGlyphSize,
        double EventGlyphSize,
        double EventGlyphMarginLeft,
        double EventGlyphMarginRight,
        double EventTitleFontSize,
        double EventSubtitleFontSize,
        double EventDismissButtonSize,
        double EventDismissButtonRadius,
        double EventDismissButtonMarginLeft,
        double EventDismissGlyphSize,
        double FocusToggleGlyphSize,
        double FileOpNameFontSize,
        double FileOpProgressHeight,
        double FileOpProgressRadius,
        double FileOpStatusFontSize,
        double FileOpStatusMarginLeft,
        double DetachedPillWidth,
        double DetachedPillHeight,
        double DetachedPillCornerRadius,
        double DetachedFrostSheenHeight,
        double DetachedStackSpacing,
        double DetachedPaddingH,
        double DetachedGlyphSize,
        double DetachedBadgeFontSize);

    public static double ClampCompactScale(double scale) =>
        double.IsFinite(scale) ? Math.Clamp(scale, MinCompactScale, MaxCompactScale) : 1.0;

    /// <summary>
    /// Computes synchronous, pill-proportioned typography, padding, icon and control
    /// metrics for all compact capsule states across the 80%–125% IslandScale range.
    /// Keeps vertical idle stacks balanced (70% of pill height, equal top/bottom margins)
    /// and preserves horizontal corner-arc clearance on rounded capsule ends.
    /// </summary>
    public static CompactMetrics ComputeCompactMetrics(double scale)
    {
        double s = ClampCompactScale(scale);
        return new CompactMetrics(
            Scale: s,
            PillHeight: 40.0 * s,
            PillCornerRadius: 20.0 * s,
            SplitGap: SplitGapDip * s,
            CompactHorizontalPadding: 12.0 * s,
            IdleInnerMarginH: 4.0 * s,
            IdleBlockMinWidth: 52.0 * s,
            IdleItemSpacing: 10.0 * s,
            IdleClockFontSize: 15.0 * s,
            IdleClockLineHeight: 17.0 * s,
            IdleDateFontSize: 9.5 * s,
            IdleDateLineHeight: 11.0 * s,
            IdleDividerWidth: Math.Max(1.0, Math.Round(1.0 * s)),
            IdleDividerHeight: 18.0 * s,
            IdleWeatherRowHeight: 17.0 * s,
            IdleWeatherHeaderSpacing: 4.0 * s,
            IdleWeatherIconSize: 13.5 * s,
            IdleWeatherTempFontSize: 13.5 * s,
            IdleWeatherTempLineHeight: 17.0 * s,
            IdleWeatherCityFontSize: 9.5 * s,
            IdleWeatherCityLineHeight: 11.0 * s,
            IdleWeatherCityMaxWidth: 72.0 * s,
            IdleBrandSpacing: 6.0 * s,
            IdleBrandIconSize: 12.0 * s,
            IdleBrandIconCornerRadius: 3.0 * s,
            IdleBrandFontSize: 10.0 * s,
            CompactArtSize: 26.0 * s,
            CompactArtCornerRadius: 7.0 * s,
            CompactArtMarginRight: 8.0 * s,
            CompactArtFallbackGlyphSize: 11.0 * s,
            CompactTrackTitleFontSize: 11.5 * s,
            CompactTrackArtistFontSize: 9.5 * s,
            CompactTrackRightSpacing: 6.0 * s,
            CompactTrackRightMarginLeft: 8.0 * s,
            CompactWaveSpacing: 2.0 * s,
            CompactWaveBarWidth: Math.Max(1.5, 2.5 * s),
            CompactWaveBarHeight: 13.0 * s,
            CompactWaveBarCornerRadius: Math.Max(0.75, 1.25 * s),
            CompactActionButtonSize: 24.0 * s,
            CompactActionButtonRadius: 12.0 * s,
            CompactActionGlyphSize: 12.0 * s,
            EventGlyphSize: 15.0 * s,
            EventGlyphMarginLeft: 2.0 * s,
            EventGlyphMarginRight: 10.0 * s,
            EventTitleFontSize: 12.5 * s,
            EventSubtitleFontSize: 10.5 * s,
            EventDismissButtonSize: 22.0 * s,
            EventDismissButtonRadius: 11.0 * s,
            EventDismissButtonMarginLeft: 6.0 * s,
            EventDismissGlyphSize: 9.0 * s,
            FocusToggleGlyphSize: 11.0 * s,
            FileOpNameFontSize: 12.0 * s,
            FileOpProgressHeight: Math.Max(2.0, 3.0 * s),
            FileOpProgressRadius: Math.Max(1.0, 1.5 * s),
            FileOpStatusFontSize: 10.5 * s,
            FileOpStatusMarginLeft: 8.0 * s,
            DetachedPillWidth: DetachedPillWidthDip * s,
            DetachedPillHeight: DetachedPillHeightDip * s,
            DetachedPillCornerRadius: 20.0 * s,
            DetachedFrostSheenHeight: 16.0 * s,
            DetachedStackSpacing: 3.0 * s,
            DetachedPaddingH: 4.0 * s,
            DetachedGlyphSize: 11.0 * s,
            DetachedBadgeFontSize: 10.5 * s);
    }

    public readonly record struct DipRect(double Left, double Top, double Right, double Bottom)
    {
        public double Width => Right - Left;
        public double Height => Bottom - Top;

        public bool Contains(double x, double y, double padding = 0.0)
            => x >= (Left - padding)
            && x <= (Right + padding)
            && y >= (Top - padding)
            && y <= (Bottom + padding);
    }

    public static (double LeadingOffsetX, double TrailingOffsetX) ComputeSplitOffsets(
        double leadingWidth,
        double detachedWidth = DetachedPillWidthDip,
        double gap = SplitGapDip)
    {
        double leadingOffsetX = -((detachedWidth + gap) / 2.0);
        double trailingOffsetX = (leadingWidth + gap) / 2.0;
        return (leadingOffsetX, trailingOffsetX);
    }

    public static DipRect ComputeCenteredPillBounds(
        double hostWidth,
        double pillWidth,
        double pillHeight,
        double offsetX = 0.0)
    {
        double left = ((hostWidth - pillWidth) / 2.0) + offsetX;
        double right = left + pillWidth;
        return new DipRect(left, 0.0, right, pillHeight);
    }

    public static double ComputeExpansionRegionWidth(
        double currentWidth,
        double targetWidth,
        double hostWidth = DefaultHostWidthDip)
    {
        double desired = Math.Max(currentWidth, targetWidth) + ExpansionOvershootPaddingDip;
        return Math.Min(hostWidth, desired);
    }

    public static double ComputeExpansionRegionHeight(
        double currentHeight,
        double targetHeight,
        double hostHeight = DefaultHostHeightDip)
    {
        double desired = Math.Max(currentHeight, targetHeight) + (ExpansionOvershootPaddingDip * 0.6);
        return Math.Min(hostHeight, desired);
    }

    public static bool IsPointInsideIsland(
        double localDipX,
        double localDipY,
        double hostWidth,
        bool isHidden,
        double pillWidth,
        double pillHeight,
        double surfaceOffsetX,
        bool isSplit,
        double detachedWidth,
        double detachedHeight,
        double detachedOffsetX)
    {
        if (isHidden)
        {
            var anchor = ComputeCenteredPillBounds(hostWidth, HiddenAnchorWidthDip, HiddenAnchorHeightDip);
            return anchor.Contains(localDipX, localDipY);
        }

        var surface = ComputeCenteredPillBounds(hostWidth, pillWidth, pillHeight, surfaceOffsetX);
        if (surface.Contains(localDipX, localDipY, HitTestPaddingDip))
        {
            return true;
        }

        if (isSplit)
        {
            var detached = ComputeCenteredPillBounds(hostWidth, detachedWidth, detachedHeight, detachedOffsetX);
            if (detached.Contains(localDipX, localDipY, HitTestPaddingDip))
            {
                return true;
            }
        }

        return false;
    }

    public static (int ScreenX, int ScreenY) ExtractSignedScreenCoordinates(IntPtr lParam)
    {
        long raw = lParam.ToInt64();
        int screenX = unchecked((short)(raw & 0xFFFF));
        int screenY = unchecked((short)((raw >> 16) & 0xFFFF));
        return (screenX, screenY);
    }

    /// <summary>
    /// Computes an outward-padded, concentrically rounded GDI Win32 HRGN rectangle so 1-bit
    /// SetWindowRgn hit/clip boundaries sit outside DirectComposition's antialiased perimeter
    /// without jagged subpixel staircases.
    /// </summary>
    public static PixelRgnRect ComputePaddedPixelRegion(
        DipRect boundsDip,
        double verticalInsetDip,
        double cornerRadiusDip,
        double regionMarginDip,
        double dpiScale,
        int maxPixelWidth,
        int maxPixelHeight)
    {
        double safeDpi = dpiScale > 0.0 ? dpiScale : 1.0;
        int padPx = regionMarginDip > 0.0
            ? Math.Max(2, (int)Math.Ceiling(regionMarginDip * safeDpi))
            : 0;

        int safeMaxW = Math.Max(1, maxPixelWidth);
        int safeMaxH = Math.Max(1, maxPixelHeight);

        int left = Math.Clamp((int)Math.Floor(boundsDip.Left * safeDpi) - padPx, 0, safeMaxW - 1);
        int top = Math.Clamp((int)Math.Floor((verticalInsetDip + boundsDip.Top) * safeDpi) - padPx, 0, safeMaxH - 1);
        int right = Math.Clamp((int)Math.Ceiling(boundsDip.Right * safeDpi) + padPx, left + 1, safeMaxW);
        int bottom = Math.Clamp((int)Math.Ceiling((verticalInsetDip + boundsDip.Bottom) * safeDpi) + padPx, top + 1, safeMaxH);

        int width = Math.Max(1, right - left);
        int height = Math.Max(1, bottom - top);
        int rawCornerDiameter = (int)Math.Round(Math.Max(2.0, cornerRadiusDip * 2.0) * safeDpi) + (padPx * 2);
        int cornerDiameter = Math.Clamp(rawCornerDiameter, 2, Math.Min(width, height));

        return new PixelRgnRect(left, top, right, bottom, cornerDiameter);
    }
}

