using System.Text.Json;
using Pulse.Core.Interfaces;
using Pulse.Core.Models;

namespace Pulse.Services.Settings;

public sealed class SettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsService(string? settingsDirectory = null)
    {
        _settingsPath = Path.Combine(settingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pulse"), "settings.json");
    }

    public AppSettings Current { get; private set; } = new();
    public event EventHandler<AppSettings>? SettingsChanged;

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                await using var stream = File.OpenRead(_settingsPath);
                Current = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Current = new AppSettings();
        }

        SettingsChanged?.Invoke(this, Current);
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Current = settings with
        {
            TopOffset = Math.Clamp(settings.TopOffset, 0, 200),
            IslandScale = Math.Clamp(settings.IslandScale, 80, 125),
            Theme = settings.Theme is "Dark" or "Light" ? settings.Theme : "System",
            AnimationIntensity = settings.AnimationIntensity is "Minimal" or "Dynamic" ? settings.AnimationIntensity : "Normal"
        };

        await _saveGate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var temporaryPath = _settingsPath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, Current, JsonOptions);
            }

            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            _saveGate.Release();
        }

        SettingsChanged?.Invoke(this, Current);
    }
}
