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
}

