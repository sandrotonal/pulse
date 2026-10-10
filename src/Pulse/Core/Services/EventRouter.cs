using Pulse.Core.Events;
using Pulse.Core.Interfaces;

namespace Pulse.Core.Services;

public sealed class EventRouter : IEventRouter
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, (IslandEvent Event, Timer? ExpirationTimer)> _events = new();
    private IslandEvent? _persistentEvent;
    private bool _isInteractionLocked;
    private bool _isDisposed;

    public IslandEvent? CurrentActiveEvent { get; private set; }

    public event EventHandler<IslandEvent?>? ActiveEventChanged;

    public void Publish(IslandEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        IslandEvent? previousActive;
        IslandEvent? newActive;

        lock (_syncRoot)
        {
            if (_isDisposed) return;

            // Handle persistent background media events
            if (@event.Priority == EventPriority.ActiveMedia && !@event.DisplayDuration.HasValue)
            {
                _persistentEvent = @event;
            }
            else
            {
                // Cancel existing expiration timer for this ID if updating
                if (_events.TryGetValue(@event.Id, out var existing))
                {
                    existing.ExpirationTimer?.Dispose();
                }

                Timer? timer = null;
                if (@event.DisplayDuration.HasValue && @event.DisplayDuration.Value > TimeSpan.Zero)
                {
                    var id = @event.Id;
                    timer = new Timer(_ => OnEventExpired(id), null, @event.DisplayDuration.Value, Timeout.InfiniteTimeSpan);
                }

                _events[@event.Id] = (@event, timer);
            }

            previousActive = CurrentActiveEvent;
            newActive = DetermineActiveEvent_NoLock();

            if (!ReferenceEquals(previousActive, newActive))
            {
                CurrentActiveEvent = newActive;
            }
        }

        if (!ReferenceEquals(previousActive, newActive))
        {
            ActiveEventChanged?.Invoke(this, newActive);
        }
    }

    public void Dismiss(string eventId)
    {
        IslandEvent? previousActive;
        IslandEvent? newActive;

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

            previousActive = CurrentActiveEvent;
            newActive = DetermineActiveEvent_NoLock();

            if (!ReferenceEquals(previousActive, newActive))
            {
                CurrentActiveEvent = newActive;
            }
        }

        if (!ReferenceEquals(previousActive, newActive))
        {
            ActiveEventChanged?.Invoke(this, newActive);
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
        IslandEvent? previousActive;
        IslandEvent? newActive;

        lock (_syncRoot)
        {
            _isInteractionLocked = false;
            previousActive = CurrentActiveEvent;
            newActive = DetermineActiveEvent_NoLock();

            if (!ReferenceEquals(previousActive, newActive))
            {
                CurrentActiveEvent = newActive;
            }
        }

        if (!ReferenceEquals(previousActive, newActive))
        {
            ActiveEventChanged?.Invoke(this, newActive);
        }
    }

    private void OnEventExpired(string eventId)
    {
        Dismiss(eventId);
    }

    private IslandEvent? DetermineActiveEvent_NoLock()
    {
        if (_isInteractionLocked && CurrentActiveEvent != null)
        {
            return CurrentActiveEvent;
        }

        var now = DateTimeOffset.UtcNow;
        IslandEvent? highestPriorityEvent = null;

        foreach (var entry in _events.Values)
        {
            if (entry.Event.IsExpired(now))
            {
                continue;
            }

            if (highestPriorityEvent == null || entry.Event.Priority > highestPriorityEvent.Priority)
            {
                highestPriorityEvent = entry.Event;
            }
        }

        if (highestPriorityEvent != null)
        {
            return highestPriorityEvent;
        }

        return _persistentEvent;
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
