using Pulse.Core.Services;

namespace Pulse.Tests.Services;

public sealed class SingleInstanceNameResolverTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Default")]
    [InlineData("default")]
    public void Resolve_UsesCanonicalNames_ForStandardInteractiveDesktop(string? desktopName)
    {
        var names = SingleInstanceNameResolver.Resolve(desktopName);

        Assert.Equal(SingleInstanceNameResolver.DefaultMutexName, names.MutexName);
        Assert.Equal(SingleInstanceNameResolver.DefaultShowIslandEventName, names.ShowIslandEventName);
        Assert.Equal(SingleInstanceNameResolver.DefaultShowSettingsEventName, names.ShowSettingsEventName);
    }

    [Fact]
    public void Resolve_ScopesNamesBySanitizedDesktop_WhenRunningOnIsolatedDesktop()
    {
        var names = SingleInstanceNameResolver.Resolve("exebox-6JURLS3A/TEST\\Desktop");

        Assert.NotEqual(SingleInstanceNameResolver.DefaultMutexName, names.MutexName);
        Assert.StartsWith($"{SingleInstanceNameResolver.DefaultMutexName}_exebox-6JURLS3A_TEST_Desktop", names.MutexName);
        Assert.StartsWith($"{SingleInstanceNameResolver.DefaultShowIslandEventName}_exebox-6JURLS3A_TEST_Desktop", names.ShowIslandEventName);
        Assert.StartsWith($"{SingleInstanceNameResolver.DefaultShowSettingsEventName}_exebox-6JURLS3A_TEST_Desktop", names.ShowSettingsEventName);
        Assert.DoesNotContain('/', names.MutexName);
        Assert.DoesNotContain('\\', names.MutexName["Local\\".Length..]);
    }
}

