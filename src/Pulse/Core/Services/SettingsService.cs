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
                var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions) ?? new AppSettings();
                Current = Normalize(loaded);
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
        Current = Normalize(settings);

        // Notify in-memory listeners immediately for instantaneous real-time UI response
        SettingsChanged?.Invoke(this, Current);

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
        catch { }
        finally
        {
            _saveGate.Release();
        }
    }

    /// <summary>Keeps persisted values inside the ranges the settings UI can represent.</summary>
    private static AppSettings Normalize(AppSettings settings)
    {
        var hasValidCoordinates =
            settings.IdleLocationLatitude is { } latitude &&
            settings.IdleLocationLongitude is { } longitude &&
            double.IsFinite(latitude) &&
            double.IsFinite(longitude) &&
            latitude is >= -90 and <= 90 &&
            longitude is >= -180 and <= 180;

        return settings with
        {
            TopOffset = Math.Clamp(settings.TopOffset, 0, 200),
            IslandScale = Math.Clamp(settings.IslandScale, 80, 125),
            Theme = settings.Theme is "Dark" or "Light" ? settings.Theme : "System",
            AnimationIntensity = settings.AnimationIntensity is "Minimal" or "Dynamic" ? settings.AnimationIntensity : "Normal",
            TransparencyLevel = Math.Clamp(settings.TransparencyLevel, 0, 100),
            ArtworkTintStrength = Math.Clamp(settings.ArtworkTintStrength, 0, 100),
            MotionSpeed = Math.Clamp(settings.MotionSpeed, 70, 140),
            IdleLocationSource = settings.IdleLocationSource == "Manual" ? "Manual" : "Auto",
            IdleLocationCity = NormalizeCity(settings.IdleLocationCity),
            IdleLocationLatitude = hasValidCoordinates ? settings.IdleLocationLatitude : null,
            IdleLocationLongitude = hasValidCoordinates ? settings.IdleLocationLongitude : null
        };
    }

    private static string NormalizeCity(string? city)
    {
        var trimmed = (city ?? string.Empty).Trim();
        return trimmed.Length <= 80 ? trimmed : trimmed[..80];
    }
}
