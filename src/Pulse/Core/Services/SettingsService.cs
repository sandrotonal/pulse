using System.Text.Json;
using System.Text.Json.Nodes;
using Pulse.Core.Interfaces;
using Pulse.Core.Models;
using Pulse.Core.Services;

namespace Pulse.Services.Settings;

public sealed class SettingsService : ISettingsService, IDisposable
{
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private Task _activeSaveTask = Task.CompletedTask;
    private volatile bool _disposed;

    public SettingsService(string? settingsDirectory = null)
    {
        _settingsPath = Path.Combine(settingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pulse"), "settings.json");
    }

    public AppSettings Current { get; private set; } = new();
    public string? LastSaveError { get; private set; }
    public event EventHandler<AppSettings>? SettingsChanged;

    public async Task LoadAsync()
    {
        if (_disposed)
        {
            return;
        }

        var acquired = false;
        try
        {
            await _saveGate.WaitAsync().ConfigureAwait(false);
            acquired = true;

            if (File.Exists(_settingsPath))
            {
                var json = await File.ReadAllTextAsync(_settingsPath).ConfigureAwait(false);
                var loaded = DeserializeResilientWithDefaults(json);
                Current = Normalize(loaded);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException or ObjectDisposedException)
        {
            CrashLogger.Write("SettingsService.LoadAsync", ex, $"Path={_settingsPath}");
            Current = new AppSettings();
        }
        finally
        {
            if (acquired)
            {
                try { _saveGate.Release(); } catch (ObjectDisposedException) { }
            }
        }

        SettingsChanged?.Invoke(this, Current);
    }

    /// <summary>
    /// Overlays persisted values on a fresh settings instance so newly added fields
    /// keep their defaults and a single malformed field cannot wipe unrelated settings.
    /// </summary>
    private static AppSettings DeserializeResilientWithDefaults(string json)
    {
        var defaults = new AppSettings();
        if (string.IsNullOrWhiteSpace(json))
        {
            return defaults;
        }

        var defaultsJson = JsonSerializer.Serialize(
            defaults,
            AppSettingsJsonContext.Default.AppSettings);
        if (JsonNode.Parse(json) is not JsonObject overrides ||
            JsonNode.Parse(defaultsJson) is not JsonObject merged)
        {
            return defaults;
        }

        foreach (var (key, value) in overrides)
        {
            merged[key] = value?.DeepClone();
        }

        try
        {
            return JsonSerializer.Deserialize(
                merged.ToJsonString(),
                AppSettingsJsonContext.Default.AppSettings) ?? defaults;
        }
        catch (JsonException)
        {
            // Fall back property-by-property so one corrupt value never discards valid fields.
            if (JsonNode.Parse(defaultsJson) is not JsonObject fallbackMerged)
            {
                return defaults;
            }

            foreach (var (key, value) in overrides)
            {
                var previous = fallbackMerged[key]?.DeepClone();
                fallbackMerged[key] = value?.DeepClone();
                try
                {
                    _ = JsonSerializer.Deserialize(
                        fallbackMerged.ToJsonString(),
                        AppSettingsJsonContext.Default.AppSettings);
                }
                catch (JsonException)
                {
                    fallbackMerged[key] = previous;
                }
            }

            return JsonSerializer.Deserialize(
                fallbackMerged.ToJsonString(),
                AppSettingsJsonContext.Default.AppSettings) ?? defaults;
        }
    }

    public Task SaveAsync(AppSettings settings)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        Current = Normalize(settings);

        // Notify in-memory listeners immediately for instantaneous real-time UI response
        SettingsChanged?.Invoke(this, Current);

        var saveTask = PersistLatestAsync();
        _activeSaveTask = saveTask;
        return saveTask;
    }

    public async Task FlushAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await _activeSaveTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("SettingsService.FlushAsync", ex);
        }

        var acquired = false;
        try
        {
            await _saveGate.WaitAsync().ConfigureAwait(false);
            acquired = true;
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            if (acquired)
            {
                try { _saveGate.Release(); } catch (ObjectDisposedException) { }
            }
        }
    }

    private async Task PersistLatestAsync()
    {
        if (_disposed)
        {
            return;
        }

        var acquired = false;
        try
        {
            await _saveGate.WaitAsync().ConfigureAwait(false);
            acquired = true;
            if (_disposed)
            {
                return;
            }

            var snapshot = Current;
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var temporaryPath = _settingsPath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    snapshot,
                    AppSettingsJsonContext.Default.AppSettings).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }

            File.Move(temporaryPath, _settingsPath, overwrite: true);
            LastSaveError = null;
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            LastSaveError = ex.Message;
            CrashLogger.Write("SettingsService.SaveAsync", ex, $"Path={_settingsPath}");
        }
        finally
        {
            if (acquired)
            {
                try { _saveGate.Release(); } catch (ObjectDisposedException) { }
            }
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
            Theme = NormalizeTheme(settings.Theme),
            AnimationIntensity = settings.AnimationIntensity is "Minimal" or "Dynamic" ? settings.AnimationIntensity : "Normal",
            TransparencyLevel = Math.Clamp(settings.TransparencyLevel, 0, 100),
            ArtworkTintStrength = Math.Clamp(settings.ArtworkTintStrength, 0, 100),
            HoverExpandDelayMs = Math.Clamp(settings.HoverExpandDelayMs, 0, 1000),
            MotionSpeed = Math.Clamp(settings.MotionSpeed, 70, 140),
            IdleLocationSource = settings.IdleLocationSource == "Manual" ? "Manual" : "Auto",
            IdleLocationCity = NormalizeCity(settings.IdleLocationCity),
            IdleLocationLatitude = hasValidCoordinates ? settings.IdleLocationLatitude : null,
            IdleLocationLongitude = hasValidCoordinates ? settings.IdleLocationLongitude : null
        };
    }

    private static string NormalizeTheme(string? theme)
    {
        if (string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase)) return "Dark";
        if (string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)) return "Light";
        return "System";
    }

    private static string NormalizeCity(string? city)
    {
        var trimmed = (city ?? string.Empty).Trim();
        return trimmed.Length <= 80 ? trimmed : trimmed[..80];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _saveGate.Dispose();
    }
}
