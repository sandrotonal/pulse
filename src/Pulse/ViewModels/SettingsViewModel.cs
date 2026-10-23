using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Pulse.Core.Interfaces;
using Pulse.Core.Models;

namespace Pulse.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ISettingsService _settingsService;
    private readonly IStartupService _startupService;
    private readonly ILocationService? _locationService;
    private bool _startWithWindows;
    private bool _autoCollapse = true;
    private bool _showSeekControls = true;
    private bool _notificationsPaused;
    private bool _audioNotifications = true;
    private bool _powerNotifications = true;
    private bool _downloadNotifications = true;
    private bool _enableHoverExpand = true;
    private double _hoverExpandDelayMs = 200;
    private bool _autoHideOnFullscreen = true;
    private bool _enableScrollWheelVolume = true;
    private bool _dynamicColorTint = true;
    private bool _showEqualizer = true;
    private bool _useTransparency;
    private double _transparencyLevel = 60;
    private double _artworkTintStrength = 25;
    private double _motionSpeed = 100;
    private int _themeIndex;
    private int _animationIndex = 1;
    private double _topOffset = 14;
    private double _islandScale = 100;
    private bool _showIdleClock = true;
    private bool _showIdleDate = true;
    private bool _showIdleWeather = true;
    private int _idleLocationSourceIndex;
    private string _idleLocationCity = string.Empty;
    private double? _idleLocationLatitude;
    private double? _idleLocationLongitude;
    private string _locationCityDraft = string.Empty;
    private string _locationStatusMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _loading;
    private bool _initialized;

    public SettingsViewModel(
        ISettingsService settingsService,
        IStartupService startupService,
        ILocationService? locationService = null)
    {
        _settingsService = settingsService;
        _startupService = startupService;
        _locationService = locationService;
        ApplyLocationCommand = new AsyncCommand(ApplyLocationAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (!SetProperty(ref _startWithWindows, value) || _loading)
            {
                return;
            }

            _ = ApplyStartupAsync(value);
        }
    }

    public bool AutoCollapse { get => _autoCollapse; set { if (SetProperty(ref _autoCollapse, value)) Save(); } }
    public bool ShowSeekControls { get => _showSeekControls; set { if (SetProperty(ref _showSeekControls, value)) Save(); } }
    public bool NotificationsPaused { get => _notificationsPaused; set { if (SetProperty(ref _notificationsPaused, value)) Save(); } }
    public bool AudioNotifications { get => _audioNotifications; set { if (SetProperty(ref _audioNotifications, value)) Save(); } }
    public bool PowerNotifications { get => _powerNotifications; set { if (SetProperty(ref _powerNotifications, value)) Save(); } }
    public bool DownloadNotifications { get => _downloadNotifications; set { if (SetProperty(ref _downloadNotifications, value)) Save(); } }
    public bool EnableHoverExpand { get => _enableHoverExpand; set { if (SetProperty(ref _enableHoverExpand, value)) Save(); } }
    public double HoverExpandDelayMs
    {
        get => _hoverExpandDelayMs;
        set
        {
            if (SetProperty(ref _hoverExpandDelayMs, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HoverExpandDelayLabel)));
                Save();
            }
        }
    }
    public string HoverExpandDelayLabel => $"{(int)Math.Round(HoverExpandDelayMs)} ms";
    public bool AutoHideOnFullscreen { get => _autoHideOnFullscreen; set { if (SetProperty(ref _autoHideOnFullscreen, value)) Save(); } }
    public bool EnableScrollWheelVolume { get => _enableScrollWheelVolume; set { if (SetProperty(ref _enableScrollWheelVolume, value)) Save(); } }
    public bool DynamicColorTint { get => _dynamicColorTint; set { if (SetProperty(ref _dynamicColorTint, value)) Save(); } }
    public bool ShowEqualizer { get => _showEqualizer; set { if (SetProperty(ref _showEqualizer, value)) Save(); } }
    public bool UseTransparency { get => _useTransparency; set { if (SetProperty(ref _useTransparency, value)) Save(); } }
    public double TransparencyLevel
    {
        get => _transparencyLevel;
        set
        {
            if (SetProperty(ref _transparencyLevel, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TransparencyLevelLabel)));
                Save();
            }
        }
    }
    public string TransparencyLevelLabel => $"{(int)Math.Round(TransparencyLevel)}%";
    public double ArtworkTintStrength
    {
        get => _artworkTintStrength;
        set
        {
            if (SetProperty(ref _artworkTintStrength, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ArtworkTintStrengthLabel)));
                Save();
            }
        }
    }
    public string ArtworkTintStrengthLabel => $"{(int)Math.Round(ArtworkTintStrength)}%";
    public double MotionSpeed
    {
        get => _motionSpeed;
        set
        {
            if (SetProperty(ref _motionSpeed, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MotionSpeedLabel)));
                Save();
            }
        }
    }
    public string MotionSpeedLabel => $"{(int)Math.Round(MotionSpeed)}%";
    public int ThemeIndex { get => _themeIndex; set { if (SetProperty(ref _themeIndex, value)) Save(); } }
    public int AnimationIndex { get => _animationIndex; set { if (SetProperty(ref _animationIndex, value)) Save(); } }
    public double TopOffset
    {
        get => _topOffset;
        set
        {
            if (SetProperty(ref _topOffset, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TopOffsetLabel)));
                Save();
            }
        }
    }
    public string TopOffsetLabel => $"{(int)Math.Round(TopOffset)} px";
    public double IslandScale
    {
        get => _islandScale;
        set
        {
            if (SetProperty(ref _islandScale, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IslandScaleLabel)));
                Save();
            }
        }
    }
    public string IslandScaleLabel => $"{(int)Math.Round(IslandScale)}%";

    public bool ShowIdleClock { get => _showIdleClock; set { if (SetProperty(ref _showIdleClock, value)) Save(); } }
    public bool ShowIdleDate { get => _showIdleDate; set { if (SetProperty(ref _showIdleDate, value)) Save(); } }
    public bool ShowIdleWeather { get => _showIdleWeather; set { if (SetProperty(ref _showIdleWeather, value)) Save(); } }

    public int IdleLocationSourceIndex
    {
        get => _idleLocationSourceIndex;
        set
        {
            if (!SetProperty(ref _idleLocationSourceIndex, value))
            {
                return;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsManualLocation)));
            if (_loading)
            {
                return;
            }

            if (value == 1)
            {
                LocationStatusMessage = string.IsNullOrWhiteSpace(LocationCityDraft)
                    ? "Şehir adı girin."
                    : "Şehri doğrulayın.";
            }
            else
            {
                LocationStatusMessage = "Konum otomatik olarak algılanacak.";
            }

            Save();
        }
    }

    public bool IsManualLocation => IdleLocationSourceIndex == 1;
    public string IdleLocationCity { get => _idleLocationCity; private set => SetProperty(ref _idleLocationCity, value); }
    public double? IdleLocationLatitude { get => _idleLocationLatitude; private set => SetProperty(ref _idleLocationLatitude, value); }
    public double? IdleLocationLongitude { get => _idleLocationLongitude; private set => SetProperty(ref _idleLocationLongitude, value); }
    public string LocationCityDraft
    {
        get => _locationCityDraft;
        set
        {
            if (!SetProperty(ref _locationCityDraft, value))
            {
                return;
            }

            if (!_loading &&
                IsManualLocation &&
                !string.Equals(value?.Trim(), IdleLocationCity, StringComparison.Ordinal))
            {
                LocationStatusMessage = "Şehri doğrulayın.";
            }
        }
    }
    public string LocationStatusMessage { get => _locationStatusMessage; private set => SetProperty(ref _locationStatusMessage, value); }
    public ICommand ApplyLocationCommand { get; }

    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _loading = true;
        try
        {
            await _settingsService.LoadAsync();
            ApplySettings(_settingsService.Current);
            StartWithWindows = await _startupService.IsEnabledAsync();
        }
        finally
        {
            _loading = false;
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        AutoCollapse = settings.AutoCollapse;
        ShowSeekControls = settings.ShowSeekControls;
        NotificationsPaused = settings.NotificationsPaused;
        AudioNotifications = settings.AudioNotifications;
        PowerNotifications = settings.PowerNotifications;
        DownloadNotifications = settings.DownloadNotifications;
        EnableHoverExpand = settings.EnableHoverExpand;
        HoverExpandDelayMs = settings.HoverExpandDelayMs;
        AutoHideOnFullscreen = settings.AutoHideOnFullscreen;
        EnableScrollWheelVolume = settings.EnableScrollWheelVolume;
        DynamicColorTint = settings.DynamicColorTint;
        ShowEqualizer = settings.ShowEqualizer;
        UseTransparency = settings.UseTransparency;
        TransparencyLevel = settings.TransparencyLevel;
        ArtworkTintStrength = settings.ArtworkTintStrength;
        MotionSpeed = settings.MotionSpeed;
        ThemeIndex = settings.Theme switch { "Dark" => 1, "Light" => 2, _ => 0 };
        AnimationIndex = settings.AnimationIntensity switch { "Minimal" => 0, "Dynamic" => 2, _ => 1 };
        TopOffset = settings.TopOffset;
        IslandScale = settings.IslandScale;
        ShowIdleClock = settings.ShowIdleClock;
        ShowIdleDate = settings.ShowIdleDate;
        ShowIdleWeather = settings.ShowIdleWeather;
        IdleLocationSourceIndex = settings.IdleLocationSource == "Manual" ? 1 : 0;
        IdleLocationCity = settings.IdleLocationCity;
        IdleLocationLatitude = settings.IdleLocationLatitude;
        IdleLocationLongitude = settings.IdleLocationLongitude;
        LocationCityDraft = settings.IdleLocationCity;
        LocationStatusMessage = settings.IdleLocationSource == "Manual"
            ? (string.IsNullOrWhiteSpace(settings.IdleLocationCity)
                ? "Şehir adı girin."
                : $"Konum: {settings.IdleLocationCity}")
            : "Konum otomatik olarak algılanacak.";
        StartWithWindows = settings.StartWithWindows;
    }

    private async Task ApplyLocationAsync()
    {
        if (_locationService is null)
        {
            LocationStatusMessage = "Konum servisi kullanılamıyor.";
            return;
        }

        var city = LocationCityDraft.Trim();
        if (city.Length == 0)
        {
            LocationStatusMessage = "Şehir adı girin.";
            return;
        }

        LocationStatusMessage = "Şehir aranıyor…";
        GeoLocation? location;
        try
        {
            location = await _locationService.GeocodeAsync(city);
        }
        catch
        {
            LocationStatusMessage = "Konum alınamadı.";
            return;
        }

        if (location is null)
        {
            LocationStatusMessage = "Şehir bulunamadı.";
            return;
        }

        _loading = true;
        try
        {
            IdleLocationSourceIndex = 1;
            IdleLocationCity = location.City;
            IdleLocationLatitude = location.Latitude;
            IdleLocationLongitude = location.Longitude;
            LocationCityDraft = location.City;
        }
        finally
        {
            _loading = false;
        }

        LocationStatusMessage = $"Konum: {location.City}";
        Save();
    }

    private async Task ApplyStartupAsync(bool enabled)
    {
        var accepted = await _startupService.SetEnabledAsync(enabled);
        if (accepted)
        {
            StatusMessage = enabled ? "Windows başlangıcı açıldı." : "Windows başlangıcı kapatıldı.";
            await _settingsService.SaveAsync(_settingsService.Current with { StartWithWindows = enabled });
        }
        else
        {
            _loading = true;
            StartWithWindows = await _startupService.IsEnabledAsync();
            _loading = false;
            StatusMessage = "Başlangıç ayarı Windows tarafından değiştirilemedi.";
        }
    }

    public async Task SaveNowAsync()
    {
        if (_loading)
        {
            return;
        }

        await _settingsService.SaveAsync(BuildSettings());
        StatusMessage = "Ayarlar kaydedildi.";
    }

    public async Task RestoreDefaultsAsync()
    {
        if (_loading)
        {
            return;
        }

        var defaults = new AppSettings();
        _loading = true;
        try
        {
            ApplySettings(defaults);
        }
        finally
        {
            _loading = false;
        }

        // StartWithWindows is backed by the Windows startup task, not just JSON.
        // Apply it once here instead of letting every property setter save separately.
        if (await _startupService.IsEnabledAsync())
        {
            var accepted = await _startupService.SetEnabledAsync(false);
            if (!accepted)
            {
                var actual = await _startupService.IsEnabledAsync();
                _loading = true;
                try
                {
                    StartWithWindows = actual;
                }
                finally
                {
                    _loading = false;
                }

                await _settingsService.SaveAsync(BuildSettings());
                StatusMessage = "Ayarlar sıfırlandı; başlangıç ayarı değiştirilemedi.";
                return;
            }
        }

        await _settingsService.SaveAsync(BuildSettings());
        StatusMessage = "Varsayılan ayarlar geri yüklendi.";
    }

    private void Save()
    {
        if (_loading)
        {
            return;
        }

        _ = _settingsService.SaveAsync(BuildSettings());
    }

    private AppSettings BuildSettings()
    {
        return new AppSettings
        {
            StartWithWindows = StartWithWindows,
            AutoCollapse = AutoCollapse,
            ShowSeekControls = ShowSeekControls,
            NotificationsPaused = NotificationsPaused,
            AudioNotifications = AudioNotifications,
            PowerNotifications = PowerNotifications,
            DownloadNotifications = DownloadNotifications,
            EnableHoverExpand = EnableHoverExpand,
            HoverExpandDelayMs = (int)Math.Round(HoverExpandDelayMs),
            AutoHideOnFullscreen = AutoHideOnFullscreen,
            EnableScrollWheelVolume = EnableScrollWheelVolume,
            DynamicColorTint = DynamicColorTint,
            ShowEqualizer = ShowEqualizer,
            UseTransparency = UseTransparency,
            TransparencyLevel = (int)Math.Round(TransparencyLevel),
            ArtworkTintStrength = (int)Math.Round(ArtworkTintStrength),
            MotionSpeed = (int)Math.Round(MotionSpeed),
            Theme = ThemeIndex switch { 1 => "Dark", 2 => "Light", _ => "System" },
            AnimationIntensity = AnimationIndex switch { 0 => "Minimal", 2 => "Dynamic", _ => "Normal" },
            TopOffset = (int)Math.Round(TopOffset),
            IslandScale = (int)Math.Round(IslandScale),
            ShowIdleClock = ShowIdleClock,
            ShowIdleDate = ShowIdleDate,
            ShowIdleWeather = ShowIdleWeather,
            IdleLocationSource = IdleLocationSourceIndex == 1 ? "Manual" : "Auto",
            IdleLocationCity = IdleLocationCity,
            IdleLocationLatitude = IdleLocationLatitude,
            IdleLocationLongitude = IdleLocationLongitude
        };
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
