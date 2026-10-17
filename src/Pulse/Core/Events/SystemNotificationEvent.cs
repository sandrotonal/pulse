namespace Pulse.Core.Events;

public sealed record SystemNotificationEvent(
    string Id,
    EventCategory Category,
    string Title,
    string Subtitle,
    string IconGlyph,
    EventPriority Priority = EventPriority.Informational,
    TimeSpan? DisplayDuration = null,
    string? LaunchTarget = null
) : IslandEvent(
    Id,
    Priority,
    Category,
    DateTimeOffset.UtcNow,
    DisplayDuration ?? TimeSpan.FromSeconds(2.8),
    CanDismiss: true
);
