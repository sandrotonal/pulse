namespace Pulse.Core.Interfaces;

public interface IStartupService
{
    Task<bool> IsEnabledAsync();
    Task<bool> SetEnabledAsync(bool enabled);
}
