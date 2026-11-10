using Pulse.Core.Events;
using Pulse.Core.Models;
using Pulse.Core.Services;

namespace Pulse.Tests;

public sealed class IslandSplitModeTests
{
    [Fact]
    public void SplitModeActivatesWhenSecondaryActivityCoexistsWithActiveMedia()
    {
        using var router = new EventRouter();
        using var timerService = new FocusTimerService(router);

        timerService.Start(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5));
        var active = router.CurrentActiveEvent as FocusTimerEvent;
        Assert.NotNull(active);
        Assert.Equal("focus_timer", active.Id);
    }

    [Fact]
    public void SplitModeYieldsFullBannerToReadableNotificationsAndDownloads()
    {
        Assert.True(IslandPresentationPolicy.ShouldUseSplitMode(
            isCompact: true,
            isSplitDemoForced: false,
            hasTrack: true,
            isFocusTimerActive: true,
            isNotificationActive: false,
            isFileOperationActive: false));

        // When media is playing and a system/audio/toast notification arrives, do NOT collapse it into "!" pill
        Assert.False(IslandPresentationPolicy.ShouldUseSplitMode(
            isCompact: true,
            isSplitDemoForced: false,
            hasTrack: true,
            isFocusTimerActive: false,
            isNotificationActive: true,
            isFileOperationActive: false));

        // Even during an active Focus Timer, a temporary toast takes the full readable banner
        Assert.False(IslandPresentationPolicy.ShouldUseSplitMode(
            isCompact: true,
            isSplitDemoForced: false,
            hasTrack: true,
            isFocusTimerActive: true,
            isNotificationActive: true,
            isFileOperationActive: false));

        // Downloads also show readable progress instead of hiding behind compact pill
        Assert.False(IslandPresentationPolicy.ShouldUseSplitMode(
            isCompact: true,
            isSplitDemoForced: false,
            hasTrack: true,
            isFocusTimerActive: false,
            isNotificationActive: false,
            isFileOperationActive: true));
    }

    [Fact]
    public void TrackChangedAndNotificationWidthsAreDistinctAndWiderThanCompact()
    {
        var compactWidth = IslandPresentationPolicy.ComputeCompactTargetWidth(
            IslandState.Compact, hasTrack: true, isSplit: false, isNotificationActive: false, isFileOperationActive: false, isFocusTimerActive: false);
        var trackChangedWidth = IslandPresentationPolicy.ComputeCompactTargetWidth(
            IslandState.TrackChanged, hasTrack: true, isSplit: false, isNotificationActive: false, isFileOperationActive: false, isFocusTimerActive: false);
        var notificationWidth = IslandPresentationPolicy.ComputeCompactTargetWidth(
            IslandState.Notification, hasTrack: true, isSplit: false, isNotificationActive: true, isFileOperationActive: false, isFocusTimerActive: false);

        Assert.True(trackChangedWidth > compactWidth);
        Assert.True(notificationWidth >= 280);
    }
}
