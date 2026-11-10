using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Pulse.Core.Interfaces;
using Pulse.Core.Services;

namespace Pulse.Services.System;

/// <summary>
/// Real Windows implementation of ISystemControlService:
/// endpoint audio via WASAPI (IAudioEndpointVolume + live callbacks & default device hot-swap recovery),
/// screen capture via GDI, clipboard via WinRT DataPackage, battery via Windows.System.Power.PowerManager.
/// </summary>
public sealed class WindowsSystemControlService : ISystemControlService, IDisposable
{
    private static readonly Guid ClsidMmDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IidIAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly Guid SelfEventContext = new("87A1F2B6-6F1C-4A28-92A7-5C6B7A9E4123");

    private readonly object _audioLock = new();
    private readonly IntPtr _selfContextPtr;
    private readonly VolumeCallbackSink _volumeCallback;
    private readonly DeviceNotificationClient _deviceCallback;

    private IMMDeviceEnumerator? _deviceEnumerator;
    private IAudioEndpointVolume? _endpointVolume;
    private bool _callbacksRegistered;
    private bool _disposed;

    public WindowsSystemControlService()
    {
        _selfContextPtr = Marshal.AllocHGlobal(16);
        Marshal.Copy(SelfEventContext.ToByteArray(), 0, _selfContextPtr, 16);
        _volumeCallback = new VolumeCallbackSink(OnVolumeNotify);
        _deviceCallback = new DeviceNotificationClient(OnDefaultRenderDeviceChanged);

        // Prime endpoint & callbacks lazily if audio subsystem is already online.
        _ = GetEndpointVolume();
    }

    public event EventHandler<AudioStateChangedEventArgs>? AudioStateChanged;

    // ---- Audio (WASAPI) ----

    private IAudioEndpointVolume? GetEndpointVolume()
    {
        lock (_audioLock)
        {
            if (_disposed)
            {
                return null;
            }

            if (_endpointVolume is not null)
            {
                return _endpointVolume;
            }

            try
            {
                if (_deviceEnumerator is null)
                {
                    var comType = Type.GetTypeFromCLSID(ClsidMmDeviceEnumerator);
                    if (comType is not null && Activator.CreateInstance(comType) is IMMDeviceEnumerator enumerator)
                    {
                        _deviceEnumerator = enumerator;
                        _ = _deviceEnumerator.RegisterEndpointNotificationCallback(_deviceCallback);
                    }
                }

                if (_deviceEnumerator is null ||
                    _deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out var device) != 0 ||
                    device is null)
                {
                    return null;
                }

                var iid = IidIAudioEndpointVolume;
                var hr = device.Activate(ref iid, CLSCTX.CLSCTX_ALL, IntPtr.Zero, out var endpoint);
                Marshal.ReleaseComObject(device);

                if (hr != 0 || endpoint is not IAudioEndpointVolume typedEndpoint)
                {
                    return null;
                }

                _endpointVolume = typedEndpoint;
                if (_endpointVolume.RegisterControlChangeNotify(_volumeCallback) == 0)
                {
                    _callbacksRegistered = true;
                }

                return _endpointVolume;
            }
            catch (Exception ex)
            {
                CrashLogger.Write("WindowsSystemControlService.GetEndpointVolume", ex);
                ReleaseEndpoint_NoLock();
                return null;
            }
        }
    }

    private void ReleaseEndpoint_NoLock()
    {
        if (_endpointVolume is null)
        {
            _callbacksRegistered = false;
            return;
        }

        try
        {
            if (_callbacksRegistered)
            {
                _ = _endpointVolume.UnregisterControlChangeNotify(_volumeCallback);
            }
        }
        catch
        {
            // Device may already be disconnected / invalidated.
        }

        try
        {
            _ = Marshal.ReleaseComObject(_endpointVolume);
        }
        catch
        {
        }

        _endpointVolume = null;
        _callbacksRegistered = false;
    }

    private T ExecuteWithEndpointRetry<T>(Func<IAudioEndpointVolume, T> action, T fallback)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var endpoint = GetEndpointVolume();
            if (endpoint is null)
            {
                return fallback;
            }

            try
            {
                return action(endpoint);
            }
            catch (COMException)
            {
                lock (_audioLock)
                {
                    ReleaseEndpoint_NoLock();
                }
            }
            catch (Exception ex)
            {
                CrashLogger.Write("WindowsSystemControlService.ExecuteWithEndpointRetry", ex);
                return fallback;
            }
        }

        return fallback;
    }

    private void OnVolumeNotify(AudioVolumeNotificationData data)
    {
        if (_disposed)
        {
            return;
        }

        var volumePercent = (int)Math.Round(Math.Clamp(data.fMasterVolume, 0f, 1f) * 100f);
        var isSelf = data.guidEventContext == SelfEventContext;
        AudioStateChanged?.Invoke(this, new AudioStateChangedEventArgs(volumePercent, data.bMuted, isSelf, IsDeviceSwitch: false));
    }

    private void OnDefaultRenderDeviceChanged()
    {
        if (_disposed)
        {
            return;
        }

        lock (_audioLock)
        {
            ReleaseEndpoint_NoLock();
        }

        // Rebind on a worker so COM MTA notifications return immediately, then report current level.
        _ = Task.Run(() =>
        {
            if (_disposed)
            {
                return;
            }

            var endpoint = GetEndpointVolume();
            if (endpoint is null)
            {
                return;
            }

            var volume = GetMasterVolume();
            var muted = IsMuted();
            AudioStateChanged?.Invoke(this, new AudioStateChangedEventArgs(volume, muted, IsSelfOriginated: false, IsDeviceSwitch: true));
        });
    }

    public int GetMasterVolume()
    {
        return ExecuteWithEndpointRetry(endpoint =>
        {
            var hr = endpoint.GetMasterVolumeLevelScalar(out var scalar);
            if (hr != 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            return (int)Math.Round(Math.Clamp(scalar, 0f, 1f) * 100f);
        }, 0);
    }

    public void SetMasterVolume(int percent)
    {
        var scalar = Math.Clamp(percent, 0, 100) / 100f;
        _ = ExecuteWithEndpointRetry(endpoint =>
        {
            var hr = endpoint.SetMasterVolumeLevelScalar(scalar, _selfContextPtr);
            if (hr != 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            return true;
        }, false);
    }

    public bool IsMuted()
    {
        return ExecuteWithEndpointRetry(endpoint =>
        {
            var hr = endpoint.GetMute(out var muted);
            if (hr != 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            return muted;
        }, false);
    }

    public void SetMute(bool muted)
    {
        _ = ExecuteWithEndpointRetry(endpoint =>
        {
            var hr = endpoint.SetMute(muted, _selfContextPtr);
            if (hr != 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            return true;
        }, false);
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

            var directory = KnownFolderResolver.GetScreenshotsDirectory();
            Directory.CreateDirectory(directory);

            var fileName = $"Pulse_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var path = Path.Combine(directory, fileName);
            bitmap.Save(path, ImageFormat.Png);

            CopyToClipboard(bitmap);
            return path;
        }
        catch (Exception ex)
        {
            CrashLogger.Write("WindowsSystemControlService.CaptureScreenshot", ex);
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
        catch (Exception ex)
        {
            CrashLogger.Write("WindowsSystemControlService.CopyToClipboard", ex);
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
        lock (_audioLock)
        {
            ReleaseEndpoint_NoLock();

            try
            {
                if (_deviceEnumerator is not null)
                {
                    _ = _deviceEnumerator.UnregisterEndpointNotificationCallback(_deviceCallback);
                    _ = Marshal.ReleaseComObject(_deviceEnumerator);
                    _deviceEnumerator = null;
                }
            }
            catch
            {
            }
        }

        Marshal.FreeHGlobal(_selfContextPtr);
        GC.KeepAlive(_volumeCallback);
        GC.KeepAlive(_deviceCallback);
    }

    // ---- WASAPI COM interop & callbacks ----

    private sealed class VolumeCallbackSink(Action<AudioVolumeNotificationData> onNotify) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr pNotify)
        {
            if (pNotify == IntPtr.Zero)
            {
                return 0;
            }

            try
            {
                var data = Marshal.PtrToStructure<AudioVolumeNotificationData>(pNotify);
                onNotify(data);
            }
            catch
            {
            }

            return 0;
        }
    }

    private sealed class DeviceNotificationClient(Action onDefaultRenderChanged) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string pwstrDeviceId, uint dwNewState) => 0;
        public int OnDeviceAdded(string pwstrDeviceId) => 0;
        public int OnDeviceRemoved(string pwstrDeviceId) => 0;
        public int OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) => 0;

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? pwstrDefaultDeviceId)
        {
            if (flow == EDataFlow.eRender && role == ERole.eConsole)
            {
                onDefaultRenderChanged();
            }

            return 0;
        }
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioVolumeNotificationData
    {
        public Guid guidEventContext;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bMuted;
        public float fMasterVolume;
        public uint nChannels;
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolumeCallback
    {
        [PreserveSig]
        int OnNotify(IntPtr pNotify);
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMNotificationClient
    {
        [PreserveSig]
        int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, uint dwNewState);

        [PreserveSig]
        int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);

        [PreserveSig]
        int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);

        [PreserveSig]
        int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? pwstrDefaultDeviceId);

        [PreserveSig]
        int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, PropertyKey key);
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
        int RegisterEndpointNotificationCallback([MarshalAs(UnmanagedType.Interface)] IMMNotificationClient pClient);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback([MarshalAs(UnmanagedType.Interface)] IMMNotificationClient pClient);
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
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify([MarshalAs(UnmanagedType.Interface)] IAudioEndpointVolumeCallback pNotify);

        [PreserveSig]
        int UnregisterControlChangeNotify([MarshalAs(UnmanagedType.Interface)] IAudioEndpointVolumeCallback pNotify);

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
