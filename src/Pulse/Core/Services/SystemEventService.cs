using Pulse.Core.Events;
using Pulse.Core.Interfaces;

namespace Pulse.Core.Services;

public sealed class SystemEventService : ISystemEventService
{
    private readonly IEventRouter _eventRouter;
    private readonly ISettingsService? _settings;

    public SystemEventService(IEventRouter eventRouter, ISettingsService? settings = null)
    {
        _eventRouter = eventRouter ?? throw new ArgumentNullException(nameof(eventRouter));
        _settings = settings;
    }

    public void ReportBatteryStatus(int percentage, bool isCharging, bool isLowBattery)
    {
        if (_settings is not null &&
            (!_settings.Current.PowerNotifications || (_settings.Current.DoNotDisturb && !isLowBattery)))
        {
            return;
        }
        var clamped = Math.Clamp(percentage, 0, 100);
        var priority = isLowBattery ? EventPriority.Warning : EventPriority.Informational;
        var title = isLowBattery ? "Düşük Pil" : (isCharging ? "Şarj Oluyor" : "Pil");
        var subtitle = isLowBattery ? $"%{clamped} kaldı" : $"%{clamped}";
        var glyph = isCharging ? "\uEBAA" : (isLowBattery ? "\uEBA0" : "\uEBA2");

        var evt = new SystemNotificationEvent(
            "battery_status",
            EventCategory.Battery,
            title,
            subtitle,
            glyph,
            priority,
            TimeSpan.FromSeconds(isLowBattery ? 4.0 : 2.5)
        );

        _eventRouter.Publish(evt);
    }

    public void ReportPowerConnected(int percentage)
    {
        if (_settings is not null && (!_settings.Current.PowerNotifications || _settings.Current.DoNotDisturb))
        {
            return;
        }

        var clamped = Math.Clamp(percentage, 0, 100);
        var evt = new SystemNotificationEvent(
            "power_connected",
            EventCategory.Battery,
            "Güç Bağlandı",
            $"%{clamped} pil",
            "\uEBAA",
            EventPriority.Informational,
            TimeSpan.FromSeconds(2.8)
        );

        _eventRouter.Publish(evt);
    }

    public void ReportBluetoothStatus(string deviceName, bool isConnected)
    {
        if (_settings is not null && (!_settings.Current.BluetoothNotifications || _settings.Current.DoNotDisturb))
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(deviceName) ? "Bluetooth Cihazı" : deviceName;
        var evt = new SystemNotificationEvent(
            $"bt_{name}",
            EventCategory.Bluetooth,
            name,
            isConnected ? "Bağlandı" : "Bağlantı Kesildi",
            "\uE702",
            EventPriority.Informational,
            TimeSpan.FromSeconds(2.8)
        );

        _eventRouter.Publish(evt);
    }

    public void ReportAudioStatus(int volumePercentage, bool isMuted)
    {
        if (_settings is not null && (!_settings.Current.AudioNotifications || _settings.Current.DoNotDisturb))
        {
            return;
        }

        var clamped = Math.Clamp(volumePercentage, 0, 100);
        var evt = new SystemNotificationEvent(
            "audio_status",
            EventCategory.Audio,
            isMuted ? "Sessiz" : "Ses",
            isMuted ? "Ses kapatıldı" : $"%{clamped}",
            isMuted ? "\uE74F" : "\uE995",
            EventPriority.Informational,
            TimeSpan.FromSeconds(1.8)
        );

        _eventRouter.Publish(evt);
    }

    public void ReportScreenshotSaved(string filePath)
    {
        if (_settings is not null && (!_settings.Current.ScreenshotNotifications || _settings.Current.DoNotDisturb))
        {
            return;
        }

        var fileName = string.IsNullOrWhiteSpace(filePath) ? "Ekran Görüntüsü" : System.IO.Path.GetFileName(filePath);
        var evt = new SystemNotificationEvent(
            "screenshot_saved",
            EventCategory.Screenshot,
            "Ekran Görüntüsü Kaydedildi",
            fileName,
            "\uE722",
            EventPriority.Informational,
            TimeSpan.FromSeconds(3.0),
            LaunchTarget: filePath
        );

        _eventRouter.Publish(evt);
    }

    public void ReportSystemToast(
        string toastId,
        string title,
        string subtitle,
        string iconGlyph,
        bool isUrgent = false,
        string? launchTarget = null)
    {
        if (_settings is not null &&
            (!_settings.Current.SystemToastNotifications || (_settings.Current.DoNotDisturb && !isUrgent)))
        {
            return;
        }

        var id = string.IsNullOrWhiteSpace(toastId)
            ? $"toast_{Guid.NewGuid():N}"
            : $"toast_{toastId}";
        var evt = new SystemNotificationEvent(
            id,
            EventCategory.SystemToast,
            string.IsNullOrWhiteSpace(title) ? "Windows Bildirimi" : title,
            subtitle ?? string.Empty,
            string.IsNullOrWhiteSpace(iconGlyph) ? "\uE7E7" : iconGlyph,
            isUrgent ? EventPriority.Warning : EventPriority.Informational,
            TimeSpan.FromSeconds(isUrgent ? 4.2 : 3.2),
            LaunchTarget: launchTarget);

        _eventRouter.Publish(evt);
    }

    public void Dispose()
    {
    }
}
