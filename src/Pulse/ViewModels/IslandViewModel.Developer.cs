using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Pulse.Core.Events;
using Pulse.Core.Interfaces;
using Pulse.Core.Models;
using Pulse.Core.Services;
using Pulse.Core.Services.Developer;
using Pulse.Services.Glass;

namespace Pulse.ViewModels;

public sealed record DeveloperRepoSelectionItem(
    string DisplayName,
    string? LocalPath,
    string? GitHubOwnerRepo);

public sealed class DevPullRequestItemViewModel
{
    public DevPullRequestItemViewModel(
        GitHubPullRequestInfo model,
        bool isArmedForMerge,
        bool hasAuthToken)
    {
        Number = model.Number;
        NumberLabel = $"#{model.Number}";
        Title = model.Title;
        HeadRef = model.HeadRef;
        BaseRef = model.BaseRef;
        RouteLabel = (!string.IsNullOrWhiteSpace(model.HeadRef) && !string.IsNullOrWhiteSpace(model.BaseRef))
            ? $"{model.HeadRef} → {model.BaseRef}"
            : model.HeadRef;
        MetaLabel = string.IsNullOrWhiteSpace(model.Author)
            ? RouteLabel
            : $"{model.Author} · {RouteLabel}";
        HtmlUrl = model.HtmlUrl;
        IsDraft = model.IsDraft;
        BadgeText = model.IsDraft ? "Taslak" : "Açık";
        IsArmedForMerge = isArmedForMerge && !model.IsDraft;
        MergeButtonVisibility = !model.IsDraft ? Visibility.Visible : Visibility.Collapsed;
        MergeActionLabel = IsArmedForMerge ? "Onayla?" : "Merge";
        MergeButtonBackground = IsArmedForMerge
            ? ArmedMergeBgBrush
            : IdleMergeBgBrush;
        MergeButtonBorderBrush = IsArmedForMerge
            ? ArmedMergeBorderBrush
            : IdleMergeBorderBrush;
        MergeToolTip = IsArmedForMerge
            ? $"PR #{model.Number} birleştirmeyi onaylamak için tekrar tıklayın"
            : (hasAuthToken
                ? $"PR #{model.Number} GitHub üzerinden birleştir"
                : $"PR #{model.Number} merge sayfasını GitHub'da aç");
    }

    private static readonly SolidColorBrush IdleMergeBgBrush =
        new(Microsoft.UI.ColorHelper.FromArgb(28, 255, 255, 255));
    private static readonly SolidColorBrush ArmedMergeBgBrush =
        new(Microsoft.UI.ColorHelper.FromArgb(72, 48, 209, 88));
    private static readonly SolidColorBrush IdleMergeBorderBrush =
        new(Microsoft.UI.ColorHelper.FromArgb(40, 255, 255, 255));
    private static readonly SolidColorBrush ArmedMergeBorderBrush =
        new(Microsoft.UI.ColorHelper.FromArgb(220, 48, 209, 88));

    public int Number { get; }
    public string NumberLabel { get; }
    public string Title { get; }
    public string HeadRef { get; }
    public string BaseRef { get; }
    public string RouteLabel { get; }
    public string MetaLabel { get; }
    public string HtmlUrl { get; }
    public bool IsDraft { get; }
    public string BadgeText { get; }
    public bool IsArmedForMerge { get; }
    public Visibility MergeButtonVisibility { get; }
    public string MergeActionLabel { get; }
    public SolidColorBrush MergeButtonBackground { get; }
    public SolidColorBrush MergeButtonBorderBrush { get; }
    public string MergeToolTip { get; }
}

public sealed partial class IslandViewModel
{
    private static readonly SolidColorBrush CiSuccessDarkBrush = MakeBrush(0xFF30D158u);
    private static readonly SolidColorBrush CiSuccessLightBrush = MakeBrush(0xFF1E7E34u);
    private static readonly SolidColorBrush CiFailDarkBrush = MakeBrush(0xFFFF453Au);
    private static readonly SolidColorBrush CiFailLightBrush = MakeBrush(0xFFD70015u);
    private static readonly SolidColorBrush CiRunningDarkBrush = MakeBrush(0xFFFFD60Au);
    private static readonly SolidColorBrush CiRunningLightBrush = MakeBrush(0xFFB86E00u);
    private static readonly SolidColorBrush CiNeutralDarkBrush = MakeBrush(0xFFA4ABB6u);
    private static readonly SolidColorBrush CiNeutralLightBrush = MakeBrush(0xFF5A5A60u);

    private ILocalGitWatcherService? _localGitWatcher;
    private IGitHubWorkspaceService? _gitHubWorkspace;
    private DispatcherQueueTimer? _devPollTimer;
    private CancellationTokenSource? _devPollCts;
    private readonly List<DeveloperRepoSelectionItem> _devRepos = [];
    private readonly Dictionary<string, GitRepositoryState> _gitStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GitHubRepositorySnapshot> _ghSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (HashSet<long> SeededRunIds, Dictionary<long, string> LastStatuses, HashSet<int> SeenPrs, bool IsSeeded)> _ghAlertState = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeCiBuildSlugs = new(StringComparer.OrdinalIgnoreCase);
    private int _selectedDevRepoIndex;
    private bool _devRefreshInFlight;
    private bool _isDeveloperWorkspaceEnabled;
    private string? _lastDevSettingsSignature;
    private string? _devFeedbackMessage;
    private DateTimeOffset _devFeedbackExpiresUtc;
    private int? _armedMergePrNumber;
    private DateTimeOffset _armedMergeExpiresUtc;
    private bool _isDevGuideOpen;

    public ObservableCollection<DevPullRequestItemViewModel> DevPullRequests { get; } = new();

    public ICommand? RefreshDeveloperCommand { get; private set; }
    public ICommand? ToggleDevGuideCommand { get; private set; }
    public ICommand? NextDevRepoCommand { get; private set; }
    public ICommand? PreviousDevRepoCommand { get; private set; }
    public ICommand? OpenDevRepoFolderCommand { get; private set; }
    public ICommand? OpenDevRepoVsCodeCommand { get; private set; }
    public ICommand? OpenDevRepoTerminalCommand { get; private set; }
    public ICommand? OpenDevGitHubCommand { get; private set; }
    public ICommand? OpenDevActionsCommand { get; private set; }
    public ICommand? GitSyncCommand { get; private set; }
    public ICommand? CopyDevCommitShaCommand { get; private set; }
    public ICommand? CreatePullRequestCommand { get; private set; }

    public bool IsDeveloperWorkspaceEnabled => _isDeveloperWorkspaceEnabled;
    public Visibility DeveloperWorkspaceEnabledVisibility => _isDeveloperWorkspaceEnabled ? Visibility.Visible : Visibility.Collapsed;

    private DeveloperRepoSelectionItem? CurrentDevRepo =>
        _selectedDevRepoIndex >= 0 && _selectedDevRepoIndex < _devRepos.Count
            ? _devRepos[_selectedDevRepoIndex]
            : null;

    private GitRepositoryState? CurrentGitState =>
        CurrentDevRepo?.LocalPath is { } path && _gitStates.TryGetValue(path, out var state)
            ? state
            : null;

    private GitHubRepositorySnapshot? CurrentGitHubSnapshot
    {
        get
        {
            var slug = CurrentDevRepo?.GitHubOwnerRepo ?? CurrentGitState?.OriginOwnerRepo;
            return !string.IsNullOrWhiteSpace(slug) && _ghSnapshots.TryGetValue(slug, out var snap) ? snap : null;
        }
    }

    public bool HasDevRepositories => _devRepos.Count > 0;
    public bool HasMultipleDevRepositories => _devRepos.Count > 1;
    public bool IsDevGuideOpen => _isDevGuideOpen;
    public Visibility DevGuideOverlayVisibility => _isDevGuideOpen ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DevRepoEmptyVisibility => !_isDevGuideOpen && !HasDevRepositories ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DevRepoPopulatedVisibility => !_isDevGuideOpen && HasDevRepositories ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DevRepoSwitcherVisibility => _devRepos.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

    public string DevRepoHeaderTitle => CurrentDevRepo?.DisplayName ?? "Git & GitHub";
    public string DevRepoIndexLabel => _devRepos.Count > 1 ? $"{_selectedDevRepoIndex + 1}/{_devRepos.Count}" : string.Empty;
    public string DevBranchLabel => CurrentGitState?.Branch ?? CurrentDevRepo?.GitHubOwnerRepo ?? "Bağlı repo yok";
    public bool HasCurrentLocalRepo => CurrentDevRepo?.LocalPath is not null;
    public Visibility DevLocalActionsVisibility => HasCurrentLocalRepo ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DevGitHubActionsVisibility => !string.IsNullOrWhiteSpace(ResolveCurrentGitHubSlug()) ? Visibility.Visible : Visibility.Collapsed;

    public string DevDirtySummary
    {
        get
        {
            if (CurrentGitState is not { } state)
            {
                return CurrentGitHubSnapshot is { } snap
                    ? $"{snap.OpenPullRequests.Count} açık PR"
                    : "GitHub takip";
            }

            var parts = new List<string>(3);
            parts.Add(state.IsClean ? "Temiz" : $"{state.TotalDirtyCount} değişiklik");
            if (state.AheadCount > 0) parts.Add($"↑{state.AheadCount}");
            if (state.BehindCount > 0) parts.Add($"↓{state.BehindCount}");
            return string.Join(" · ", parts);
        }
    }

    public string DevLastCommitSummary
    {
        get
        {
            if (CurrentGitState is not { } state)
            {
                return CurrentGitHubSnapshot?.ErrorMessage ?? "Yerel klasör seçili değil";
            }

            if (string.IsNullOrWhiteSpace(state.LastCommitShortSha))
            {
                return state.LastCommitSubject;
            }

            var rel = string.IsNullOrWhiteSpace(state.LastCommitRelativeTime)
                ? string.Empty
                : $" ({state.LastCommitRelativeTime})";
            return $"{state.LastCommitShortSha} · {state.LastCommitSubject}{rel}";
        }
    }

    public string DevCommitSha => CurrentGitState?.LastCommitShortSha ?? string.Empty;

    public Visibility DevCommitShaVisibility =>
        !string.IsNullOrWhiteSpace(DevCommitSha) ? Visibility.Visible : Visibility.Collapsed;

    public string DevCommitSubject
    {
        get
        {
            if (CurrentGitState is not { } state)
            {
                return CurrentGitHubSnapshot?.ErrorMessage ?? "Yerel klasör seçili değil";
            }

            var rel = string.IsNullOrWhiteSpace(state.LastCommitRelativeTime)
                ? string.Empty
                : $" · {state.LastCommitRelativeTime}";
            return $"{state.LastCommitSubject}{rel}";
        }
    }

    public string DevSyncButtonLabel =>
        CurrentGitState is { IsClean: true, BehindCount: > 0 }
            ? $"Pull ↓{CurrentGitState.BehindCount}"
            : "Fetch";

    public string DevSyncToolTip =>
        CurrentGitState is { IsClean: true, BehindCount: > 0 }
            ? "Uzak değişiklikleri fast-forward çek (git pull --ff-only)"
            : "Uzak dalları ve commit durumunu yenile (git fetch)";

    public Visibility DevCanCreatePrVisibility
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ResolveCurrentGitHubSlug()) || CurrentGitState is not { } state)
            {
                return Visibility.Collapsed;
            }

            if (state.IsDetachedHead ||
                string.IsNullOrWhiteSpace(state.Branch) ||
                string.Equals(state.Branch, "main", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state.Branch, "master", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state.Branch, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                return Visibility.Collapsed;
            }

            var hasOpenPrForBranch = CurrentGitHubSnapshot?.OpenPullRequests.Any(pr =>
                string.Equals(pr.HeadRef, state.Branch, StringComparison.OrdinalIgnoreCase)) == true;
            return hasOpenPrForBranch ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public string DevCreatePrToolTip =>
        CurrentGitState is { Branch: { Length: > 0 } b }
            ? $"{b} dalından GitHub Pull Request aç (Compare)"
            : "Pull Request aç";

    public string DevCiStatusSummary
    {
        get
        {
            var snap = CurrentGitHubSnapshot;
            if (snap is null)
            {
                return string.IsNullOrWhiteSpace(ResolveCurrentGitHubSlug())
                    ? "GitHub bağlantısı yok"
                    : "GitHub verisi bekleniyor…";
            }

            if (snap.ActiveRun is { } active)
            {
                return $"CI çalışıyor: {active.Name} ({active.Branch})";
            }

            if (snap.LatestRun is { } latest)
            {
                var statusWord = latest.IsSuccess ? "Başarılı" : (latest.IsFailure ? "Başarısız" : latest.Conclusion);
                return $"{latest.Name}: {statusWord}";
            }

            return snap.ErrorMessage ?? "Workflow çalıştırması yok";
        }
    }

    public string DevCiStatusGlyph
    {
        get
        {
            var latest = CurrentGitHubSnapshot?.LatestRun;
            if (latest is null) return "\uE943";
            if (latest.IsInProgress) return "\uE895";
            if (latest.IsSuccess) return "\uE73E";
            if (latest.IsFailure) return "\uEA39";
            return "\uE946";
        }
    }

    public SolidColorBrush DevCiStatusBrush
    {
        get
        {
            var isLight = DesktopFrostService.ResolveIsLightTheme(_media.Theme);
            var latest = CurrentGitHubSnapshot?.LatestRun;
            if (latest is null) return isLight ? CiNeutralLightBrush : CiNeutralDarkBrush;
            if (latest.IsInProgress) return isLight ? CiRunningLightBrush : CiRunningDarkBrush;
            if (latest.IsSuccess) return isLight ? CiSuccessLightBrush : CiSuccessDarkBrush;
            if (latest.IsFailure) return isLight ? CiFailLightBrush : CiFailDarkBrush;
            return isLight ? CiNeutralLightBrush : CiNeutralDarkBrush;
        }
    }

    public bool HasDevPullRequests => DevPullRequests.Count > 0;
    public Visibility DevPullRequestsVisibility => HasDevPullRequests ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DevNoPullRequestsVisibility => !HasDevPullRequests && HasDevRepositories ? Visibility.Visible : Visibility.Collapsed;
    public string DevPullRequestHeaderLabel => $"Açık PR ({DevPullRequests.Count})";

    public string DevStatusNote
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_devFeedbackMessage) && DateTimeOffset.UtcNow < _devFeedbackExpiresUtc)
            {
                return _devFeedbackMessage;
            }

            if (CurrentGitHubSnapshot?.ErrorMessage is { Length: > 0 } err)
            {
                return err;
            }

            if (CurrentGitHubSnapshot is { RateLimitRemaining: { } rem })
            {
                return rem <= 10
                    ? $"API limiti düşük ({rem} kaldı)"
                    : $"Son yenileme: {CurrentGitHubSnapshot.FetchedAtUtc.ToLocalTime():HH:mm}";
            }

            return _devRefreshInFlight ? "Yenileniyor…" : string.Empty;
        }
    }

    private bool ShowDeveloperLiveActivitySetting => _settingsService?.Current.ShowDeveloperLiveActivity ?? true;

    public bool HasDeveloperLiveSplitBadge =>
        _isDeveloperWorkspaceEnabled &&
        ShowDeveloperLiveActivitySetting &&
        (_activeCiBuildSlugs.Count > 0 || (CurrentGitHubSnapshot?.OpenPullRequests.Count > 0));

    public string DeveloperSplitGlyph => _activeCiBuildSlugs.Count > 0 ? "\uE895" : "\uE943";

    public string DeveloperSplitBadgeText
    {
        get
        {
            if (_activeCiBuildSlugs.Count > 0)
            {
                return "CI…";
            }

            var prCount = CurrentGitHubSnapshot?.OpenPullRequests.Count ?? 0;
            if (prCount > 0)
            {
                return $"{prCount}PR";
            }

            return CurrentGitState is { IsClean: false } dirty
                ? $"Δ{dirty.TotalDirtyCount}"
                : "Git";
        }
    }

    public string DeveloperSplitToolTip => _activeCiBuildSlugs.Count > 0
        ? "GitHub Actions çalışıyor"
        : $"Git & GitHub ({DevRepoHeaderTitle})";

    private void InitializeDeveloperWorkspace(
        ILocalGitWatcherService? localGitWatcher,
        IGitHubWorkspaceService? gitHubWorkspace)
    {
        _localGitWatcher = localGitWatcher;
        _gitHubWorkspace = gitHubWorkspace;

        RefreshDeveloperCommand = new AsyncCommand(ForceRefreshDeveloperAsync);
        ToggleDevGuideCommand = new RelayCommand(ToggleDevGuide);
        NextDevRepoCommand = new RelayCommand(() => SwitchDevRepo(1));
        PreviousDevRepoCommand = new RelayCommand(() => SwitchDevRepo(-1));
        OpenDevRepoFolderCommand = new RelayCommand(() =>
        {
            if (CurrentDevRepo?.LocalPath is { } path)
            {
                _ = _systemControl?.TryOpenPath(path);
            }
        });
        OpenDevRepoVsCodeCommand = new RelayCommand(() =>
        {
            if (CurrentDevRepo?.LocalPath is { } path)
            {
                _ = _systemControl?.TryOpenRepositoryInEditor(path);
            }
        });
        OpenDevRepoTerminalCommand = new RelayCommand(() =>
        {
            if (CurrentDevRepo?.LocalPath is { } path)
            {
                _ = _systemControl?.TryOpenRepositoryInTerminal(path);
            }
        });
        OpenDevGitHubCommand = new RelayCommand(() =>
        {
            if (ResolveCurrentGitHubSlug() is { Length: > 0 } slug)
            {
                _ = _systemControl?.TryOpenUri($"https://github.com/{slug}");
            }
        });
        OpenDevActionsCommand = new RelayCommand(() =>
        {
            var url = CurrentGitHubSnapshot?.LatestRun?.HtmlUrl;
            if (!string.IsNullOrWhiteSpace(url))
            {
                _ = _systemControl?.TryOpenUri(url);
                return;
            }

            if (ResolveCurrentGitHubSlug() is { Length: > 0 } slug)
            {
                _ = _systemControl?.TryOpenUri($"https://github.com/{slug}/actions");
            }
        });
        GitSyncCommand = new AsyncCommand(SyncCurrentLocalRepoAsync);
        CopyDevCommitShaCommand = new RelayCommand(CopyCurrentCommitSha);
        CreatePullRequestCommand = new RelayCommand(OpenCreatePullRequestPage);

        if (_localGitWatcher is not null)
        {
            _localGitWatcher.RepositoryUpdated += OnLocalGitRepositoryUpdated;
            _localGitWatcher.ActivityDetected += OnLocalGitActivityDetected;
        }

        if (_dispatcherQueue is not null)
        {
            _devPollTimer = _dispatcherQueue.CreateTimer();
            _devPollTimer.IsRepeating = true;
            _devPollTimer.Tick += OnDevPollTimerTick;
        }

        if (_settingsService is not null)
        {
            OnDeveloperSettingsChanged(_settingsService.Current);
        }
    }

    public void OpenDeveloperUrl(string? url)
    {
        if (!_disposed && !string.IsNullOrWhiteSpace(url))
        {
            _ = _systemControl?.TryOpenUri(url);
        }
    }

    public async Task HandlePullRequestMergeClickAsync(int prNumber)
    {
        if (_disposed || prNumber <= 0)
        {
            return;
        }

        var pr = DevPullRequests.FirstOrDefault(p => p.Number == prNumber);
        if (pr is null || pr.IsDraft)
        {
            if (pr is not null)
            {
                OpenDeveloperUrl(pr.HtmlUrl);
            }
            return;
        }

        var slug = ResolveCurrentGitHubSlug();
        if (string.IsNullOrWhiteSpace(slug) || _gitHubWorkspace is null)
        {
            return;
        }

        if (!_gitHubWorkspace.HasAuthToken)
        {
            OpenDeveloperUrl(pr.HtmlUrl);
            ShowDevFeedback("Merge için PR GitHub'da açıldı (token eklenirse doğrudan merge yapılır)", 4.2);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (_armedMergePrNumber != prNumber || now >= _armedMergeExpiresUtc)
        {
            _armedMergePrNumber = prNumber;
            _armedMergeExpiresUtc = now.AddSeconds(4.5);
            ShowDevFeedback($"#{prNumber} birleştirilsin mi? Onay için tekrar Merge'e bas", 4.5);
            NotifyDeveloperPropertiesChanged();

            _ = Task.Delay(4600).ContinueWith(_ =>
            {
                ApplyOnUi(() =>
                {
                    if (!_disposed && _armedMergePrNumber == prNumber && DateTimeOffset.UtcNow >= _armedMergeExpiresUtc)
                    {
                        _armedMergePrNumber = null;
                        NotifyDeveloperPropertiesChanged();
                    }
                });
            }, TaskScheduler.Default);
            return;
        }

        _armedMergePrNumber = null;
        ShowDevFeedback($"PR #{prNumber} merge ediliyor…", 4.0);
        NotifyDeveloperPropertiesChanged();

        var res = await _gitHubWorkspace.MergePullRequestAsync(slug, prNumber);
        ApplyOnUi(() =>
        {
            if (_disposed)
            {
                return;
            }

            ShowDevFeedback(res.Message, 4.5);
            if (res.Success && _settingsService?.Current.NotifyOnPullRequests == true && !_settingsService.Current.DoNotDisturb)
            {
                _eventRouter.Publish(new SystemNotificationEvent(
                    Id: $"dev_pr_merged_{slug}_{prNumber}",
                    Category: EventCategory.Developer,
                    Title: $"PR #{prNumber} Merge Edildi",
                    Subtitle: $"{slug} · {pr.RouteLabel}",
                    IconGlyph: "\uE73E",
                    Priority: EventPriority.Informational,
                    DisplayDuration: TimeSpan.FromSeconds(4.0),
                    LaunchTarget: pr.HtmlUrl));
            }
            NotifyDeveloperPropertiesChanged();
        });

        if (res.Success)
        {
            if (CurrentDevRepo?.LocalPath is { } localPath && _localGitWatcher is not null)
            {
                _ = await _localGitWatcher.FetchAsync(localPath);
            }
            await RefreshGitHubRepositoriesAsync(force: true);
        }
    }

    private void CopyCurrentCommitSha()
    {
        var sha = DevCommitSha;
        if (string.IsNullOrWhiteSpace(sha))
        {
            return;
        }

        if (Services.System.ShelfShellService.CopyTextToClipboard(sha))
        {
            ShowDevFeedback($"Commit SHA kopyalandı: {sha}", 3.2);
            NotifyDeveloperPropertiesChanged();
        }
    }

    private void OpenCreatePullRequestPage()
    {
        var slug = ResolveCurrentGitHubSlug();
        var branch = CurrentGitState?.Branch;
        if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(branch))
        {
            return;
        }

        var compareUrl = $"https://github.com/{slug}/compare/{Uri.EscapeDataString(branch)}?expand=1";
        OpenDeveloperUrl(compareUrl);
    }

    private async Task SyncCurrentLocalRepoAsync()
    {
        if (_disposed || _localGitWatcher is null || CurrentDevRepo?.LocalPath is not { } path)
        {
            return;
        }

        var shouldPull = CurrentGitState is { IsClean: true, BehindCount: > 0 };
        ShowDevFeedback(shouldPull ? "Pull yapılıyor…" : "Remote kontrol ediliyor (fetch)…", 4.0);
        ApplyOnUi(() => OnPropertyChanged(nameof(DevStatusNote)));

        var result = shouldPull
            ? await _localGitWatcher.PullFastForwardAsync(path).ConfigureAwait(false)
            : await _localGitWatcher.FetchAsync(path).ConfigureAwait(false);

        ApplyOnUi(() =>
        {
            if (!_disposed)
            {
                ShowDevFeedback(result.Message, 3.8);
                NotifyDeveloperPropertiesChanged();
            }
        });
    }

    private void ToggleDevGuide()
    {
        _isDevGuideOpen = !_isDevGuideOpen;
        OnPropertyChanged(nameof(IsDevGuideOpen));
        OnPropertyChanged(nameof(DevGuideOverlayVisibility));
        OnPropertyChanged(nameof(DevRepoEmptyVisibility));
        OnPropertyChanged(nameof(DevRepoPopulatedVisibility));
    }

    private void ShowDevFeedback(string message, double seconds)
    {
        _devFeedbackMessage = message;
        _devFeedbackExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(seconds);
    }

    private void OnDeveloperPanelOpened()
    {
        if (!_isDeveloperWorkspaceEnabled || _disposed)
        {
            return;
        }

        if (_localGitWatcher is not null)
        {
            _ = _localGitWatcher.RefreshAllAsync();
        }

        if (CurrentGitHubSnapshot is null && ResolveCurrentGitHubSlug() is { Length: > 0 })
        {
            _ = RefreshGitHubRepositoriesAsync(force: false);
        }
    }

    private void SwitchDevRepo(int delta)
    {
        if (_devRepos.Count <= 1)
        {
            return;
        }

        _selectedDevRepoIndex = (_selectedDevRepoIndex + delta + _devRepos.Count) % _devRepos.Count;
        NotifyDeveloperPropertiesChanged();
        if (_settingsService is not null)
        {
            _ = _settingsService.SaveAsync(_settingsService.Current with { DeveloperSelectedRepoIndex = _selectedDevRepoIndex });
        }
    }

    private void OnDeveloperSettingsChanged(AppSettings settings)
    {
        if (_disposed)
        {
            return;
        }

        var signature = BuildDevSettingsSignature(settings);
        if (string.Equals(_lastDevSettingsSignature, signature, StringComparison.Ordinal))
        {
            return;
        }
        _lastDevSettingsSignature = signature;

        var wasEnabled = _isDeveloperWorkspaceEnabled;
        _isDeveloperWorkspaceEnabled = settings.EnableDeveloperWorkspace;

        RebuildDevRepositoryList(settings);

        if (!_isDeveloperWorkspaceEnabled)
        {
            _devPollTimer?.Stop();
            CancelDevPollRequest();
            _localGitWatcher?.UpdateWatchedRepositories([]);
            foreach (var slug in _activeCiBuildSlugs.ToList())
            {
                _eventRouter.Dismiss($"dev_ci_{slug}");
            }
            _activeCiBuildSlugs.Clear();

            if (_selectedPageIndex >= VisiblePageCount)
            {
                SelectedPageIndex = 0;
            }
        }
        else
        {
            _localGitWatcher?.UpdateWatchedRepositories(settings.DeveloperLocalRepositories);
            RestartDevPollTimer(settings.DeveloperPollIntervalSeconds);
            if (!wasEnabled || !_isSessionLocked)
            {
                _ = RefreshGitHubRepositoriesAsync(force: false);
            }
        }

        if (wasEnabled != _isDeveloperWorkspaceEnabled)
        {
            OnPropertyChanged(nameof(IsDeveloperWorkspaceEnabled));
            OnPropertyChanged(nameof(DeveloperWorkspaceEnabledVisibility));
            OnPropertyChanged(nameof(VisiblePageCount));
            OnPropertyChanged(nameof(DevPageVisibility));
        }

        NotifyDeveloperPropertiesChanged();
        NotifySplitStateChanged();
    }

    private static string BuildDevSettingsSignature(AppSettings settings) =>
        $"{settings.EnableDeveloperWorkspace}|{settings.DeveloperPollIntervalSeconds}|{settings.DeveloperSelectedRepoIndex}|{settings.ShowDeveloperLiveActivity}|{string.Join(';', settings.DeveloperLocalRepositories)}|{string.Join(';', settings.DeveloperGitHubRepositories)}";

    private void RebuildDevRepositoryList(AppSettings settings)
    {
        _devRepos.Clear();
        var seenGitHubSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawPath in settings.DeveloperLocalRepositories)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                continue;
            }

            var cleanPath = Path.GetFullPath(rawPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(cleanPath);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = cleanPath;
            }

            _gitStates.TryGetValue(cleanPath, out var existingGit);
            var slug = existingGit?.OriginOwnerRepo;
            if (!string.IsNullOrWhiteSpace(slug))
            {
                seenGitHubSlugs.Add(slug);
            }

            _devRepos.Add(new DeveloperRepoSelectionItem(name, cleanPath, slug));
        }

        foreach (var rawSlug in settings.DeveloperGitHubRepositories)
        {
            var slug = GitStatusParser.NormalizeGitHubSlug(rawSlug);
            if (string.IsNullOrEmpty(slug) || !seenGitHubSlugs.Add(slug))
            {
                continue;
            }

            var slash = slug.IndexOf('/');
            var display = slash >= 0 ? slug[(slash + 1)..] : slug;
            _devRepos.Add(new DeveloperRepoSelectionItem(display, null, slug));
        }

        _selectedDevRepoIndex = _devRepos.Count == 0
            ? 0
            : Math.Clamp(settings.DeveloperSelectedRepoIndex, 0, _devRepos.Count - 1);
    }

    private string? ResolveCurrentGitHubSlug()
    {
        if (CurrentDevRepo?.GitHubOwnerRepo is { Length: > 0 } slug)
        {
            return slug;
        }

        return CurrentGitState?.OriginOwnerRepo;
    }

    private void RestartDevPollTimer(int pollIntervalSeconds)
    {
        if (_devPollTimer is null || !_isDeveloperWorkspaceEnabled || _isSessionLocked)
        {
            return;
        }

        var clamped = Math.Clamp(pollIntervalSeconds, 30, 600);
        _devPollTimer.Interval = TimeSpan.FromSeconds(clamped);
        if (!_devPollTimer.IsRunning)
        {
            _devPollTimer.Start();
        }
    }

    private void SetDeveloperSessionLocked(bool isLocked)
    {
        if (isLocked)
        {
            _devPollTimer?.Stop();
            CancelDevPollRequest();
        }
        else if (_isDeveloperWorkspaceEnabled && _settingsService is not null)
        {
            RestartDevPollTimer(_settingsService.Current.DeveloperPollIntervalSeconds);
            _ = ForceRefreshDeveloperAsync();
        }
    }

    private void OnDevPollTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (!_disposed && _isDeveloperWorkspaceEnabled && !_isSessionLocked)
        {
            _ = RefreshGitHubRepositoriesAsync(force: false);
        }
    }

    private async Task ForceRefreshDeveloperAsync()
    {
        if (_disposed || !_isDeveloperWorkspaceEnabled)
        {
            return;
        }

        if (_localGitWatcher is not null)
        {
            await _localGitWatcher.RefreshAllAsync().ConfigureAwait(false);
        }

        await RefreshGitHubRepositoriesAsync(force: true).ConfigureAwait(false);
    }

    private void OnLocalGitRepositoryUpdated(object? sender, GitRepositoryState state)
    {
        ApplyOnUi(() =>
        {
            if (_disposed)
            {
                return;
            }

            _gitStates[state.RepoPath] = state;
            if (!string.IsNullOrWhiteSpace(state.OriginOwnerRepo))
            {
                var updated = false;
                for (int i = 0; i < _devRepos.Count; i++)
                {
                    if (string.Equals(_devRepos[i].LocalPath, state.RepoPath, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(_devRepos[i].GitHubOwnerRepo, state.OriginOwnerRepo, StringComparison.OrdinalIgnoreCase))
                    {
                        _devRepos[i] = _devRepos[i] with { GitHubOwnerRepo = state.OriginOwnerRepo };
                        updated = true;
                    }
                }

                if (updated && _isDeveloperWorkspaceEnabled && !_ghSnapshots.ContainsKey(state.OriginOwnerRepo))
                {
                    _ = RefreshGitHubRepositoriesAsync(force: false);
                }
            }

            NotifyDeveloperPropertiesChanged();
        });
    }

    private void OnLocalGitActivityDetected(object? sender, GitCommitTransition transition)
    {
        if (_disposed || !_isDeveloperWorkspaceEnabled || _settingsService is null)
        {
            return;
        }

        if (!_settingsService.Current.NotifyOnGitCommits || _settingsService.Current.DoNotDisturb)
        {
            return;
        }

        var title = transition.IsBranchChange
            ? $"{transition.DisplayName} · {transition.Branch}"
            : $"Yeni Commit · {transition.DisplayName}";
        var subtitle = transition.IsBranchChange
            ? $"Branch değişti ({transition.ShortSha})"
            : $"{transition.ShortSha} — {transition.Subject}";

        _eventRouter.Publish(new SystemNotificationEvent(
            Id: $"dev_git_{transition.DisplayName}",
            Category: EventCategory.Developer,
            Title: title,
            Subtitle: subtitle,
            IconGlyph: "\uE943",
            Priority: EventPriority.Informational,
            DisplayDuration: TimeSpan.FromSeconds(3.6),
            LaunchTarget: transition.RepoPath));
    }

    private async Task RefreshGitHubRepositoriesAsync(bool force)
    {
        if (_disposed || !_isDeveloperWorkspaceEnabled || _gitHubWorkspace is null || _devRefreshInFlight)
        {
            return;
        }

        var slugs = CollectAllTrackedGitHubSlugs();
        if (slugs.Count == 0)
        {
            return;
        }

        _devRefreshInFlight = true;
        ApplyOnUi(() => OnPropertyChanged(nameof(DevStatusNote)));

        CancelDevPollRequest();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        _devPollCts = cts;

        try
        {
            foreach (var slug in slugs)
            {
                if (_disposed || cts.IsCancellationRequested)
                {
                    break;
                }

                var snapshot = await _gitHubWorkspace.FetchRepositoryAsync(slug, force, cts.Token).ConfigureAwait(false);
                if (snapshot is not null && !_disposed)
                {
                    ApplyOnUi(() => ProcessGitHubSnapshot(snapshot));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CrashLogger.Write("IslandViewModel.RefreshGitHubRepositoriesAsync", ex);
        }
        finally
        {
            if (ReferenceEquals(_devPollCts, cts))
            {
                _devPollCts = null;
            }
            cts.Dispose();
            _devRefreshInFlight = false;
            ApplyOnUi(() => NotifyDeveloperPropertiesChanged());
        }
    }

    private List<string> CollectAllTrackedGitHubSlugs()
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var repo in _devRepos)
        {
            if (!string.IsNullOrWhiteSpace(repo.GitHubOwnerRepo) && seen.Add(repo.GitHubOwnerRepo))
            {
                result.Add(repo.GitHubOwnerRepo);
            }
        }

        foreach (var state in _gitStates.Values)
        {
            if (!string.IsNullOrWhiteSpace(state.OriginOwnerRepo) && seen.Add(state.OriginOwnerRepo))
            {
                result.Add(state.OriginOwnerRepo);
            }
        }

        return result;
    }

    private void ProcessGitHubSnapshot(GitHubRepositorySnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        _ghSnapshots[snapshot.OwnerRepo] = snapshot;

        if (_settingsService is { Current: { } settings } && settings.EnableDeveloperWorkspace)
        {
            EvaluateGitHubAlerts(snapshot, settings);
        }

        NotifyDeveloperPropertiesChanged();
        NotifySplitStateChanged();
    }

    private void EvaluateGitHubAlerts(GitHubRepositorySnapshot snapshot, AppSettings settings)
    {
        if (!_ghAlertState.TryGetValue(snapshot.OwnerRepo, out var tracker))
        {
            tracker = (new HashSet<long>(), new Dictionary<long, string>(), new HashSet<int>(), false);
            _ghAlertState[snapshot.OwnerRepo] = tracker;
        }

        var dnd = settings.DoNotDisturb;
        var activeRun = snapshot.ActiveRun;
        var ciEventId = $"dev_ci_{snapshot.OwnerRepo}";

        if (activeRun is not null)
        {
            _activeCiBuildSlugs.Add(snapshot.OwnerRepo);
            if (settings.ShowDeveloperLiveActivity && settings.NotifyOnGitHubActions && !dnd)
            {
                _eventRouter.Publish(new DeveloperActivityEvent(
                    Id: ciEventId,
                    ActivityKind: DeveloperActivityKind.BuildInProgress,
                    Title: $"CI Çalışıyor · {snapshot.OwnerRepo}",
                    Subtitle: $"{activeRun.Name} ({activeRun.Branch})",
                    IconGlyph: "\uE895",
                    BadgeText: "CI…",
                    LaunchTarget: activeRun.HtmlUrl,
                    Priority: EventPriority.Progress,
                    DisplayDuration: null));
            }
        }
        else if (_activeCiBuildSlugs.Remove(snapshot.OwnerRepo))
        {
            _eventRouter.Dismiss(ciEventId);
        }

        foreach (var run in snapshot.WorkflowRuns)
        {
            tracker.LastStatuses.TryGetValue(run.Id, out var prevStatus);
            var currentKey = $"{run.Status}:{run.Conclusion}";
            tracker.LastStatuses[run.Id] = currentKey;

            if (tracker.IsSeeded &&
                !run.IsInProgress &&
                !string.Equals(prevStatus, currentKey, StringComparison.Ordinal) &&
                tracker.SeededRunIds.Contains(run.Id) &&
                settings.NotifyOnGitHubActions && !dnd)
            {
                var ok = run.IsSuccess;
                var failed = run.IsFailure;
                _eventRouter.Publish(new SystemNotificationEvent(
                    Id: $"dev_ci_done_{run.Id}",
                    Category: EventCategory.Developer,
                    Title: ok ? $"CI Başarılı · {run.Name}" : (failed ? $"CI Başarısız · {run.Name}" : $"CI Bitti · {run.Name}"),
                    Subtitle: $"{snapshot.OwnerRepo} ({run.Branch})",
                    IconGlyph: ok ? "\uE73E" : (failed ? "\uEA39" : "\uE946"),
                    Priority: failed ? EventPriority.Warning : EventPriority.Informational,
                    DisplayDuration: TimeSpan.FromSeconds(4.2),
                    LaunchTarget: run.HtmlUrl));
            }

            tracker.SeededRunIds.Add(run.Id);
        }

        foreach (var pr in snapshot.OpenPullRequests)
        {
            var isNewPr = tracker.SeenPrs.Add(pr.Number);
            if (tracker.IsSeeded && isNewPr && settings.NotifyOnPullRequests && !dnd)
            {
                _eventRouter.Publish(new SystemNotificationEvent(
                    Id: $"dev_pr_{snapshot.OwnerRepo}_{pr.Number}",
                    Category: EventCategory.Developer,
                    Title: $"Açık PR #{pr.Number} · {snapshot.OwnerRepo}",
                    Subtitle: pr.Title,
                    IconGlyph: "\uE943",
                    Priority: EventPriority.Informational,
                    DisplayDuration: TimeSpan.FromSeconds(4.2),
                    LaunchTarget: pr.HtmlUrl));
            }
        }

        if (!tracker.IsSeeded)
        {
            _ghAlertState[snapshot.OwnerRepo] = (tracker.SeededRunIds, tracker.LastStatuses, tracker.SeenPrs, true);
        }
    }

    private void NotifyDeveloperPropertiesChanged()
    {
        if (_disposed)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (_armedMergePrNumber is not null && now >= _armedMergeExpiresUtc)
        {
            _armedMergePrNumber = null;
        }

        var hasAuthToken = _gitHubWorkspace?.HasAuthToken == true;
        var targetPrs = CurrentGitHubSnapshot?.OpenPullRequests.Take(3).ToList() ?? [];
        DevPullRequests.Clear();
        foreach (var pr in targetPrs)
        {
            var isArmed = _armedMergePrNumber == pr.Number && now < _armedMergeExpiresUtc;
            DevPullRequests.Add(new DevPullRequestItemViewModel(pr, isArmed, hasAuthToken));
        }

        OnPropertyChanged(nameof(HasDevRepositories));
        OnPropertyChanged(nameof(HasMultipleDevRepositories));
        OnPropertyChanged(nameof(DevRepoEmptyVisibility));
        OnPropertyChanged(nameof(DevRepoPopulatedVisibility));
        OnPropertyChanged(nameof(DevRepoSwitcherVisibility));
        OnPropertyChanged(nameof(DevRepoHeaderTitle));
        OnPropertyChanged(nameof(DevRepoIndexLabel));
        OnPropertyChanged(nameof(DevBranchLabel));
        OnPropertyChanged(nameof(HasCurrentLocalRepo));
        OnPropertyChanged(nameof(DevLocalActionsVisibility));
        OnPropertyChanged(nameof(DevGitHubActionsVisibility));
        OnPropertyChanged(nameof(DevDirtySummary));
        OnPropertyChanged(nameof(DevLastCommitSummary));
        OnPropertyChanged(nameof(DevCommitSha));
        OnPropertyChanged(nameof(DevCommitShaVisibility));
        OnPropertyChanged(nameof(DevCommitSubject));
        OnPropertyChanged(nameof(DevSyncButtonLabel));
        OnPropertyChanged(nameof(DevSyncToolTip));
        OnPropertyChanged(nameof(DevCanCreatePrVisibility));
        OnPropertyChanged(nameof(DevCreatePrToolTip));
        OnPropertyChanged(nameof(DevCiStatusSummary));
        OnPropertyChanged(nameof(DevCiStatusGlyph));
        OnPropertyChanged(nameof(DevCiStatusBrush));
        OnPropertyChanged(nameof(HasDevPullRequests));
        OnPropertyChanged(nameof(DevPullRequestsVisibility));
        OnPropertyChanged(nameof(DevNoPullRequestsVisibility));
        OnPropertyChanged(nameof(DevPullRequestHeaderLabel));
        OnPropertyChanged(nameof(DevStatusNote));
        OnPropertyChanged(nameof(ExpandedHeaderTitle));
        OnPropertyChanged(nameof(HasDeveloperLiveSplitBadge));
        OnPropertyChanged(nameof(DeveloperSplitGlyph));
        OnPropertyChanged(nameof(DeveloperSplitBadgeText));
        OnPropertyChanged(nameof(DeveloperSplitToolTip));
    }

    private void CancelDevPollRequest()
    {
        var cts = Interlocked.Exchange(ref _devPollCts, null);
        if (cts is not null)
        {
            try { cts.Cancel(); } catch { }
            try { cts.Dispose(); } catch { }
        }
    }

    private void DisposeDeveloperWorkspace()
    {
        if (_devPollTimer is not null)
        {
            _devPollTimer.Stop();
            _devPollTimer.Tick -= OnDevPollTimerTick;
            _devPollTimer = null;
        }

        CancelDevPollRequest();

        if (_localGitWatcher is not null)
        {
            _localGitWatcher.RepositoryUpdated -= OnLocalGitRepositoryUpdated;
            _localGitWatcher.ActivityDetected -= OnLocalGitActivityDetected;
        }
    }
}
