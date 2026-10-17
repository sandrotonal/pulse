using Pulse.Core.Events;
using Pulse.Core.Interfaces;

namespace Pulse.Core.Services;

public sealed class FocusTimerService : IFocusTimerService
{
    private const string FocusTimerEventId = "focus_timer";
    private readonly IEventRouter _eventRouter;
    private readonly object _syncRoot = new();
    private Timer? _tickerTimer;
    private DateTimeOffset _sessionEndTime;
    private TimeSpan _workDuration = TimeSpan.FromMinutes(25);
    private TimeSpan _breakDuration = TimeSpan.FromMinutes(5);
    private TimeSpan _pausedRemaining;
    private bool _isDisposed;

    public TimeSpan RemainingTime { get; private set; }
    public TimeSpan TotalDuration { get; private set; }
    public bool IsRunning => State == FocusTimerState.Running;
    public bool IsBreak { get; private set; }
    public FocusTimerState State { get; private set; } = FocusTimerState.Paused;

    public event EventHandler<FocusTimerEvent>? Tick;
    public event EventHandler<FocusTimerEvent>? SessionCompleted;

    public FocusTimerService(IEventRouter eventRouter)
    {
        _eventRouter = eventRouter ?? throw new ArgumentNullException(nameof(eventRouter));
        RemainingTime = _workDuration;
        TotalDuration = _workDuration;
        _pausedRemaining = _workDuration;
    }

    public void Start(TimeSpan workDuration, TimeSpan breakDuration)
    {
        lock (_syncRoot)
        {
            if (_isDisposed) return;

            _workDuration = workDuration > TimeSpan.Zero ? workDuration : TimeSpan.FromMinutes(25);
            _breakDuration = breakDuration > TimeSpan.Zero ? breakDuration : TimeSpan.FromMinutes(5);
            IsBreak = false;
            TotalDuration = _workDuration;
            RemainingTime = _workDuration;
            _sessionEndTime = DateTimeOffset.UtcNow + _workDuration;
            State = FocusTimerState.Running;
            PublishCurrentState();

            _tickerTimer?.Dispose();
            _tickerTimer = new Timer(OnTimerTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
    }

    public void Pause()
    {
        lock (_syncRoot)
        {
            if (_isDisposed || State != FocusTimerState.Running) return;

            _pausedRemaining = RemainingTime;
            State = FocusTimerState.Paused;
            _tickerTimer?.Dispose();
            _tickerTimer = null;

            PublishCurrentState();
        }
    }

    public void Resume()
    {
        lock (_syncRoot)
        {
            if (_isDisposed || State != FocusTimerState.Paused) return;

            _sessionEndTime = DateTimeOffset.UtcNow + _pausedRemaining;
            State = FocusTimerState.Running;
            PublishCurrentState();

            _tickerTimer?.Dispose();
            _tickerTimer = new Timer(OnTimerTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
    }

    public void Reset()
    {
        lock (_syncRoot)
        {
            if (_isDisposed) return;

            State = FocusTimerState.Paused;
            _tickerTimer?.Dispose();
            _tickerTimer = null;
            IsBreak = false;
            RemainingTime = _workDuration;
            TotalDuration = _workDuration;
            _pausedRemaining = _workDuration;

            _eventRouter.Dismiss(FocusTimerEventId);
        }
    }

    public void Restart()
    {
        lock (_syncRoot)
        {
            if (_isDisposed) return;
        }

        Start(_workDuration, _breakDuration);
    }

    public void SkipToNextSession()
    {
        lock (_syncRoot)
        {
            if (_isDisposed) return;

            IsBreak = !IsBreak;
            var duration = IsBreak ? _breakDuration : _workDuration;
            TotalDuration = duration;
            RemainingTime = duration;
            _sessionEndTime = DateTimeOffset.UtcNow + duration;
            State = FocusTimerState.Running;
            PublishCurrentState();

            _tickerTimer?.Dispose();
            _tickerTimer = new Timer(OnTimerTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
    }

    private void OnTimerTick(object? state)
    {
        FocusTimerEvent? evt = null;
        bool isCompleted = false;

        lock (_syncRoot)
        {
            if (_isDisposed || State != FocusTimerState.Running) return;

            var remaining = _sessionEndTime - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
                RemainingTime = TimeSpan.Zero;
                State = FocusTimerState.Completed;
                isCompleted = true;

                _tickerTimer?.Dispose();
                _tickerTimer = null;

                evt = new FocusTimerEvent(
                    FocusTimerEventId,
                    TimeSpan.Zero,
                    TotalDuration,
                    IsBreak,
                    FocusTimerState.Completed
                );
            }
            else
            {
                RemainingTime = remaining;
                evt = new FocusTimerEvent(
                    FocusTimerEventId,
                    remaining,
                    TotalDuration,
                    IsBreak,
                    FocusTimerState.Running
                );
            }

            _eventRouter.Publish(evt);
        }

        if (isCompleted)
        {
            SessionCompleted?.Invoke(this, evt!);
        }
        else
        {
            Tick?.Invoke(this, evt!);
        }
    }

    private void PublishCurrentState()
    {
        var evt = new FocusTimerEvent(
            FocusTimerEventId,
            RemainingTime,
            TotalDuration,
            IsBreak,
            State
        );
        _eventRouter.Publish(evt);
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _tickerTimer?.Dispose();
            _tickerTimer = null;
        }
    }
}
