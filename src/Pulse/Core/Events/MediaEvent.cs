using Pulse.Core.Models;

namespace Pulse.Core.Events;

public sealed record MediaEvent(
    string Id,
    MediaTrack? Track,
    bool IsPlaying,
    DateTimeOffset Timestamp
) : IslandEvent(
    Id,
    EventPriority.ActiveMedia,
    EventCategory.Media,
    Timestamp,
    DisplayDuration: null,
    CanDismiss: false
);
