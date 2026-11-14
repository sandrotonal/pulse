namespace Pulse.Core.Services;

public sealed record AppActivationPlan(
    string TrimAppId,
    bool IsPackagedOrExplicitAumid,
    bool IsExplicitExecutable,
    bool AllowDirectAumidActivation,
    IReadOnlyList<string> CandidateProcessNames,
    string? ProtocolFallbackUri);

/// <summary>
/// Pure classifier and target resolver for SMTC SourceApplicationId values
/// (bare .exe names, full paths, Win32 AUMIDs, and Microsoft Store MSIX AUMIDs).
/// </summary>
public static class AppActivationTargetResolver
{
    private static readonly Dictionary<string, string> KnownAumidToProcessName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Chrome"] = "chrome",
        ["MSEdge"] = "msedge",
        ["Brave"] = "brave",
        ["Opera"] = "opera",
        ["Vivaldi"] = "vivaldi",
        ["Firefox"] = "firefox",
        ["308046B0AF4A39CB"] = "firefox",
        ["Spotify"] = "Spotify",
        ["vlc"] = "vlc",
        ["foobar2000"] = "foobar2000",
        ["iTunes"] = "iTunes",
        ["MusicBee"] = "MusicBee"
    };

    private static readonly Dictionary<string, string> KnownPackageFamilyToProcessName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SpotifyAB.SpotifyMusic"] = "Spotify",
        ["AppleInc.AppleMusicWin"] = "AppleMusic",
        ["Microsoft.ZuneMusic"] = "Music.UI"
    };

    public static AppActivationPlan? BuildPlan(string? rawAppId)
    {
        if (string.IsNullOrWhiteSpace(rawAppId))
        {
            return null;
        }

        var trimmed = rawAppId.Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            return null;
        }

        var isPackagedOrExplicitAumid = trimmed.Contains('!', StringComparison.Ordinal);
        var lastSlash = trimmed.LastIndexOfAny(['\\', '/']);
        var tail = lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;
        var isExplicitExecutable = tail.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        var processNames = new List<string>(2);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddProcess(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var clean = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name[..^4]
                : name;
            if (!string.IsNullOrWhiteSpace(clean) && seen.Add(clean))
            {
                processNames.Add(clean);
            }
        }

        if (!isPackagedOrExplicitAumid && isExplicitExecutable)
        {
            var stem = Path.GetFileNameWithoutExtension(tail);
            AddProcess(stem);
            if (string.Equals(stem, "SpotifyLauncher", StringComparison.OrdinalIgnoreCase))
            {
                AddProcess("Spotify");
            }
        }
        else if (isPackagedOrExplicitAumid)
        {
            var bangIdx = trimmed.IndexOf('!', StringComparison.Ordinal);
            var packagePart = bangIdx > 0 ? trimmed[..bangIdx] : string.Empty;
            var appPart = bangIdx >= 0 && bangIdx + 1 < trimmed.Length ? trimmed[(bangIdx + 1)..] : string.Empty;

            foreach (var pair in KnownPackageFamilyToProcessName)
            {
                if (packagePart.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase))
                {
                    AddProcess(pair.Value);
                }
            }

            if (!string.IsNullOrWhiteSpace(appPart) &&
                !string.Equals(appPart, "App", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(appPart, "Application", StringComparison.OrdinalIgnoreCase))
            {
                AddProcess(appPart);
            }
        }
        else
        {
            if (KnownAumidToProcessName.TryGetValue(trimmed, out var mapped))
            {
                AddProcess(mapped);
            }
        }

        // Only allow AUMID / shell:AppsFolder lookup when the input isn't a bare .exe filename
        // (exploring "shell:AppsFolder\Spotify.exe" fails silently on Windows).
        var allowDirectAumidActivation = isPackagedOrExplicitAumid || !isExplicitExecutable;

        string? protocolFallback = null;
        if (tail.Equals("Spotify.exe", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Spotify", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("SpotifyAB.SpotifyMusic", StringComparison.OrdinalIgnoreCase))
        {
            protocolFallback = "spotify:";
        }

        return new AppActivationPlan(
            TrimAppId: trimmed,
            IsPackagedOrExplicitAumid: isPackagedOrExplicitAumid,
            IsExplicitExecutable: isExplicitExecutable,
            AllowDirectAumidActivation: allowDirectAumidActivation,
            CandidateProcessNames: processNames,
            ProtocolFallbackUri: protocolFallback);
    }

    public static bool IsWindowsAppsExecutablePath(string? fullPath)
        => !string.IsNullOrWhiteSpace(fullPath) &&
           fullPath.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase);
}

