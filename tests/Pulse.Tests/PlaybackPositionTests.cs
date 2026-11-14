using Pulse.Core.Services;

namespace Pulse.Tests;

public sealed class PlaybackPositionTests
{
    [Theory]
    [InlineData(-10, 0)]
    [InlineData(25, 25)]
    [InlineData(150, 100)]
    public void ClampKeepsPositionWithinTrack(double seconds, double expectedSeconds)
    {
        var result = PlaybackPosition.Clamp(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(100));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), result);
    }

    [Fact]
    public void OffsetClampsAtBothEndsAndHandlesOverflow()
    {
        Assert.Equal(TimeSpan.Zero, PlaybackPosition.Offset(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(-15), TimeSpan.FromSeconds(90)));
        Assert.Equal(TimeSpan.FromSeconds(90), PlaybackPosition.Offset(TimeSpan.FromSeconds(80), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(90)));
        Assert.Equal(TimeSpan.FromSeconds(90), PlaybackPosition.Offset(TimeSpan.MaxValue, TimeSpan.MaxValue, TimeSpan.FromSeconds(90)));
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(50, 100, 50)]
    [InlineData(100, 100, 100)]
    public void BoundaryAndMidpointSeekingClampAccurately(double currentSec, double totalSec, double expectedSec)
    {
        var result = PlaybackPosition.Clamp(TimeSpan.FromSeconds(currentSec), TimeSpan.FromSeconds(totalSec));
        Assert.Equal(TimeSpan.FromSeconds(expectedSec), result);
    }

    [Fact]
    public void OffsetWithZeroOrNullDurationHandlesSafely()
    {
        Assert.Equal(TimeSpan.Zero, PlaybackPosition.Offset(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(-15), TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromSeconds(25), PlaybackPosition.Offset(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15), null));
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(50, "0:50")]
    [InlineData(169, "2:49")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3665, "1:01:05")]
    [InlineData(-10, "0:00")]
    public void FormatTime_FormatsCorrectly(double seconds, string expected)
    {
        var result = PlaybackPosition.FormatTime(TimeSpan.FromSeconds(seconds));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ComputePreviewText_WithValidDuration_CalculatesAccurateTimestamp()
    {
        var duration = TimeSpan.FromSeconds(200); // 3:20
        Assert.Equal("0:00", PlaybackPosition.ComputePreviewText(duration, 0));
        Assert.Equal("1:40", PlaybackPosition.ComputePreviewText(duration, 50));
        Assert.Equal("3:20", PlaybackPosition.ComputePreviewText(duration, 100));
    }

    [Fact]
    public void ComputePreviewText_WithNullOrZeroDuration_FallsBackToPercentage()
    {
        Assert.Equal("45%", PlaybackPosition.ComputePreviewText(null, 45.2));
        Assert.Equal("0%", PlaybackPosition.ComputePreviewText(TimeSpan.Zero, 0));
        Assert.Equal("80%", PlaybackPosition.ComputePreviewText(null, 80));
    }

    [Fact]
    public void ComputePreviewText_ClampsPercentOutOfBounds()
    {
        var duration = TimeSpan.FromSeconds(100);
        Assert.Equal("0:00", PlaybackPosition.ComputePreviewText(duration, -20));
        Assert.Equal("1:40", PlaybackPosition.ComputePreviewText(duration, 120));
        Assert.Equal("0%", PlaybackPosition.ComputePreviewText(null, -5));
        Assert.Equal("100%", PlaybackPosition.ComputePreviewText(null, 105));
    }
}
