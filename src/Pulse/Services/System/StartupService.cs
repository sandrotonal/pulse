using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using Pulse.Core.Interfaces;
using Pulse.Core.Services;
using Windows.ApplicationModel;

namespace Pulse.Services.System;

public sealed class StartupService : IStartupService
{
    private const string TaskId = "PulseStartup";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "Pulse";

    public async Task<bool> IsEnabledAsync()
    {
        if (HasPackageIdentity())
        {
            try
            {
                var task = await StartupTask.GetAsync(TaskId);
                return task.State == StartupTaskState.Enabled;
            }
            catch (Exception ex)
            {
                CrashLogger.Write("StartupService.IsEnabledAsync.Packaged", ex);
                return false;
            }
        }

        return IsUnpackagedStartupEnabled();
    }

    public async Task<bool> SetEnabledAsync(bool enabled)
    {
        if (HasPackageIdentity())
        {
            try
            {
                var task = await StartupTask.GetAsync(TaskId);
                if (enabled)
                {
                    var state = task.State == StartupTaskState.Disabled ? await task.RequestEnableAsync() : task.State;
                    return state == StartupTaskState.Enabled;
                }

                if (task.State == StartupTaskState.Enabled)
                {
                    task.Disable();
                }

                return task.State != StartupTaskState.Enabled;
            }
            catch (Exception ex)
            {
                CrashLogger.Write("StartupService.SetEnabledAsync.Packaged", ex);
                return false;
            }
        }

        return SetUnpackagedStartupEnabled(enabled);
    }

    public async Task<bool> ReconcileAsync(bool desiredEnabled)
    {
        if (HasPackageIdentity())
        {
            var isEnabled = await IsEnabledAsync();
            if (desiredEnabled && !isEnabled)
            {
                return await SetEnabledAsync(true);
            }

            return isEnabled;
        }

        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            var existingCommand = runKey?.GetValue(ValueName) as string;
            using var approvedKey = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: false);
            var approvalBytes = approvedKey?.GetValue(ValueName) as byte[];
            var approvedEnabled = StartupCommandHelper.IsStartupApprovedEnabled(approvalBytes);

            if (!string.IsNullOrWhiteSpace(existingCommand))
            {
                // Respect user disabling the app inside Windows Task Manager.
                if (!approvedEnabled)
                {
                    return false;
                }

                // Heal stale Run paths after app upgrade / directory relocation.
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath) &&
                    !StartupCommandHelper.IsMatchingCommand(existingCommand, exePath))
                {
                    runKey!.SetValue(ValueName, StartupCommandHelper.BuildRunCommand(exePath), RegistryValueKind.String);
                }

                return true;
            }

            if (desiredEnabled)
            {
                return SetUnpackagedStartupEnabled(true);
            }

            return false;
        }
        catch (Exception ex)
        {
            CrashLogger.Write("StartupService.ReconcileAsync", ex);
            return await IsEnabledAsync();
        }
    }

    private static bool IsUnpackagedStartupEnabled()
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var command = runKey?.GetValue(ValueName) as string;
            if (string.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            using var approvedKey = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: false);
            var approvalBytes = approvedKey?.GetValue(ValueName) as byte[];
            return StartupCommandHelper.IsStartupApprovedEnabled(approvalBytes);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            CrashLogger.Write("StartupService.IsUnpackagedStartupEnabled", ex);
            return false;
        }
    }

    private static bool SetUnpackagedStartupEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                {
                    CrashLogger.WriteMessage("StartupService.SetUnpackagedStartupEnabled", $"Invalid ProcessPath: '{exePath}'");
                    return false;
                }

                using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                if (runKey is null)
                {
                    return false;
                }

                runKey.SetValue(ValueName, StartupCommandHelper.BuildRunCommand(exePath), RegistryValueKind.String);

                using var approvedKey = Registry.CurrentUser.CreateSubKey(ApprovedKeyPath, writable: true);
                approvedKey?.SetValue(ValueName, StartupCommandHelper.CreateEnabledApprovalValue(), RegistryValueKind.Binary);

                return IsUnpackagedStartupEnabled();
            }

            using var existingRunKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            existingRunKey?.DeleteValue(ValueName, throwOnMissingValue: false);

            using var existingApprovedKey = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: true);
            existingApprovedKey?.DeleteValue(ValueName, throwOnMissingValue: false);

            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            CrashLogger.Write("StartupService.SetUnpackagedStartupEnabled", ex);
            return false;
        }
    }

    private static bool HasPackageIdentity()
    {
        try
        {
            var length = 0;
            var result = GetCurrentPackageFullName(ref length, IntPtr.Zero);
            return result != AppModelErrorNoPackage;
        }
        catch
        {
            return false;
        }
    }

    private const int AppModelErrorNoPackage = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, IntPtr packageFullName);
}
