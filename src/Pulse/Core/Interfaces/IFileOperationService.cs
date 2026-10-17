namespace Pulse.Core.Interfaces;

public interface IFileOperationService : IDisposable
{
    void ReportProgress(string operationId, string fileName, long transferredBytes, long totalBytes, string? destinationPath = null);
    void ReportCompleted(string operationId, string fileName, string destinationPath);
    void ReportFailed(string operationId, string fileName, string errorMessage);
}
