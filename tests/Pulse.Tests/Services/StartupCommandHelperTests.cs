using Pulse.Core.Services;

namespace Pulse.Tests.Services;

public sealed class StartupCommandHelperTests
{
    [Fact]
    public void BuildsQuotedExecutableCommandAndExtractsNormalizedPath()
    {
        var exePath = @"C:\Users\ACER NİTRO\AppData\Local\Programs\Pulse\Pulse.exe";
        var command = StartupCommandHelper.BuildRunCommand(exePath);

        Assert.Equal($"\"{exePath}\"", command);
        Assert.Equal(exePath, StartupCommandHelper.ExtractExecutablePath(command));
        Assert.True(StartupCommandHelper.IsMatchingCommand($"  \"{exePath}\" --minimized  ", exePath));
        Assert.False(StartupCommandHelper.IsMatchingCommand(@"C:\OldFolder\Pulse.exe", exePath));
    }

    [Fact]
    public void DecodesWindowsStartupApprovedBytesAccurately()
    {
        Assert.True(StartupCommandHelper.IsStartupApprovedEnabled(null));
        Assert.True(StartupCommandHelper.IsStartupApprovedEnabled(StartupCommandHelper.CreateEnabledApprovalValue()));
        Assert.True(StartupCommandHelper.IsStartupApprovedEnabled([0x06, 0x00, 0x00, 0x00, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.False(StartupCommandHelper.IsStartupApprovedEnabled([0x03, 0x00, 0x00, 0x00, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.False(StartupCommandHelper.IsStartupApprovedEnabled([0x01, 0x00, 0x00, 0x00, 0, 0, 0, 0, 0, 0, 0, 0]));
    }
}

