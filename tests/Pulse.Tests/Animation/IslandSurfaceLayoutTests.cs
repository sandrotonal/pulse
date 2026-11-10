using Pulse.Core.Animation;

namespace Pulse.Tests.Animation;

public sealed class IslandSurfaceLayoutTests
{
    [Fact]
    public void SplitOffsetsCenterDualCapsulesSymmetricallyInAnyHostWidth()
    {
        const double hostWidth = IslandSurfaceLayout.DefaultHostWidthDip; // 560
        const double leadingWidth = 196.0;
        const double detachedWidth = IslandSurfaceLayout.DetachedPillWidthDip; // 66
        const double gap = IslandSurfaceLayout.SplitGapDip; // 10

        var (leadingOffsetX, trailingOffsetX) = IslandSurfaceLayout.ComputeSplitOffsets(leadingWidth, detachedWidth, gap);
        Assert.Equal(-38.0, leadingOffsetX, precision: 3);
        Assert.Equal(103.0, trailingOffsetX, precision: 3);

        var leadingRect = IslandSurfaceLayout.ComputeCenteredPillBounds(hostWidth, leadingWidth, 40.0, leadingOffsetX);
        var detachedRect = IslandSurfaceLayout.ComputeCenteredPillBounds(hostWidth, detachedWidth, 40.0, trailingOffsetX);

        Assert.Equal(gap, detachedRect.Left - leadingRect.Right, precision: 3);
        // Combined span should be symmetrically centered in hostWidth
        double leftMargin = leadingRect.Left;
        double rightMargin = hostWidth - detachedRect.Right;
        Assert.Equal(leftMargin, rightMargin, precision: 3);
    }

    [Fact]
    public void HitTestAllowsClicksInsidePrimaryAndSplitCapsulesAndBlocksCanvasMargins()
    {
        const double hostWidth = IslandSurfaceLayout.DefaultHostWidthDip;
        var (leadingOffsetX, trailingOffsetX) = IslandSurfaceLayout.ComputeSplitOffsets(196.0, 66.0, 10.0);

        // Inside primary capsule
        Assert.True(IslandSurfaceLayout.IsPointInsideIsland(
            localDipX: 220.0,
            localDipY: 20.0,
            hostWidth: hostWidth,
            isHidden: false,
            pillWidth: 196.0,
            pillHeight: 40.0,
            surfaceOffsetX: leadingOffsetX,
            isSplit: true,
            detachedWidth: 66.0,
            detachedHeight: 40.0,
            detachedOffsetX: trailingOffsetX));

        // Inside detached capsule
        Assert.True(IslandSurfaceLayout.IsPointInsideIsland(
            localDipX: 380.0,
            localDipY: 20.0,
            hostWidth: hostWidth,
            isHidden: false,
            pillWidth: 196.0,
            pillHeight: 40.0,
            surfaceOffsetX: leadingOffsetX,
            isSplit: true,
            detachedWidth: 66.0,
            detachedHeight: 40.0,
            detachedOffsetX: trailingOffsetX));

        // In transparent canvas margin outside both pills
        Assert.False(IslandSurfaceLayout.IsPointInsideIsland(
            localDipX: 40.0,
            localDipY: 20.0,
            hostWidth: hostWidth,
            isHidden: false,
            pillWidth: 196.0,
            pillHeight: 40.0,
            surfaceOffsetX: leadingOffsetX,
            isSplit: true,
            detachedWidth: 66.0,
            detachedHeight: 40.0,
            detachedOffsetX: trailingOffsetX));

        // Below capsule height in compact mode
        Assert.False(IslandSurfaceLayout.IsPointInsideIsland(
            localDipX: 280.0,
            localDipY: 120.0,
            hostWidth: hostWidth,
            isHidden: false,
            pillWidth: 196.0,
            pillHeight: 40.0,
            surfaceOffsetX: leadingOffsetX,
            isSplit: true,
            detachedWidth: 66.0,
            detachedHeight: 40.0,
            detachedOffsetX: trailingOffsetX));
    }

    [Fact]
    public void ExtractSignedScreenCoordinatesHandlesNegativeMultiMonitorOffsets()
    {
        short negX = -1280;
        short negY = -45;
        long packed = ((long)(ushort)negY << 16) | (ushort)negX;

        var (x, y) = IslandSurfaceLayout.ExtractSignedScreenCoordinates(new IntPtr(packed));

        Assert.Equal(-1280, x);
        Assert.Equal(-45, y);
    }

    [Fact]
    public void ComputeExpansionRegionWidthIncludesOvershootMarginClampedToHost()
    {
        var width = IslandSurfaceLayout.ComputeExpansionRegionWidth(
            currentWidth: 216.0,
            targetWidth: 384.0,
            hostWidth: 560.0);

        Assert.True(width >= 384.0 + 32.0);
        Assert.True(width <= 560.0);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void ComputePaddedPixelRegionLeavesAntialiasingMarginAndPreservesSplitGap(double dpiScale)
    {
        int hostPixelW = (int)Math.Round(IslandSurfaceLayout.DefaultHostWidthDip * dpiScale);
        int hostPixelH = (int)Math.Round(IslandSurfaceLayout.DefaultHostWindowHeightDip * dpiScale);

        var (leadOffX, trailOffX) = IslandSurfaceLayout.ComputeSplitOffsets(196.0);
        var leadBounds = IslandSurfaceLayout.ComputeCenteredPillBounds(IslandSurfaceLayout.DefaultHostWidthDip, 196.0, 40.0, leadOffX);
        var detachBounds = IslandSurfaceLayout.ComputeCenteredPillBounds(IslandSurfaceLayout.DefaultHostWidthDip, 66.0, 40.0, trailOffX);

        var leadRgn = IslandSurfaceLayout.ComputePaddedPixelRegion(
            leadBounds,
            IslandSurfaceLayout.SurfaceTopInsetDip,
            cornerRadiusDip: 20.0,
            IslandSurfaceLayout.RegionAntialiasMarginDip,
            dpiScale,
            hostPixelW,
            hostPixelH);

        var detachRgn = IslandSurfaceLayout.ComputePaddedPixelRegion(
            detachBounds,
            IslandSurfaceLayout.SurfaceTopInsetDip,
            cornerRadiusDip: 20.0,
            IslandSurfaceLayout.RegionAntialiasMarginDip,
            dpiScale,
            hostPixelW,
            hostPixelH);

        int visualLeadTop = (int)Math.Floor(IslandSurfaceLayout.SurfaceTopInsetDip * dpiScale);
        int visualLeadBottom = (int)Math.Ceiling((IslandSurfaceLayout.SurfaceTopInsetDip + 40.0) * dpiScale);
        int visualLeadLeft = (int)Math.Floor(leadBounds.Left * dpiScale);
        int visualLeadRight = (int)Math.Ceiling(leadBounds.Right * dpiScale);

        Assert.True(leadRgn.Top <= visualLeadTop - 3);
        Assert.True(leadRgn.Bottom >= visualLeadBottom + 3);
        Assert.True(leadRgn.Left <= visualLeadLeft - 3);
        Assert.True(leadRgn.Right >= visualLeadRight + 3);
        Assert.Equal(leadRgn.Bottom - leadRgn.Top, leadRgn.CornerDiameter);

        // Even with 3 DIP outward AA margin on each capsule, the 10 DIP split gap stays unmerged.
        Assert.True(detachRgn.Left > leadRgn.Right);
    }

    [Theory]
    [InlineData(1.75, 0.80)]
    [InlineData(1.75, 1.25)]
    [InlineData(2.00, 0.80)]
    [InlineData(2.00, 1.25)]
    public void ComputePaddedPixelRegionHandlesHighDpiAndExtremeCompactScalesInSplitMode(double dpiScale, double islandScale)
    {
        var metrics = IslandSurfaceLayout.ComputeCompactMetrics(islandScale);
        int hostPixelW = (int)Math.Round(IslandSurfaceLayout.DefaultHostWidthDip * dpiScale);
        int hostPixelH = (int)Math.Round(IslandSurfaceLayout.DefaultHostWindowHeightDip * dpiScale);

        double leadWidth = 196.0 * metrics.Scale;
        var (leadOffX, trailOffX) = IslandSurfaceLayout.ComputeSplitOffsets(leadWidth, metrics.DetachedPillWidth, metrics.SplitGap);
        var leadBounds = IslandSurfaceLayout.ComputeCenteredPillBounds(IslandSurfaceLayout.DefaultHostWidthDip, leadWidth, metrics.PillHeight, leadOffX);
        var detachBounds = IslandSurfaceLayout.ComputeCenteredPillBounds(IslandSurfaceLayout.DefaultHostWidthDip, metrics.DetachedPillWidth, metrics.DetachedPillHeight, trailOffX);

        var leadRgn = IslandSurfaceLayout.ComputePaddedPixelRegion(
            leadBounds,
            IslandSurfaceLayout.SurfaceTopInsetDip,
            metrics.PillCornerRadius,
            IslandSurfaceLayout.RegionAntialiasMarginDip,
            dpiScale,
            hostPixelW,
            hostPixelH);

        var detachRgn = IslandSurfaceLayout.ComputePaddedPixelRegion(
            detachBounds,
            IslandSurfaceLayout.SurfaceTopInsetDip,
            metrics.DetachedPillCornerRadius,
            IslandSurfaceLayout.RegionAntialiasMarginDip,
            dpiScale,
            hostPixelW,
            hostPixelH);

        Assert.True(leadRgn.Top >= 0 && leadRgn.Left >= 0);
        Assert.True(detachRgn.Right <= hostPixelW && detachRgn.Bottom <= hostPixelH);
        // At 0.80 scale (SplitGap = 8 DIP) and 2x RegionMargin (6 DIP), the capsules still do not intersect or reorder.
        Assert.True(detachRgn.Left > leadRgn.Right);
    }

    [Fact]
    public void ComputePaddedPixelRegionFallsBackCleanlyOnInvalidDpiAndClampsTinyCorners()
    {
        var bounds = IslandSurfaceLayout.ComputeCenteredPillBounds(IslandSurfaceLayout.DefaultHostWidthDip, 10.0, 4.0);
        var rgn = IslandSurfaceLayout.ComputePaddedPixelRegion(
            bounds,
            verticalInsetDip: 3.0,
            cornerRadiusDip: 50.0,
            regionMarginDip: 0.0,
            dpiScale: 0.0,
            maxPixelWidth: 560,
            maxPixelHeight: 306);

        Assert.Equal(4, rgn.Bottom - rgn.Top);
        Assert.Equal(4, rgn.CornerDiameter);
    }

    [Fact]
    public void IsPointInsideIslandUsesHiddenAnchorExactBoundsWhenHiddenAndChecksHeights()
    {
        double centerX = IslandSurfaceLayout.DefaultHostWidthDip / 2.0;

        // Inside 48x16 anchor when hidden
        Assert.True(IslandSurfaceLayout.IsPointInsideIsland(centerX, 8.0, IslandSurfaceLayout.DefaultHostWidthDip, true, 216, 40, 0, false, 66, 40, 66));
        // Even 1px outside anchor (no +4 DIP hit padding when hidden)
        Assert.False(IslandSurfaceLayout.IsPointInsideIsland(centerX, 17.5, IslandSurfaceLayout.DefaultHostWidthDip, true, 216, 40, 0, false, 66, 40, 66));
        // ComputeExpansionRegionHeight reserves room within host bounds
        Assert.InRange(IslandSurfaceLayout.ComputeExpansionRegionHeight(40, 240), 260, IslandSurfaceLayout.DefaultHostHeightDip);
    }

    [Theory]
    [InlineData(false)] // Entering split (Compact -> Split)
    [InlineData(true)]  // Exiting split (Split -> Notification)
    public void ComputeTransitionEnvelopeBoundsSpansFullStartAndTargetDualCapsulesWithoutClipping(bool exitingSplit)
    {
        var metrics = IslandSurfaceLayout.ComputeCompactMetrics(1.25);
        var (splitLeadX, splitTrailX) = IslandSurfaceLayout.ComputeSplitOffsets(196.0 * metrics.Scale, metrics.DetachedPillWidth, metrics.SplitGap);

        double startW = exitingSplit ? 196.0 * metrics.Scale : 216.0 * metrics.Scale;
        double startLeadX = exitingSplit ? splitLeadX : 0.0;
        bool startDetached = exitingSplit;
        double startTrailX = exitingSplit ? splitTrailX : metrics.DetachedPillWidth;

        double targetW = exitingSplit ? 288.0 * metrics.Scale : 196.0 * metrics.Scale;
        double targetLeadX = exitingSplit ? 0.0 : splitLeadX;
        bool targetDetached = !exitingSplit;
        double targetTrailX = exitingSplit ? metrics.DetachedPillWidth : splitTrailX;

        var envelope = IslandSurfaceLayout.ComputeTransitionEnvelopeBounds(
            IslandSurfaceLayout.DefaultHostWidthDip,
            IslandSurfaceLayout.DefaultHostHeightDip,
            startW, metrics.PillHeight, startLeadX,
            targetW, metrics.PillHeight, targetLeadX,
            startDetached, startTrailX,
            targetDetached, targetTrailX,
            metrics.DetachedPillWidth, metrics.DetachedPillHeight,
            includeOvershootPadding: false);

        var splitLeadBounds = IslandSurfaceLayout.ComputeCenteredPillBounds(IslandSurfaceLayout.DefaultHostWidthDip, 196.0 * metrics.Scale, metrics.PillHeight, splitLeadX);
        var splitDetachBounds = IslandSurfaceLayout.ComputeCenteredPillBounds(IslandSurfaceLayout.DefaultHostWidthDip, metrics.DetachedPillWidth, metrics.DetachedPillHeight, splitTrailX);
        var wideBounds = IslandSurfaceLayout.ComputeCenteredPillBounds(IslandSurfaceLayout.DefaultHostWidthDip, exitingSplit ? targetW : startW, metrics.PillHeight, 0.0);

        Assert.True(envelope.Left <= Math.Min(splitLeadBounds.Left, wideBounds.Left));
        Assert.True(envelope.Right >= Math.Max(splitDetachBounds.Right, wideBounds.Right));
        Assert.Equal(0.0, envelope.Top);
        Assert.True(envelope.Bottom >= metrics.PillHeight);
    }

    [Fact]
    public void ComputeHostWindowBoundsCentersEvenOnPortraitHighDpiAndNegativeMonitors()
    {
        // Portrait 1080px monitor at 200% DPI -> winWidth = 1120 > 1080; centering must stay negative, not clamped to 0.
        var portrait = IslandSurfaceLayout.ComputeHostWindowBounds(
            workAreaLeft: 0, workAreaTop: 0, workAreaRight: 1080, workAreaBottom: 1920,
            dpiScale: 2.0, effectiveTopOffsetDip: 14.0);
        Assert.Equal(-20, portrait.X);
        Assert.Equal(28, portrait.Y);
        Assert.Equal(1120, portrait.Width);

        // Secondary monitor to the left of primary (-1920..0) at 125% DPI
        var leftMonitor = IslandSurfaceLayout.ComputeHostWindowBounds(
            workAreaLeft: -1920, workAreaTop: 0, workAreaRight: 0, workAreaBottom: 1080,
            dpiScale: 1.25, effectiveTopOffsetDip: 0.0);
        int expectedW = (int)Math.Round(560 * 1.25);
        Assert.Equal(-1920 + ((1920 - expectedW) / 2), leftMonitor.X);
        Assert.Equal(0, leftMonitor.Y);
    }

    [Theory]
    [InlineData(144, 1.5)]
    [InlineData(168, 1.75)]
    [InlineData(192, 2.0)]
    [InlineData(0, 1.0)]
    public void ExtractDpiScaleFromWParamParsesValidDpiAndFallsBackOnInvalid(int dpiWord, double expected)
    {
        IntPtr wParam = new IntPtr(((long)dpiWord << 16) | (uint)dpiWord);
        Assert.Equal(expected, IslandSurfaceLayout.ExtractDpiScaleFromWParam(wParam), precision: 3);
    }

    [Fact]
    public void WheelStepAccumulatorCollectsPrecisionTouchpadPacketsAcrossEvents()
    {
        var acc = new IslandSurfaceLayout.WheelStepAccumulator();

        // Three precision packets of -10 reach threshold (-30) on the 3rd packet -> emit 1 forward step
        Assert.Equal(0, acc.Accumulate(-10));
        Assert.Equal(0, acc.Accumulate(-10));
        Assert.Equal(-1, acc.Accumulate(-12));

        // Flipping direction clears leftover momentum smoothly before accumulating
        Assert.Equal(0, acc.Accumulate(15));
        Assert.Equal(1, acc.Accumulate(18));
    }

    [Theory]
    [InlineData(0.80)]
    [InlineData(1.00)]
    [InlineData(1.25)]
    public void ComputeCompactMetricsScalesSynchronouslyWithCapsuleBordersAndSymmetry(double scale)
    {
        var m = IslandSurfaceLayout.ComputeCompactMetrics(scale);

        Assert.Equal(scale, m.Scale, precision: 3);
        Assert.Equal(40.0 * scale, m.PillHeight, precision: 3);
        Assert.Equal(20.0 * scale, m.PillCornerRadius, precision: 3);
        Assert.Equal(10.0 * scale, m.SplitGap, precision: 3);

        // Left clock/date column and right weather/city column must share identical stacked height
        double leftHeight = m.IdleClockLineHeight + m.IdleDateLineHeight;
        double rightHeight = m.IdleWeatherRowHeight + m.IdleWeatherCityLineHeight;
        Assert.Equal(leftHeight, rightHeight, precision: 3);

        // 70% vertical fill leaves equal 15% breathing room above and below against the rounded pill rim
        Assert.Equal(0.70, leftHeight / m.PillHeight, precision: 3);
        Assert.True(m.IdleDividerHeight < leftHeight);

        // Horizontal padding + inner margin must comfortably clear the capsule corner curvature at stack top/bottom
        double topMargin = (m.PillHeight - leftHeight) * 0.5;
        double arcDy = m.PillCornerRadius - topMargin;
        double cornerInset = m.PillCornerRadius - Math.Sqrt((m.PillCornerRadius * m.PillCornerRadius) - (arcDy * arcDy));
        Assert.True((m.CompactHorizontalPadding + m.IdleInnerMarginH) > (cornerInset + (4.0 * scale)));

        // Action buttons & wave bars remain safely inside the scaled pill height
        Assert.True(m.CompactActionButtonSize < m.PillHeight);
        Assert.True(m.CompactArtSize < m.PillHeight);
        Assert.True(m.CompactWaveBarHeight < m.PillHeight);
    }

    [Theory]
    [InlineData(0.50, 0.80)]
    [InlineData(2.00, 1.25)]
    [InlineData(double.NaN, 1.00)]
    [InlineData(double.PositiveInfinity, 1.00)]
    public void ComputeCompactMetricsClampsInvalidOrOutOfRangeScales(double input, double expected)
    {
        var m = IslandSurfaceLayout.ComputeCompactMetrics(input);
        Assert.Equal(expected, m.Scale, precision: 3);
    }
}

