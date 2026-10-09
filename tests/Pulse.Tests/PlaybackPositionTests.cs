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
}
