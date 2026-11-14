using System.Runtime.InteropServices;

namespace Pulse.Services.System;

public sealed record DisplayMonitorOption(
    string DeviceName,
    string DisplayName,
    bool IsPrimary,
    int Left,
    int Top,
    int Right,
    int Bottom);

internal readonly record struct MonitorTargetInfo(
    IntPtr Handle,
    string DeviceName,
    int WorkLeft,
    int WorkTop,
    int WorkRight,
    int WorkBottom,
    double DpiScale);

internal static class DisplayMonitorHelper
{
    public const string PrimaryDeviceToken = "";
    public const string FollowCursorDeviceToken = "FollowCursor";

    private const uint MonitorInfoFPrimary = 0x00000001;
    private const uint MonitorDefaultToPrimary = 1;
    private const uint MonitorDefaultToNearest = 2;
    private const int MdtEffectiveDpi = 0;

    private readonly record struct PhysicalMonitor(
        IntPtr Handle,
        string DeviceName,
        bool IsPrimary,
        Rect MonitorRect,
        Rect WorkRect);

    public static IReadOnlyList<DisplayMonitorOption> GetMonitorOptions(string? currentSelection)
    {
        var monitors = EnumeratePhysicalMonitors();
        var primary = monitors.FirstOrDefault(m => m.IsPrimary);

        var options = new List<DisplayMonitorOption>(monitors.Count + 3)
        {
            new(
                PrimaryDeviceToken,
                "Birincil ekran",
                true,
                primary.MonitorRect.Left,
                primary.MonitorRect.Top,
                primary.MonitorRect.Right,
                primary.MonitorRect.Bottom),
            new(
                FollowCursorDeviceToken,
                "İmlecin bulunduğu ekran",
                false,
                primary.MonitorRect.Left,
                primary.MonitorRect.Top,
                primary.MonitorRect.Right,
                primary.MonitorRect.Bottom)
        };

        for (var i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            var width = Math.Max(0, m.MonitorRect.Right - m.MonitorRect.Left);
            var height = Math.Max(0, m.MonitorRect.Bottom - m.MonitorRect.Top);
            var label = m.IsPrimary
                ? $"Ekran {i + 1} — {width}×{height} (Ana)"
                : $"Ekran {i + 1} — {width}×{height}";

            options.Add(new DisplayMonitorOption(
                m.DeviceName,
                label,
                m.IsPrimary,
                m.MonitorRect.Left,
                m.MonitorRect.Top,
                m.MonitorRect.Right,
                m.MonitorRect.Bottom));
        }

        if (!string.IsNullOrWhiteSpace(currentSelection) &&
            !string.Equals(currentSelection, FollowCursorDeviceToken, StringComparison.OrdinalIgnoreCase) &&
            !options.Any(o => string.Equals(o.DeviceName, currentSelection, StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new DisplayMonitorOption(
                currentSelection,
                $"Bağlı değil ({currentSelection})",
                false,
                0,
                0,
                0,
                0));
        }

        return options;
    }

    public static bool TryGetCursorMonitorHandle(out IntPtr hMonitor)
    {
        hMonitor = IntPtr.Zero;
        if (!GetCursorPos(out var pt))
        {
            return false;
        }

        hMonitor = MonitorFromPoint(pt, MonitorDefaultToNearest);
        return hMonitor != IntPtr.Zero;
    }

    public static bool TryResolveTargetMonitor(string? preferredDeviceName, IntPtr hwnd, out MonitorTargetInfo target)
    {
        target = default;
        var monitors = EnumeratePhysicalMonitors();

        PhysicalMonitor? selected = null;
        if (string.Equals(preferredDeviceName, FollowCursorDeviceToken, StringComparison.OrdinalIgnoreCase))
        {
            if (GetCursorPos(out var cursorPt))
            {
                var cursorMonitor = MonitorFromPoint(cursorPt, MonitorDefaultToPrimary);
                selected = FindByHandleOrInfo(monitors, cursorMonitor);
            }
        }
        else if (!string.IsNullOrWhiteSpace(preferredDeviceName))
        {
            selected = monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, preferredDeviceName, StringComparison.OrdinalIgnoreCase));
        }

        selected ??= monitors.FirstOrDefault(m => m.IsPrimary);
        if (selected is null && monitors.Count > 0)
        {
            selected = monitors[0];
        }

        if (selected is null)
        {
            var fallbackHandle = hwnd != IntPtr.Zero
                ? MonitorFromWindow(hwnd, MonitorDefaultToPrimary)
                : MonitorFromPoint(default, MonitorDefaultToPrimary);
            selected = FindByHandleOrInfo(monitors, fallbackHandle);
        }

        if (selected is not { } chosen || chosen.Handle == IntPtr.Zero)
        {
            return false;
        }

        double dpiScale = 1.0;
        if (GetDpiForMonitor(chosen.Handle, MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0)
        {
            dpiScale = dpiX / 96.0;
        }
        else if (hwnd != IntPtr.Zero)
        {
            dpiScale = Math.Max(1u, GetDpiForWindow(hwnd)) / 96.0;
        }

        target = new MonitorTargetInfo(
            chosen.Handle,
            chosen.DeviceName,
            chosen.WorkRect.Left,
            chosen.WorkRect.Top,
            chosen.WorkRect.Right,
            chosen.WorkRect.Bottom,
            dpiScale);
        return true;
    }

    private static PhysicalMonitor? FindByHandleOrInfo(List<PhysicalMonitor> monitors, IntPtr hMonitor)
    {
        if (hMonitor == IntPtr.Zero)
        {
            return null;
        }

        foreach (var monitor in monitors)
        {
            if (monitor.Handle == hMonitor)
            {
                return monitor;
            }
        }

        if (TryReadMonitorInfo(hMonitor, out var info))
        {
            return info;
        }

        return null;
    }

    private static List<PhysicalMonitor> EnumeratePhysicalMonitors()
    {
        var collected = new List<PhysicalMonitor>(4);
        EnumMonitorsDelegate callback = (hMonitor, _, _, _) =>
        {
            if (TryReadMonitorInfo(hMonitor, out var monitor))
            {
                collected.Add(monitor);
            }
            return true;
        };

        try
        {
            _ = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        }
        finally
        {
            GC.KeepAlive(callback);
        }

        collected.Sort((a, b) =>
        {
            var primaryCompare = b.IsPrimary.CompareTo(a.IsPrimary);
            if (primaryCompare != 0)
            {
                return primaryCompare;
            }

            var leftCompare = a.MonitorRect.Left.CompareTo(b.MonitorRect.Left);
            return leftCompare != 0 ? leftCompare : a.MonitorRect.Top.CompareTo(b.MonitorRect.Top);
        });

        return collected;
    }

    private static bool TryReadMonitorInfo(IntPtr hMonitor, out PhysicalMonitor monitor)
    {
        var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>(), DeviceName = string.Empty };
        if (!GetMonitorInfo(hMonitor, ref info))
        {
            monitor = default;
            return false;
        }

        var isPrimary = (info.Flags & MonitorInfoFPrimary) != 0;
        monitor = new PhysicalMonitor(hMonitor, info.DeviceName ?? string.Empty, isPrimary, info.Monitor, info.WorkArea);
        return true;
    }

    private delegate bool EnumMonitorsDelegate(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, EnumMonitorsDelegate lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point pt);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Win32Point pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }
}

