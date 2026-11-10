using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Pulse.Core.Interfaces;
using Pulse.Core.Services;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Media.Devices;

namespace Pulse.Services.System;

/// <summary>
/// Watches paired Bluetooth classic/BLE devices via PnP AEP enumeration, live WinRT
/// BluetoothDevice/BluetoothLEDevice ConnectionStatus events, and AudioRender endpoints,
/// surfacing reliable connection/disconnection notifications on the Dynamic Island.
/// </summary>
public sealed class BluetoothWatcherService : IDisposable
{
    private const string IsConnectedProperty = "System.Devices.Aep.IsConnected";
    private const string ItemNameDisplayProperty = "System.ItemNameDisplay";
    private const string InterfaceEnabledProperty = "System.Devices.InterfaceEnabled";
    private const string BluetoothSelector =
        "(System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\" " +
        "OR System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\") " +
        "AND System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True";

    private readonly ISystemEventService _systemEvents;
    private readonly ISettingsService _settings;
    private readonly BluetoothConnectionCoordinator _coordinator = new();
    private readonly object _watcherSync = new();
    private readonly ConcurrentDictionary<string, WinRtDeviceHandle> _winRtDevices = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _activeAepIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastEventTimes = new(StringComparer.OrdinalIgnoreCase);

    private DeviceWatcher? _aepWatcher;
    private DeviceWatcher? _audioWatcher;
    private long _watcherSessionToken;
    private volatile bool _aepEnumerationCompleted;
    private volatile bool _audioEnumerationCompleted;
    private volatile bool _disposed;

    public BluetoothWatcherService(ISystemEventService systemEvents, ISettingsService settings)
    {
        _systemEvents = systemEvents ?? throw new ArgumentNullException(nameof(systemEvents));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings.SettingsChanged += OnSettingsChanged;

        if (_settings.Current.BluetoothNotifications)
        {
            StartWatcher();
        }
    }

    private void OnSettingsChanged(object? sender, Core.Models.AppSettings settings)
    {
        if (_disposed)
        {
            return;
        }

        if (settings.BluetoothNotifications)
        {
            StartWatcher();
        }
        else
        {
            StopWatcher();
        }
    }

    private void StartWatcher()
    {
        lock (_watcherSync)
        {
            if (_disposed || _aepWatcher is not null)
            {
                return;
            }

            try
            {
                _watcherSessionToken++;
                _aepEnumerationCompleted = false;
                _audioEnumerationCompleted = false;
                _coordinator.Clear();
                _activeAepIds.Clear();
                DisposeAllWinRtDevices();

                var aepWatcher = DeviceInformation.CreateWatcher(
                    BluetoothSelector,
                    [IsConnectedProperty, ItemNameDisplayProperty],
                    DeviceInformationKind.AssociationEndpoint);
                aepWatcher.Added += OnDeviceAdded;
                aepWatcher.Updated += OnDeviceUpdated;
                aepWatcher.Removed += OnDeviceRemoved;
                aepWatcher.EnumerationCompleted += OnAepEnumerationCompleted;
                aepWatcher.Start();
                _aepWatcher = aepWatcher;

                StartAudioWatcher_NoLock();
            }
            catch (Exception ex)
            {
                CrashLogger.Write("BluetoothWatcherService.StartWatcher", ex);
                StopWatcher();
            }
        }
    }

    private void StartAudioWatcher_NoLock()
    {
        try
        {
            var audioSelector = MediaDevice.GetAudioRenderSelector();
            var audioWatcher = DeviceInformation.CreateWatcher(
                audioSelector,
                [InterfaceEnabledProperty]);
            audioWatcher.Added += OnAudioEndpointAdded;
            audioWatcher.Updated += OnAudioEndpointUpdated;
            audioWatcher.Removed += OnAudioEndpointRemoved;
            audioWatcher.EnumerationCompleted += OnAudioEnumerationCompleted;
            audioWatcher.Start();
            _audioWatcher = audioWatcher;
        }
        catch (Exception ex)
        {
            CrashLogger.Write("BluetoothWatcherService.StartAudioWatcher", ex);
            _audioWatcher = null;
        }
    }

    private void StopWatcher()
    {
        lock (_watcherSync)
        {
            _watcherSessionToken++;

            if (_aepWatcher is not null)
            {
                try
                {
                    if (_aepWatcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                    {
                        _aepWatcher.Stop();
                    }

                    _aepWatcher.Added -= OnDeviceAdded;
                    _aepWatcher.Updated -= OnDeviceUpdated;
                    _aepWatcher.Removed -= OnDeviceRemoved;
                    _aepWatcher.EnumerationCompleted -= OnAepEnumerationCompleted;
                }
                catch
                {
                }

                _aepWatcher = null;
            }

            if (_audioWatcher is not null)
            {
                try
                {
                    if (_audioWatcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                    {
                        _audioWatcher.Stop();
                    }

                    _audioWatcher.Added -= OnAudioEndpointAdded;
                    _audioWatcher.Updated -= OnAudioEndpointUpdated;
                    _audioWatcher.Removed -= OnAudioEndpointRemoved;
                    _audioWatcher.EnumerationCompleted -= OnAudioEnumerationCompleted;
                }
                catch
                {
                }

                _audioWatcher = null;
            }

            _aepEnumerationCompleted = false;
            _audioEnumerationCompleted = false;
            _activeAepIds.Clear();
            DisposeAllWinRtDevices();
            _coordinator.Clear();
        }
    }

    private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation args)
    {
        try
        {
            if (_disposed || !ReferenceEquals(sender, _aepWatcher))
            {
                return;
            }

            var sessionToken = Volatile.Read(ref _watcherSessionToken);
            var notifyTransition = _aepEnumerationCompleted;
            _activeAepIds[args.Id] = 0;

            var name = ResolveDeviceName(args.Name, args.Properties);
            var isConnected = TryReadBoolProperty(args.Properties, IsConnectedProperty, out var flag) && flag;

            var change = _coordinator.RegisterOrUpdatePrimaryEndpoint(args.Id, name, isConnected, notifyTransition);
            EmitIfNeeded(change);

            _ = AttachWinRtDeviceAsync(args.Id, notifyTransition, sessionToken);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("BluetoothWatcherService.OnDeviceAdded", ex);
        }
    }

    private void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        try
        {
            if (_disposed || !ReferenceEquals(sender, _aepWatcher))
            {
                return;
            }

            var hasName = TryReadStringProperty(args.Properties, ItemNameDisplayProperty, out var updatedName);
            var hasConn = TryReadBoolProperty(args.Properties, IsConnectedProperty, out var isConnected);

            if (hasConn && !_winRtDevices.ContainsKey(args.Id))
            {
                var change = _coordinator.RegisterOrUpdatePrimaryEndpoint(
                    args.Id,
                    hasName ? updatedName : null,
                    isConnected,
                    _aepEnumerationCompleted);
                EmitIfNeeded(change);
            }
            else if (hasName)
            {
                var change = _coordinator.UpdatePrimaryName(args.Id, updatedName, _aepEnumerationCompleted);
                EmitIfNeeded(change);
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("BluetoothWatcherService.OnDeviceUpdated", ex);
        }
    }

    private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        try
        {
            if (_disposed || !ReferenceEquals(sender, _aepWatcher))
            {
                return;
            }

            _activeAepIds.TryRemove(args.Id, out _);
            if (_winRtDevices.TryRemove(args.Id, out var handle))
            {
                handle.Dispose();
            }

            var change = _coordinator.RemoveEndpoint(args.Id, _aepEnumerationCompleted);
            EmitIfNeeded(change);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("BluetoothWatcherService.OnDeviceRemoved", ex);
        }
    }

    private void OnAepEnumerationCompleted(DeviceWatcher sender, object args)
    {
        if (ReferenceEquals(sender, _aepWatcher))
        {
            _aepEnumerationCompleted = true;
        }
    }

    private void OnAudioEndpointAdded(DeviceWatcher sender, DeviceInformation args)
    {
        try
        {
            if (_disposed || !ReferenceEquals(sender, _audioWatcher))
            {
                return;
            }

            var isEnabled = TryReadBoolProperty(args.Properties, InterfaceEnabledProperty, out var propEnabled)
                ? propEnabled
                : args.IsEnabled;

            var change = _coordinator.RegisterOrUpdateAudioEndpoint(
                AudioSourceId(args.Id),
                args.Name,
                isEnabled,
                _audioEnumerationCompleted);
            EmitIfNeeded(change);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("BluetoothWatcherService.OnAudioEndpointAdded", ex);
        }
    }

    private void OnAudioEndpointUpdated(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        try
        {
            if (_disposed || !ReferenceEquals(sender, _audioWatcher))
            {
                return;
            }

            if (TryReadBoolProperty(args.Properties, InterfaceEnabledProperty, out var isEnabled))
            {
                var change = _coordinator.RegisterOrUpdateAudioEndpoint(
                    AudioSourceId(args.Id),
                    rawEndpointName: null,
                    isEnabled,
                    _audioEnumerationCompleted);
                EmitIfNeeded(change);
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("BluetoothWatcherService.OnAudioEndpointUpdated", ex);
        }
    }

    private void OnAudioEndpointRemoved(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        try
        {
            if (_disposed || !ReferenceEquals(sender, _audioWatcher))
            {
                return;
            }

            var change = _coordinator.RemoveEndpoint(AudioSourceId(args.Id), _audioEnumerationCompleted);
            EmitIfNeeded(change);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("BluetoothWatcherService.OnAudioEndpointRemoved", ex);
        }
    }

    private void OnAudioEnumerationCompleted(DeviceWatcher sender, object args)
    {
        if (ReferenceEquals(sender, _audioWatcher))
        {
            _audioEnumerationCompleted = true;
        }
    }

    private async Task AttachWinRtDeviceAsync(string aepId, bool notifyInitialConnection, long sessionToken)
    {
        if (_disposed ||
            Volatile.Read(ref _watcherSessionToken) != sessionToken ||
            !_activeAepIds.ContainsKey(aepId))
        {
            return;
        }

        try
        {
            var isLe = aepId.StartsWith("BluetoothLE#", StringComparison.OrdinalIgnoreCase);
            if (isLe)
            {
                var leDevice = await BluetoothLEDevice.FromIdAsync(aepId).AsTask().ConfigureAwait(false);
                if (leDevice is null)
                {
                    return;
                }

                if (_disposed ||
                    Volatile.Read(ref _watcherSessionToken) != sessionToken ||
                    !_activeAepIds.ContainsKey(aepId))
                {
                    leDevice.Dispose();
                    return;
                }

                var handle = WinRtDeviceHandle.Create(
                    leDevice,
                    onStatusChanged: dev => OnWinRtStatusChanged(aepId, dev.Name, dev.ConnectionStatus == BluetoothConnectionStatus.Connected, sessionToken),
                    onNameChanged: dev => OnWinRtNameChanged(aepId, dev.Name, sessionToken));

                _winRtDevices.AddOrUpdate(aepId, handle, (_, previous) =>
                {
                    previous.Dispose();
                    return handle;
                });

                var name = string.IsNullOrWhiteSpace(leDevice.Name) ? _coordinator.TryGetPrimaryName(aepId) : leDevice.Name;
                var connected = leDevice.ConnectionStatus == BluetoothConnectionStatus.Connected;
                var change = _coordinator.RegisterOrUpdatePrimaryEndpoint(aepId, name, connected, notifyInitialConnection);
                EmitIfNeeded(change);
            }
            else
            {
                var classicDevice = await BluetoothDevice.FromIdAsync(aepId).AsTask().ConfigureAwait(false);
                if (classicDevice is null)
                {
                    return;
                }

                if (_disposed ||
                    Volatile.Read(ref _watcherSessionToken) != sessionToken ||
                    !_activeAepIds.ContainsKey(aepId))
                {
                    classicDevice.Dispose();
                    return;
                }

                var handle = WinRtDeviceHandle.Create(
                    classicDevice,
                    onStatusChanged: dev => OnWinRtStatusChanged(aepId, dev.Name, dev.ConnectionStatus == BluetoothConnectionStatus.Connected, sessionToken),
                    onNameChanged: dev => OnWinRtNameChanged(aepId, dev.Name, sessionToken));

                _winRtDevices.AddOrUpdate(aepId, handle, (_, previous) =>
                {
                    previous.Dispose();
                    return handle;
                });

                var name = string.IsNullOrWhiteSpace(classicDevice.Name) ? _coordinator.TryGetPrimaryName(aepId) : classicDevice.Name;
                var connected = classicDevice.ConnectionStatus == BluetoothConnectionStatus.Connected;
                var change = _coordinator.RegisterOrUpdatePrimaryEndpoint(aepId, name, connected, notifyInitialConnection);
                EmitIfNeeded(change);
            }
        }
        catch (Exception) when (_disposed || Volatile.Read(ref _watcherSessionToken) != sessionToken || !_activeAepIds.ContainsKey(aepId))
        {
            // Device removed or watcher stopped mid-attach.
        }
        catch (Exception ex)
        {
            CrashLogger.Write("BluetoothWatcherService.AttachWinRtDeviceAsync", ex, $"AepId={aepId}");
        }
    }

    private void OnWinRtStatusChanged(string aepId, string? rawName, bool isConnected, long sessionToken)
    {
        try
        {
            if (_disposed ||
                Volatile.Read(ref _watcherSessionToken) != sessionToken ||
                !_activeAepIds.ContainsKey(aepId))
            {
                return;
            }

            var resolvedName = string.IsNullOrWhiteSpace(rawName) ? _coordinator.TryGetPrimaryName(aepId) : rawName;
            var change = _coordinator.RegisterOrUpdatePrimaryEndpoint(aepId, resolvedName, isConnected, notifyTransition: true);
            EmitIfNeeded(change);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("BluetoothWatcherService.OnWinRtStatusChanged", ex);
        }
    }

    private void OnWinRtNameChanged(string aepId, string? rawName, long sessionToken)
    {
        try
        {
            if (_disposed ||
                Volatile.Read(ref _watcherSessionToken) != sessionToken ||
                !_activeAepIds.ContainsKey(aepId))
            {
                return;
            }

            var change = _coordinator.UpdatePrimaryName(aepId, rawName, notifyTransition: true);
            EmitIfNeeded(change);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("BluetoothWatcherService.OnWinRtNameChanged", ex);
        }
    }

    private void EmitIfNeeded(BluetoothConnectionChange? change)
    {
        if (change is null)
        {
            return;
        }

        PublishDebounced(change.DeviceName, change.IsConnected);
    }

    private void PublishDebounced(string name, bool connected)
    {
        if (_disposed || !_settings.Current.BluetoothNotifications)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (_lastEventTimes.Count > 32)
        {
            foreach (var entry in _lastEventTimes)
            {
                if ((now - entry.Value) >= TimeSpan.FromSeconds(30))
                {
                    _lastEventTimes.TryRemove(entry.Key, out _);
                }
            }
        }

        var key = $"{name}|{(connected ? '1' : '0')}";
        if (_lastEventTimes.TryGetValue(key, out var prevTime) && (now - prevTime) < TimeSpan.FromSeconds(1.8))
        {
            return;
        }

        _lastEventTimes[key] = now;
        _systemEvents.ReportBluetoothStatus(name, connected);
    }

    private void DisposeAllWinRtDevices()
    {
        foreach (var key in _winRtDevices.Keys)
        {
            if (_winRtDevices.TryRemove(key, out var handle))
            {
                handle.Dispose();
            }
        }
    }

    private static string AudioSourceId(string endpointId) => "audio:" + endpointId;

    private static string ResolveDeviceName(string? name, IReadOnlyDictionary<string, object> properties)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return TryReadStringProperty(properties, ItemNameDisplayProperty, out var display) && display is not null
            ? display
            : string.Empty;
    }

    private static bool TryReadBoolProperty(IReadOnlyDictionary<string, object> properties, string key, out bool value)
    {
        if (properties.TryGetValue(key, out var raw) && raw is bool flag)
        {
            value = flag;
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryReadStringProperty(IReadOnlyDictionary<string, object> properties, string key, out string? value)
    {
        if (properties.TryGetValue(key, out var raw) && raw is string text && !string.IsNullOrWhiteSpace(text))
        {
            value = text;
            return true;
        }

        value = null;
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.SettingsChanged -= OnSettingsChanged;
        StopWatcher();
        _lastEventTimes.Clear();
    }

    private sealed class WinRtDeviceHandle : IDisposable
    {
        private BluetoothDevice? _classic;
        private BluetoothLEDevice? _le;
        private TypedEventHandler<BluetoothDevice, object>? _classicStatusHandler;
        private TypedEventHandler<BluetoothDevice, object>? _classicNameHandler;
        private TypedEventHandler<BluetoothLEDevice, object>? _leStatusHandler;
        private TypedEventHandler<BluetoothLEDevice, object>? _leNameHandler;
        private int _disposedFlag;

        private WinRtDeviceHandle()
        {
        }

        public static WinRtDeviceHandle Create(
            BluetoothDevice device,
            Action<BluetoothDevice> onStatusChanged,
            Action<BluetoothDevice> onNameChanged)
        {
            var handle = new WinRtDeviceHandle { _classic = device };
            handle._classicStatusHandler = (sender, _) => onStatusChanged(sender);
            handle._classicNameHandler = (sender, _) => onNameChanged(sender);
            device.ConnectionStatusChanged += handle._classicStatusHandler;
            device.NameChanged += handle._classicNameHandler;
            return handle;
        }

        public static WinRtDeviceHandle Create(
            BluetoothLEDevice device,
            Action<BluetoothLEDevice> onStatusChanged,
            Action<BluetoothLEDevice> onNameChanged)
        {
            var handle = new WinRtDeviceHandle { _le = device };
            handle._leStatusHandler = (sender, _) => onStatusChanged(sender);
            handle._leNameHandler = (sender, _) => onNameChanged(sender);
            device.ConnectionStatusChanged += handle._leStatusHandler;
            device.NameChanged += handle._leNameHandler;
            return handle;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposedFlag, 1) != 0)
            {
                return;
            }

            if (_classic is not null)
            {
                try
                {
                    if (_classicStatusHandler is not null)
                    {
                        _classic.ConnectionStatusChanged -= _classicStatusHandler;
                    }

                    if (_classicNameHandler is not null)
                    {
                        _classic.NameChanged -= _classicNameHandler;
                    }
                }
                catch
                {
                }

                try { _classic.Dispose(); } catch { }
                _classic = null;
            }

            if (_le is not null)
            {
                try
                {
                    if (_leStatusHandler is not null)
                    {
                        _le.ConnectionStatusChanged -= _leStatusHandler;
                    }

                    if (_leNameHandler is not null)
                    {
                        _le.NameChanged -= _leNameHandler;
                    }
                }
                catch
                {
                }

                try { _le.Dispose(); } catch { }
                _le = null;
            }
        }
    }
}
