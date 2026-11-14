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

    [Fact]
    public async Task LockedInteractionDoesNotExpireQueuedNotificationBeforeUnlock()
    {
        using var router = new EventRouter();
        var mediaEvent = new MediaEvent("media_1", null, true, DateTimeOffset.UtcNow);
        var shortNotification = new SystemNotificationEvent(
            "toast_short",
            EventCategory.SystemToast,
            "Discord: Ali",
            "Oyuna gel",
            "\uE7E7",
            EventPriority.Informational,
            DisplayDuration: TimeSpan.FromMilliseconds(140));

        router.Publish(mediaEvent);
        router.LockUserInteraction();
        router.Publish(shortNotification);

        // Wait longer than the notification's own display duration while locked
        await Task.Delay(260);
        Assert.Equal("media_1", router.CurrentActiveEvent?.Id);

        // Upon unlock, the queued notification must STILL appear instead of having expired unseen
        router.UnlockUserInteraction();
        Assert.Equal("toast_short", router.CurrentActiveEvent?.Id);

        // After its display window elapses while actually visible, it dismisses back to media
        await Task.Delay(320);
        Assert.Equal("media_1", router.CurrentActiveEvent?.Id);
    }

    [Fact]
    public void PublishNullThrowsArgumentNullException()
    {
        using var router = new EventRouter();
        Assert.Throws<ArgumentNullException>(() => router.Publish(null!));
    }

    [Fact]
    public void DisposeStopsFurtherPublishAndDismiss()
    {
        var router = new EventRouter();
        var raisedAfterDispose = false;
        router.ActiveEventChanged += (_, _) => raisedAfterDispose = true;
        router.Dispose();

        router.Publish(new MediaEvent("media_1", null, true, DateTimeOffset.UtcNow));
        router.Dismiss("media_1");

        Assert.Null(router.CurrentActiveEvent);
        Assert.False(raisedAfterDispose);
    }

    [Fact]
    public async Task ActiveEventChangedDeliversLatestStateUnderConcurrentUpdates()
    {
        using var router = new EventRouter();
        var deliveredIds = new List<string?>();
        router.ActiveEventChanged += (_, evt) =>
        {
            lock (deliveredIds)
            {
                deliveredIds.Add(evt?.Id);
            }
        };

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() =>
        {
            var id = $"notif_{i}";
            router.Publish(new SystemNotificationEvent(id, EventCategory.SystemToast, id, "msg", "\uE7E7", EventPriority.Informational));
            if (i % 2 == 0)
            {
                router.Dismiss(id);
            }
        })));

        string? lastDelivered;
        lock (deliveredIds)
        {
            lastDelivered = deliveredIds.LastOrDefault();
        }

        Assert.Equal(router.CurrentActiveEvent?.Id, lastDelivered);
    }

    [Fact]
    public void OngoingFocusTimerYieldsToRoutineAndInformationalEventsThenRestores()
    {
        using var router = new EventRouter();
        var focus = new FocusTimerEvent("focus_timer", TimeSpan.FromMinutes(24), TimeSpan.FromMinutes(25), false, FocusTimerState.Running);
        var volume = new SystemNotificationEvent("audio_status", EventCategory.Audio, "Ses", "%60", "\uE995", EventPriority.Routine);
        var screenshot = new SystemNotificationEvent("screenshot_saved", EventCategory.Screenshot, "Ekran Görüntüsü Kaydedildi", "Shot.png", "\uE722", EventPriority.Informational);

        router.Publish(focus);
        Assert.Equal("focus_timer", router.CurrentActiveEvent?.Id);

        router.Publish(volume);
        Assert.Equal("audio_status", router.CurrentActiveEvent?.Id);

        router.Publish(screenshot);
        Assert.Equal("screenshot_saved", router.CurrentActiveEvent?.Id);

        router.Dismiss("screenshot_saved");
        Assert.Equal("audio_status", router.CurrentActiveEvent?.Id);

        router.Dismiss("audio_status");
        Assert.Equal("focus_timer", router.CurrentActiveEvent?.Id);
    }

    [Fact]
    public void SamePriorityEventsPreferMostRecentlyPublishedEvent()
    {
        using var router = new EventRouter();
        var first = new SystemNotificationEvent("toast_1", EventCategory.SystemToast, "Birinci", "Msg 1", "\uE7E7", EventPriority.Informational);
        var second = new SystemNotificationEvent("toast_2", EventCategory.SystemToast, "İkinci", "Msg 2", "\uE7E7", EventPriority.Informational);

        router.Publish(first);
        Assert.Equal("toast_1", router.CurrentActiveEvent?.Id);

        router.Publish(second);
        Assert.Equal("toast_2", router.CurrentActiveEvent?.Id);

        router.Dismiss("toast_2");
        Assert.Equal("toast_1", router.CurrentActiveEvent?.Id);
    }

    [Fact]
    public async Task SetPresentationPausedDefersTimedNotificationsUntilUnhiddenWithoutExpiring()
    {
        using var router = new EventRouter();
        var focus = new FocusTimerEvent("focus_timer", TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(25), false, FocusTimerState.Running);
        var screenshot = new SystemNotificationEvent(
            "screenshot_saved",
            EventCategory.Screenshot,
            "Ekran Görüntüsü Kaydedildi",
            "Shot.png",
            "\uE722",
            EventPriority.Informational,
            DisplayDuration: TimeSpan.FromMilliseconds(140));

        router.Publish(focus);
        router.SetPresentationPaused(true);

        // Published while island is hidden (e.g. Snipping Tool overlay active)
        router.Publish(screenshot);
        Assert.Equal("focus_timer", router.CurrentActiveEvent?.Id);

        // Wait longer than DisplayDuration while still paused
        await Task.Delay(260);
        Assert.Equal("focus_timer", router.CurrentActiveEvent?.Id);

        // Once island unhides, deferred notification surfaces immediately for its display window
        router.SetPresentationPaused(false);
        Assert.Equal("screenshot_saved", router.CurrentActiveEvent?.Id);

        await Task.Delay(320);
        Assert.Equal("focus_timer", router.CurrentActiveEvent?.Id);
    }

    [Fact]
    public async Task SetPresentationPausedSuspendsActiveTimerAndSkipsTransientRoutineEvents()
    {
        using var router = new EventRouter();
        var toast = new SystemNotificationEvent(
            "bt_AirPods",
            EventCategory.Bluetooth,
            "AirPods",
            "Bağlandı",
            "\uE702",
            EventPriority.Informational,
            DisplayDuration: TimeSpan.FromMilliseconds(260));
        var hiddenVolume = new SystemNotificationEvent(
            "audio_status",
            EventCategory.Audio,
            "Ses",
            "%40",
            "\uE995",
            EventPriority.Routine,
            DisplayDuration: TimeSpan.FromMilliseconds(200));

        router.Publish(toast);
        Assert.Equal("bt_AirPods", router.CurrentActiveEvent?.Id);

        await Task.Delay(60);
        router.SetPresentationPaused(true);
        router.Publish(hiddenVolume);

        // Wait longer than original duration while hidden
        await Task.Delay(320);

        // Unpausing restores remaining toast duration and never surfaces transient routine volume HUD
        router.SetPresentationPaused(false);
        Assert.Equal("bt_AirPods", router.CurrentActiveEvent?.Id);

        await Task.Delay(360);
        Assert.Null(router.CurrentActiveEvent);
    }
}
