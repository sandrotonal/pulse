namespace Pulse.Core.Interfaces;

/// <summary>
/// Real system-level controls: endpoint volume, mute, screen capture, battery.
/// Platform implementations live in the app project; tests may stub it.
/// </summary>
public interface ISystemControlService
{
    int GetMasterVolume();

    void SetMasterVolume(int percent);

    bool IsMuted();

    void SetMute(bool muted);

    /// <summary>Captures the primary screen. Returns the saved file path, or empty on failure.</summary>
    string CaptureScreenshot();

    /// <summary>Returns battery charge percent and charging state, or null when no battery exists.</summary>
    (int Percent, bool IsCharging, bool IsLow)? GetBatteryStatus();
}
