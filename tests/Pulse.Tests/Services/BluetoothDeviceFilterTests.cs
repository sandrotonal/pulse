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
}
