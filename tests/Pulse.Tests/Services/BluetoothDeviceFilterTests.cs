using Pulse.Core.Services;

namespace Pulse.Tests.Services;

public sealed class BluetoothDeviceFilterTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("Bluetooth HID Device", "")]
    [InlineData("Bluetooth LE Generic Attribute Service", "")]
    [InlineData("  AirPods Pro  ", "AirPods Pro")]
    [InlineData("WH-1000XM5", "WH-1000XM5")]
    public void NormalizeNameFiltersProtocolNoiseAndTrimsValidNames(string? input, string expected)
    {
        Assert.Equal(expected, BluetoothDeviceFilter.NormalizeName(input));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("NoParentheses", "")]
    [InlineData("Kulaklıklar (AirPods)", "AirPods")]
    [InlineData("Headphones (WH-1000XM5 Stereo)", "WH-1000XM5")]
    [InlineData("Headset (JBL TUNE500BT Hands-Free AG Audio)", "JBL TUNE500BT")]
    [InlineData("Headset (2- AirPods Hands-Free)", "AirPods")]
    [InlineData("Hoparlör (Realtek(R) Audio)", "Realtek(R) Audio")]
    public void TryExtractAudioEndpointDeviceNameStripsProfilesAndPrefixes(string? input, string expected)
    {
        Assert.Equal(expected, BluetoothDeviceFilter.TryExtractAudioEndpointDeviceName(input));
    }

    [Fact]
    public void CoordinatorSilentlySeedsConnectedDevicesAndEmitsOnceOnRealStateTransitions()
    {
        var coordinator = new BluetoothConnectionCoordinator();

        // Already-connected headset seeded during startup must not fire startup toast
        Assert.Null(coordinator.RegisterOrUpdatePrimaryEndpoint("bt_1", "AirPods", isConnected: true, notifyTransition: false));
        Assert.Null(coordinator.RegisterOrUpdateAudioEndpoint("aud_1", "Kulaklıklar (AirPods)", isConnected: true, notifyTransition: true));

        // Disconnect removes/disables audio and marks primary disconnected -> emits disconnect only once all sources drop
        Assert.Null(coordinator.RemoveEndpoint("aud_1", notifyTransition: true));
        var disconnected = coordinator.RegisterOrUpdatePrimaryEndpoint("bt_1", "AirPods", isConnected: false, notifyTransition: true);
        Assert.NotNull(disconnected);
        Assert.Equal("AirPods", disconnected.DeviceName);
        Assert.False(disconnected.IsConnected);

        // Reconnect emits connect once even if both WinRT and AudioRender report it
        var connected = coordinator.RegisterOrUpdatePrimaryEndpoint("bt_1", "AirPods", isConnected: true, notifyTransition: true);
        Assert.NotNull(connected);
        Assert.Equal("AirPods", connected.DeviceName);
        Assert.True(connected.IsConnected);
        Assert.Null(coordinator.RegisterOrUpdateAudioEndpoint("aud_1", "Kulaklıklar (AirPods)", isConnected: true, notifyTransition: true));

        // Unknown non-Bluetooth audio endpoints (Realtek) must always be ignored
        Assert.Null(coordinator.RegisterOrUpdateAudioEndpoint("aud_realtek", "Hoparlör (Realtek(R) Audio)", isConnected: true, notifyTransition: true));
    }

    [Fact]
    public void CoordinatorBackfillsEmptyPrimaryNameAndConnectsViaAudioFallbackWhenWinRtStateMissing()
    {
        var coordinator = new BluetoothConnectionCoordinator();

        Assert.Null(coordinator.RegisterOrUpdatePrimaryEndpoint("bt_philips", string.Empty, isConnected: false, notifyTransition: false));
        Assert.Null(coordinator.RegisterOrUpdatePrimaryEndpoint("bt_philips", "Philips TAH4205", isConnected: false, notifyTransition: true));

        // If AEP status didn't flip yet, enabled audio endpoint for known paired BT device triggers connect
        var evt = coordinator.RegisterOrUpdateAudioEndpoint("aud_philips", "Kulaklıklar (Philips TAH4205 Stereo)", isConnected: true, notifyTransition: true);
        Assert.NotNull(evt);
        Assert.Equal("Philips TAH4205", evt.DeviceName);
        Assert.True(evt.IsConnected);
    }

    [Fact]
    public void CoordinatorUpdatePrimaryNamePreservesConnectedStateAndAudioUpdatesWorkByIdOnly()
    {
        var coordinator = new BluetoothConnectionCoordinator();

        // Connected WinRT signal arrives before AEP display name resolves
        Assert.Null(coordinator.RegisterOrUpdatePrimaryEndpoint("bt_jbl", string.Empty, isConnected: true, notifyTransition: true));

        // Name-only backfill preserves Connected=true and announces connection
        var evt = coordinator.UpdatePrimaryName("bt_jbl", "JBL Tune 510BT", notifyTransition: true);
        Assert.NotNull(evt);
        Assert.Equal("JBL Tune 510BT", evt.DeviceName);
        Assert.True(evt.IsConnected);

        // Audio endpoint added with friendly name then toggled via DeviceInformationUpdate (Name == null)
        Assert.Null(coordinator.RegisterOrUpdateAudioEndpoint("aud_jbl", "Headphones (JBL Tune 510BT Stereo)", isConnected: true, notifyTransition: true));
        Assert.Null(coordinator.RegisterOrUpdateAudioEndpoint("aud_jbl", null, isConnected: false, notifyTransition: true));

        // Primary still connected -> still connected overall until primary drops too
        var disconnect = coordinator.RegisterOrUpdatePrimaryEndpoint("bt_jbl", null, isConnected: false, notifyTransition: true);
        Assert.NotNull(disconnect);
        Assert.Equal("JBL Tune 510BT", disconnect.DeviceName);
        Assert.False(disconnect.IsConnected);

        // Audio-only reconnect by sourceId (DeviceInformationUpdate with InterfaceEnabled=true and Name=null) announces connect!
        var audioReconnect = coordinator.RegisterOrUpdateAudioEndpoint("aud_jbl", null, isConnected: true, notifyTransition: true);
        Assert.NotNull(audioReconnect);
        Assert.Equal("JBL Tune 510BT", audioReconnect.DeviceName);
        Assert.True(audioReconnect.IsConnected);

        // Non-Bluetooth audio endpoint update by id only stays ignored
        Assert.Null(coordinator.RegisterOrUpdateAudioEndpoint("aud_realtek", "Hoparlör (Realtek(R) Audio)", isConnected: true, notifyTransition: true));
        Assert.Null(coordinator.RegisterOrUpdateAudioEndpoint("aud_realtek", null, isConnected: false, notifyTransition: true));
    }
}
