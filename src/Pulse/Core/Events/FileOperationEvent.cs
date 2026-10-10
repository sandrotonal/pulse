namespace Pulse.Core.Events;

public enum FileOperationStatus
{
    InProgress,
    Completed,
    Failed
}

public sealed record FileOperationEvent(
    string Id,
    string FileName,
    long TransferredBytes,
    long TotalBytes,
    FileOperationStatus Status,
    string? DestinationPath = null,
    string? ErrorMessage = null,
    TimeSpan? DisplayDuration = null
) : IslandEvent(
    Id,
    Status == FileOperationStatus.InProgress ? EventPriority.Progress : EventPriority.Informational,
    EventCategory.FileOperation,
    DateTimeOffset.UtcNow,
    DisplayDuration ?? (Status == FileOperationStatus.InProgress ? null : TimeSpan.FromSeconds(3.5)),
    CanDismiss: true
)
{
    public double ProgressPercentage => TotalBytes > 0
        ? Math.Clamp((double)TransferredBytes / TotalBytes * 100.0, 0.0, 100.0)
        : 0.0;
}
