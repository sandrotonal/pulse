using Pulse.Core.Models;

namespace Pulse.Core.Services.Developer;

/// <summary>
/// Pure parser for <c>git status --porcelain=v2 --branch</c>, <c>git log -1</c>,
/// and GitHub repository slug/remote extraction.
/// </summary>
public static class GitStatusParser
{
    public static string NormalizeGitHubSlug(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim().Trim('/');
        if (trimmed.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("http://github.com/", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("ssh://git@github.com/", StringComparison.OrdinalIgnoreCase))
        {
            return ParseGitHubOwnerRepo(trimmed) ?? string.Empty;
        }

        var slashIdx = trimmed.IndexOf('/');
        if (slashIdx <= 0 || slashIdx >= trimmed.Length - 1)
        {
            return string.Empty;
        }

        var owner = trimmed[..slashIdx];
        var repo = trimmed[(slashIdx + 1)..];
        var nextSlash = repo.IndexOf('/');
        if (nextSlash >= 0)
        {
            repo = repo[..nextSlash];
        }

        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            repo = repo[..^4];
        }

        if (!IsValidGitHubSegment(owner) || !IsValidGitHubSegment(repo))
        {
            return string.Empty;
        }

        return $"{owner}/{repo}";
    }

    public static string? ParseGitHubOwnerRepo(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return null;
        }

        var trimmed = remoteUrl.Trim();
        string pathPart;

        if (trimmed.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            pathPart = trimmed["git@github.com:".Length..];
        }
        else if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
                 string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            pathPart = uri.AbsolutePath.TrimStart('/');
        }
        else
        {
            return null;
        }

        if (pathPart.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            pathPart = pathPart[..^4];
        }

        pathPart = pathPart.Trim('/');
        var parts = pathPart.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        var owner = parts[0];
        var repo = parts[1];
        if (!IsValidGitHubSegment(owner) || !IsValidGitHubSegment(repo))
        {
            return null;
        }

        return $"{owner}/{repo}";
    }

    public static GitRepositoryState ParsePorcelainV2(
        string repoPath,
        string porcelainOutput,
        string? lastCommitLine = null,
        string? remoteOriginUrl = null,
        DateTimeOffset? timestampUtc = null)
    {
        var branch = "HEAD";
        string? upstream = null;
        var ahead = 0;
        var behind = 0;
        var staged = 0;
        var modified = 0;
        var untracked = 0;
        var isDetached = false;
        var oidPrefix = string.Empty;

        if (!string.IsNullOrEmpty(porcelainOutput))
        {
            using var reader = new StringReader(porcelainOutput);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith("# branch.oid ", StringComparison.Ordinal))
                {
                    var oid = line["# branch.oid ".Length..].Trim();
                    if (!string.Equals(oid, "(initial)", StringComparison.Ordinal) && oid.Length >= 7)
                    {
                        oidPrefix = oid[..7];
                    }
                }
                else if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
                {
                    var head = line["# branch.head ".Length..].Trim();
                    if (string.Equals(head, "(detached)", StringComparison.Ordinal))
                    {
                        isDetached = true;
                        branch = string.IsNullOrEmpty(oidPrefix) ? "detached" : $"detached@{oidPrefix}";
                    }
                    else if (!string.IsNullOrWhiteSpace(head))
                    {
                        branch = head;
                    }
                }
                else if (line.StartsWith("# branch.upstream ", StringComparison.Ordinal))
                {
                    upstream = line["# branch.upstream ".Length..].Trim();
                }
                else if (line.StartsWith("# branch.ab ", StringComparison.Ordinal))
                {
                    var parts = line["# branch.ab ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        if (part.StartsWith('+') && int.TryParse(part.AsSpan(1), out var a))
                        {
                            ahead = Math.Max(0, a);
                        }
                        else if (part.StartsWith('-') && int.TryParse(part.AsSpan(1), out var b))
                        {
                            behind = Math.Max(0, b);
                        }
                    }
                }
                else if (line.StartsWith("? ", StringComparison.Ordinal))
                {
                    untracked++;
                }
                else if ((line.StartsWith("1 ", StringComparison.Ordinal) ||
                          line.StartsWith("2 ", StringComparison.Ordinal)) &&
                         line.Length >= 4)
                {
                    // Format: "1 XY ..." or "2 XY ..." where X = staged status, Y = worktree status
                    var x = line[2];
                    var y = line[3];
                    if (x != '.')
                    {
                        staged++;
                    }
                    if (y != '.')
                    {
                        modified++;
                    }
                }
                else if (line.StartsWith("u ", StringComparison.Ordinal))
                {
                    modified++;
                    staged++;
                }
            }
        }

        var (commitSha, commitSubject, commitRelative) = ParseLastCommitLine(lastCommitLine, oidPrefix);
        var cleanPath = Path.GetFullPath(repoPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var displayName = Path.GetFileName(cleanPath);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = cleanPath;
        }

        return new GitRepositoryState(
            RepoPath: cleanPath,
            DisplayName: displayName,
            Branch: branch,
            Upstream: upstream,
            AheadCount: ahead,
            BehindCount: behind,
            ModifiedCount: modified,
            StagedCount: staged,
            UntrackedCount: untracked,
            IsDetachedHead: isDetached,
            LastCommitShortSha: commitSha,
            LastCommitSubject: commitSubject,
            LastCommitRelativeTime: commitRelative,
            OriginOwnerRepo: ParseGitHubOwnerRepo(remoteOriginUrl),
            UpdatedAtUtc: timestampUtc ?? DateTimeOffset.UtcNow);
    }

    private static (string Sha, string Subject, string RelativeTime) ParseLastCommitLine(
        string? line,
        string fallbackSha)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return (fallbackSha, "Henüz commit yok", string.Empty);
        }

        var parts = line.TrimEnd('\r', '\n').Split('\x1f');
        var sha = parts.Length > 0 ? parts[0].Trim() : fallbackSha;
        var subject = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        var rel = parts.Length > 2 ? parts[2].Trim() : string.Empty;
        return (sha, subject, rel);
    }

    private static bool IsValidGitHubSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100)
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.'))
            {
                return false;
            }
        }

        return !string.Equals(value, ".", StringComparison.Ordinal) &&
               !string.Equals(value, "..", StringComparison.Ordinal);
    }
}
