using Pulse.Core.Models;

namespace Pulse.Core.Interfaces;

public interface ISettingsService
{
    AppSettings Current { get; }
    string? LastSaveError => null;
    event EventHandler<AppSettings>? SettingsChanged;
    Task LoadAsync();
    Task SaveAsync(AppSettings settings);
    Task FlushAsync() => Task.CompletedTask;
}
