using Pulse.Core.Models;

namespace Pulse.Tests;

public sealed class IslandStatePresentationTests
{
    [Theory]
    [InlineData(IslandState.Idle, false)]
    [InlineData(IslandState.Compact, true)]
    [InlineData(IslandState.Hover, true)]
    [InlineData(IslandState.TrackChanged, true)]
    [InlineData(IslandState.Notification, true)]
    [InlineData(IslandState.Expanded, false)]
    public void CompactPillCoversOnlyRestingCapsuleStates(IslandState state, bool expected)
    {
        Assert.Equal(expected, state.IsCompactPill());
    }

    [Theory]
    [InlineData(IslandState.Idle, false)]
    [InlineData(IslandState.Compact, true)]
    [InlineData(IslandState.Hover, true)]
    [InlineData(IslandState.TrackChanged, true)]
    [InlineData(IslandState.Notification, true)]
    [InlineData(IslandState.Collapsing, true)]
    [InlineData(IslandState.Expanded, false)]
    public void ShowsCompactContentKeepsTheCapsuleAliveWhileCollapsing(IslandState state, bool expected)
    {
        Assert.Equal(expected, state.ShowsCompactContent());
    }
}
