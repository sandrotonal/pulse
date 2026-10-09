using System.Runtime.InteropServices;

namespace Pulse.Services.System;

public sealed class TrayService : IDisposable
{
    private const uint WmApp = 0x8000;
    private const uint TrayCallbackMessage = WmApp + 0x31;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonUp = 0x0205;
    private const uint NimAdd = 0;
    private const uint NimDelete = 2;
    private const uint NimSetVersion = 4;
    private const uint NifMessage = 0x01;
    private const uint NifIcon = 0x02;
    private const uint NifTip = 0x04;
    private const uint NifGuid = 0x20;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x10;
    private const uint LrDefaultSize = 0x40;
    private const uint MfString = 0;
    private const uint MfSeparator = 0x800;
    private const uint MfChecked = 0x8;
    private const uint TpmRightButton = 0x2;
    private const uint TpmReturnCommand = 0x100;
    private static readonly IntPtr HwndMessage = new(-3);
    private static readonly Guid IconGuid = new("1D5B773E-2E97-4B39-AE78-7624A0110F51");

    private readonly string _windowClassName = $"PulseTray_{Guid.NewGuid():N}";
    private readonly WndProcDelegate _windowProcedure;
    private readonly IntPtr _windowHandle;
    private readonly IntPtr _iconHandle;
    private bool _startupEnabled;
    private bool _notificationsPaused;
    private bool _disposed;

    public event EventHandler? ShowRequested;
    public event EventHandler? IslandToggleRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<bool>? StartupRequested;
    public event EventHandler<bool>? NotificationsPauseRequested;
    public event EventHandler? AboutRequested;

    public TrayService(string iconPath, bool startWithWindows)
    {
        _startupEnabled = startWithWindows;
        _windowProcedure = WindowProcedure;
        var instance = GetModuleHandle(null);
        var windowClass = new WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            Instance = instance,
            ClassName = _windowClassName,
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(_windowProcedure)
        };

        if (RegisterClassEx(ref windowClass) == 0)
        {
            throw new InvalidOperationException($"Tray message window registration failed ({Marshal.GetLastWin32Error()}).");
        }

        _windowHandle = CreateWindowEx(0, _windowClassName, "PulseTray", 0, 0, 0, 0, 0,
            HwndMessage, IntPtr.Zero, instance, IntPtr.Zero);
        if (_windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Tray message window creation failed ({Marshal.GetLastWin32Error()}).");
        }

        _iconHandle = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
        if (_iconHandle == IntPtr.Zero)
        {
            throw new FileNotFoundException("Pulse tray icon could not be loaded.", iconPath);
        }

        var data = CreateIconData();
        if (!Shell_NotifyIcon(NimAdd, ref data))
        {
            throw new InvalidOperationException($"Pulse tray icon could not be added ({Marshal.GetLastWin32Error()}).");
        }

        data.Version = 4;
        Shell_NotifyIcon(NimSetVersion, ref data);
    }

    public void SetStartupEnabled(bool enabled) => _startupEnabled = enabled;
    public void SetNotificationsPaused(bool paused) => _notificationsPaused = paused;

    private IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == TrayCallbackMessage)
        {
            var mouseEvent = unchecked((uint)lParam.ToInt64());
            if (mouseEvent == WmLButtonUp)
            {
                ShowRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (mouseEvent == WmRButtonUp)
            {
                ShowContextMenu();
            }

            return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        AppendMenu(menu, MfString, 1, "Pulse Kontrol Paneli");
        AppendMenu(menu, MfString, 7, "Dynamic Island'ı Gizle / Göster");
        AppendMenu(menu, MfString, 2, "Ayarlar");
        AppendMenu(menu, MfSeparator, 0, null);
        AppendMenu(menu, MfString | (_startupEnabled ? MfChecked : 0), 3, "Windows ile başlat");
        AppendMenu(menu, MfString | (_notificationsPaused ? MfChecked : 0), 4, "Parça değişimlerini duraklat");
        AppendMenu(menu, MfSeparator, 0, null);
        AppendMenu(menu, MfString, 5, "Hakkında");
        AppendMenu(menu, MfString, 6, "Çıkış");

        if (GetCursorPos(out var cursor))
        {
            SetForegroundWindow(_windowHandle);
            var command = TrackPopupMenu(menu, TpmReturnCommand | TpmRightButton, cursor.X, cursor.Y, 0, _windowHandle, IntPtr.Zero);
            switch (command)
            {
                case 1: ShowRequested?.Invoke(this, EventArgs.Empty); break;
                case 7: IslandToggleRequested?.Invoke(this, EventArgs.Empty); break;
                case 2: SettingsRequested?.Invoke(this, EventArgs.Empty); break;
                case 3:
                    _startupEnabled = !_startupEnabled;
                    StartupRequested?.Invoke(this, _startupEnabled);
                    break;
                case 4:
                    _notificationsPaused = !_notificationsPaused;
                    NotificationsPauseRequested?.Invoke(this, _notificationsPaused);
                    break;
                case 5: AboutRequested?.Invoke(this, EventArgs.Empty); break;
                case 6: ExitRequested?.Invoke(this, EventArgs.Empty); break;
            }

            PostMessage(_windowHandle, 0, IntPtr.Zero, IntPtr.Zero);
        }

        DestroyMenu(menu);
    }

    private NotifyIconData CreateIconData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        WindowHandle = _windowHandle,
        Id = 1,
        Flags = NifMessage | NifIcon | NifTip | NifGuid,
        CallbackMessage = TrayCallbackMessage,
        IconHandle = _iconHandle,
        Tip = "Pulse — Windows medya denetimi",
        Guid = IconGuid
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var data = CreateIconData();
        Shell_NotifyIcon(NimDelete, ref data);
        DestroyIcon(_iconHandle);
        DestroyWindow(_windowHandle);
        GC.KeepAlive(_windowProcedure);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X; public int Y; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenu(IntPtr menu, uint flags, nuint idNewItem, string? newItem);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
