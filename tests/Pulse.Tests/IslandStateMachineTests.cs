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

    [Fact]
    public void TrackChangeKeepsExpandedIslandOpen()
    {
        var machine = new IslandStateMachine();
        machine.SetHasMedia(true);
        machine.ToggleExpanded();

        var announced = machine.ShowTrackChanged();

        Assert.False(announced);
        Assert.Equal(IslandState.Expanded, machine.CurrentState);
    }

    [Fact]
    public void TrackChangeAnnouncesItselfWhileCompact()
    {
        var machine = new IslandStateMachine();
        machine.SetHasMedia(true);

        var announced = machine.ShowTrackChanged();

        Assert.True(announced);
        Assert.Equal(IslandState.TrackChanged, machine.CurrentState);
    }

    [Fact]
    public void TrackChangeDuringCollapseLetsTheCollapseFinish()
    {
        var machine = new IslandStateMachine();
        machine.SetHasMedia(true);
        machine.ToggleExpanded();
        machine.ToggleExpanded();

        var announced = machine.ShowTrackChanged();

        Assert.False(announced);
        Assert.Equal(IslandState.Collapsing, machine.CurrentState);
    }

    [Fact]
    public void HoverRescuesCollapsingToExpandedAndIgnoresCompleteCollapseOutsideCollapsing()
    {
        var machine = new IslandStateMachine();
        machine.SetHasMedia(true);
        machine.ToggleExpanded(); // Expanded
        machine.ToggleExpanded(); // Collapsing
        Assert.Equal(IslandState.Collapsing, machine.CurrentState);

        machine.SetHovered(true);
        Assert.Equal(IslandState.Expanded, machine.CurrentState);

        // CompleteCollapse outside Collapsing is a no-op
        machine.CompleteCollapse();
        Assert.Equal(IslandState.Expanded, machine.CurrentState);
    }

    [Fact]
    public void TransientTrackChangeRestoresHoverWhenPointerRemainsOverIsland()
    {
        var machine = new IslandStateMachine();
        machine.SetHasMedia(true);
        machine.SetHovered(true);
        Assert.Equal(IslandState.Hover, machine.CurrentState);

        Assert.True(machine.ShowTrackChanged());
        Assert.Equal(IslandState.TrackChanged, machine.CurrentState);

        // Pointer stays hovered when 2.4s announcement ends -> returns to Hover
        machine.ReturnToCompact(hasMedia: true);
        Assert.Equal(IslandState.Hover, machine.CurrentState);

        // Moving pointer out during transient announcement returns to Compact instead
        Assert.True(machine.ShowTrackChanged());
        machine.SetHovered(false);
        machine.ReturnToCompact(hasMedia: true);
        Assert.Equal(IslandState.Compact, machine.CurrentState);
    }

    [Fact]
    public void ShowNotificationAndNoOpTransitionsEmitCleanEvents()
    {
        var machine = new IslandStateMachine();
        int transitions = 0;
        machine.StateChanged += (_, _) => transitions++;

        machine.ShowNotification();
        Assert.Equal(IslandState.Notification, machine.CurrentState);
        machine.ShowNotification(); // duplicate -> no-op
        machine.ReturnToCompact(hasMedia: false);
        Assert.Equal(IslandState.Idle, machine.CurrentState);
        Assert.Equal(2, transitions);
    }
}
