using Pulse.Core.Models;

namespace Pulse.Core.Interfaces;

public interface ISettingsService
{
    AppSettings Current { get; }
    event EventHandler<AppSettings>? SettingsChanged;
    Task LoadAsync();
    Task SaveAsync(AppSettings settings);
}
