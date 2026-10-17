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

    /// <summary>Returns battery charge percent, charging state and AC power state, or null when no battery exists.</summary>
    (int Percent, bool IsCharging, bool IsLow, bool IsOnAc)? GetBatteryStatus();

    /// <summary>Brings the app identified by an AUMID (packaged) or exe name (Win32) to the foreground.</summary>
    bool TryActivateApp(string appId);

    /// <summary>Opens a file or folder with the shell (files are revealed in Explorer).</summary>
    bool TryOpenPath(string path);
}
