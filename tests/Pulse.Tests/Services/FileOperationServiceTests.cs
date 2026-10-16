using Pulse.Core.Events;
using Pulse.Core.Services;
using Xunit;

namespace Pulse.Tests.Services;

public sealed class FileOperationServiceTests
{
    [Fact]
    public void ReportProgress_PublishesInProgressEvent()
    {
        var router = new EventRouter();
        var service = new FileOperationService(router);

        service.ReportProgress("op_1", "ProjectReport.pdf", 500, 1000);

        var active = router.CurrentActiveEvent as FileOperationEvent;
        Assert.NotNull(active);
        Assert.Equal("op_1", active.Id);
        Assert.Equal("ProjectReport.pdf", active.FileName);
        Assert.Equal(50.0, active.ProgressPercentage);
        Assert.Equal(FileOperationStatus.InProgress, active.Status);
    }

    [Fact]
    public void ReportCompleted_PublishesCompletedEvent()
    {
        var router = new EventRouter();
        var service = new FileOperationService(router);

        service.ReportCompleted("op_2", "Archive.zip", @"C:\Downloads\Archive.zip");

        var active = router.CurrentActiveEvent as FileOperationEvent;
        Assert.NotNull(active);
        Assert.Equal("op_2", active.Id);
        Assert.Equal("Archive.zip", active.FileName);
        Assert.Equal(FileOperationStatus.Completed, active.Status);
        Assert.NotNull(active.DisplayDuration);
    }

    [Fact]
    public void ReportFailed_PublishesFailedEventWithErrorMessage()
    {
        var router = new EventRouter();
        var service = new FileOperationService(router);

        service.ReportFailed("op_3", "Data.iso", "Yetersiz disk alanı.");

        var active = router.CurrentActiveEvent as FileOperationEvent;
        Assert.NotNull(active);
        Assert.Equal("op_3", active.Id);
        Assert.Equal(FileOperationStatus.Failed, active.Status);
        Assert.Equal("Yetersiz disk alanı.", active.ErrorMessage);
    }
}
