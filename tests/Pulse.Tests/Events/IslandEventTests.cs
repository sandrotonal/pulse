using Pulse.Core.Events;
using Pulse.Core.Models;

namespace Pulse.Tests.Events;

public sealed class IslandEventTests
{
    [Fact]
    public void EventPriorityHierarchyIsCorrect()
    {
        Assert.True(EventPriority.UserInteraction > EventPriority.ActiveMedia);
        Assert.True(EventPriority.ActiveMedia > EventPriority.Warning);
        Assert.True(EventPriority.Warning > EventPriority.Progress);
        Assert.True(EventPriority.Progress > EventPriority.Informational);
        Assert.True(EventPriority.Informational > EventPriority.Routine);
    }

    [Fact]
    public void FileOperationProgressPercentageCalculatesCorrectly()
    {
        var fileOp = new FileOperationEvent("op1", "video.mp4", 500, 1000, FileOperationStatus.InProgress);
        Assert.Equal(50.0, fileOp.ProgressPercentage);

        var completed = new FileOperationEvent("op2", "image.png", 1000, 1000, FileOperationStatus.Completed);
        Assert.Equal(100.0, completed.ProgressPercentage);
    }

    [Fact]
    public void FocusTimerProgressPercentageCalculatesCorrectly()
    {
        var timer = new FocusTimerEvent("t1", TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(25), false, FocusTimerState.Running);
        Assert.Equal(60.0, timer.ProgressPercentage);
    }

    [Fact]
    public void EventExpirationWorksCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var expEvent = new SystemNotificationEvent("n1", EventCategory.Bluetooth, "AirPods", "Connected", "\uE702", DisplayDuration: TimeSpan.FromSeconds(2));

        Assert.False(expEvent.IsExpired(now));
        Assert.True(expEvent.IsExpired(now.AddSeconds(3)));
    }
}
