using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Pulse.Core.Interfaces;
using Pulse.Core.Models;

namespace Pulse.Core.Services.Developer;

/// <summary>
/// Watches local Git repositories via zero-CPU <see cref="FileSystemWatcher"/> on <c>.git</c> metadata
/// (<c>HEAD</c>, <c>index</c>, <c>refs/heads</c>) and runs bounded <c>git --no-optional-locks</c> status queries.
/// </summary>
public sealed class LocalGitWatcherService : ILocalGitWatcherService
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan GitCommandTimeout = TimeSpan.FromSeconds(4);

    private sealed class RepoWatchEntry : IDisposable
    {
        public RepoWatchEntry(string rootPath, string gitMetadataDir)
        {
            RootPath = rootPath;
            GitMetadataDir = gitMetadataDir;
        }

        public string RootPath { get; }
        public string GitMetadataDir { get; }
        public FileSystemWatcher? Watcher { get; set; }
        public CancellationTokenSource? DebounceCts { get; set; }
        public GitRepositoryState? LastState { get; set; }

        public void Dispose()
        {
            if (Watcher is not null)
            {
                Watcher.EnableRaisingEvents = false;
                Watcher.Dispose();
                Watcher = null;
            }

            try { DebounceCts?.Cancel(); } catch { }
            try { DebounceCts?.Dispose(); } catch { }
            DebounceCts = null;
        }
    }

    private readonly object _sync = new();
    private readonly Dictionary<string, RepoWatchEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, GitRepositoryState> _states = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _disposed;

    public IReadOnlyDictionary<string, GitRepositoryState> States => _states;
    public event EventHandler<GitRepositoryState>? RepositoryUpdated;
    public event EventHandler<GitCommitTransition>? ActivityDetected;

    public void UpdateWatchedRepositories(IReadOnlyList<string> repositoryPaths)
    {
        if (_disposed)
        {
            return;
        }

        var normalizedRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (repositoryPaths is not null)
        {
            foreach (var rawPath in repositoryPaths)
            {
                if (TryResolveGitRoot(rawPath, out var rootPath, out var gitMetadataDir) &&
                    !normalizedRoots.ContainsKey(rootPath))
                {
                    normalizedRoots.Add(rootPath, gitMetadataDir);
                }
            }
        }

        List<RepoWatchEntry> added = [];
        List<RepoWatchEntry> removed = [];

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var existingKey in _entries.Keys.ToList())
            {
                if (!normalizedRoots.ContainsKey(existingKey))
                {
                    removed.Add(_entries[existingKey]);
                    _entries.Remove(existingKey);
                    _states.TryRemove(existingKey, out _);
                }
            }

            foreach (var (rootPath, gitMetadataDir) in normalizedRoots)
            {
                if (!_entries.ContainsKey(rootPath))
                {
                    var entry = CreateWatchEntry(rootPath, gitMetadataDir);
                    _entries.Add(rootPath, entry);
                    added.Add(entry);
                }
            }
        }

        foreach (var oldEntry in removed)
        {
            oldEntry.Dispose();
        }

        foreach (var newEntry in added)
        {
            ScheduleRefresh(newEntry, immediate: true);
        }
    }

    public async Task RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        List<RepoWatchEntry> snapshot;
        lock (_sync)
        {
            snapshot = _entries.Values.ToList();
        }

        await Task.WhenAll(snapshot.Select(entry => RefreshEntryAsync(entry, notifyTransitions: false, cancellationToken)))
            .ConfigureAwait(false);
    }

    public async Task<GitActionResult> FetchAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        if (_disposed || !TryResolveGitRoot(repoPath, out var rootPath, out _))
        {
            return new GitActionResult(false, "Geçerli yerel Git reposu bulunamadı.");
        }

        var result = await RunGitDetailedAsync(
            rootPath,
            ["--no-optional-locks", "fetch", "--prune", "--quiet"],
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        await RefreshRepoByPathAsync(rootPath, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? new GitActionResult(true, "Fetch tamamlandı")
            : new GitActionResult(false, SummarizeGitFailure(result.Error, "Fetch başarısız."));
    }

    public async Task<GitActionResult> PullFastForwardAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        if (_disposed || !TryResolveGitRoot(repoPath, out var rootPath, out _))
        {
            return new GitActionResult(false, "Geçerli yerel Git reposu bulunamadı.");
        }

        var result = await RunGitDetailedAsync(
            rootPath,
            ["--no-optional-locks", "pull", "--ff-only", "--quiet"],
            TimeSpan.FromSeconds(12),
            cancellationToken).ConfigureAwait(false);

        await RefreshRepoByPathAsync(rootPath, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? new GitActionResult(true, "Pull (ff-only) tamamlandı")
            : new GitActionResult(false, SummarizeGitFailure(result.Error, "Fast-forward pull yapılamadı."));
    }

    private async Task RefreshRepoByPathAsync(string rootPath, CancellationToken cancellationToken)
    {
        RepoWatchEntry? target = null;
        lock (_sync)
        {
            _entries.TryGetValue(rootPath, out target);
        }

        if (target is not null)
        {
            await RefreshEntryAsync(target, notifyTransitions: false, cancellationToken).ConfigureAwait(false);
        }
    }

    public static bool TryResolveGitRoot(string? inputPath, out string rootPath, out string gitMetadataDir)
    {
        rootPath = string.Empty;
        gitMetadataDir = string.Empty;
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return false;
        }

        try
        {
            var current = new DirectoryInfo(Environment.ExpandEnvironmentVariables(inputPath.Trim().Trim('"')));
            if (!current.Exists)
            {
                return false;
            }

            while (current is not null)
            {
                var dotGitDir = Path.Combine(current.FullName, ".git");
                if (Directory.Exists(dotGitDir))
                {
                    rootPath = current.FullName;
                    gitMetadataDir = dotGitDir;
                    return true;
                }

                if (File.Exists(dotGitDir))
                {
                    var line = File.ReadLines(dotGitDir).FirstOrDefault()?.Trim();
                    if (line is not null && line.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
                    {
                        var target = line["gitdir:".Length..].Trim();
                        var resolvedGitDir = Path.IsPathRooted(target)
                            ? target
                            : Path.GetFullPath(Path.Combine(current.FullName, target));
                        if (Directory.Exists(resolvedGitDir))
                        {
                            rootPath = current.FullName;
                            gitMetadataDir = resolvedGitDir;
                            return true;
                        }
                    }
                }

                current = current.Parent;
            }
        }
        catch
        {
        }

        return false;
    }

    private RepoWatchEntry CreateWatchEntry(string rootPath, string gitMetadataDir)
    {
        var entry = new RepoWatchEntry(rootPath, gitMetadataDir);
        try
        {
            var watcher = new FileSystemWatcher(gitMetadataDir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
            };

            FileSystemEventHandler onChanged = (_, e) =>
            {
                if (!_disposed && IsRelevantGitMetadataFile(e.FullPath, e.Name))
                {
                    ScheduleRefresh(entry, immediate: false);
                }
            };
            RenamedEventHandler onRenamed = (_, e) =>
            {
                if (!_disposed && IsRelevantGitMetadataFile(e.FullPath, e.Name))
                {
                    ScheduleRefresh(entry, immediate: false);
                }
            };

            watcher.Changed += onChanged;
            watcher.Created += onChanged;
            watcher.Deleted += onChanged;
            watcher.Renamed += onRenamed;
            watcher.EnableRaisingEvents = true;
            entry.Watcher = watcher;
        }
        catch (Exception ex)
        {
            CrashLogger.Write("LocalGitWatcherService.CreateWatchEntry", ex, $"Root={rootPath}");
        }

        return entry;
    }

    private static bool IsRelevantGitMetadataFile(string? fullPath, string? relativeName)
    {
        var name = relativeName ?? fullPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.Equals("HEAD", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("index", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("MERGE_HEAD", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("REBASE_HEAD", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("refs" + Path.DirectorySeparatorChar + "heads", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("refs" + Path.DirectorySeparatorChar + "remotes", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("logs" + Path.DirectorySeparatorChar + "HEAD", StringComparison.OrdinalIgnoreCase);
    }

    private void ScheduleRefresh(RepoWatchEntry entry, bool immediate)
    {
        if (_disposed)
        {
            return;
        }

        CancellationTokenSource cts;
        CancellationTokenSource? previous;
        lock (_sync)
        {
            if (_disposed || !_entries.ContainsKey(entry.RootPath))
            {
                return;
            }

            previous = entry.DebounceCts;
            cts = new CancellationTokenSource();
            entry.DebounceCts = cts;
        }

        if (previous is not null)
        {
            try { previous.Cancel(); } catch { }
            try { previous.Dispose(); } catch { }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (!immediate)
                {
                    await Task.Delay(DebounceDelay, cts.Token).ConfigureAwait(false);
                }

                await RefreshEntryAsync(entry, notifyTransitions: !immediate, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                CrashLogger.Write("LocalGitWatcherService.ScheduleRefresh", ex, $"Root={entry.RootPath}");
            }
        });
    }

    private async Task RefreshEntryAsync(RepoWatchEntry entry, bool notifyTransitions, CancellationToken cancellationToken)
    {
        if (_disposed || cancellationToken.IsCancellationRequested || !Directory.Exists(entry.RootPath))
        {
            return;
        }

        var statusTask = RunGitAsync(entry.RootPath, "--no-optional-locks status --porcelain=v2 --branch", cancellationToken);
        var logTask = RunGitAsync(entry.RootPath, "--no-optional-locks log -1 --format=%h%x1f%s%x1f%cr", cancellationToken);
        var remoteTask = RunGitAsync(entry.RootPath, "--no-optional-locks config --get remote.origin.url", cancellationToken);

        await Task.WhenAll(statusTask, logTask, remoteTask).ConfigureAwait(false);

        var statusOutput = await statusTask.ConfigureAwait(false);
        if (statusOutput is null)
        {
            return;
        }

        var nextState = GitStatusParser.ParsePorcelainV2(
            entry.RootPath,
            statusOutput,
            await logTask.ConfigureAwait(false),
            await remoteTask.ConfigureAwait(false),
            DateTimeOffset.UtcNow);

        GitRepositoryState? previousState;
        lock (_sync)
        {
            if (_disposed || !_entries.ContainsKey(entry.RootPath))
            {
                return;
            }

            previousState = entry.LastState;
            entry.LastState = nextState;
            _states[entry.RootPath] = nextState;
        }

        RepositoryUpdated?.Invoke(this, nextState);

        if (notifyTransitions && previousState is not null)
        {
            var branchChanged = !string.Equals(previousState.Branch, nextState.Branch, StringComparison.Ordinal);
            var commitChanged = !string.IsNullOrWhiteSpace(nextState.LastCommitShortSha) &&
                                !string.Equals(previousState.LastCommitShortSha, nextState.LastCommitShortSha, StringComparison.Ordinal);

            if (branchChanged || commitChanged)
            {
                ActivityDetected?.Invoke(this, new GitCommitTransition(
                    RepoPath: nextState.RepoPath,
                    DisplayName: nextState.DisplayName,
                    Branch: nextState.Branch,
                    ShortSha: nextState.LastCommitShortSha,
                    Subject: nextState.LastCommitSubject,
                    IsBranchChange: branchChanged && !commitChanged));
            }
        }
    }

    private static async Task<string?> RunGitAsync(string workingDirectory, string arguments, CancellationToken cancellationToken)
    {
        var args = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var res = await RunGitDetailedAsync(workingDirectory, args, GitCommandTimeout, cancellationToken).ConfigureAwait(false);
        return res.ExitCode == 0 ? res.Output : null;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunGitDetailedAsync(
        string workingDirectory,
        IReadOnlyList<string> argumentList,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in argumentList)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return (-1, string.Empty, string.Empty);
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);

            return (process.ExitCode, output, error);
        }
        catch (OperationCanceledException)
        {
            return (-1, string.Empty, "Git işlemi zaman aşımına uğradı.");
        }
        catch
        {
            return (-1, string.Empty, string.Empty);
        }
    }

    private static string SummarizeGitFailure(string? stderr, string fallback)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return fallback;
        }

        if (stderr.Contains("diverg", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("Not possible to fast-forward", StringComparison.OrdinalIgnoreCase))
        {
            return "Dallar ayrışmış; terminalden merge/rebase gerekir.";
        }

        if (stderr.Contains("local changes", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("uncommitted changes", StringComparison.OrdinalIgnoreCase))
        {
            return "Önce yerel değişiklikleri commit/stash edin.";
        }

        if (stderr.Contains("Authentication", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
            stderr.Contains("Could not read from remote", StringComparison.OrdinalIgnoreCase))
        {
            return "Remote erişim/kimlik doğrulaması başarısız.";
        }

        return fallback;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        List<RepoWatchEntry> snapshot;
        lock (_sync)
        {
            snapshot = _entries.Values.ToList();
            _entries.Clear();
            _states.Clear();
        }

        foreach (var entry in snapshot)
        {
            entry.Dispose();
        }
    }
}
