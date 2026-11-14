namespace Pulse.Core.Services;

public sealed record BluetoothConnectionChange(string DeviceName, bool IsConnected);

/// <summary>
/// Coalesces multiple Windows Bluetooth signals (PnP AEP, WinRT BluetoothDevice/BLE ConnectionStatus,
/// and paired AudioRender endpoints) into single per-device connect/disconnect transitions.
/// </summary>
public sealed class BluetoothConnectionCoordinator
{
    private sealed record EndpointEntry(string DeviceName, bool IsConnected, bool IsPrimary);
    private sealed record PendingAudioEntry(string CandidateName, bool IsConnected, bool NotifyTransition);

    private readonly object _syncRoot = new();
    private readonly HashSet<string> _knownPrimaryNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EndpointEntry> _endpoints = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingAudioEntry> _unmatchedAudioEndpoints = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _emittedStates = new(StringComparer.OrdinalIgnoreCase);

    public BluetoothConnectionChange? RegisterOrUpdatePrimaryEndpoint(
        string sourceId,
        string? rawName,
        bool isConnected,
        bool notifyTransition)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return null;
        }

        lock (_syncRoot)
        {
            var normalized = BluetoothDeviceFilter.NormalizeName(rawName);
            _endpoints.TryGetValue(sourceId, out var existing);

            var resolvedName = !string.IsNullOrEmpty(normalized)
                ? normalized
                : existing?.DeviceName ?? string.Empty;

            var promotedShouldNotify = false;
            if (!string.IsNullOrEmpty(resolvedName))
            {
                _knownPrimaryNames.Add(resolvedName);
                promotedShouldNotify = PromoteMatchingUnmatchedAudioEndpoints_NoLock(resolvedName);
            }

            _endpoints[sourceId] = new EndpointEntry(resolvedName, isConnected, IsPrimary: true);

            if (!string.IsNullOrEmpty(existing?.DeviceName) &&
                !string.Equals(existing.DeviceName, resolvedName, StringComparison.OrdinalIgnoreCase))
            {
                _ = ReconcileDeviceState_NoLock(existing.DeviceName, notifyTransition: false);
            }

            var effectiveNotify = notifyTransition || promotedShouldNotify;
            return ReconcileDeviceState_NoLock(resolvedName, effectiveNotify);
        }
    }

    public BluetoothConnectionChange? UpdatePrimaryName(
        string sourceId,
        string? rawName,
        bool notifyTransition)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return null;
        }

        var normalized = BluetoothDeviceFilter.NormalizeName(rawName);
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        lock (_syncRoot)
        {
            _endpoints.TryGetValue(sourceId, out var existing);
            var isConnected = existing?.IsConnected ?? false;
            var oldName = existing?.DeviceName;

            _knownPrimaryNames.Add(normalized);
            var promotedShouldNotify = PromoteMatchingUnmatchedAudioEndpoints_NoLock(normalized);

            _endpoints[sourceId] = new EndpointEntry(normalized, isConnected, IsPrimary: true);

            if (!string.IsNullOrEmpty(oldName) &&
                !string.Equals(oldName, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _ = ReconcileDeviceState_NoLock(oldName, notifyTransition: false);
            }

            var effectiveNotify = (notifyTransition && isConnected) || promotedShouldNotify;
            return ReconcileDeviceState_NoLock(normalized, effectiveNotify);
        }
    }

    public BluetoothConnectionChange? RegisterOrUpdateAudioEndpoint(
        string sourceId,
        string? rawEndpointName,
        bool isConnected,
        bool notifyTransition)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return null;
        }

        lock (_syncRoot)
        {
            var candidate = BluetoothDeviceFilter.TryExtractAudioEndpointDeviceName(rawEndpointName);
            if (string.IsNullOrEmpty(candidate))
            {
                if (_endpoints.TryGetValue(sourceId, out var existing) &&
                    !existing.IsPrimary &&
                    !string.IsNullOrEmpty(existing.DeviceName))
                {
                    _endpoints[sourceId] = existing with { IsConnected = isConnected };
                    return ReconcileDeviceState_NoLock(existing.DeviceName, notifyTransition);
                }

                if (_unmatchedAudioEndpoints.TryGetValue(sourceId, out var pending))
                {
                    var matchedPending = FindKnownPrimaryName_NoLock(pending.CandidateName);
                    if (matchedPending is null)
                    {
                        _unmatchedAudioEndpoints[sourceId] = pending with
                        {
                            IsConnected = isConnected,
                            NotifyTransition = pending.NotifyTransition || notifyTransition
                        };
                        return null;
                    }

                    _unmatchedAudioEndpoints.Remove(sourceId);
                    _endpoints[sourceId] = new EndpointEntry(matchedPending, isConnected, IsPrimary: false);
                    return ReconcileDeviceState_NoLock(matchedPending, pending.NotifyTransition || notifyTransition);
                }

                return null;
            }

            var matchedName = FindKnownPrimaryName_NoLock(candidate);
            if (matchedName is null)
            {
                _unmatchedAudioEndpoints[sourceId] = new PendingAudioEntry(candidate, isConnected, notifyTransition);
                return null;
            }

            _unmatchedAudioEndpoints.Remove(sourceId);
            _endpoints[sourceId] = new EndpointEntry(matchedName, isConnected, IsPrimary: false);

            return ReconcileDeviceState_NoLock(matchedName, notifyTransition);
        }
    }

    public BluetoothConnectionChange? RemoveEndpoint(string sourceId, bool notifyTransition)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return null;
        }

        lock (_syncRoot)
        {
            _unmatchedAudioEndpoints.Remove(sourceId);

            if (!_endpoints.Remove(sourceId, out var removed) || string.IsNullOrEmpty(removed.DeviceName))
            {
                return null;
            }

            return ReconcileDeviceState_NoLock(removed.DeviceName, notifyTransition);
        }
    }

    public string? TryGetPrimaryName(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return null;
        }

        lock (_syncRoot)
        {
            return _endpoints.TryGetValue(sourceId, out var entry) && !string.IsNullOrEmpty(entry.DeviceName)
                ? entry.DeviceName
                : null;
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _knownPrimaryNames.Clear();
            _endpoints.Clear();
            _unmatchedAudioEndpoints.Clear();
            _emittedStates.Clear();
        }
    }

    private string? FindKnownPrimaryName_NoLock(string candidate)
    {
        if (_knownPrimaryNames.TryGetValue(candidate, out var exact))
        {
            return exact;
        }

        foreach (var known in _knownPrimaryNames)
        {
            if (BluetoothDeviceFilter.MatchesKnownDevice(candidate, known))
            {
                return known;
            }
        }

        return null;
    }

    private bool PromoteMatchingUnmatchedAudioEndpoints_NoLock(string primaryName)
    {
        if (_unmatchedAudioEndpoints.Count == 0)
        {
            return false;
        }

        List<string>? matchedIds = null;
        foreach (var (id, pending) in _unmatchedAudioEndpoints)
        {
            if (BluetoothDeviceFilter.MatchesKnownDevice(pending.CandidateName, primaryName))
            {
                matchedIds ??= [];
                matchedIds.Add(id);
            }
        }

        if (matchedIds is null)
        {
            return false;
        }

        var shouldNotify = false;
        foreach (var id in matchedIds)
        {
            if (_unmatchedAudioEndpoints.Remove(id, out var pending))
            {
                shouldNotify |= pending.NotifyTransition && pending.IsConnected;
                _endpoints[id] = new EndpointEntry(primaryName, pending.IsConnected, IsPrimary: false);
            }
        }

        return shouldNotify;
    }

    private BluetoothConnectionChange? ReconcileDeviceState_NoLock(string deviceName, bool notifyTransition)
    {
        if (string.IsNullOrEmpty(deviceName))
        {
            return null;
        }

        var anyConnected = false;
        foreach (var entry in _endpoints.Values)
        {
            if (entry.IsConnected && string.Equals(entry.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                anyConnected = true;
                break;
            }
        }

        if (!_emittedStates.TryGetValue(deviceName, out var prevConnected))
        {
            _emittedStates[deviceName] = anyConnected;
            return notifyTransition && anyConnected
                ? new BluetoothConnectionChange(deviceName, true)
                : null;
        }

        if (prevConnected == anyConnected)
        {
            return null;
        }

        _emittedStates[deviceName] = anyConnected;
        return notifyTransition
            ? new BluetoothConnectionChange(deviceName, anyConnected)
            : null;
    }
}
