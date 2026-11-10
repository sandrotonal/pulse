using Pulse.Core.Models;

namespace Pulse.Core.Services;

/// <summary>
/// Shared presentation decisions between compact pills, split capsules, notifications,
/// and track-change micro-expansions.
/// </summary>
public static class IslandPresentationPolicy
{
    public static bool ShouldUseSplitMode(
        bool isCompact,
        bool isSplitDemoForced,
        bool hasTrack,
        bool isFocusTimerActive,
        bool isNotificationActive,
        bool isFileOperationActive)
    {
        if (!isCompact)
        {
            return false;
        }

        if (isSplitDemoForced)
        {
            return true;
        }

        // Priority rule: active system notifications (volume, toasts, Bluetooth, battery, screenshots)
        // and file downloads always receive the full readable capsule instead of hiding behind "!".
        if (isNotificationActive || isFileOperationActive)
        {
            return false;
        }

        return hasTrack && isFocusTimerActive;
    }

    public static double ComputeCompactTargetWidth(
        IslandState state,
        bool hasTrack,
        bool isSplit,
        bool isNotificationActive,
        bool isFileOperationActive,
        bool isFocusTimerActive)
    {
        if (state == IslandState.Expanded)
        {
            return 384;
        }

        if (isSplit)
        {
            return 196;
        }

        if (isNotificationActive || isFileOperationActive)
        {
            return 288;
        }

        if (isFocusTimerActive)
        {
            return 220;
        }

        if (!hasTrack && state == IslandState.Idle)
        {
            return 216;
        }

        if (state == IslandState.TrackChanged)
        {
            return hasTrack ? 256 : 216;
        }

        if (state == IslandState.Hover)
        {
            return hasTrack ? 252 : 216;
        }

        return 216;
    }
}

