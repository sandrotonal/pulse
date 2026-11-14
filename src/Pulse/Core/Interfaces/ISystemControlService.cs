namespace Pulse.Core.Interfaces;

public sealed record AudioStateChangedEventArgs(
    int VolumePercent,
    bool IsMuted,
    bool IsSelfOriginated,
    bool IsDeviceSwitch = false,
    string? DeviceName = null,
    string? DeviceGlyph = null);

/// <summary>
/// Real system-level controls: endpoint volume, mute, screen capture, battery.
/// Platform implementations live in the app project; tests may stub it.
/// </summary>
public interface ISystemControlService
{
    event EventHandler<AudioStateChangedEventArgs>? AudioStateChanged
    {
        add { }
        remove { }
    }

    int GetMasterVolume();

    void SetMasterVolume(int percent);

    bool IsMuted();

    void SetMute(bool muted);

    /// <summary>Returns the friendly name and glyph for the default render audio endpoint.</summary>
    (string Name, string Glyph) GetActiveAudioDevice() => ("Hoparlör", "\uE7F5");

    /// <summary>Opens Windows audio flyout or sound settings.</summary>
    void OpenSoundFlyout() { }

    /// <summary>Opens Windows sound settings.</summary>
    void OpenSoundSettings() { }

    /// <summary>Captures the primary screen. Returns the saved file path, or empty on failure.</summary>
    string CaptureScreenshot();

    /// <summary>Asynchronously captures the primary screen without blocking the UI thread.</summary>
    Task<string> CaptureScreenshotAsync() => Task.FromResult(CaptureScreenshot());

    /// <summary>Returns battery charge percent, charging state and AC power state, or null when no battery exists.</summary>
    (int Percent, bool IsCharging, bool IsLow, bool IsOnAc)? GetBatteryStatus();

    /// <summary>Brings the app identified by an AUMID (packaged) or exe name (Win32) to the foreground.</summary>
    bool TryActivateApp(string appId);

    /// <summary>Opens a file or folder with the shell (files are revealed in Explorer).</summary>
    bool TryOpenPath(string path);
}
