namespace Pulse.Core.Events;

public enum FocusTimerState
{
    Running,
    Paused,
    Completed
}

public sealed record FocusTimerEvent(
    string Id,
    TimeSpan RemainingTime,
    TimeSpan TotalDuration,
    bool IsBreak,
    FocusTimerState State,
    TimeSpan? DisplayDuration = null
) : IslandEvent(
    Id,
    State == FocusTimerState.Completed ? EventPriority.Warning : EventPriority.Progress,
    EventCategory.FocusTimer,
    DateTimeOffset.UtcNow,
    DisplayDuration ?? (State == FocusTimerState.Completed ? TimeSpan.FromSeconds(5.0) : null),
    CanDismiss: State == FocusTimerState.Completed
)
{
    public double ProgressPercentage => TotalDuration.TotalSeconds > 0
        ? Math.Clamp((1.0 - (RemainingTime.TotalSeconds / TotalDuration.TotalSeconds)) * 100.0, 0.0, 100.0)
        : 0.0;
}
