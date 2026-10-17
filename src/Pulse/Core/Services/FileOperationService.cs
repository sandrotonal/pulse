using Pulse.Core.Events;
using Pulse.Core.Interfaces;

namespace Pulse.Core.Services;

public sealed class FileOperationService : IFileOperationService
{
    private readonly IEventRouter _eventRouter;
    private readonly object _syncLock = new();
    private bool _isDisposed;

    public FileOperationService(IEventRouter eventRouter)
    {
        _eventRouter = eventRouter ?? throw new ArgumentNullException(nameof(eventRouter));
    }

    public void ReportProgress(string operationId, string fileName, long transferredBytes, long totalBytes, string? destinationPath = null)
    {
        if (string.IsNullOrWhiteSpace(operationId)) return;
        if (string.IsNullOrWhiteSpace(fileName)) return;

        lock (_syncLock)
        {
            if (_isDisposed) return;

            var evt = new FileOperationEvent(
                operationId,
                fileName,
                transferredBytes,
                totalBytes,
                FileOperationStatus.InProgress,
                destinationPath
            );

            _eventRouter.Publish(evt);
        }
    }

    public void ReportCompleted(string operationId, string fileName, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(operationId)) return;
        if (string.IsNullOrWhiteSpace(fileName)) return;

        lock (_syncLock)
        {
            if (_isDisposed) return;

            var evt = new FileOperationEvent(
                operationId,
                fileName,
                100,
                100,
                FileOperationStatus.Completed,
                destinationPath,
                DisplayDuration: TimeSpan.FromSeconds(3.5)
            );

            _eventRouter.Publish(evt);
        }
    }

    public void ReportFailed(string operationId, string fileName, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(operationId)) return;
        if (string.IsNullOrWhiteSpace(fileName)) return;

        lock (_syncLock)
        {
            if (_isDisposed) return;

            var evt = new FileOperationEvent(
                operationId,
                fileName,
                0,
                100,
                FileOperationStatus.Failed,
                ErrorMessage: errorMessage,
                DisplayDuration: TimeSpan.FromSeconds(4.0)
            );

            _eventRouter.Publish(evt);
        }
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            _isDisposed = true;
        }
    }
}
