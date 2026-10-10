using Pulse.Core.Events;
using Pulse.Core.Services;

namespace Pulse.Tests.Services;

public sealed class EventRouterTests
{
    [Fact]
    public void PublishMediaEventBecomesActiveEventWhenQueueEmpty()
    {
        using var router = new EventRouter();
        var mediaEvent = new MediaEvent("media_1", null, true, DateTimeOffset.UtcNow);

        router.Publish(mediaEvent);

        Assert.NotNull(router.CurrentActiveEvent);
        Assert.Equal("media_1", router.CurrentActiveEvent.Id);
        Assert.Equal(EventPriority.ActiveMedia, router.CurrentActiveEvent.Priority);
    }

    [Fact]
    public void HigherPriorityWarningPreemptsProgressEvent()
    {
        using var router = new EventRouter();
        var progressEvent = new FileOperationEvent("file_1", "archive.zip", 200, 1000, FileOperationStatus.InProgress);
        var warningEvent = new SystemNotificationEvent("battery_low", EventCategory.Battery, "Battery Low", "15% remaining", "\uE83F", EventPriority.Warning);

        router.Publish(progressEvent);
        Assert.Equal("file_1", router.CurrentActiveEvent?.Id);

        router.Publish(warningEvent);
        Assert.Equal("battery_low", router.CurrentActiveEvent?.Id);
    }

    [Fact]
    public void DismissingTopEventRestoresUnderlyingMediaEvent()
    {
        using var router = new EventRouter();
        var mediaEvent = new MediaEvent("media_1", null, true, DateTimeOffset.UtcNow);
        var toastEvent = new SystemNotificationEvent("bt_1", EventCategory.Bluetooth, "Connected", "AirPods", "\uE702", EventPriority.Informational);

        router.Publish(mediaEvent);
        router.Publish(toastEvent);
        Assert.Equal("bt_1", router.CurrentActiveEvent?.Id);

        router.Dismiss("bt_1");
        Assert.Equal("media_1", router.CurrentActiveEvent?.Id);
    }

    [Fact]
    public void LockUserInteractionPreservesCurrentActiveEvent()
    {
        using var router = new EventRouter();
        var mediaEvent = new MediaEvent("media_1", null, true, DateTimeOffset.UtcNow);
        var notification = new SystemNotificationEvent("notif_1", EventCategory.Network, "Wi-Fi", "Connected", "\uE701", EventPriority.Informational);

        router.Publish(mediaEvent);
        router.LockUserInteraction();

        // While locked, a new routine/informational event must not replace the current active view
        router.Publish(notification);
        Assert.Equal("media_1", router.CurrentActiveEvent?.Id);

        router.UnlockUserInteraction();
        // After unlock, the pending notification is presented
        Assert.Equal("notif_1", router.CurrentActiveEvent?.Id);

        // Once the notification completes/dismisses, underlying media is restored
        router.Dismiss("notif_1");
        Assert.Equal("media_1", router.CurrentActiveEvent?.Id);
    }
}
