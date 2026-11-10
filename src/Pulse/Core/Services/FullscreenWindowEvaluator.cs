namespace Pulse.Core.Services;

public sealed record FullscreenWindowCandidate(
    IntPtr Handle,
    IntPtr OwnHandle,
    string? ClassName,
    bool IsVisible,
    bool IsMinimized,
    bool IsMaximized,
    bool IsCloaked,
    long ExStyle,
    int Left,
    int Top,
    int Right,
    int Bottom,
    int MonitorLeft,
    int MonitorTop,
    int MonitorRight,
    int MonitorBottom,
    bool IsSameMonitorAsIsland);

/// <summary>
/// Evaluates whether a foreground Win32 window genuinely occupies fullscreen game/media space
/// on the island's monitor, ignoring cloaked UWP surfaces, transparent overlays, and shell hosts.
/// </summary>
public static class FullscreenWindowEvaluator
{
    public const long WsExTransparent = 0x00000020L;
    public const long WsExLayered = 0x00080000L;
    public const long WsExNoActivate = 0x08000000L;

    public static bool IsBorderlessFullscreen(FullscreenWindowCandidate candidate, int tolerancePixels = 2)
    {
        if (candidate.Handle == IntPtr.Zero || candidate.Handle == candidate.OwnHandle)
        {
            return false;
        }

        if (!candidate.IsSameMonitorAsIsland || !candidate.IsVisible || candidate.IsMinimized || candidate.IsMaximized || candidate.IsCloaked)
        {
            return false;
        }

        // Ignore transparent/click-through or no-activate overlay windows (NVIDIA, Game Bar, Discord, Vanguard overlays).
        if ((candidate.ExStyle & WsExTransparent) != 0 || (candidate.ExStyle & WsExNoActivate) != 0)
        {
            return false;
        }

        if (candidate.ClassName is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow")
        {
            return false;
        }

        return candidate.Left <= candidate.MonitorLeft + tolerancePixels
            && candidate.Top <= candidate.MonitorTop + tolerancePixels
            && candidate.Right >= candidate.MonitorRight - tolerancePixels
            && candidate.Bottom >= candidate.MonitorBottom - tolerancePixels;
    }
}

/// <summary>
/// Hysteresis guard for fullscreen transitions: enters immediately, exits only after
/// consecutive non-fullscreen polls so exclusive D3D / stretched-res mode switches settle first.
/// </summary>
public sealed class FullscreenStabilityTracker
{
    private readonly int _exitConfirmationThreshold;
    private int _nonFullscreenStreak;
    private bool _effectiveFullscreen;

    public FullscreenStabilityTracker(int exitConfirmationThreshold = 2)
    {
        _exitConfirmationThreshold = Math.Max(1, exitConfirmationThreshold);
    }

    public bool Update(bool rawIsFullscreen)
    {
        if (rawIsFullscreen)
        {
            _nonFullscreenStreak = 0;
            _effectiveFullscreen = true;
            return true;
        }

        if (!_effectiveFullscreen)
        {
            _nonFullscreenStreak = 0;
            return false;
        }

        _nonFullscreenStreak++;
        if (_nonFullscreenStreak >= _exitConfirmationThreshold)
        {
            _nonFullscreenStreak = 0;
            _effectiveFullscreen = false;
        }

        return _effectiveFullscreen;
    }

    public void Reset()
    {
        _nonFullscreenStreak = 0;
        _effectiveFullscreen = false;
    }
}

