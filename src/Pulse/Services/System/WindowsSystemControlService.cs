using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;
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
    private static readonly Guid IidIMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid IidIAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly Guid ClsidApplicationActivationManager = new("45BA127D-10A8-46EA-8AB7-56EA9078943C");
    private static readonly Guid IidIApplicationActivationManager = new("2E941141-7F97-4756-BA1D-9DECDE894A3D");
    private static readonly Guid SelfEventContext = new("87A1F2B6-6F1C-4A28-92A7-5C6B7A9E4123");

    private readonly object _audioLock = new();
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly VolumeCallbackSink _volumeCallback;
    private readonly DeviceNotificationClient _deviceCallback;

    private IMMDeviceEnumerator? _deviceEnumerator;
    private IAudioEndpointVolume? _endpointVolume;
    private bool _callbacksRegistered;
    private volatile bool _disposed;

    public WindowsSystemControlService(DispatcherQueue? dispatcherQueue = null)
    {
        _dispatcherQueue = dispatcherQueue;
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
                    var clsid = ClsidMmDeviceEnumerator;
                    var enumeratorIid = IidIMMDeviceEnumerator;
                    if (CoCreateInstance(ref clsid, IntPtr.Zero, (uint)CLSCTX.CLSCTX_ALL, ref enumeratorIid, out var rawEnumerator) == 0 &&
                        rawEnumerator is IMMDeviceEnumerator enumerator)
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
            if (_disposed)
            {
                return fallback;
            }

            var endpoint = GetEndpointVolume();
            if (endpoint is null)
            {
                return fallback;
            }

            try
            {
                return action(endpoint);
            }
            catch (Exception ex) when (ex is COMException or InvalidComObjectException)
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
            var context = SelfEventContext;
            var hr = endpoint.SetMasterVolumeLevelScalar(scalar, ref context);
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
            var context = SelfEventContext;
            var hr = endpoint.SetMute(muted, ref context);
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
        var (path, pngBytes) = CaptureScreenshotBytes();
        if (pngBytes is not null)
        {
            CopyPngBytesToClipboard(pngBytes);
        }

        return path;
    }

    public async Task<string> CaptureScreenshotAsync()
    {
        var (path, pngBytes) = await Task.Run(CaptureScreenshotBytes).ConfigureAwait(false);
        if (pngBytes is null)
        {
            return path;
        }

        if (_dispatcherQueue is { HasThreadAccess: false } dispatcher)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!dispatcher.TryEnqueue(() =>
                {
                    try
                    {
                        CopyPngBytesToClipboard(pngBytes);
                    }
                    finally
                    {
                        tcs.TrySetResult();
                    }
                }))
            {
                CopyPngBytesToClipboard(pngBytes);
                return path;
            }

            await tcs.Task.ConfigureAwait(false);
        }
        else
        {
            CopyPngBytesToClipboard(pngBytes);
        }

        return path;
    }

    private static (string Path, byte[]? PngBytes) CaptureScreenshotBytes()
    {
        try
        {
            var bounds = GetScreenBounds();
            if (bounds is null)
            {
                return (string.Empty, null);
            }

            using var bitmap = new Bitmap(bounds.Value.Width, bounds.Value.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Value.X, bounds.Value.Y, 0, 0, bounds.Value.Size, CopyPixelOperation.SourceCopy);
            }

            var directory = KnownFolderResolver.GetScreenshotsDirectory();
            Directory.CreateDirectory(directory);

            var fileName = $"Pulse_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            var path = Path.Combine(directory, fileName);

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            var bytes = stream.ToArray();
            File.WriteAllBytes(path, bytes);
            return (path, bytes);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("WindowsSystemControlService.CaptureScreenshot", ex);
            return (string.Empty, null);
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

    private static void CopyPngBytesToClipboard(byte[] bytes)
    {
        try
        {
            using var memoryStream = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = new global::Windows.Storage.Streams.DataWriter(memoryStream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                _ = writer.StoreAsync().AsTask().GetAwaiter().GetResult();
                _ = writer.FlushAsync().AsTask().GetAwaiter().GetResult();
                writer.DetachStream();
            }

            memoryStream.Seek(0);
            var package = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            package.RequestedOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            package.SetBitmap(global::Windows.Storage.Streams.RandomAccessStreamReference.CreateFromStream(memoryStream));
            global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            global::Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
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
        var plan = AppActivationTargetResolver.BuildPlan(appId);
        if (plan is null)
        {
            return false;
        }

        try
        {
            if (plan.IsPackagedOrExplicitAumid && TryActivateAumid(plan.TrimAppId))
            {
                return true;
            }

            var matchingPids = new HashSet<uint>();
            var discoveredAumids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var discoveredExePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (plan.CandidateProcessNames.Count > 0 &&
                TryActivateRunningProcesses(plan.CandidateProcessNames, matchingPids, discoveredAumids, discoveredExePaths))
            {
                return true;
            }

            if (matchingPids.Count > 0 && TryActivateWindowByPids(matchingPids))
            {
                return true;
            }

            foreach (var packagedAumid in discoveredAumids)
            {
                if (TryActivateAumid(packagedAumid))
                {
                    return true;
                }
            }

            foreach (var exePath in discoveredExePaths)
            {
                if (!AppActivationTargetResolver.IsWindowsAppsExecutablePath(exePath) &&
                    TryStartShellTarget(exePath))
                {
                    return true;
                }
            }

            if (!plan.IsPackagedOrExplicitAumid &&
                plan.AllowDirectAumidActivation &&
                TryActivateAumid(plan.TrimAppId))
            {
                return true;
            }

            if (Path.IsPathRooted(plan.TrimAppId) &&
                File.Exists(plan.TrimAppId) &&
                TryStartShellTarget(plan.TrimAppId))
            {
                return true;
            }

            if (plan.IsExplicitExecutable)
            {
                var fileName = Path.GetFileName(plan.TrimAppId);
                var registeredPath = TryResolveAppPathFromRegistry(fileName);
                if (registeredPath is not null && TryStartShellTarget(registeredPath))
                {
                    return true;
                }
            }

            if (plan.ProtocolFallbackUri is not null && TryStartShellTarget(plan.ProtocolFallbackUri))
            {
                return true;
            }

            if (!plan.IsExplicitExecutable && TryStartShellTarget(plan.TrimAppId))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("WindowsSystemControlService.TryActivateApp", ex);
        }

        return false;
    }

    private static bool TryActivateRunningProcesses(
        IReadOnlyList<string> processNames,
        HashSet<uint> matchingPids,
        HashSet<string> discoveredAumids,
        HashSet<string> discoveredExePaths)
    {
        foreach (var processName in processNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch
            {
                continue;
            }

            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        if (process.HasExited)
                        {
                            continue;
                        }

                        if (process.MainWindowHandle != IntPtr.Zero &&
                            BringWindowToForeground(process.MainWindowHandle))
                        {
                            return true;
                        }

                        matchingPids.Add(unchecked((uint)process.Id));

                        try
                        {
                            var handle = process.Handle;
                            if (TryGetProcessAumid(handle, out var aumid) && !string.IsNullOrWhiteSpace(aumid))
                            {
                                discoveredAumids.Add(aumid);
                            }

                            if (TryGetProcessExecutablePath(handle, out var exePath) && !string.IsNullOrWhiteSpace(exePath))
                            {
                                discoveredExePaths.Add(exePath);
                            }
                        }
                        catch
                        {
                        }
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        return false;
    }

    private static bool TryActivateWindowByPids(HashSet<uint> targetPids)
    {
        var candidateHwnd = IntPtr.Zero;
        try
        {
            EnumWindows((hwnd, unusedParam) =>
            {
                _ = GetWindowThreadProcessId(hwnd, out var pid);
                if (!targetPids.Contains(pid))
                {
                    return true;
                }

                if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GwOwner) != IntPtr.Zero)
                {
                    return true;
                }

                var exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
                if ((exStyle & WsExToolWindow) != 0)
                {
                    return true;
                }

                if (!GetWindowRect(hwnd, out var rect) ||
                    rect.Right - rect.Left <= 0 ||
                    rect.Bottom - rect.Top <= 0)
                {
                    return true;
                }

                candidateHwnd = hwnd;
                return false;
            }, IntPtr.Zero);
        }
        catch
        {
            return false;
        }

        return candidateHwnd != IntPtr.Zero && BringWindowToForeground(candidateHwnd);
    }

    private static bool BringWindowToForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
        {
            return false;
        }

        if (IsIconic(hwnd))
        {
            _ = ShowWindow(hwnd, SwRestore);
        }
        else if (!IsWindowVisible(hwnd))
        {
            _ = ShowWindow(hwnd, SwShow);
        }

        _ = BringWindowToTop(hwnd);
        if (!SetForegroundWindow(hwnd))
        {
            // IslandWindow is WS_EX_NOACTIVATE, so direct SetForegroundWindow can stop at a
            // taskbar flash; SwitchToThisWindow forces focus transfer reliably.
            SwitchToThisWindow(hwnd, fAltTab: true);
        }

        return true;
    }

    private static bool TryActivateAumid(string aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid))
        {
            return false;
        }

        object? activationObj = null;
        try
        {
            var clsid = ClsidApplicationActivationManager;
            var iid = IidIApplicationActivationManager;
            var hr = CoCreateInstance(ref clsid, IntPtr.Zero, (uint)CLSCTX.CLSCTX_LOCAL_SERVER, ref iid, out activationObj);
            if (hr >= 0 && activationObj is IApplicationActivationManager manager)
            {
                hr = manager.ActivateApplication(aumid, null, 0, out _);
                if (hr >= 0)
                {
                    return true;
                }
            }
        }
        catch
        {
        }
        finally
        {
            if (activationObj is not null && Marshal.IsComObject(activationObj))
            {
                try { _ = Marshal.ReleaseComObject(activationObj); } catch { }
            }
        }

        // Only launch explorer fallback for real packaged AUMIDs or shell aliases (not bare .exe names).
        if (!aumid.Contains('!', StringComparison.Ordinal) &&
            aumid.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{aumid}")
            {
                UseShellExecute = true
            });
            return process is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryStartShellTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return process is not null;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryResolveAppPathFromRegistry(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        string[] roots =
        [
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{fileName}"
        ];

        foreach (var subKeyPath in roots)
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using var key = hive.OpenSubKey(subKeyPath);
                    var raw = key?.GetValue(null) as string;
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        continue;
                    }

                    var cleaned = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
                    if (File.Exists(cleaned))
                    {
                        return cleaned;
                    }
                }
                catch
                {
                }
            }
        }

        return null;
    }

    private static bool TryGetProcessAumid(IntPtr processHandle, out string? aumid)
    {
        aumid = null;
        uint length = 256;
        var buffer = new char[length];
        var result = GetApplicationUserModelId(processHandle, ref length, buffer);
        if (result == ErrorInsufficientBuffer && length > 0 && length <= 2048)
        {
            buffer = new char[length];
            result = GetApplicationUserModelId(processHandle, ref length, buffer);
        }

        if (result != 0 || length == 0)
        {
            return false;
        }

        var count = (int)Math.Min(buffer.Length, length);
        aumid = new string(buffer, 0, count).TrimEnd('\0');
        return !string.IsNullOrWhiteSpace(aumid);
    }

    private static bool TryGetProcessExecutablePath(IntPtr processHandle, out string? path)
    {
        path = null;
        var buffer = new char[512];
        var length = buffer.Length;
        if (!QueryFullProcessImageName(processHandle, 0, buffer, ref length) || length <= 0)
        {
            return false;
        }

        path = new string(buffer, 0, Math.Min(buffer.Length, length)).TrimEnd('\0');
        return !string.IsNullOrWhiteSpace(path);
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
                using var process = global::System.Diagnostics.Process.Start(new global::System.Diagnostics.ProcessStartInfo(
                    "explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                return true;
            }

            if (Directory.Exists(path))
            {
                using var process = global::System.Diagnostics.Process.Start(new global::System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                return true;
            }

            using var fallback = global::System.Diagnostics.Process.Start(new global::System.Diagnostics.ProcessStartInfo(
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

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CoCreateInstance(
        ref Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SwShow = 5;
    private const int SwRestore = 9;
    private const uint GwOwner = 4;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const int ErrorInsufficientBuffer = 122;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool fAltTab);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Win32Rect lpRect);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(
        IntPtr hProcess,
        ref uint applicationUserModelIdLength,
        [Out] char[]? applicationUserModelId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(
        IntPtr hProcess,
        int dwFlags,
        [Out] char[] lpExeName,
        ref int lpdwSize);

    [Flags]
    private enum CLSCTX : uint
    {
        CLSCTX_LOCAL_SERVER = 0x4,
        CLSCTX_ALL = 0x17
    }

    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            uint options,
            out uint processId);

        [PreserveSig]
        int ActivateForFile(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            [MarshalAs(UnmanagedType.LPWStr)] string verb,
            out uint processId);

        [PreserveSig]
        int ActivateForProtocol(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            out uint processId);
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
        int SetMasterVolumeLevelScalar(float fLevel, ref Guid guidEventContext);

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
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid guidEventContext);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }
}
