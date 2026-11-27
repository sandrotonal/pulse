namespace Pulse.Core.Models;

public sealed record GitRepositoryState(
    string RepoPath,
    string DisplayName,
    string Branch,
    string? Upstream,
    int AheadCount,
    int BehindCount,
    int ModifiedCount,
    int StagedCount,
    int UntrackedCount,
    bool IsDetachedHead,
    string LastCommitShortSha,
    string LastCommitSubject,
    string LastCommitRelativeTime,
    string? OriginOwnerRepo,
    DateTimeOffset UpdatedAtUtc)
{
    public int TotalDirtyCount => ModifiedCount + StagedCount + UntrackedCount;
    public bool IsClean => TotalDirtyCount == 0;
}

public sealed record GitCommitTransition(
    string RepoPath,
    string DisplayName,
    string Branch,
    string ShortSha,
    string Subject,
    bool IsBranchChange);

public sealed record GitHubWorkflowRunInfo(
    long Id,
    string Name,
    string DisplayTitle,
    string Branch,
    string Status,
    string Conclusion,
    string HtmlUrl,
    DateTimeOffset UpdatedAtUtc)
{
    public bool IsInProgress =>
        string.Equals(Status, "in_progress", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Status, "queued", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Status, "waiting", StringComparison.OrdinalIgnoreCase);

    public bool IsSuccess => string.Equals(Conclusion, "success", StringComparison.OrdinalIgnoreCase);

    public bool IsFailure =>
        string.Equals(Conclusion, "failure", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Conclusion, "timed_out", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Conclusion, "action_required", StringComparison.OrdinalIgnoreCase);
}

public sealed record GitHubPullRequestInfo(
    int Number,
    string Title,
    string Author,
    string HeadRef,
    string BaseRef,
    bool IsDraft,
    string HtmlUrl,
    DateTimeOffset UpdatedAtUtc);

public sealed record GitHubRepositorySnapshot(
    string OwnerRepo,
    IReadOnlyList<GitHubWorkflowRunInfo> WorkflowRuns,
    IReadOnlyList<GitHubPullRequestInfo> OpenPullRequests,
    int? RateLimitRemaining,
    DateTimeOffset FetchedAtUtc,
    string? ErrorMessage = null,
    bool NotModified = false)
{
    public GitHubWorkflowRunInfo? ActiveRun => WorkflowRuns.FirstOrDefault(r => r.IsInProgress);
    public GitHubWorkflowRunInfo? LatestRun => WorkflowRuns.FirstOrDefault();
}

public sealed record GitActionResult(bool Success, string Message);

public sealed record GitHubMergeResult(
    bool Success,
    bool Merged,
    string Message,
    string? Sha = null);
