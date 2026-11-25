using Pulse.Core.Services;

namespace Pulse.Tests.Services;

public sealed class BluetoothConnectionCoordinatorTests
{
    [Fact]
    public void PrimaryEndpointCoalescesDuplicateConnectsAndDisconnects()
    {
        var coordinator = new BluetoothConnectionCoordinator();

        var first = coordinator.RegisterOrUpdatePrimaryEndpoint("pnp-1", "AirPods Pro", isConnected: true, notifyTransition: true);
        Assert.Equal(new BluetoothConnectionChange("AirPods Pro", true), first);

        // Second endpoint for the same device must not emit a duplicate connect event.
        var second = coordinator.RegisterOrUpdatePrimaryEndpoint("ble-1", "AirPods Pro", isConnected: true, notifyTransition: true);
        Assert.Null(second);

        // Disconnecting one endpoint while the other remains connected must not disconnect the device.
        var partialDisconnect = coordinator.RegisterOrUpdatePrimaryEndpoint("ble-1", null, isConnected: false, notifyTransition: true);
        Assert.Null(partialDisconnect);

        // Disconnecting the last connected endpoint emits a single disconnect.
        var finalDisconnect = coordinator.RegisterOrUpdatePrimaryEndpoint("pnp-1", null, isConnected: false, notifyTransition: true);
        Assert.Equal(new BluetoothConnectionChange("AirPods Pro", false), finalDisconnect);
    }

    [Fact]
    public void UnmatchedAudioEndpointPromotesWhenPrimaryRegisters()
    {
        var coordinator = new BluetoothConnectionCoordinator();

        // Audio endpoint arrives before primary device name is known -> queued as unmatched.
        var beforePrimary = coordinator.RegisterOrUpdateAudioEndpoint(
            "audio-1",
            "Headphones (WH-1000XM5 Stereo)",
            isConnected: true,
            notifyTransition: true);
        Assert.Null(beforePrimary);

        // Primary registers as disconnected, but the promoted audio endpoint is connected -> emits connect.
        var onPrimary = coordinator.RegisterOrUpdatePrimaryEndpoint(
            "pnp-1",
            "WH-1000XM5",
            isConnected: false,
            notifyTransition: false);
        Assert.Equal(new BluetoothConnectionChange("WH-1000XM5", true), onPrimary);
    }

    [Fact]
    public void AudioEndpointMatchesKnownPrimaryAndTracksDisconnect()
    {
        var coordinator = new BluetoothConnectionCoordinator();

        Assert.Null(coordinator.RegisterOrUpdatePrimaryEndpoint("pnp-1", "Sony Buds", isConnected: false, notifyTransition: false));

        var connected = coordinator.RegisterOrUpdateAudioEndpoint(
            "audio-1",
            "Headphones (Sony Buds Stereo)",
            isConnected: true,
            notifyTransition: true);
        Assert.Equal(new BluetoothConnectionChange("Sony Buds", true), connected);

        var disconnected = coordinator.RegisterOrUpdateAudioEndpoint(
            "audio-1",
            null,
            isConnected: false,
            notifyTransition: true);
        Assert.Equal(new BluetoothConnectionChange("Sony Buds", false), disconnected);
    }

    [Fact]
    public void UpdatePrimaryNameReconcilesAndRemoveEndpointEmitsDisconnectWhenLast()
    {
        var coordinator = new BluetoothConnectionCoordinator();

        coordinator.RegisterOrUpdatePrimaryEndpoint("pnp-1", "Device A", isConnected: true, notifyTransition: false);
        Assert.Equal("Device A", coordinator.TryGetPrimaryName("pnp-1"));

        var renamed = coordinator.UpdatePrimaryName("pnp-1", "Device B", notifyTransition: true);
        Assert.Equal(new BluetoothConnectionChange("Device B", true), renamed);
        Assert.Equal("Device B", coordinator.TryGetPrimaryName("pnp-1"));

        var removed = coordinator.RemoveEndpoint("pnp-1", notifyTransition: true);
        Assert.Equal(new BluetoothConnectionChange("Device B", false), removed);
        Assert.Null(coordinator.TryGetPrimaryName("pnp-1"));
    }

    [Fact]
    public void ClearResetsStateAndBlankSourceIdReturnsNull()
    {
        var coordinator = new BluetoothConnectionCoordinator();

        Assert.Null(coordinator.RegisterOrUpdatePrimaryEndpoint(" ", "Buds", true, true));
        Assert.Null(coordinator.UpdatePrimaryName("", "Buds", true));
        Assert.Null(coordinator.RegisterOrUpdateAudioEndpoint("", "Headphones (Buds)", true, true));
        Assert.Null(coordinator.RemoveEndpoint(" ", true));
        Assert.Null(coordinator.TryGetPrimaryName(" "));

        Assert.NotNull(coordinator.RegisterOrUpdatePrimaryEndpoint("pnp-1", "Buds", true, true));
        coordinator.Clear();

        var afterClear = coordinator.RegisterOrUpdatePrimaryEndpoint("pnp-1", "Buds", true, true);
        Assert.Equal(new BluetoothConnectionChange("Buds", true), afterClear);
    }
}
