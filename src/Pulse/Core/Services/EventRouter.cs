using Pulse.Core.Events;
using Pulse.Core.Interfaces;

namespace Pulse.Core.Services;

public sealed class EventRouter : IEventRouter
{
    private static readonly TimeSpan MaxDeferredPendingAge = TimeSpan.FromSeconds(45);

    private sealed class TrackedEntry
    {
        public TrackedEntry(IslandEvent @event, DateTimeOffset queuedAtUtc)
        {
            Event = @event;
            QueuedAtUtc = queuedAtUtc;
        }

        public IslandEvent Event { get; set; }
        public DateTimeOffset QueuedAtUtc { get; set; }
        public DateTimeOffset? PresentedAtUtc { get; set; }
        public Timer? ExpirationTimer { get; set; }
    }

    private readonly object _syncRoot = new();
    private readonly Dictionary<string, TrackedEntry> _events = new();
    private readonly Queue<IslandEvent?> _pendingNotifications = new();
    private bool _isDispatchingNotifications;
    private IslandEvent? _persistentEvent;
    private bool _isInteractionLocked;
    private bool _isDisposed;

    public IslandEvent? CurrentActiveEvent { get; private set; }

    public event EventHandler<IslandEvent?>? ActiveEventChanged;

    public void Publish(IslandEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var shouldDrain = false;

        lock (_syncRoot)
        {
            if (_isDisposed) return;

            var now = DateTimeOffset.UtcNow;

            if (@event.Priority == EventPriority.ActiveMedia && !@event.DisplayDuration.HasValue)
            {
                _persistentEvent = @event;
            }
            else if (_events.TryGetValue(@event.Id, out var existing))
            {
                existing.ExpirationTimer?.Dispose();
                existing.ExpirationTimer = null;
                existing.Event = @event;
                existing.QueuedAtUtc = now;
                existing.PresentedAtUtc = null;
            }
            else
            {
                _events[@event.Id] = new TrackedEntry(@event, now);
            }

            var previousActive = CurrentActiveEvent;
            var newActive = DetermineActiveEvent_NoLock(now);

            if (!ReferenceEquals(previousActive, newActive))
            {
                CurrentActiveEvent = newActive;
                _pendingNotifications.Enqueue(newActive);
                if (!_isDispatchingNotifications)
                {
                    _isDispatchingNotifications = true;
                    shouldDrain = true;
                }
            }

            EnsureActiveTimerStarted_NoLock(newActive, now);
        }

        if (shouldDrain)
        {
            DrainNotifications();
        }
    }

    public void Dismiss(string eventId)
    {
        var shouldDrain = false;

        lock (_syncRoot)
        {
            if (_isDisposed) return;

            if (_persistentEvent?.Id == eventId)
            {
                _persistentEvent = null;
            }

            if (_events.TryGetValue(eventId, out var entry))
            {
                entry.ExpirationTimer?.Dispose();
                _events.Remove(eventId);
            }

            var now = DateTimeOffset.UtcNow;
            var previousActive = CurrentActiveEvent;
            var newActive = DetermineActiveEvent_NoLock(now);

            if (!ReferenceEquals(previousActive, newActive))
            {
                CurrentActiveEvent = newActive;
                _pendingNotifications.Enqueue(newActive);
                if (!_isDispatchingNotifications)
                {
                    _isDispatchingNotifications = true;
                    shouldDrain = true;
                }
            }

            EnsureActiveTimerStarted_NoLock(newActive, now);
        }

        if (shouldDrain)
        {
            DrainNotifications();
        }
    }

    public void LockUserInteraction()
    {
        lock (_syncRoot)
        {
            _isInteractionLocked = true;
        }
    }

    public void UnlockUserInteraction()
    {
        var shouldDrain = false;

        lock (_syncRoot)
        {
            if (_isDisposed) return;

            _isInteractionLocked = false;
            var now = DateTimeOffset.UtcNow;
            var previousActive = CurrentActiveEvent;
            var newActive = DetermineActiveEvent_NoLock(now);

            if (!ReferenceEquals(previousActive, newActive))
            {
                CurrentActiveEvent = newActive;
                _pendingNotifications.Enqueue(newActive);
                if (!_isDispatchingNotifications)
                {
                    _isDispatchingNotifications = true;
                    shouldDrain = true;
                }
            }

            EnsureActiveTimerStarted_NoLock(newActive, now);
        }

        if (shouldDrain)
        {
            DrainNotifications();
        }
    }

    private void DrainNotifications()
    {
        while (true)
        {
            IslandEvent? next;
            lock (_syncRoot)
            {
                if (_isDisposed || _pendingNotifications.Count == 0)
                {
                    _pendingNotifications.Clear();
                    _isDispatchingNotifications = false;
                    return;
                }

                next = _pendingNotifications.Dequeue();
            }

            ActiveEventChanged?.Invoke(this, next);
        }
    }

    private void OnEventExpired(string eventId)
    {
        Dismiss(eventId);
    }

    private IslandEvent? DetermineActiveEvent_NoLock(DateTimeOffset now)
    {
        if (_isInteractionLocked && CurrentActiveEvent != null)
        {
            return CurrentActiveEvent;
        }

        IslandEvent? highestPriorityEvent = null;
        List<string>? staleIds = null;

        foreach (var (id, entry) in _events)
        {
            if (IsEntryExpired(entry, now))
            {
                staleIds ??= [];
                staleIds.Add(id);
                continue;
            }

            if (highestPriorityEvent == null || entry.Event.Priority > highestPriorityEvent.Priority)
            {
                highestPriorityEvent = entry.Event;
            }
        }

        if (staleIds is not null)
        {
            foreach (var staleId in staleIds)
            {
                if (_events.TryGetValue(staleId, out var stale))
                {
                    stale.ExpirationTimer?.Dispose();
                    _events.Remove(staleId);
                }
            }
        }

        if (highestPriorityEvent != null)
        {
            return highestPriorityEvent;
        }

        return _persistentEvent;
    }

    private static bool IsEntryExpired(TrackedEntry entry, DateTimeOffset now)
    {
        if (!entry.Event.DisplayDuration.HasValue)
        {
            return false;
        }

        // If never presented yet (e.g., queued while interaction lock held an expanded panel),
        // keep it valid for up to MaxDeferredPendingAge rather than aging it out unseen.
        if (entry.PresentedAtUtc is null)
        {
            return (now - entry.QueuedAtUtc) >= MaxDeferredPendingAge;
        }

        return (now - entry.PresentedAtUtc.Value) >= entry.Event.DisplayDuration.Value;
    }

    private void EnsureActiveTimerStarted_NoLock(IslandEvent? activeEvent, DateTimeOffset now)
    {
        if (activeEvent is null || !activeEvent.DisplayDuration.HasValue || activeEvent.DisplayDuration.Value <= TimeSpan.Zero)
        {
            return;
        }

        if (!_events.TryGetValue(activeEvent.Id, out var entry) || !ReferenceEquals(entry.Event, activeEvent))
        {
            return;
        }

        if (entry.PresentedAtUtc.HasValue)
        {
            return;
        }

        entry.PresentedAtUtc = now;
        var id = activeEvent.Id;
        entry.ExpirationTimer = new Timer(_ => OnEventExpired(id), null, activeEvent.DisplayDuration.Value, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            foreach (var entry in _events.Values)
            {
                entry.ExpirationTimer?.Dispose();
            }
            _events.Clear();
            _persistentEvent = null;
            CurrentActiveEvent = null;
        }
    }
}
