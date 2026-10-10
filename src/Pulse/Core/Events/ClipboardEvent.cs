namespace Pulse.Core.Events;

public sealed record ClipboardEvent(
    string Id,
    string TextPreview,
    string Format,
    TimeSpan? DisplayDuration = null
) : IslandEvent(
    Id,
    EventPriority.Routine,
    EventCategory.Clipboard,
    DateTimeOffset.UtcNow,
    DisplayDuration ?? TimeSpan.FromSeconds(2.0),
    CanDismiss: true
);
