namespace Pulse.Core.Models;

/// <summary>
/// Maps a state to the layout it renders. A collapsing frame is a compact capsule in
/// motion, so it must keep the compact content laid out while the card shrinks —
/// otherwise the pill is blank for the whole collapse animation.
/// </summary>
public static class IslandStatePresentation
{
    /// <summary>Resting capsule states (drives split-mode layout decisions).</summary>
    public static bool IsCompactPill(this IslandState state) =>
        state is IslandState.Compact or IslandState.Hover or IslandState.TrackChanged or IslandState.Notification;

    /// <summary>States that present the compact content, including the collapse morph.</summary>
    public static bool ShowsCompactContent(this IslandState state) =>
        state.IsCompactPill() || state == IslandState.Collapsing;
}
