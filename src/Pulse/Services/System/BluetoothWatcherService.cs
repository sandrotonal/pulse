using System.Collections.Concurrent;
using Pulse.Core.Interfaces;
using Pulse.Core.Services;
using Windows.Devices.Enumeration;

namespace Pulse.Services.System;

/// <summary>
/// Watches paired Bluetooth classic and BLE devices via Windows PnP AEP enumeration
/// and surfaces real connection/disconnection notifications on the Dynamic Island.
/// </summary>
public sealed class BluetoothWatcherService : IDisposable
{
    private const string IsConnectedProperty = "System.Devices.Aep.IsConnected";
    private const string BluetoothSelector =
        "(System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\" " +
        "OR System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\") " +
        "AND System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True";

    private readonly ISystemEventService _systemEvents;
    private readonly ISettingsService _settings;
    private readonly ConcurrentDictionary<string, (string Name, bool Connected)> _devices = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastEventTimes = new();
    private DeviceWatcher? _watcher;
    private volatile bool _enumerationCompleted;
    private bool _disposed;

    public BluetoothWatcherService(ISystemEventService systemEvents, ISettingsService settings)
    {
        _systemEvents = systemEvents ?? throw new ArgumentNullException(nameof(systemEvents));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        try
        {
            _watcher = DeviceInformation.CreateWatcher(
                BluetoothSelector,
                [IsConnectedProperty],
                DeviceInformationKind.AssociationEndpoint);
            _watcher.Added += OnDeviceAdded;
            _watcher.Updated += OnDeviceUpdated;
            _watcher.Removed += OnDeviceRemoved;
            _watcher.EnumerationCompleted += OnEnumerationCompleted;
            _watcher.Start();
        }
        catch (Exception ex)
        {
            CrashLogger.Write("BluetoothWatcherService.Ctor", ex);
            _watcher = null;
        }
    }

    private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation args)
    {
        if (_disposed) return;

        var isConnected = TryReadConnectedFlag(args.Properties);
        var name = NormalizeDeviceName(args.Name);
        _devices[args.Id] = (name, isConnected);

        if (_enumerationCompleted && isConnected && !string.IsNullOrEmpty(name))
        {
            PublishDebounced(args.Id, name, connected: true);
        }
    }

    private void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        if (_disposed || !args.Properties.ContainsKey(IsConnectedProperty))
        {
            return;
        }

        var isConnected = TryReadConnectedFlag(args.Properties);
        var hadPrevious = _devices.TryGetValue(args.Id, out var prev);
        var name = !string.IsNullOrEmpty(prev.Name) ? prev.Name : "Bluetooth Cihazı";
        _devices[args.Id] = (name, isConnected);

        if (_enumerationCompleted && hadPrevious && prev.Connected != isConnected && !string.IsNullOrEmpty(prev.Name))
        {
            PublishDebounced(args.Id, prev.Name, isConnected);
        }
    }

    private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        if (_devices.TryRemove(args.Id, out var prev) && _enumerationCompleted && prev.Connected && !string.IsNullOrEmpty(prev.Name))
        {
            PublishDebounced(args.Id, prev.Name, connected: false);
        }
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        _enumerationCompleted = true;
    }

    private void PublishDebounced(string id, string name, bool connected)
    {
        if (_disposed || !_settings.Current.BluetoothNotifications)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var key = $"{name}|{(connected ? '1' : '0')}";
        if (_lastEventTimes.TryGetValue(key, out var prevTime) && (now - prevTime) < TimeSpan.FromSeconds(1.8))
        {
            return;
        }

        _lastEventTimes[key] = now;
        _systemEvents.ReportBluetoothStatus(name, connected);
    }

    private static bool TryReadConnectedFlag(IReadOnlyDictionary<string, object> properties)
    {
        if (properties.TryGetValue(IsConnectedProperty, out var value) && value is bool connected)
        {
            return connected;
        }

        return false;
    }

    private static string NormalizeDeviceName(string? rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return string.Empty;
        }

        var trimmed = rawName.Trim();
        // Skip generic protocol noise names
        if (trimmed.StartsWith("Bluetooth ", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("Device", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return trimmed;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_watcher is not null)
        {
            try
            {
                if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                {
                    _watcher.Stop();
                }

                _watcher.Added -= OnDeviceAdded;
                _watcher.Updated -= OnDeviceUpdated;
                _watcher.Removed -= OnDeviceRemoved;
                _watcher.EnumerationCompleted -= OnEnumerationCompleted;
            }
            catch
            {
            }

            _watcher = null;
        }

        _devices.Clear();
        _lastEventTimes.Clear();
    }
}

