using System.Net;
using System.Net.Http.Headers;
using Pulse.Core.Events;
using Pulse.Core.Models;
using Pulse.Core.Services;
using Pulse.Core.Services.Developer;

namespace Pulse.Tests.Services;

public sealed class DeveloperWorkspaceTests
{
    [Fact]
    public void GitStatusParserParsesPorcelainV2BranchAheadBehindAndCounts()
    {
        var porcelain = """
            # branch.oid 9639de5a1234567890abcdef
            # branch.head feat/v2.3-dev
            # branch.upstream origin/feat/v2.3-dev
            # branch.ab +2 -1
            1 M. N... 100644 100644 100644 abc def src/StagedOnly.cs
            1 .M N... 100644 100644 100644 abc def src/ModifiedOnly.cs
            1 MM N... 100644 100644 100644 abc def src/Both.cs
            ? docs/new-note.md
            """;

        var state = GitStatusParser.ParsePorcelainV2(
            @"C:\Projects\pulse",
            porcelain,
            lastCommitLine: "9639de5\u001fAdd Git workspace feature\u001f2 hours ago",
            remoteOriginUrl: "https://github.com/sandrotonal/pulse.git");

        Assert.Equal("pulse", state.DisplayName);
        Assert.Equal("feat/v2.3-dev", state.Branch);
        Assert.Equal("origin/feat/v2.3-dev", state.Upstream);
        Assert.Equal(2, state.AheadCount);
        Assert.Equal(1, state.BehindCount);
        Assert.Equal(2, state.StagedCount);
        Assert.Equal(2, state.ModifiedCount);
        Assert.Equal(1, state.UntrackedCount);
        Assert.Equal(5, state.TotalDirtyCount);
        Assert.False(state.IsClean);
        Assert.Equal("9639de5", state.LastCommitShortSha);
        Assert.Equal("Add Git workspace feature", state.LastCommitSubject);
        Assert.Equal("2 hours ago", state.LastCommitRelativeTime);
        Assert.Equal("sandrotonal/pulse", state.OriginOwnerRepo);
    }

    [Fact]
    public void GitStatusParserParsesDetachedHeadAndSshRemote()
    {
        var porcelain = """
            # branch.oid a1b2c3d4e5f6070809
            # branch.head (detached)
            """;

        var state = GitStatusParser.ParsePorcelainV2(
            @"C:\Projects\pulse\",
            porcelain,
            remoteOriginUrl: "git@github.com:sandrotonal/pulse.git");

        Assert.True(state.IsDetachedHead);
        Assert.Equal("detached@a1b2c3d", state.Branch);
        Assert.True(state.IsClean);
        Assert.Equal("sandrotonal/pulse", state.OriginOwnerRepo);
    }

    [Theory]
    [InlineData("sandrotonal/pulse", "sandrotonal/pulse")]
    [InlineData("https://github.com/sandrotonal/pulse", "sandrotonal/pulse")]
    [InlineData("https://github.com/sandrotonal/pulse.git/", "sandrotonal/pulse")]
    [InlineData("git@github.com:sandrotonal/pulse.git", "sandrotonal/pulse")]
    [InlineData("invalid-single-segment", "")]
    [InlineData("../bad/repo", "")]
    public void NormalizeGitHubSlugHandlesUrlsAndRejectsInvalid(string raw, string expected)
    {
        Assert.Equal(expected, GitStatusParser.NormalizeGitHubSlug(raw));
    }

    [Fact]
    public async Task GitHubWorkspaceServiceFetchesRunsAndPullsThenUsesETag304()
    {
        var timeProvider = new MutableTimeProvider();
        var seenIfNoneMatch = new List<string?>();
        string? seenAuth = null;

        var handler = new StubHttpMessageHandler(req =>
        {
            seenAuth = req.Headers.Authorization?.ToString();
            seenIfNoneMatch.Add(req.Headers.IfNoneMatch.FirstOrDefault()?.Tag);

            if (req.Headers.IfNoneMatch.Count > 0)
            {
                var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);
                notModified.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "4999");
                return notModified;
            }

            if (req.RequestUri!.AbsolutePath.EndsWith("/actions/runs", StringComparison.Ordinal))
            {
                var msg = StubHttpMessageHandler.Json("""
                    {
                      "workflow_runs": [
                        {
                          "id": 42,
                          "name": "CI Build",
                          "display_title": "feat: git panel",
                          "head_branch": "main",
                          "status": "in_progress",
                          "conclusion": "",
                          "html_url": "https://github.com/sandrotonal/pulse/actions/runs/42",
                          "updated_at": "2026-11-28T00:00:00Z"
                        }
                      ]
                    }
                    """);
                msg.Headers.ETag = new EntityTagHeaderValue("\"runs-v1\"");
                msg.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "4999");
                return msg;
            }

            var pullsMsg = StubHttpMessageHandler.Json("""
                [
                  {
                    "number": 19,
                    "title": "Add GitHub live island workspace",
                    "draft": false,
                    "html_url": "https://github.com/sandrotonal/pulse/pull/19",
                    "updated_at": "2026-11-28T00:00:00Z",
                    "user": { "login": "sandrotonal" },
                    "head": { "ref": "feat/git" },
                    "base": { "ref": "main" }
                  }
                ]
                """);
            pullsMsg.Headers.ETag = new EntityTagHeaderValue("\"pulls-v1\"");
            pullsMsg.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "4998");
            return pullsMsg;
        });

        using var http = new HttpClient(handler);
        using var service = new GitHubWorkspaceService(http, () => "secret-pat", timeProvider);

        var first = await service.FetchRepositoryAsync("sandrotonal/pulse");
        Assert.NotNull(first);
        Assert.False(first.NotModified);
        Assert.Equal("Bearer secret-pat", seenAuth);
        Assert.Single(first.WorkflowRuns);
        Assert.True(first.ActiveRun?.IsInProgress);
        Assert.Single(first.OpenPullRequests);
        Assert.Equal(19, first.OpenPullRequests[0].Number);

        var second = await service.FetchRepositoryAsync("sandrotonal/pulse");
        Assert.NotNull(second);
        Assert.True(second.NotModified);
        Assert.Single(second.WorkflowRuns);
        Assert.Contains("\"runs-v1\"", seenIfNoneMatch);
        Assert.Contains("\"pulls-v1\"", seenIfNoneMatch);
    }

    [Fact]
    public async Task GitHubWorkspaceServiceBacksOffOnRateLimit()
    {
        var timeProvider = new MutableTimeProvider();
        var resetEpoch = timeProvider.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        var calls = 0;

        var handler = new StubHttpMessageHandler(_ =>
        {
            calls++;
            var res = new HttpResponseMessage((HttpStatusCode)429);
            res.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
            res.Headers.TryAddWithoutValidation("X-RateLimit-Reset", resetEpoch.ToString());
            return res;
        });

        using var http = new HttpClient(handler);
        using var service = new GitHubWorkspaceService(http, timeProvider: timeProvider);

        var first = await service.FetchRepositoryAsync("sandrotonal/pulse");
        Assert.NotNull(first?.ErrorMessage);
        Assert.Equal(2, calls);

        // Subsequent non-forced poll before reset does not hit the network.
        var second = await service.FetchRepositoryAsync("sandrotonal/pulse");
        Assert.NotNull(second?.ErrorMessage);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void DeveloperActivitySplitModeYieldsToNotificationsAndFileOps()
    {
        Assert.True(IslandPresentationPolicy.ShouldUseSplitMode(
            isCompact: true,
            isSplitDemoForced: false,
            hasTrack: true,
            isFocusTimerActive: false,
            isNotificationActive: false,
            isFileOperationActive: false,
            hasShelfItems: false,
            hasDeveloperActivity: true));

        Assert.False(IslandPresentationPolicy.ShouldUseSplitMode(
            isCompact: true,
            isSplitDemoForced: false,
            hasTrack: true,
            isFocusTimerActive: false,
            isNotificationActive: true,
            isFileOperationActive: false,
            hasShelfItems: false,
            hasDeveloperActivity: true));

        Assert.Equal(252, IslandPresentationPolicy.ComputeCompactTargetWidth(
            IslandState.Compact,
            hasTrack: false,
            isSplit: false,
            isNotificationActive: false,
            isFileOperationActive: false,
            isFocusTimerActive: false,
            isDeveloperActivityActive: true));
    }

    [Fact]
    public void DeveloperActivityEventCoexistsWithMediaPriorityInEventRouter()
    {
        using var router = new EventRouter();
        var track = new MediaTrack
        {
            Title = "Song",
            Artist = "Artist",
            PlaybackStatus = PlaybackStatus.Playing,
            Duration = TimeSpan.FromMinutes(3)
        };
        var media = new MediaEvent("media_1", track, IsPlaying: true, DateTimeOffset.UtcNow);
        var ci = new DeveloperActivityEvent("dev_ci_pulse", DeveloperActivityKind.BuildInProgress, "CI Çalışıyor", "Build", "\uE895", "CI…", "https://github.com/sandrotonal/pulse/actions");

        router.Publish(media);
        Assert.Equal("media_1", router.CurrentActiveEvent?.Id);

        // DeveloperActivityEvent (Progress) temporarily surfaces above persistent media until dismissed,
        // and dismissing it automatically restores persistent ActiveMedia.
        router.Publish(ci);
        Assert.Equal("dev_ci_pulse", router.CurrentActiveEvent?.Id);

        router.Dismiss("dev_ci_pulse");
        Assert.Equal("media_1", router.CurrentActiveEvent?.Id);

        router.Dismiss("media_1");
        Assert.Null(router.CurrentActiveEvent);
    }

    [Fact]
    public void LocalGitWatcherResolvesDirectoryAndWorktreeFileRoots()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PulseTests", Guid.NewGuid().ToString("N"));
        var subFolder = Path.Combine(tempRoot, "src", "nested");
        var actualGitDir = Path.Combine(tempRoot, "bare.git", "worktrees", "wt1");
        var worktreeRoot = Path.Combine(tempRoot, "wt1");

        try
        {
            Directory.CreateDirectory(Path.Combine(tempRoot, ".git"));
            Directory.CreateDirectory(subFolder);
            Directory.CreateDirectory(actualGitDir);
            Directory.CreateDirectory(worktreeRoot);
            File.WriteAllText(Path.Combine(worktreeRoot, ".git"), $"gitdir: {actualGitDir}");

            Assert.True(LocalGitWatcherService.TryResolveGitRoot(subFolder, out var resolvedRoot, out var resolvedGit));
            Assert.Equal(tempRoot, resolvedRoot);
            Assert.Equal(Path.Combine(tempRoot, ".git"), resolvedGit);

            Assert.True(LocalGitWatcherService.TryResolveGitRoot(worktreeRoot, out var wtRoot, out var wtGit));
            Assert.Equal(worktreeRoot, wtRoot);
            Assert.Equal(actualGitDir, wtGit);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GitHubWorkspaceServiceMergePullRequestHandlesTokenPutAndConflict()
    {
        var methodUsed = string.Empty;
        var seenAuth = false;
        string? requestUri = null;
        string? requestBody = null;

        var handler = new Pulse.Tests.StubHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath.EndsWith("/pulls/12/merge", StringComparison.Ordinal))
            {
                methodUsed = req.Method.Method;
                requestUri = req.RequestUri.AbsolutePath;
                seenAuth = string.Equals(req.Headers.Authorization?.Parameter, "test-pat", StringComparison.Ordinal);
                requestBody = req.Content is not null ? req.Content.ReadAsStringAsync().GetAwaiter().GetResult() : null;
                return Pulse.Tests.StubHttpMessageHandler.Json("""{"merged":true,"message":"Pull Request successfully merged","sha":"abc1234"}""");
            }

            if (req.Method == HttpMethod.Put && req.RequestUri!.AbsolutePath.EndsWith("/pulls/13/merge", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.Conflict);
            }

            if (req.RequestUri!.AbsolutePath.EndsWith("/pulls", StringComparison.Ordinal))
            {
                return Pulse.Tests.StubHttpMessageHandler.Json("""[{"number":12,"title":"Add PR merge","draft":false,"html_url":"https://github.com/sandrotonal/pulse/pull/12","updated_at":"2026-11-27T12:00:00Z","user":{"login":"sandro"},"head":{"ref":"feat/merge"},"base":{"ref":"main"}}]""");
            }

            return Pulse.Tests.StubHttpMessageHandler.Json("""{"workflow_runs":[]}""");
        });

        using var http = new HttpClient(handler);
        string? token = null;
        using var gh = new GitHubWorkspaceService(http, () => token);

        Assert.False(gh.HasAuthToken);
        var noTokenResult = await gh.MergePullRequestAsync("sandrotonal/pulse", 12);
        Assert.False(noTokenResult.Success);

        token = "test-pat";
        Assert.True(gh.HasAuthToken);

        var initial = await gh.FetchRepositoryAsync("sandrotonal/pulse");
        Assert.NotNull(initial);
        Assert.Single(initial.OpenPullRequests);

        var merged = await gh.MergePullRequestAsync("sandrotonal/pulse", 12, "squash");
        Assert.True(merged.Success);
        Assert.True(merged.Merged);
        Assert.Equal("PUT", methodUsed);
        Assert.Equal("/repos/sandrotonal/pulse/pulls/12/merge", requestUri);
        Assert.True(seenAuth);
        Assert.Contains("squash", requestBody);

        var conflict = await gh.MergePullRequestAsync("sandrotonal/pulse", 13);
        Assert.False(conflict.Success);
        Assert.False(conflict.Merged);
    }

    [Fact]
    public async Task LocalGitWatcherFetchAndPullRejectNonGitDirectoriesSafely()
    {
        using var watcher = new LocalGitWatcherService();
        var missingPath = Path.Combine(Path.GetTempPath(), "PulseMissing_" + Guid.NewGuid().ToString("N"));

        var fetchRes = await watcher.FetchAsync(missingPath);
        var pullRes = await watcher.PullFastForwardAsync(missingPath);

        Assert.False(fetchRes.Success);
        Assert.False(pullRes.Success);
    }

    [Fact]
    public async Task SettingsServiceDoesNotEmitRedundantSettingsChangedOnNoOpSaveOrLoad()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PulseSettingsDiff_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var service = new Pulse.Services.Settings.SettingsService(tempDir);
            var changedCount = 0;
            service.SettingsChanged += (_, _) => Interlocked.Increment(ref changedCount);

            await service.LoadAsync();
            Assert.Equal(0, changedCount);

            await service.SaveAsync(new AppSettings());
            await service.FlushAsync();
            Assert.Equal(0, changedCount);

            await service.SaveAsync(new AppSettings { EnableDeveloperWorkspace = true });
            await service.FlushAsync();
            Assert.Equal(1, changedCount);

            await service.SaveAsync(new AppSettings { EnableDeveloperWorkspace = true });
            await service.FlushAsync();
            Assert.Equal(1, changedCount);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
