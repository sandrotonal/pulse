using Pulse.Core.Events;
using Pulse.Core.Services;
using Xunit;

namespace Pulse.Tests.Services;

public sealed class SystemEventServiceTests
{
    [Fact]
    public void ReportBatteryStatus_LowBattery_PublishesWarningPriority()
    {
        var router = new EventRouter();
        var service = new SystemEventService(router);

        service.ReportBatteryStatus(15, false, true);

        var active = router.CurrentActiveEvent as SystemNotificationEvent;
        Assert.NotNull(active);
        Assert.Equal(EventCategory.Battery, active.Category);
        Assert.Equal(EventPriority.Warning, active.Priority);
        Assert.Contains("15", active.Subtitle);
    }

    [Fact]
    public void ReportBatteryStatus_Charging_PublishesInformationalPriority()
    {
        var router = new EventRouter();
        var service = new SystemEventService(router);

        service.ReportBatteryStatus(85, true, false);

        var active = router.CurrentActiveEvent as SystemNotificationEvent;
        Assert.NotNull(active);
        Assert.Equal(EventCategory.Battery, active.Category);
        Assert.Equal(EventPriority.Informational, active.Priority);
        Assert.Equal("Şarj Oluyor", active.Title);
    }

    [Fact]
    public void ReportBluetoothStatus_PublishesDeviceConnected()
    {
        var router = new EventRouter();
        var service = new SystemEventService(router);

        service.ReportBluetoothStatus("AirPods Pro", true);

        var active = router.CurrentActiveEvent as SystemNotificationEvent;
        Assert.NotNull(active);
        Assert.Equal(EventCategory.Bluetooth, active.Category);
        Assert.Equal("AirPods Pro", active.Title);
        Assert.Equal("Bağlandı", active.Subtitle);
    }

    [Fact]
    public void ReportAudioStatus_Muted_PublishesMutedTitle()
    {
        var router = new EventRouter();
        var service = new SystemEventService(router);

        service.ReportAudioStatus(40, true);

        var active = router.CurrentActiveEvent as SystemNotificationEvent;
        Assert.NotNull(active);
        Assert.Equal(EventCategory.Audio, active.Category);
        Assert.Equal("Sessiz", active.Title);
    }

    [Fact]
    public void ReportScreenshotSaved_PublishesFileName()
    {
        var router = new EventRouter();
        var service = new SystemEventService(router);

        service.ReportScreenshotSaved(@"C:\Users\Test\Pictures\Screenshots\Screen_01.png");

        var active = router.CurrentActiveEvent as SystemNotificationEvent;
        Assert.NotNull(active);
        Assert.Equal(EventCategory.Screenshot, active.Category);
        Assert.Equal("Screen_01.png", active.Subtitle);
    }
}
