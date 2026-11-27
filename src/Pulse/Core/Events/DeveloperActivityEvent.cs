namespace Pulse.Core.Events;

public enum DeveloperActivityKind
{
    BuildInProgress,
    BuildSucceeded,
    BuildFailed,
    PullRequest,
    LocalCommit
}

public sealed record DeveloperActivityEvent(
    string Id,
    DeveloperActivityKind ActivityKind,
    string Title,
    string Subtitle,
    string IconGlyph,
    string? BadgeText = null,
    string? LaunchTarget = null,
    EventPriority Priority = EventPriority.Progress,
    TimeSpan? DisplayDuration = null
) : IslandEvent(
    Id,
    Priority,
    EventCategory.Developer,
    DateTimeOffset.UtcNow,
    DisplayDuration,
    CanDismiss: true
);
