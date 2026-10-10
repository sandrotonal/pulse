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
    private bool _enableHoverExpand = true;
    private bool _dynamicColorTint = true;
    private bool _showEqualizer = true;
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
    public bool EnableHoverExpand { get => _enableHoverExpand; set { if (SetProperty(ref _enableHoverExpand, value)) Save(); } }
    public bool DynamicColorTint { get => _dynamicColorTint; set { if (SetProperty(ref _dynamicColorTint, value)) Save(); } }
    public bool ShowEqualizer { get => _showEqualizer; set { if (SetProperty(ref _showEqualizer, value)) Save(); } }
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
        EnableHoverExpand = settings.EnableHoverExpand;
        DynamicColorTint = settings.DynamicColorTint;
        ShowEqualizer = settings.ShowEqualizer;
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
            EnableHoverExpand = EnableHoverExpand,
            DynamicColorTint = DynamicColorTint,
            ShowEqualizer = ShowEqualizer,
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
