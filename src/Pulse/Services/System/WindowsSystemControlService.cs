using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Pulse.Core.Interfaces;

namespace Pulse.Services.System;

/// <summary>
/// Real Windows implementation of ISystemControlService:
/// endpoint audio via WASAPI (IAudioEndpointVolume), screen capture via GDI,
/// clipboard via WinRT DataPackage, battery via Windows.System.Power.PowerManager.
/// </summary>
public sealed class WindowsSystemControlService : ISystemControlService, IDisposable
{
    private bool _disposed;

    // ---- Audio (WASAPI) ----

    private static IMMDeviceEnumerator? _deviceEnumerator;
    private static IAudioEndpointVolume? _endpointVolume;

    private static IAudioEndpointVolume? GetEndpointVolume()
    {
        if (_endpointVolume is not null)
        {
            return _endpointVolume;
        }

        try
        {
            _deviceEnumerator ??= (IMMDeviceEnumerator)Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!);
            if (_deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out var device) != 0 || device is null)
            {
                return null;
            }

            var guid = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
            device.Activate(ref guid, CLSCTX.CLSCTX_ALL, IntPtr.Zero, out var endpoint);
            _endpointVolume = (IAudioEndpointVolume)endpoint!;
            return _endpointVolume;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public int GetMasterVolume()
    {
        var endpoint = GetEndpointVolume();
        if (endpoint is null)
        {
            return 0;
        }

        try
        {
            endpoint.GetMasterVolumeLevelScalar(out var scalar);
            return (int)Math.Round(scalar * 100f);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public void SetMasterVolume(int percent)
    {
        var endpoint = GetEndpointVolume();
        if (endpoint is null)
        {
            return;
        }

        try
        {
            endpoint.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, IntPtr.Zero);
        }
        catch (Exception)
        {
        }
    }

    public bool IsMuted()
    {
        var endpoint = GetEndpointVolume();
        if (endpoint is null)
        {
            return false;
        }

        try
        {
            endpoint.GetMute(out var muted);
            return muted;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void SetMute(bool muted)
    {
        var endpoint = GetEndpointVolume();
        if (endpoint is null)
        {
            return;
        }

        try
        {
            endpoint.SetMute(muted, IntPtr.Zero);
        }
        catch (Exception)
        {
        }
    }

    public void ToggleMute() => SetMute(!IsMuted());

    // ---- Screen capture ----

    public string CaptureScreenshot()
    {
        try
        {
            var bounds = GetScreenBounds();
            if (bounds is null)
            {
                return string.Empty;
            }

            using var bitmap = new Bitmap(bounds.Value.Width, bounds.Value.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Value.X, bounds.Value.Y, 0, 0, bounds.Value.Size, CopyPixelOperation.SourceCopy);
            }

            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "Screenshots");
            Directory.CreateDirectory(directory);

            var fileName = $"Pulse_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var path = Path.Combine(directory, fileName);
            bitmap.Save(path, ImageFormat.Png);

            CopyToClipboard(bitmap);
            return path;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static Rectangle? GetScreenBounds()
    {
        var x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        return width <= 0 || height <= 0 ? null : new Rectangle(x, y, width, height);
    }

    private static void CopyToClipboard(Bitmap bitmap)
    {
        try
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            var bytes = stream.ToArray();

            var memoryStream = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = new global::Windows.Storage.Streams.DataWriter(memoryStream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                _ = writer.StoreAsync().AsTask().GetAwaiter().GetResult();
                _ = writer.FlushAsync().AsTask().GetAwaiter().GetResult();
            }

            var package = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            package.RequestedOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            package.SetBitmap(global::Windows.Storage.Streams.RandomAccessStreamReference.CreateFromStream(memoryStream));
            global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (Exception)
        {
        }
    }

    // ---- Battery ----

    public (int Percent, bool IsCharging, bool IsLow, bool IsOnAc)? GetBatteryStatus()
    {
        try
        {
            var battery = global::Windows.System.Power.PowerManager.BatteryStatus;
            if (battery == global::Windows.System.Power.BatteryStatus.NotPresent)
            {
                return null;
            }

            var percent = global::Windows.System.Power.PowerManager.RemainingChargePercent;
            var isCharging = battery == global::Windows.System.Power.BatteryStatus.Charging;
            var isOnAc = global::Windows.System.Power.PowerManager.PowerSupplyStatus
                == global::Windows.System.Power.PowerSupplyStatus.Adequate;
            return (percent, isCharging, percent <= 20 && !isCharging, isOnAc);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool TryActivateApp(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            return false;
        }

        // Win32 media sessions report the bare exe name (e.g. "Spotify.exe").
        var fileName = Path.GetFileName(appId.Trim());
        if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var processName = Path.GetFileNameWithoutExtension(fileName);
                foreach (var process in global::System.Diagnostics.Process.GetProcessesByName(processName))
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        _ = ShowWindow(process.MainWindowHandle, SwRestore);
                        _ = SetForegroundWindow(process.MainWindowHandle);
                        return true;
                    }
                }
            }
            catch
            {
            }
        }

        // Packaged apps (e.g. "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify").
        try
        {
            global::System.Diagnostics.Process.Start(new global::System.Diagnostics.ProcessStartInfo(
                "explorer.exe",
                $"shell:AppsFolder\\{appId}")
            {
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
        }

        try
        {
            global::System.Diagnostics.Process.Start(new global::System.Diagnostics.ProcessStartInfo(appId) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryOpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            if (File.Exists(path))
            {
                global::System.Diagnostics.Process.Start(new global::System.Diagnostics.ProcessStartInfo(
                    "explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                return true;
            }

            if (Directory.Exists(path))
            {
                global::System.Diagnostics.Process.Start(new global::System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                return true;
            }

            global::System.Diagnostics.Process.Start(new global::System.Diagnostics.ProcessStartInfo(
                "explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_endpointVolume is not null)
            {
                _ = Marshal.ReleaseComObject(_endpointVolume);
                _endpointVolume = null;
            }

            if (_deviceEnumerator is not null)
            {
                _ = Marshal.ReleaseComObject(_deviceEnumerator);
                _deviceEnumerator = null;
            }
        }
        catch (Exception)
        {
        }
    }

    // ---- WASAPI COM interop ----

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [Flags]
    private enum CLSCTX : uint
    {
        CLSCTX_ALL = 0x17
    }

    private enum EDataFlow
    {
        eRender,
        eCapture,
        eAll
    }

    private enum ERole
    {
        eConsole,
        eMultimedia,
        eCommunications
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr ppDevices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, [MarshalAs(UnmanagedType.Interface)] out IMMDevice? ppEndpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string wszId, out IMMDevice ppEndpoint);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr pClient);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, CLSCTX dwClsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object? ppInterface);

        [PreserveSig]
        int OpenPropertyStore(uint stgmAccess, out IntPtr ppProperties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppszId);

        // IMMDevice has a 5th method (GetState) — omitted; IUnknown layouts pad fine for our use.
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IntPtr pNotify);

        [PreserveSig]
        int UnregisterControlChangeNotify(IntPtr pNotify);

        [PreserveSig]
        int GetChannelCount(out uint pnChannelCount);

        [PreserveSig]
        int SetMasterVolumeLevel(float fLevelDB, IntPtr pguidEventContext);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float fLevel, IntPtr pguidEventContext);

        [PreserveSig]
        int GetMasterVolumeLevel(out float pfLevelDB);

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float pfLevel);

        [PreserveSig]
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, IntPtr pguidEventContext);

        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, IntPtr pguidEventContext);

        [PreserveSig]
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);

        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);

        [PreserveSig]
        int SetMute(bool bMute, IntPtr pguidEventContext);

        [PreserveSig]
        int GetMute(out bool pbMute);
    }
}

