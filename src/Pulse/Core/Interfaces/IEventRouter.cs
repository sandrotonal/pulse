using Pulse.Core.Events;

namespace Pulse.Core.Interfaces;

public interface IEventRouter : IDisposable
{
    IslandEvent? CurrentActiveEvent { get; }
    event EventHandler<IslandEvent?> ActiveEventChanged;

    void Publish(IslandEvent @event);
    void Dismiss(string eventId);
    void LockUserInteraction();
    void UnlockUserInteraction();
}
