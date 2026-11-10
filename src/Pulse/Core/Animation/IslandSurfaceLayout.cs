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

    public readonly record struct PixelRgnRect(int Left, int Top, int Right, int Bottom, int CornerDiameter);

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

