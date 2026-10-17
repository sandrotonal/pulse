using Pulse.Core.Events;
using Pulse.Core.Services;

namespace Pulse.Tests.Services;

public sealed class FocusTimerServiceTests
{
    [Fact]
    public void StartBeginsFocusSessionAndPublishesEvent()
    {
        using var router = new EventRouter();
        using var timerService = new FocusTimerService(router);

        timerService.Start(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5));

        Assert.True(timerService.IsRunning);
        Assert.False(timerService.IsBreak);
        Assert.NotNull(router.CurrentActiveEvent);
        Assert.Equal("focus_timer", router.CurrentActiveEvent.Id);
        Assert.IsType<FocusTimerEvent>(router.CurrentActiveEvent);
    }

    [Fact]
    public void PauseFreezesRemainingTime()
    {
        using var router = new EventRouter();
        using var timerService = new FocusTimerService(router);

        timerService.Start(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5));
        timerService.Pause();

        Assert.False(timerService.IsRunning);
        Assert.Equal(FocusTimerState.Paused, timerService.State);

        var active = router.CurrentActiveEvent as FocusTimerEvent;
        Assert.NotNull(active);
        Assert.Equal(FocusTimerState.Paused, active.State);
    }

    [Fact]
    public void ResumeRestoresRunningSession()
    {
        using var router = new EventRouter();
        using var timerService = new FocusTimerService(router);

        timerService.Start(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5));
        timerService.Pause();
        timerService.Resume();

        Assert.True(timerService.IsRunning);
        Assert.Equal(FocusTimerState.Running, timerService.State);
    }

    [Fact]
    public void ResetDismissesFocusTimerEventFromRouter()
    {
        using var router = new EventRouter();
        using var timerService = new FocusTimerService(router);

        timerService.Start(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5));
        Assert.NotNull(router.CurrentActiveEvent);

        timerService.Reset();
        Assert.Null(router.CurrentActiveEvent);
    }

    [Fact]
    public void SkipToNextSessionTogglesBreakMode()
    {
        using var router = new EventRouter();
        using var timerService = new FocusTimerService(router);

        timerService.Start(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5));
        Assert.False(timerService.IsBreak);

        timerService.SkipToNextSession();
        Assert.True(timerService.IsBreak);
        Assert.Equal(TimeSpan.FromMinutes(5), timerService.TotalDuration);
    }
}
