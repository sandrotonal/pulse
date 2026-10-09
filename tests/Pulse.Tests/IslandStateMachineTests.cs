using Pulse.Core.Models;
using Pulse.Core.Services;

namespace Pulse.Tests;

public sealed class IslandStateMachineTests
{
    [Fact]
    public void StartsIdleAndMovesToCompactWhenMediaAppears()
    {
        var machine = new IslandStateMachine();

        Assert.Equal(IslandState.Idle, machine.CurrentState);
        machine.SetHasMedia(true);
        Assert.Equal(IslandState.Compact, machine.CurrentState);
    }

    [Fact]
    public void HoverAndExpandedStatesFollowPointerAndClick()
    {
        var machine = new IslandStateMachine();
        machine.SetHasMedia(true);

        machine.SetHovered(true);
        Assert.Equal(IslandState.Hover, machine.CurrentState);
        machine.ToggleExpanded();
        Assert.Equal(IslandState.Expanded, machine.CurrentState);
        machine.SetHovered(false);
        Assert.Equal(IslandState.Expanded, machine.CurrentState);
        machine.ToggleExpanded();
        Assert.Equal(IslandState.Collapsing, machine.CurrentState);
        machine.CompleteCollapse();
        Assert.Equal(IslandState.Compact, machine.CurrentState);
    }

    [Fact]
    public void LosingMediaReturnsIslandToIdle()
    {
        var machine = new IslandStateMachine();
        machine.SetHasMedia(true);
        machine.ToggleExpanded();

        machine.SetHasMedia(false);

        Assert.Equal(IslandState.Idle, machine.CurrentState);
    }

    [Fact]
    public void EmptyIslandCanBeHoveredAndOpened()
    {
        var machine = new IslandStateMachine();

        machine.SetHovered(true);
        Assert.Equal(IslandState.Hover, machine.CurrentState);
        machine.SetHovered(false);
        Assert.Equal(IslandState.Idle, machine.CurrentState);
        machine.ToggleExpanded();
        Assert.Equal(IslandState.Expanded, machine.CurrentState);
    }

    [Fact]
    public void TemporaryStateReturnsToCurrentMediaState()
    {
        var machine = new IslandStateMachine();
        machine.SetHasMedia(true);
        machine.ShowTrackChanged();

        machine.ReturnToCompact(hasMedia: true);

        Assert.Equal(IslandState.Compact, machine.CurrentState);
    }
}
