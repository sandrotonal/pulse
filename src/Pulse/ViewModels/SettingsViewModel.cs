using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Pulse.Core.Interfaces;
using Pulse.Core.Models;
using Pulse.Core.Services;
using Pulse.Services.System;

namespace Pulse.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IStartupService _startupService;
    private readonly ILocationService? _locationService;
    private readonly Func<string?>? _hotkeyWarningProvider;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;
    private bool _startWithWindows;
    private bool _autoCollapse = true;
    private bool _showSeekControls = true;
    private bool _notificationsPaused;
    private bool _audioNotifications = true;
    private bool _powerNotifications = true;
    private bool _downloadNotifications = true;
    private bool _systemToastNotifications = true;
    private bool _bluetoothNotifications = true;
    private bool _screenshotNotifications = true;
    private bool _doNotDisturb;
    private bool _focusCompletionSound = true;
    private bool _enableHoverExpand = true;
    private double _hoverExpandDelayMs = 200;
    private double _autoCollapseDelayMs = 600;
    private int _compactClickActionIndex;
    private int _hoverActionIndex;
    private int _scrollWheelActionIndex;
    private bool _autoHideOnFullscreen = true;
    private bool _dynamicColorTint = true;
    private bool _showEqualizer = true;
    private bool _showLyrics = true;
    private bool _useTransparency;
    private double _transparencyLevel = 60;
    private double _artworkTintStrength = 25;
    private double _motionSpeed = 100;
    private int _themeIndex;
    private int _animationIndex = 1;
    private double _topOffset = 14;
    private double _horizontalOffset;
    private double _islandScale = 100;
    private IReadOnlyList<DisplayMonitorOption> _monitorOptions = [];
    private string _preferredMonitorDeviceName = string.Empty;
    private int _selectedMonitorIndex;
    private bool _showIdleClock = true;
    private bool _showIdleDate = true;
    private bool _showIdleWeather = true;
    private int _clockFormatIndex;
    private int _temperatureUnitIndex;
    private int _idleLocationSourceIndex;
    private string _idleLocationCity = string.Empty;
    private double? _idleLocationLatitude;
    private double? _idleLocationLongitude;
    private string _locationCityDraft = string.Empty;
    private string _locationStatusMessage = string.Empty;
    private double _focusDefaultMinutes = 25;
    private double _focusBreakMinutes = 5;
    private double _focusCustomMinutes = 45;
    private bool _enableGlobalHotkeys = true;
    private string _hotkeyWarningMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _loading;
    private bool _initialized;
    private bool _isSelfSaving;
    private bool _disposed;

    public SettingsViewModel(
        ISettingsService settingsService,
        IStartupService startupService,
        ILocationService? locationService = null,
        Func<string?>? hotkeyWarningProvider = null)
    {
        _settingsService = settingsService;
        _startupService = startupService;
        _locationService = locationService;
        _hotkeyWarningProvider = hotkeyWarningProvider;
        _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _themeIndex = _settingsService.Current.Theme switch { "Dark" => 1, "Light" => 2, _ => 0 };
        RefreshMonitorOptions(_settingsService.Current.PreferredMonitorDeviceName);
        ApplyLocationCommand = new AsyncCommand(() => ApplyLocationAsync());
        ResetHorizontalOffsetCommand = new RelayCommand(() => HorizontalOffset = 0);
        _settingsService.SettingsChanged += OnExternalSettingsChanged;
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

    public bool NotificationsPaused
    {
        get => _notificationsPaused;
        set
        {
            if (SetProperty(ref _notificationsPaused, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowTrackChangeNotifications)));
                Save();
            }
        }
    }

    /// <summary>Positive UI polarity: ON means track change banners are enabled.</summary>
    public bool ShowTrackChangeNotifications
    {
        get => !_notificationsPaused;
        set => NotificationsPaused = !value;
    }

    public bool AudioNotifications { get => _audioNotifications; set { if (SetProperty(ref _audioNotifications, value)) Save(); } }
    public bool PowerNotifications { get => _powerNotifications; set { if (SetProperty(ref _powerNotifications, value)) Save(); } }
    public bool DownloadNotifications { get => _downloadNotifications; set { if (SetProperty(ref _downloadNotifications, value)) Save(); } }
    public bool SystemToastNotifications { get => _systemToastNotifications; set { if (SetProperty(ref _systemToastNotifications, value)) Save(); } }
    public bool BluetoothNotifications { get => _bluetoothNotifications; set { if (SetProperty(ref _bluetoothNotifications, value)) Save(); } }
    public bool ScreenshotNotifications { get => _screenshotNotifications; set { if (SetProperty(ref _screenshotNotifications, value)) Save(); } }
    public bool DoNotDisturb { get => _doNotDisturb; set { if (SetProperty(ref _doNotDisturb, value)) Save(); } }
    public bool FocusCompletionSound { get => _focusCompletionSound; set { if (SetProperty(ref _focusCompletionSound, value)) Save(); } }
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
    public double AutoCollapseDelayMs
    {
        get => _autoCollapseDelayMs;
        set
        {
            if (SetProperty(ref _autoCollapseDelayMs, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutoCollapseDelayLabel)));
                Save();
            }
        }
    }
    public string AutoCollapseDelayLabel => $"{(int)Math.Round(AutoCollapseDelayMs)} ms";
    public int CompactClickActionIndex { get => _compactClickActionIndex; set { if (SetProperty(ref _compactClickActionIndex, value)) Save(); } }
    public int HoverActionIndex { get => _hoverActionIndex; set { if (SetProperty(ref _hoverActionIndex, value)) Save(); } }
    public int ScrollWheelActionIndex { get => _scrollWheelActionIndex; set { if (SetProperty(ref _scrollWheelActionIndex, value)) Save(); } }
    public bool AutoHideOnFullscreen { get => _autoHideOnFullscreen; set { if (SetProperty(ref _autoHideOnFullscreen, value)) Save(); } }
    public bool DynamicColorTint { get => _dynamicColorTint; set { if (SetProperty(ref _dynamicColorTint, value)) Save(); } }
    public bool ShowEqualizer { get => _showEqualizer; set { if (SetProperty(ref _showEqualizer, value)) Save(); } }
    public bool ShowLyrics { get => _showLyrics; set { if (SetProperty(ref _showLyrics, value)) Save(); } }
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
    public double HorizontalOffset
    {
        get => _horizontalOffset;
        set
        {
            if (SetProperty(ref _horizontalOffset, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HorizontalOffsetLabel)));
                Save();
            }
        }
    }
    public string HorizontalOffsetLabel
    {
        get
        {
            var val = (int)Math.Round(HorizontalOffset);
            return val == 0 ? "Orta (0 px)" : (val > 0 ? $"+{val} px" : $"{val} px");
        }
    }
    public ICommand ResetHorizontalOffsetCommand { get; }
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
    public IReadOnlyList<DisplayMonitorOption> MonitorOptions => _monitorOptions;
    public int SelectedMonitorIndex
    {
        get => _selectedMonitorIndex;
        set
        {
            if (value < 0 || value >= _monitorOptions.Count)
            {
                return;
            }

            if (SetProperty(ref _selectedMonitorIndex, value))
            {
                _preferredMonitorDeviceName = _monitorOptions[value].DeviceName;
                Save();
            }
        }
    }

    public bool ShowIdleClock { get => _showIdleClock; set { if (SetProperty(ref _showIdleClock, value)) Save(); } }
    public bool ShowIdleDate { get => _showIdleDate; set { if (SetProperty(ref _showIdleDate, value)) Save(); } }
    public bool ShowIdleWeather
    {
        get => _showIdleWeather;
        set
        {
            if (SetProperty(ref _showIdleWeather, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanEditManualLocation)));
                Save();
            }
        }
    }
    public int ClockFormatIndex { get => _clockFormatIndex; set { if (SetProperty(ref _clockFormatIndex, value)) Save(); } }
    public int TemperatureUnitIndex { get => _temperatureUnitIndex; set { if (SetProperty(ref _temperatureUnitIndex, value)) Save(); } }
    public double FocusDefaultMinutes
    {
        get => _focusDefaultMinutes;
        set
        {
            if (SetProperty(ref _focusDefaultMinutes, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FocusDefaultMinutesLabel)));
                Save();
            }
        }
    }
    public string FocusDefaultMinutesLabel => $"{(int)Math.Round(FocusDefaultMinutes)} dk";
    public double FocusBreakMinutes
    {
        get => _focusBreakMinutes;
        set
        {
            if (SetProperty(ref _focusBreakMinutes, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FocusBreakMinutesLabel)));
                Save();
            }
        }
    }
    public string FocusBreakMinutesLabel => $"{(int)Math.Round(FocusBreakMinutes)} dk";
    public double FocusCustomMinutes
    {
        get => _focusCustomMinutes;
        set
        {
            if (SetProperty(ref _focusCustomMinutes, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FocusCustomMinutesLabel)));
                Save();
            }
        }
    }
    public string FocusCustomMinutesLabel => $"{(int)Math.Round(FocusCustomMinutes)} dk";
    public bool EnableGlobalHotkeys
    {
        get => _enableGlobalHotkeys;
        set
        {
            if (SetProperty(ref _enableGlobalHotkeys, value))
            {
                Save();
            }
        }
    }
    public string HotkeyWarningMessage
    {
        get => _hotkeyWarningMessage;
        private set
        {
            if (SetProperty(ref _hotkeyWarningMessage, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HotkeyWarningVisibility)));
            }
        }
    }
    public Microsoft.UI.Xaml.Visibility HotkeyWarningVisibility =>
        string.IsNullOrWhiteSpace(HotkeyWarningMessage)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanEditManualLocation)));
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
    public bool CanEditManualLocation => ShowIdleWeather && IsManualLocation;
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
            var osStartupEnabled = await _startupService.IsEnabledAsync();
            ApplySettings(_settingsService.Current with { StartWithWindows = osStartupEnabled });
        }
        catch (Exception ex)
        {
            CrashLogger.Write("SettingsViewModel.InitializeAsync", ex);
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnExternalSettingsChanged(object? sender, AppSettings settings)
    {
        if (_disposed || _isSelfSaving || _loading || !_initialized)
        {
            return;
        }

        void Sync()
        {
            if (_disposed || _isSelfSaving || _loading)
            {
                return;
            }

            _loading = true;
            try
            {
                ApplySettings(settings);
            }
            finally
            {
                _loading = false;
            }
        }

        if (_dispatcherQueue is not null && !_dispatcherQueue.HasThreadAccess)
        {
            _ = _dispatcherQueue.TryEnqueue(Sync);
        }
        else
        {
            Sync();
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        AutoCollapse = settings.AutoCollapse;
        ShowSeekControls = settings.ShowSeekControls;
        NotificationsPaused = settings.NotificationsPaused;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowTrackChangeNotifications)));
        AudioNotifications = settings.AudioNotifications;
        PowerNotifications = settings.PowerNotifications;
        DownloadNotifications = settings.DownloadNotifications;
        SystemToastNotifications = settings.SystemToastNotifications;
        BluetoothNotifications = settings.BluetoothNotifications;
        ScreenshotNotifications = settings.ScreenshotNotifications;
        DoNotDisturb = settings.DoNotDisturb;
        FocusCompletionSound = settings.FocusCompletionSound;
        EnableHoverExpand = settings.EnableHoverExpand;
        HoverExpandDelayMs = settings.HoverExpandDelayMs;
        AutoCollapseDelayMs = settings.AutoCollapseDelayMs;
        CompactClickActionIndex = settings.CompactClickAction switch { "ActivateApp" => 1, "PlayPause" => 2, _ => 0 };
        HoverActionIndex = settings.HoverAction == "ExpandCard" ? 1 : 0;
        ScrollWheelActionIndex = settings.ScrollWheelAction switch { "Volume" => 1, "PageSwitch" => 2, _ => 0 };
        AutoHideOnFullscreen = settings.AutoHideOnFullscreen;
        DynamicColorTint = settings.DynamicColorTint;
        ShowEqualizer = settings.ShowEqualizer;
        ShowLyrics = settings.ShowLyrics;
        UseTransparency = settings.UseTransparency;
        TransparencyLevel = settings.TransparencyLevel;
        ArtworkTintStrength = settings.ArtworkTintStrength;
        MotionSpeed = settings.MotionSpeed;
        ThemeIndex = settings.Theme switch { "Dark" => 1, "Light" => 2, _ => 0 };
        AnimationIndex = settings.AnimationIntensity switch { "Minimal" => 0, "Dynamic" => 2, _ => 1 };
        RefreshMonitorOptions(settings.PreferredMonitorDeviceName);
        TopOffset = settings.TopOffset;
        HorizontalOffset = settings.HorizontalOffset;
        IslandScale = settings.IslandScale;
        ShowIdleClock = settings.ShowIdleClock;
        ShowIdleDate = settings.ShowIdleDate;
        ShowIdleWeather = settings.ShowIdleWeather;
        ClockFormatIndex = settings.Use24HourClock ? 0 : 1;
        TemperatureUnitIndex = string.Equals(settings.TemperatureUnit, "F", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        FocusDefaultMinutes = settings.FocusDefaultMinutes;
        FocusBreakMinutes = settings.FocusBreakMinutes;
        FocusCustomMinutes = settings.FocusCustomMinutes;
        EnableGlobalHotkeys = settings.EnableGlobalHotkeys;
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
        RefreshHotkeyWarning();
    }

    private void RefreshMonitorOptions(string? preferredDeviceName)
    {
        var options = DisplayMonitorHelper.GetMonitorOptions(preferredDeviceName);
        _monitorOptions = options;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MonitorOptions)));

        var normalized = preferredDeviceName ?? string.Empty;
        var index = 0;
        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i].DeviceName, normalized, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        _preferredMonitorDeviceName = options.Count > index ? options[index].DeviceName : string.Empty;
        if (_selectedMonitorIndex != index)
        {
            _selectedMonitorIndex = index;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedMonitorIndex)));
        }
    }

    private void RefreshHotkeyWarning()
    {
        if (!EnableGlobalHotkeys)
        {
            HotkeyWarningMessage = string.Empty;
            return;
        }

        HotkeyWarningMessage = _hotkeyWarningProvider?.Invoke() ?? string.Empty;
    }

    private async Task<bool> ApplyLocationAsync()
    {
        if (_locationService is null)
        {
            LocationStatusMessage = "Konum servisi kullanılamıyor.";
            return false;
        }

        var city = LocationCityDraft.Trim();
        if (city.Length == 0)
        {
            LocationStatusMessage = "Şehir adı girin.";
            return false;
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
            return false;
        }

        if (location is null)
        {
            LocationStatusMessage = "Şehir bulunamadı.";
            return false;
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
        await SaveAsyncImmediate(BuildSettings());
        return true;
    }

    private async Task ApplyStartupAsync(bool enabled)
    {
        try
        {
            var accepted = await _startupService.SetEnabledAsync(enabled);
            var actual = await _startupService.IsEnabledAsync();
            if (accepted && actual == enabled)
            {
                await SaveAsyncImmediate(_settingsService.Current with { StartWithWindows = enabled });
                StatusMessage = _settingsService.LastSaveError is null
                    ? (enabled ? "Windows başlangıcı açıldı." : "Windows başlangıcı kapatıldı.")
                    : $"Kayıt hatası: {_settingsService.LastSaveError}";
            }
            else
            {
                _loading = true;
                try
                {
                    StartWithWindows = actual;
                }
                finally
                {
                    _loading = false;
                }

                await SaveAsyncImmediate(_settingsService.Current with { StartWithWindows = actual });
                StatusMessage = "Başlangıç ayarı Windows tarafından değiştirilemedi.";
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("SettingsViewModel.ApplyStartupAsync", ex);
            StatusMessage = "Başlangıç ayarı güncellenemedi.";
        }
    }

    public async Task SaveNowAsync()
    {
        if (_loading)
        {
            return;
        }

        _saveDebounce?.Stop();
        lock (_saveLock)
        {
            _pendingSave = null;
        }

        var locationApplied = true;
        if (IsManualLocation &&
            !string.IsNullOrWhiteSpace(LocationCityDraft) &&
            (IdleLocationLatitude is null ||
             !string.Equals(LocationCityDraft.Trim(), IdleLocationCity, StringComparison.OrdinalIgnoreCase)))
        {
            locationApplied = await ApplyLocationAsync();
        }

        await SaveAsyncImmediate(BuildSettings());
        if (_settingsService.LastSaveError is not null)
        {
            StatusMessage = $"Kayıt hatası: {_settingsService.LastSaveError}";
        }
        else if (!locationApplied)
        {
            StatusMessage = $"Diğer ayarlar kaydedildi ({LocationStatusMessage})";
        }
        else
        {
            StatusMessage = "Ayarlar kaydedildi.";
        }
    }

    public async Task RestoreDefaultsAsync()
    {
        if (_loading)
        {
            return;
        }

        _saveDebounce?.Stop();
        lock (_saveLock)
        {
            _pendingSave = null;
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

                await SaveAsyncImmediate(BuildSettings());
                StatusMessage = "Ayarlar sıfırlandı; başlangıç ayarı değiştirilemedi.";
                return;
            }
        }

        await SaveAsyncImmediate(BuildSettings());
        StatusMessage = _settingsService.LastSaveError is null
            ? "Varsayılan ayarlar geri yüklendi."
            : $"Sıfırlama kayıt hatası: {_settingsService.LastSaveError}";
    }

    private readonly object _saveLock = new();
    private AppSettings? _pendingSave;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _saveDebounce;

    private void Save()
    {
        if (_loading || _disposed)
        {
            return;
        }

        lock (_saveLock)
        {
            _pendingSave = BuildSettings();
        }

        var dispatcher = _dispatcherQueue ?? Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dispatcher is null)
        {
            _ = FlushPendingSaveAsync();
            return;
        }

        if (dispatcher.HasThreadAccess)
        {
            RestartSaveDebounceTimer(dispatcher);
        }
        else
        {
            _ = dispatcher.TryEnqueue(() =>
            {
                if (!_disposed)
                {
                    RestartSaveDebounceTimer(dispatcher);
                }
            });
        }
    }

    private void RestartSaveDebounceTimer(Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        if (_saveDebounce is null)
        {
            _saveDebounce = dispatcher.CreateTimer();
            _saveDebounce.IsRepeating = false;
            _saveDebounce.Interval = TimeSpan.FromMilliseconds(300);
            _saveDebounce.Tick += (_, _) => _ = FlushPendingSaveAsync();
        }

        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    public void FlushPendingSave() => _ = FlushPendingSaveAsync();

    public async Task FlushPendingSaveAsync()
    {
        _saveDebounce?.Stop();
        AppSettings? pending;
        lock (_saveLock)
        {
            pending = _pendingSave;
            _pendingSave = null;
        }

        if (pending is not null)
        {
            await SaveAsyncImmediate(pending);
            if (!_disposed)
            {
                StatusMessage = _settingsService.LastSaveError is null
                    ? "Otomatik kaydedildi."
                    : $"Kayıt hatası: {_settingsService.LastSaveError}";
            }
        }
        else
        {
            await _settingsService.FlushAsync();
        }
    }

    private async Task SaveAsyncImmediate(AppSettings settings)
    {
        _isSelfSaving = true;
        try
        {
            await _settingsService.SaveAsync(settings);
            await _settingsService.FlushAsync();
        }
        catch (Exception ex)
        {
            CrashLogger.Write("SettingsViewModel.SaveAsyncImmediate", ex);
        }
        finally
        {
            _isSelfSaving = false;
            if (!_disposed)
            {
                RefreshHotkeyWarning();
            }
        }
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
            SystemToastNotifications = SystemToastNotifications,
            BluetoothNotifications = BluetoothNotifications,
            ScreenshotNotifications = ScreenshotNotifications,
            DoNotDisturb = DoNotDisturb,
            FocusCompletionSound = FocusCompletionSound,
            EnableHoverExpand = EnableHoverExpand,
            HoverExpandDelayMs = (int)Math.Round(HoverExpandDelayMs),
            AutoCollapseDelayMs = (int)Math.Round(AutoCollapseDelayMs),
            CompactClickAction = CompactClickActionIndex switch { 1 => "ActivateApp", 2 => "PlayPause", _ => "Expand" },
            HoverAction = HoverActionIndex == 1 ? "ExpandCard" : "Preview",
            ScrollWheelAction = ScrollWheelActionIndex switch { 1 => "Volume", 2 => "PageSwitch", _ => "Smart" },
            AutoHideOnFullscreen = AutoHideOnFullscreen,
            DynamicColorTint = DynamicColorTint,
            ShowEqualizer = ShowEqualizer,
            ShowLyrics = ShowLyrics,
            UseTransparency = UseTransparency,
            TransparencyLevel = (int)Math.Round(TransparencyLevel),
            ArtworkTintStrength = (int)Math.Round(ArtworkTintStrength),
            MotionSpeed = (int)Math.Round(MotionSpeed),
            Theme = ThemeIndex switch { 1 => "Dark", 2 => "Light", _ => "System" },
            AnimationIntensity = AnimationIndex switch { 0 => "Minimal", 2 => "Dynamic", _ => "Normal" },
            PreferredMonitorDeviceName = _preferredMonitorDeviceName,
            TopOffset = (int)Math.Round(TopOffset),
            HorizontalOffset = (int)Math.Round(HorizontalOffset),
            IslandScale = (int)Math.Round(IslandScale),
            ShowIdleClock = ShowIdleClock,
            ShowIdleDate = ShowIdleDate,
            ShowIdleWeather = ShowIdleWeather,
            Use24HourClock = ClockFormatIndex == 0,
            TemperatureUnit = TemperatureUnitIndex == 1 ? "F" : "C",
            FocusDefaultMinutes = (int)Math.Round(FocusDefaultMinutes),
            FocusBreakMinutes = (int)Math.Round(FocusBreakMinutes),
            FocusCustomMinutes = (int)Math.Round(FocusCustomMinutes),
            EnableGlobalHotkeys = EnableGlobalHotkeys,
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _saveDebounce?.Stop();
        _settingsService.SettingsChanged -= OnExternalSettingsChanged;
    }
}
