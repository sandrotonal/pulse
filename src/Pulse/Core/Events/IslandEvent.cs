namespace Pulse.Core.Events;

public abstract record IslandEvent(
    string Id,
    EventPriority Priority,
    EventCategory Category,
    DateTimeOffset Timestamp,
    TimeSpan? DisplayDuration = null,
    bool CanDismiss = true
)
{
    public bool IsExpired(DateTimeOffset now) =>
        DisplayDuration.HasValue && (now - Timestamp) >= DisplayDuration.Value;
}
