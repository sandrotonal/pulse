namespace Pulse.Core.Interfaces;

public interface ISystemEventService : IDisposable
{
    void ReportBatteryStatus(int percentage, bool isCharging, bool isLowBattery);
    void ReportPowerConnected(int percentage);
    void ReportBluetoothStatus(string deviceName, bool isConnected);
    void ReportAudioStatus(int volumePercentage, bool isMuted);
    void ReportScreenshotSaved(string filePath);
    void ReportSystemToast(string toastId, string title, string subtitle, string iconGlyph, bool isUrgent = false, string? launchTarget = null) { }
}
