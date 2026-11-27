using Pulse.Core.Models;

namespace Pulse.Core.Interfaces;

public interface IGitHubTokenStore
{
    string? GetToken();
    bool SaveToken(string? token);
    void ClearToken();
    bool HasToken();
}

public interface ILocalGitWatcherService : IDisposable
{
    IReadOnlyDictionary<string, GitRepositoryState> States { get; }
    event EventHandler<GitRepositoryState>? RepositoryUpdated;
    event EventHandler<GitCommitTransition>? ActivityDetected;

    void UpdateWatchedRepositories(IReadOnlyList<string> repositoryPaths);
    Task RefreshAllAsync(CancellationToken cancellationToken = default);
    Task<GitActionResult> FetchAsync(string repoPath, CancellationToken cancellationToken = default);
    Task<GitActionResult> PullFastForwardAsync(string repoPath, CancellationToken cancellationToken = default);
}

public interface IGitHubWorkspaceService : IDisposable
{
    bool HasAuthToken { get; }

    Task<GitHubRepositorySnapshot?> FetchRepositoryAsync(
        string ownerRepo,
        bool force = false,
        CancellationToken cancellationToken = default);

    Task<GitHubMergeResult> MergePullRequestAsync(
        string ownerRepo,
        int pullNumber,
        string? mergeMethod = null,
        CancellationToken cancellationToken = default);
}
