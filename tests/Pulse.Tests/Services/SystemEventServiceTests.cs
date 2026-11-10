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

    private sealed class ToggleSettingsService : Pulse.Core.Interfaces.ISettingsService
    {
        public Pulse.Core.Models.AppSettings Current { get; set; } = new()
        {
            PowerNotifications = false,
            BluetoothNotifications = false,
            AudioNotifications = false,
            SystemToastNotifications = false
        };
        public string? LastSaveError => null;
        public event EventHandler<Pulse.Core.Models.AppSettings>? SettingsChanged;
        public Task LoadAsync() => Task.CompletedTask;
        public Task SaveAsync(Pulse.Core.Models.AppSettings settings)
        {
            Current = settings;
            SettingsChanged?.Invoke(this, settings);
            return Task.CompletedTask;
        }
        public Task FlushAsync() => Task.CompletedTask;
    }

    [Fact]
    public void DisabledNotificationSettingsSuppressEvents()
    {
        using var router = new EventRouter();
        var service = new SystemEventService(router, new ToggleSettingsService());

        service.ReportPowerConnected(80);
        service.ReportBluetoothStatus("AirPods", true);
        service.ReportAudioStatus(50, false);
        service.ReportSystemToast("id1", "Title", "Sub", "\uE7E7");

        Assert.Null(router.CurrentActiveEvent);
    }

    [Fact]
    public void DisabledScreenshotNotificationSettingSuppressesScreenshotEvents()
    {
        using var router = new EventRouter();
        var settings = new ToggleSettingsService
        {
            Current = new Pulse.Core.Models.AppSettings { ScreenshotNotifications = false }
        };
        var service = new SystemEventService(router, settings);

        service.ReportScreenshotSaved(@"C:\Screenshots\Snap.png");

        Assert.Null(router.CurrentActiveEvent);
    }

    [Fact]
    public void DoNotDisturbSuppressesInformationalEventsButAllowsUrgentAndLowBattery()
    {
        using var router = new EventRouter();
        var settings = new ToggleSettingsService
        {
            Current = new Pulse.Core.Models.AppSettings { DoNotDisturb = true }
        };
        var service = new SystemEventService(router, settings);

        service.ReportPowerConnected(90);
        service.ReportBatteryStatus(85, isCharging: true, isLowBattery: false);
        service.ReportBluetoothStatus("AirPods", true);
        service.ReportAudioStatus(45, false);
        service.ReportScreenshotSaved(@"C:\Screenshots\Snap.png");
        service.ReportSystemToast("info1", "Normal", "Mesaj", "\uE7E7", isUrgent: false);

        Assert.Null(router.CurrentActiveEvent);

        service.ReportSystemToast("urg1", "Acil", "Kritik", "\uE7E7", isUrgent: true);
        var urgentEvt = Assert.IsType<SystemNotificationEvent>(router.CurrentActiveEvent);
        Assert.Equal("Acil", urgentEvt.Title);

        router.Dismiss(urgentEvt.Id);
        service.ReportBatteryStatus(12, isCharging: false, isLowBattery: true);
        var lowBatteryEvt = Assert.IsType<SystemNotificationEvent>(router.CurrentActiveEvent);
        Assert.Equal(EventPriority.Warning, lowBatteryEvt.Priority);
    }

    [Fact]
    public void ReportAudioStatus_PublishesRoutinePrioritySoBluetoothBeatsAudioSwitchRegardlessOfOrder()
    {
        using var routerA = new EventRouter();
        var serviceA = new SystemEventService(routerA);
        serviceA.ReportAudioStatus(50, false);
        serviceA.ReportBluetoothStatus("AirPods", true);
        var activeA = Assert.IsType<SystemNotificationEvent>(routerA.CurrentActiveEvent);
        Assert.Equal("bt_AirPods", activeA.Id);

        using var routerB = new EventRouter();
        var serviceB = new SystemEventService(routerB);
        serviceB.ReportBluetoothStatus("AirPods", true);
        serviceB.ReportAudioStatus(50, false);
        var activeB = Assert.IsType<SystemNotificationEvent>(routerB.CurrentActiveEvent);
        Assert.Equal("bt_AirPods", activeB.Id);
        Assert.Equal(EventPriority.Informational, activeB.Priority);

        routerB.Dismiss("bt_AirPods");
        var audioEvt = Assert.IsType<SystemNotificationEvent>(routerB.CurrentActiveEvent);
        Assert.Equal(EventPriority.Routine, audioEvt.Priority);
    }
}
