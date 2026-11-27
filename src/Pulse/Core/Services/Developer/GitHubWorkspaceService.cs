using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pulse.Core.Interfaces;
using Pulse.Core.Models;

namespace Pulse.Core.Services.Developer;

[JsonSerializable(typeof(GitHubWorkflowRunsResponseDto))]
[JsonSerializable(typeof(List<GitHubPullRequestDto>))]
[JsonSerializable(typeof(GitHubMergeRequestDto))]
[JsonSerializable(typeof(GitHubMergeResponseDto))]
internal sealed partial class GitHubApiJsonContext : JsonSerializerContext
{
}

internal sealed record GitHubMergeRequestDto(
    [property: JsonPropertyName("merge_method")] string MergeMethod);

internal sealed record GitHubMergeResponseDto(
    [property: JsonPropertyName("merged")] bool Merged,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("sha")] string? Sha);

internal sealed record GitHubWorkflowRunsResponseDto(
    [property: JsonPropertyName("workflow_runs")] List<GitHubWorkflowRunDto>? WorkflowRuns);

internal sealed record GitHubWorkflowRunDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("display_title")] string? DisplayTitle,
    [property: JsonPropertyName("head_branch")] string? HeadBranch,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("conclusion")] string? Conclusion,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

internal sealed record GitHubUserDto(
    [property: JsonPropertyName("login")] string? Login);

internal sealed record GitHubRefDto(
    [property: JsonPropertyName("ref")] string? Ref);

internal sealed record GitHubPullRequestDto(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("user")] GitHubUserDto? User,
    [property: JsonPropertyName("head")] GitHubRefDto? Head,
    [property: JsonPropertyName("base")] GitHubRefDto? Base);

/// <summary>
/// ETag-cached GitHub REST API client for Actions workflow runs and open Pull Requests.
/// Uses <see cref="GitHubApiJsonContext"/> for trim-safe deserialization and honors GitHub rate limits.
/// </summary>
public sealed class GitHubWorkspaceService : IGitHubWorkspaceService
{
    private static readonly Uri DefaultApiBase = new("https://api.github.com/");
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(6);

    private sealed class RepoCacheEntry
    {
        public string? RunsETag { get; set; }
        public string? PullsETag { get; set; }
        public IReadOnlyList<GitHubWorkflowRunInfo> Runs { get; set; } = [];
        public IReadOnlyList<GitHubPullRequestInfo> Pulls { get; set; } = [];
        public GitHubRepositorySnapshot? LastSnapshot { get; set; }
        public DateTimeOffset? RateLimitedUntilUtc { get; set; }
    }

    private readonly HttpClient _httpClient;
    private readonly Func<string?>? _tokenProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, RepoCacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _disposed;

    public GitHubWorkspaceService(
        HttpClient httpClient,
        Func<string?>? tokenProvider = null,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _tokenProvider = tokenProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool HasAuthToken => !string.IsNullOrWhiteSpace(_tokenProvider?.Invoke());

    public async Task<GitHubMergeResult> MergePullRequestAsync(
        string ownerRepo,
        int pullNumber,
        string? mergeMethod = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return new GitHubMergeResult(false, false, "Servis kapatıldı.");
        }

        var slug = GitStatusParser.NormalizeGitHubSlug(ownerRepo);
        if (string.IsNullOrEmpty(slug) || pullNumber <= 0)
        {
            return new GitHubMergeResult(false, false, "Geçersiz repo veya PR numarası.");
        }

        var token = _tokenProvider?.Invoke()?.Trim();
        if (string.IsNullOrEmpty(token))
        {
            return new GitHubMergeResult(false, false, "PR merge için GitHub token gerekli.");
        }

        var normalizedMethod = mergeMethod?.ToLowerInvariant() switch
        {
            "squash" => "squash",
            "rebase" => "rebase",
            _ => "merge"
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);

        try
        {
            using var request = CreateRequest($"repos/{slug}/pulls/{pullNumber}/merge", token, etag: null);
            request.Method = HttpMethod.Put;
            var payloadJson = JsonSerializer.Serialize(
                new GitHubMergeRequestDto(normalizedMethod),
                GitHubApiJsonContext.Default.GitHubMergeRequestDto);
            request.Content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync(
                    GitHubApiJsonContext.Default.GitHubMergeResponseDto,
                    timeoutCts.Token).ConfigureAwait(false);

                if (_cache.TryGetValue(slug, out var entry))
                {
                    entry.PullsETag = null;
                    entry.RunsETag = null;
                    entry.Pulls = entry.Pulls.Where(p => p.Number != pullNumber).ToArray();
                    if (entry.LastSnapshot is not null)
                    {
                        entry.LastSnapshot = entry.LastSnapshot with { OpenPullRequests = entry.Pulls };
                    }
                }

                return new GitHubMergeResult(
                    Success: true,
                    Merged: body?.Merged ?? true,
                    Message: $"PR #{pullNumber} birleştirildi.",
                    Sha: body?.Sha);
            }

            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new GitHubMergeResult(false, false, "Token yazma yetkisi yetersiz (repo izni gerekli)."),
                HttpStatusCode.NotFound => new GitHubMergeResult(false, false, "PR bulunamadı."),
                HttpStatusCode.MethodNotAllowed => new GitHubMergeResult(false, false, "PR merge engelli (check veya onay bekliyor)."),
                HttpStatusCode.Conflict => new GitHubMergeResult(false, false, "Merge çakışması veya dal güncel değil."),
                _ => new GitHubMergeResult(false, false, $"GitHub HTTP {(int)response.StatusCode}")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException)
        {
            return new GitHubMergeResult(false, false, "GitHub merge isteği tamamlanamadı.");
        }
    }

    public async Task<GitHubRepositorySnapshot?> FetchRepositoryAsync(
        string ownerRepo,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return null;
        }

        var slug = GitStatusParser.NormalizeGitHubSlug(ownerRepo);
        if (string.IsNullOrEmpty(slug))
        {
            return null;
        }

        var cacheEntry = _cache.GetOrAdd(slug, static _ => new RepoCacheEntry());
        var now = _timeProvider.GetUtcNow();

        if (!force && cacheEntry.RateLimitedUntilUtc is { } limitedUntil && now < limitedUntil)
        {
            return cacheEntry.LastSnapshot is not null
                ? cacheEntry.LastSnapshot with
                {
                    ErrorMessage = "GitHub API hız sınırı aşıldı; token ekleyebilir veya bekleyebilirsiniz."
                }
                : new GitHubRepositorySnapshot(
                    slug,
                    [],
                    [],
                    RateLimitRemaining: 0,
                    now,
                    "GitHub API hız sınırı aşıldı.");
        }

        var token = _tokenProvider?.Invoke()?.Trim();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);

        try
        {
            var runsTask = FetchRunsAsync(slug, cacheEntry, token, force, timeoutCts.Token);
            var pullsTask = FetchPullsAsync(slug, cacheEntry, token, force, timeoutCts.Token);
            await Task.WhenAll(runsTask, pullsTask).ConfigureAwait(false);

            var runsRes = await runsTask.ConfigureAwait(false);
            var pullsRes = await pullsTask.ConfigureAwait(false);

            var error = runsRes.Error ?? pullsRes.Error;
            var remaining = runsRes.RateLimitRemaining ?? pullsRes.RateLimitRemaining;

            if (runsRes.RateLimitedUntilUtc is { } rLimit)
            {
                cacheEntry.RateLimitedUntilUtc = rLimit;
            }
            else if (pullsRes.RateLimitedUntilUtc is { } pLimit)
            {
                cacheEntry.RateLimitedUntilUtc = pLimit;
            }
            else if (error is null)
            {
                cacheEntry.RateLimitedUntilUtc = null;
            }

            if (runsRes.Runs is not null)
            {
                cacheEntry.Runs = runsRes.Runs;
                cacheEntry.RunsETag = runsRes.ETag;
            }

            if (pullsRes.Pulls is not null)
            {
                cacheEntry.Pulls = pullsRes.Pulls;
                cacheEntry.PullsETag = pullsRes.ETag;
            }

            var bothNotModified = error is null && runsRes.NotModified && pullsRes.NotModified;
            var snapshot = new GitHubRepositorySnapshot(
                OwnerRepo: slug,
                WorkflowRuns: cacheEntry.Runs,
                OpenPullRequests: cacheEntry.Pulls,
                RateLimitRemaining: remaining ?? cacheEntry.LastSnapshot?.RateLimitRemaining,
                FetchedAtUtc: now,
                ErrorMessage: error,
                NotModified: bothNotModified);

            cacheEntry.LastSnapshot = snapshot;
            return snapshot;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException)
        {
            return cacheEntry.LastSnapshot is not null
                ? cacheEntry.LastSnapshot with { ErrorMessage = "GitHub bağlantısı zaman aşımına uğradı." }
                : new GitHubRepositorySnapshot(slug, [], [], null, now, "GitHub bağlantısı kurulamadı.");
        }
    }

    private async Task<(IReadOnlyList<GitHubWorkflowRunInfo>? Runs, string? ETag, int? RateLimitRemaining, DateTimeOffset? RateLimitedUntilUtc, bool NotModified, string? Error)> FetchRunsAsync(
        string slug,
        RepoCacheEntry cacheEntry,
        string? token,
        bool force,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest($"repos/{slug}/actions/runs?per_page=5", token, force ? null : cacheEntry.RunsETag);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var remaining = ParseRateLimitRemaining(response);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return (null, cacheEntry.RunsETag, remaining, null, true, null);
        }

        if (IsRateLimited(response))
        {
            return (null, cacheEntry.RunsETag, remaining ?? 0, ParseRateLimitReset(response, _timeProvider.GetUtcNow()), false, "GitHub API hız sınırı aşıldı.");
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound)
        {
            var err = response.StatusCode == HttpStatusCode.Unauthorized
                ? "GitHub token geçersiz."
                : "Repo veya Actions erişilemedi (özel repo ise token gerekli).";
            return (null, cacheEntry.RunsETag, remaining, null, false, err);
        }

        if (!response.IsSuccessStatusCode)
        {
            return (null, cacheEntry.RunsETag, remaining, null, false, $"GitHub HTTP {(int)response.StatusCode}");
        }

        var dto = await response.Content.ReadFromJsonAsync(
            GitHubApiJsonContext.Default.GitHubWorkflowRunsResponseDto,
            cancellationToken).ConfigureAwait(false);
        var list = (dto?.WorkflowRuns ?? [])
            .Select(r => new GitHubWorkflowRunInfo(
                Id: r.Id,
                Name: r.Name ?? "Workflow",
                DisplayTitle: string.IsNullOrWhiteSpace(r.DisplayTitle) ? (r.Name ?? "Run") : r.DisplayTitle!,
                Branch: r.HeadBranch ?? string.Empty,
                Status: r.Status ?? string.Empty,
                Conclusion: r.Conclusion ?? string.Empty,
                HtmlUrl: r.HtmlUrl ?? $"https://github.com/{slug}/actions",
                UpdatedAtUtc: r.UpdatedAt))
            .ToArray();

        return (list, response.Headers.ETag?.Tag, remaining, null, false, null);
    }

    private async Task<(IReadOnlyList<GitHubPullRequestInfo>? Pulls, string? ETag, int? RateLimitRemaining, DateTimeOffset? RateLimitedUntilUtc, bool NotModified, string? Error)> FetchPullsAsync(
        string slug,
        RepoCacheEntry cacheEntry,
        string? token,
        bool force,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest($"repos/{slug}/pulls?state=open&sort=updated&direction=desc&per_page=5", token, force ? null : cacheEntry.PullsETag);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var remaining = ParseRateLimitRemaining(response);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return (null, cacheEntry.PullsETag, remaining, null, true, null);
        }

        if (IsRateLimited(response))
        {
            return (null, cacheEntry.PullsETag, remaining ?? 0, ParseRateLimitReset(response, _timeProvider.GetUtcNow()), false, "GitHub API hız sınırı aşıldı.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return (null, cacheEntry.PullsETag, remaining, null, false,
                response.StatusCode == HttpStatusCode.NotFound ? "Repo bulunamadı (özel repo ise token gerekli)." : $"GitHub HTTP {(int)response.StatusCode}");
        }

        var dtos = await response.Content.ReadFromJsonAsync(
            GitHubApiJsonContext.Default.ListGitHubPullRequestDto,
            cancellationToken).ConfigureAwait(false);
        var list = (dtos ?? [])
            .Select(p => new GitHubPullRequestInfo(
                Number: p.Number,
                Title: p.Title ?? $"PR #{p.Number}",
                Author: p.User?.Login ?? string.Empty,
                HeadRef: p.Head?.Ref ?? string.Empty,
                BaseRef: p.Base?.Ref ?? string.Empty,
                IsDraft: p.Draft,
                HtmlUrl: p.HtmlUrl ?? $"https://github.com/{slug}/pulls",
                UpdatedAtUtc: p.UpdatedAt))
            .ToArray();

        return (list, response.Headers.ETag?.Tag, remaining, null, false, null);
    }

    private static HttpRequestMessage CreateRequest(string relativePath, string? token, string? etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(DefaultApiBase, relativePath));
        request.Headers.UserAgent.ParseAdd("Pulse/1.0 (+https://github.com/sandrotonal/pulse)");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (!string.IsNullOrWhiteSpace(etag) && EntityTagHeaderValue.TryParse(etag, out var parsedEtag))
        {
            request.Headers.IfNoneMatch.Add(parsedEtag);
        }

        return request;
    }

    private static int? ParseRateLimitRemaining(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) &&
            int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool IsRateLimited(HttpResponseMessage response)
    {
        if (response.StatusCode == (HttpStatusCode)429)
        {
            return true;
        }

        return response.StatusCode == HttpStatusCode.Forbidden &&
               ParseRateLimitRemaining(response) == 0;
    }

    private static DateTimeOffset ParseRateLimitReset(HttpResponseMessage response, DateTimeOffset now)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values) &&
            long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
        {
            return DateTimeOffset.FromUnixTimeSeconds(epoch);
        }

        return now.AddMinutes(5);
    }

    public void Dispose()
    {
        _disposed = true;
        _cache.Clear();
    }
}
