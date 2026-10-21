using Pulse.Core.Events;
using Pulse.Core.Services;

namespace Pulse.Tests;

public sealed class IslandCarouselNavigationTests
{
    [Fact]
    public void FocusTimerPresetsInitializeCorrectDurations()
    {
        using var router = new EventRouter();
        using var timerService = new FocusTimerService(router);

        // 25 min focus
        timerService.Start(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(25), timerService.TotalDuration);

        // 50 min deep work
        timerService.Start(TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(10));
        Assert.Equal(TimeSpan.FromMinutes(50), timerService.TotalDuration);

        // 5 min short break
        timerService.Start(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(5), timerService.TotalDuration);

        // 15 min long break
        timerService.Start(TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(15), timerService.TotalDuration);
    }

    [Fact]
    public void SystemAudioStatusReportsAccurately()
    {
        using var router = new EventRouter();
        using var systemService = new SystemEventService(router);

        systemService.ReportAudioStatus(80, isMuted: false);
        var active = router.CurrentActiveEvent as SystemNotificationEvent;
        Assert.NotNull(active);
        Assert.Equal(EventCategory.Audio, active.Category);
        Assert.Equal("Ses", active.Title);
        Assert.Equal("%80", active.Subtitle);

        systemService.ReportAudioStatus(0, isMuted: true);
        var activeMuted = router.CurrentActiveEvent as SystemNotificationEvent;
        Assert.NotNull(activeMuted);
        Assert.Equal("Sessiz", activeMuted.Title);
    }

    [Fact]
    public void SystemScreenshotNotificationDispatchesCorrectly()
    {
        using var router = new EventRouter();
        using var systemService = new SystemEventService(router);

        systemService.ReportScreenshotSaved(@"C:\Screenshots\Capture_01.png");
        var active = router.CurrentActiveEvent as SystemNotificationEvent;
        Assert.NotNull(active);
        Assert.Equal(EventCategory.Screenshot, active.Category);
        Assert.Equal("Ekran Görüntüsü Kaydedildi", active.Title);
        Assert.Equal("Capture_01.png", active.Subtitle);
    }
}
