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
    [InlineData("AirPods Pro", true)]
    [InlineData("Sony WH-1000XM4 Headset", true)]
    [InlineData("Kulaklık (Realtek Audio)", true)]
    [InlineData("Galaxy Buds2 Pro", true)]
    [InlineData("Earphones", true)]
    [InlineData("Pixel Buds Pro", true)]
    [InlineData("FreeBuds 5i", true)]
    [InlineData("Sony WF-1000XM5", true)]
    [InlineData("Hoparlör (Realtek Audio)", false)]
    [InlineData("Dell Soundbar", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsHeadphones_DetectsCorrectlyByName(string? deviceName, bool expectedIsHeadphones)
    {
        var result = AudioDeviceResolver.IsHeadphones(deviceName);
        Assert.Equal(expectedIsHeadphones, result);
    }

    [Theory]
    [InlineData(3, true)] // Headphones
    [InlineData(5, true)] // Headset
    [InlineData(6, true)] // Handset
    [InlineData(1, false)] // Speakers
    public void IsHeadphones_DetectsCorrectlyByFormFactor(uint formFactor, bool expectedIsHeadphones)
    {
        var result = AudioDeviceResolver.IsHeadphones("Generic Audio", formFactor);
        Assert.Equal(expectedIsHeadphones, result);
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
