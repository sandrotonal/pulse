namespace Pulse.Core.Services;

/// <summary>
/// Values returned by Win32 <c>SHQueryUserNotificationState</c> that mean a
/// fullscreen app, game or presentation owns the screen.
/// </summary>
public static class FullscreenNotificationState
{
    public const int Busy = 2;
    public const int RunningD3DFullScreen = 3;
    public const int PresentationMode = 4;

    public static bool IsFullscreen(int state) => state is Busy or RunningD3DFullScreen or PresentationMode;
}
