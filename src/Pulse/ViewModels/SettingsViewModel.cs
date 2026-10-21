using System.ComponentModel;
using System.Runtime.CompilerServices;
using Pulse.Core.Interfaces;
using Pulse.Core.Models;

namespace Pulse.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ISettingsService _settingsService;
    private readonly IStartupService _startupService;
    private bool _startWithWindows;
    private bool _autoCollapse = true;
    private bool _showSeekControls = true;
    private bool _notificationsPaused;
    private bool _audioNotifications = true;
    private bool _powerNotifications = true;
    private bool _downloadNotifications = true;
    private bool _enableHoverExpand = true;
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
    private string _statusMessage = string.Empty;
    private bool _loading;
    private bool _initialized;

    public SettingsViewModel(ISettingsService settingsService, IStartupService startupService)
    {
        _settingsService = settingsService;
        _startupService = startupService;
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
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _loading = true;
        await _settingsService.LoadAsync();
        var settings = _settingsService.Current;
        AutoCollapse = settings.AutoCollapse;
        ShowSeekControls = settings.ShowSeekControls;
        NotificationsPaused = settings.NotificationsPaused;
        AudioNotifications = settings.AudioNotifications;
        PowerNotifications = settings.PowerNotifications;
        DownloadNotifications = settings.DownloadNotifications;
        EnableHoverExpand = settings.EnableHoverExpand;
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
        StartWithWindows = await _startupService.IsEnabledAsync();
        _loading = false;
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

    private void Save()
    {
        if (_loading)
        {
            return;
        }

        _ = _settingsService.SaveAsync(new AppSettings
        {
            StartWithWindows = StartWithWindows,
            AutoCollapse = AutoCollapse,
            ShowSeekControls = ShowSeekControls,
            NotificationsPaused = NotificationsPaused,
            AudioNotifications = AudioNotifications,
            PowerNotifications = PowerNotifications,
            DownloadNotifications = DownloadNotifications,
            EnableHoverExpand = EnableHoverExpand,
            DynamicColorTint = DynamicColorTint,
            ShowEqualizer = ShowEqualizer,
            UseTransparency = UseTransparency,
            TransparencyLevel = (int)Math.Round(TransparencyLevel),
            ArtworkTintStrength = (int)Math.Round(ArtworkTintStrength),
            MotionSpeed = (int)Math.Round(MotionSpeed),
            Theme = ThemeIndex switch { 1 => "Dark", 2 => "Light", _ => "System" },
            AnimationIntensity = AnimationIndex switch { 0 => "Minimal", 2 => "Dynamic", _ => "Normal" },
            TopOffset = (int)Math.Round(TopOffset),
            IslandScale = (int)Math.Round(IslandScale)
        });
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
