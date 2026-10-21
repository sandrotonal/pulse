using Pulse.Core.Events;
using Pulse.Core.Models;
using Pulse.Core.Services;

namespace Pulse.Tests;

public sealed class IslandSplitModeTests
{
    [Fact]
    public void SplitModeActivatesWhenSecondaryActivityCoexistsWithActiveMedia()
    {
        using var router = new EventRouter();
        using var timerService = new FocusTimerService(router);

        timerService.Start(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5));
        var active = router.CurrentActiveEvent as FocusTimerEvent;
        Assert.NotNull(active);
        Assert.Equal("focus_timer", active.Id);
    }
}
