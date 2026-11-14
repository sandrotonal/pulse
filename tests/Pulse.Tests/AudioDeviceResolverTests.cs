using Pulse.Core.Services;

namespace Pulse.Tests;

public sealed class AudioDeviceResolverTests
{
    [Theory]
    [InlineData("AirPods Pro", "\uE7F6")]
    [InlineData("Sony WH-1000XM4 Headset", "\uE7F6")]
    [InlineData("Kulaklık (Realtek Audio)", "\uE7F6")]
    [InlineData("Galaxy Buds2 Pro", "\uE7F6")]
    [InlineData("Earphones", "\uE7F6")]
    [InlineData("Bluetooth Audio Device", "\uE702")]
    [InlineData("BT Stereo", "\uE702")]
    [InlineData("Hoparlör (Realtek Audio)", "\uE7F5")]
    [InlineData("Dell Soundbar", "\uE7F5")]
    [InlineData("", "\uE7F5")]
    [InlineData(null, "\uE7F5")]
    public void ResolveGlyph_ByName_ReturnsExpectedGlyph(string? deviceName, string expectedGlyph)
    {
        var result = AudioDeviceResolver.ResolveGlyph(deviceName);
        Assert.Equal(expectedGlyph, result);
    }

    [Theory]
    [InlineData(3, "\uE7F6")] // Headphones
    [InlineData(5, "\uE7F6")] // Headset
    [InlineData(6, "\uE7F6")] // Handset
    [InlineData(1, "\uE7F5")] // Speakers
    public void ResolveGlyph_ByFormFactor_ReturnsExpectedGlyph(uint formFactor, string expectedGlyph)
    {
        var result = AudioDeviceResolver.ResolveGlyph("Generic Audio", formFactor);
        Assert.Equal(expectedGlyph, result);
    }

    [Theory]
    [InlineData(null, "Hoparlör")]
    [InlineData("", "Hoparlör")]
    [InlineData("   ", "Hoparlör")]
    [InlineData("Hoparlör (Realtek(R) Audio)", "Hoparlör")]
    [InlineData("Kulaklık (High Definition Audio)", "Kulaklık")]
    [InlineData("Sony WH-1000XM4", "Sony WH-1000XM4")]
    [InlineData("A Very Extremely Long Device Audio Name Exceeding Limits", "A Very Extremely…")]
    public void FormatShortName_FormatsCleanly(string? deviceName, string expectedShortName)
    {
        var result = AudioDeviceResolver.FormatShortName(deviceName);
        Assert.Equal(expectedShortName, result);
    }
}
