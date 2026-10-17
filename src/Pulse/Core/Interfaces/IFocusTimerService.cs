using Pulse.Core.Events;

namespace Pulse.Core.Interfaces;

public interface IFocusTimerService : IDisposable
{
    TimeSpan RemainingTime { get; }
    TimeSpan TotalDuration { get; }
    bool IsRunning { get; }
    bool IsBreak { get; }
    FocusTimerState State { get; }

    event EventHandler<FocusTimerEvent>? Tick;
    event EventHandler<FocusTimerEvent>? SessionCompleted;

    void Start(TimeSpan workDuration, TimeSpan breakDuration);
    void Pause();
    void Resume();
    void Reset();
    void SkipToNextSession();
}
