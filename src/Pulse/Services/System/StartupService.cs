using Pulse.Core.Interfaces;
using Windows.ApplicationModel;

namespace Pulse.Services.System;

public sealed class StartupService : IStartupService
{
    private const string TaskId = "PulseStartup";

    public async Task<bool> IsEnabledAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            return task.State == StartupTaskState.Enabled;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> SetEnabledAsync(bool enabled)
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
        catch (Exception)
        {
            return false;
        }
    }
}
