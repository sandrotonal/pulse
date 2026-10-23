using Pulse.Core.Services;

namespace Pulse.Tests;

public sealed class FullscreenNotificationStateTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void BusyD3DAndPresentationStatesAreFullscreen(int state)
    {
        Assert.True(FullscreenNotificationState.IsFullscreen(state));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void DesktopAndNotificationStatesAreNotFullscreen(int state)
    {
        Assert.False(FullscreenNotificationState.IsFullscreen(state));
    }
}
