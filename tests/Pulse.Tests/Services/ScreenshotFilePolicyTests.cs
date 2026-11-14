using Pulse.Core.Services;

namespace Pulse.Tests.Services;

public sealed class ScreenshotFilePolicyTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(@"C:\Shots\.temp.png", false)]
    [InlineData(@"C:\Shots\Pulse_20261110_233000_123.png", false)]
    [InlineData(@"C:\Shots\Ekran görüntüsü.tmp", false)]
    [InlineData(@"C:\Shots\Ekran görüntüsü 2026-11-10.png", true)]
    [InlineData(@"C:\Shots\capture.JPEG", true)]
    public void IsExternalScreenshotPathFiltersTempSelfAndNonImages(string? path, bool expected)
    {
        Assert.Equal(expected, ScreenshotFilePolicy.IsExternalScreenshotPath(path));
    }

    [Fact]
    public void ShouldInspectChangeAllowsRecentlyCreatedFileAndRejectsOldFileEdits()
    {
        var policy = new ScreenshotFilePolicy();
        var now = DateTimeOffset.UtcNow;
        var path = @"C:\Shots\Ekran görüntüsü 2026-11-10 233000.png";

        // File created earlier today (old timestamp, never recently observed) -> manual edit ignored
        Assert.False(policy.ShouldInspectChange(path, now.AddHours(-2), now));

        // Observed on Created/Renamed (e.g. 0-byte Snipping Tool placeholder) -> Changed 400ms later IS inspected
        Assert.True(policy.RecordObservedCreation(path, now));
        Assert.True(policy.ShouldInspectChange(path, now.AddHours(-2), now.AddMilliseconds(400)));

        // Even without prior Created (file watcher missed Created), brand-new CreationTimeUtc IS inspected
        var secondPath = @"C:\Shots\Ekran görüntüsü 2026-11-10 233010.png";
        Assert.True(policy.ShouldInspectChange(secondPath, now.AddSeconds(-3), now));

        // Once reported, follow-up metadata/OneDrive sync Changed events stay suppressed
        Assert.True(policy.TryMarkReported(secondPath, now));
        Assert.False(policy.ShouldInspectChange(secondPath, now.AddSeconds(-3), now.AddSeconds(7)));
    }

    [Fact]
    public void TryMarkReportedDeduplicatesWithinFiveSecondWindow()
    {
        var policy = new ScreenshotFilePolicy();
        var now = DateTimeOffset.UtcNow;
        var path = @"C:\Shots\Shot.png";

        Assert.True(policy.TryMarkReported(path, now));
        Assert.False(policy.TryMarkReported(path, now.AddSeconds(2)));
        Assert.True(policy.TryMarkReported(path, now.AddSeconds(6)));
    }
}
