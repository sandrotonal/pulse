using Pulse.Core.Services;

namespace Pulse.Tests.Services;

public sealed class FullscreenWindowEvaluatorTests
{
    private static FullscreenWindowCandidate BaseCandidate() => new(
        Handle: new IntPtr(100),
        OwnHandle: new IntPtr(200),
        ClassName: "UnrealWindow",
        IsVisible: true,
        IsMinimized: false,
        IsMaximized: false,
        IsCloaked: false,
        ExStyle: 0,
        Left: 0,
        Top: 0,
        Right: 1920,
        Bottom: 1080,
        MonitorLeft: 0,
        MonitorTop: 0,
        MonitorRight: 1920,
        MonitorBottom: 1080,
        IsSameMonitorAsIsland: true);

    [Fact]
    public void RealBorderlessGameOnIslandMonitorDetectsFullscreen()
    {
        Assert.True(FullscreenWindowEvaluator.IsBorderlessFullscreen(BaseCandidate()));
    }

    [Fact]
    public void RejectsOverlaysCloakedWindowsAndOtherMonitorFullscreen()
    {
        var own = BaseCandidate() with { Handle = new IntPtr(200) };
        var cloaked = BaseCandidate() with { IsCloaked = true };
        var overlayTransparent = BaseCandidate() with { ExStyle = FullscreenWindowEvaluator.WsExTransparent | FullscreenWindowEvaluator.WsExLayered };
        var noActivateOverlay = BaseCandidate() with { ExStyle = FullscreenWindowEvaluator.WsExNoActivate };
        var shell = BaseCandidate() with { ClassName = "WorkerW" };
        var secondaryShell = BaseCandidate() with { ClassName = "Shell_SecondaryTrayWnd" };
        var otherMonitor = BaseCandidate() with { IsSameMonitorAsIsland = false };
        var maximized = BaseCandidate() with { IsMaximized = true };

        Assert.False(FullscreenWindowEvaluator.IsBorderlessFullscreen(own));
        Assert.False(FullscreenWindowEvaluator.IsBorderlessFullscreen(cloaked));
        Assert.False(FullscreenWindowEvaluator.IsBorderlessFullscreen(overlayTransparent));
        Assert.False(FullscreenWindowEvaluator.IsBorderlessFullscreen(noActivateOverlay));
        Assert.False(FullscreenWindowEvaluator.IsBorderlessFullscreen(shell));
        Assert.False(FullscreenWindowEvaluator.IsBorderlessFullscreen(secondaryShell));
        Assert.False(FullscreenWindowEvaluator.IsBorderlessFullscreen(otherMonitor));
        Assert.False(FullscreenWindowEvaluator.IsBorderlessFullscreen(maximized));
    }

    [Fact]
    public void StabilityTrackerDelaysExitRestorationUntilMonitorSettles()
    {
        var tracker = new FullscreenStabilityTracker(exitConfirmationThreshold: 2);

        // Entering fullscreen hides promptly
        Assert.True(tracker.Update(rawIsFullscreen: true));

        // A single spurious desktop/focus flash during game exit / alt-tab stays hidden
        Assert.True(tracker.Update(rawIsFullscreen: false));

        // Second consecutive desktop poll confirms real exit so ShowIsland can restore cleanly
        Assert.False(tracker.Update(rawIsFullscreen: false));
    }
}

