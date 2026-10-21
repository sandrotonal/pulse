using Pulse.Core.Models;

namespace Pulse.Core.Services;

public sealed class IslandStateMachine
{
    private bool _hasMedia;

    public IslandState CurrentState { get; private set; } = IslandState.Idle;
    public event EventHandler<IslandState>? StateChanged;

    public void SetHasMedia(bool hasMedia)
    {
        _hasMedia = hasMedia;
        if (!hasMedia)
        {
            SetState(IslandState.Idle);
        }
        else if (CurrentState == IslandState.Idle)
        {
            SetState(IslandState.Compact);
        }
    }

    public void SetHovered(bool isHovered)
    {
        if (isHovered && CurrentState == IslandState.Collapsing)
        {
            SetState(IslandState.Expanded);
            return;
        }

        if (isHovered && CurrentState is IslandState.Compact or IslandState.Idle)
        {
            SetState(IslandState.Hover);
            return;
        }

        if (!isHovered && CurrentState == IslandState.Hover)
        {
            SetState(_hasMedia ? IslandState.Compact : IslandState.Idle);
        }
    }

    public void ToggleExpanded()
    {
        if (CurrentState == IslandState.Expanded)
        {
            SetState(IslandState.Collapsing);
        }
        else
        {
            SetState(IslandState.Expanded);
        }
    }

    public void CompleteCollapse()
    {
        if (CurrentState == IslandState.Collapsing)
        {
            SetState(_hasMedia ? IslandState.Compact : IslandState.Idle);
        }
    }

    /// <summary>
    /// Announces a track change in the compact presentation. An expanded island keeps
    /// its layout and updates its content in place, matching Apple's guidance for
    /// Live Activities; only compact presentations briefly morph to announce the change.
    /// Returns true when the announcement actually became the current state.
    /// </summary>
    public bool ShowTrackChanged()
    {
        if (CurrentState is IslandState.Expanded or IslandState.Collapsing)
        {
            return false;
        }

        SetState(IslandState.TrackChanged);
        return true;
    }

    public void ShowNotification() => SetState(IslandState.Notification);

    public void ReturnToCompact(bool hasMedia) => SetState(hasMedia ? IslandState.Compact : IslandState.Idle);

    private void SetState(IslandState state)
    {
        if (CurrentState == state)
        {
            return;
        }

        CurrentState = state;
        StateChanged?.Invoke(this, state);
    }
}
