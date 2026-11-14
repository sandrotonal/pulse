using Pulse.Core.Services;

namespace Pulse.Tests.Services;

public sealed class AppActivationTargetResolverTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildPlan_ReturnsNull_WhenAppIdMissing(string? appId)
    {
        Assert.Null(AppActivationTargetResolver.BuildPlan(appId));
    }

    [Fact]
    public void BuildPlan_HandlesBareSpotifyExecutable_WithoutBogusAppsFolderFallback()
    {
        var plan = AppActivationTargetResolver.BuildPlan("Spotify.exe");

        Assert.NotNull(plan);
        Assert.False(plan.IsPackagedOrExplicitAumid);
        Assert.True(plan.IsExplicitExecutable);
        Assert.False(plan.AllowDirectAumidActivation);
        Assert.Equal(["Spotify"], plan.CandidateProcessNames);
        Assert.Equal("spotify:", plan.ProtocolFallbackUri);
    }

    [Fact]
    public void BuildPlan_HandlesPackagedStoreAumid_WithProcessHintAndDirectAumid()
    {
        var plan = AppActivationTargetResolver.BuildPlan("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify");

        Assert.NotNull(plan);
        Assert.True(plan.IsPackagedOrExplicitAumid);
        Assert.False(plan.IsExplicitExecutable);
        Assert.True(plan.AllowDirectAumidActivation);
        Assert.Equal(["Spotify"], plan.CandidateProcessNames);
        Assert.Equal("spotify:", plan.ProtocolFallbackUri);
    }

    [Theory]
    [InlineData("Chrome", "chrome")]
    [InlineData("MSEdge", "msedge")]
    [InlineData("308046B0AF4A39CB", "firefox")]
    public void BuildPlan_MapsRegisteredWin32BrowserAumidsToProcessAndAumid(string appId, string expectedProcess)
    {
        var plan = AppActivationTargetResolver.BuildPlan(appId);

        Assert.NotNull(plan);
        Assert.False(plan.IsPackagedOrExplicitAumid);
        Assert.False(plan.IsExplicitExecutable);
        Assert.True(plan.AllowDirectAumidActivation);
        Assert.Equal([expectedProcess], plan.CandidateProcessNames);
        Assert.Null(plan.ProtocolFallbackUri);
    }

    [Fact]
    public void BuildPlan_DoesNotHijackBrowserForChromePwaAumid()
    {
        var plan = AppActivationTargetResolver.BuildPlan("Chrome._crx_cinhimbnkkaeohfgghhklpknlkffjgod");

        Assert.NotNull(plan);
        Assert.False(plan.IsPackagedOrExplicitAumid);
        Assert.False(plan.IsExplicitExecutable);
        Assert.True(plan.AllowDirectAumidActivation);
        Assert.Empty(plan.CandidateProcessNames);
    }

    [Fact]
    public void IsWindowsAppsExecutablePath_DetectsPackagedBinaryFolder()
    {
        Assert.True(AppActivationTargetResolver.IsWindowsAppsExecutablePath(
            @"C:\Program Files\WindowsApps\SpotifyAB.SpotifyMusic_1.308.620.0_x64__zpdnekdrzrea0\Spotify.exe"));
        Assert.False(AppActivationTargetResolver.IsWindowsAppsExecutablePath(
            @"C:\Program Files\Google\Chrome\Application\chrome.exe"));
    }
}

