using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System.ComponentModel;
using Pulse.ViewModels;
using Windows.Graphics;

namespace Pulse.Views.Settings;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        SettingsRoot.DataContext = viewModel;
        AppWindow.Resize(new SizeInt32(520, 700));
        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        SetTitleBar(TitleBarDragRegion);
        try
        {
            var iconPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (System.IO.File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }
        catch { }
        viewModel.PropertyChanged += OnSettingsPropertyChanged;
        SettingsRoot.ActualThemeChanged += OnActualThemeChanged;
        Closed += (_, _) =>
        {
            viewModel.PropertyChanged -= OnSettingsPropertyChanged;
            SettingsRoot.ActualThemeChanged -= OnActualThemeChanged;
        };
        Activated += async (_, _) =>
        {
            await viewModel.InitializeAsync();
            ApplyTheme(viewModel.ThemeIndex);
        };
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SettingsViewModel.ThemeIndex) && sender is SettingsViewModel viewModel)
        {
            ApplyTheme(viewModel.ThemeIndex);
        }
    }

    private void ApplyTheme(int themeIndex)
    {
        SettingsRoot.RequestedTheme = themeIndex switch
        {
            1 => ElementTheme.Dark,
            2 => ElementTheme.Light,
            _ => ElementTheme.Default
        };

        var isLight = themeIndex switch
        {
            1 => false,
            2 => true,
            _ => SettingsRoot.ActualTheme != ElementTheme.Dark
        };
        SettingsRoot.Resources["SettingsText"] = Brush(isLight ? 0xFF1C1C1E : 0xFFF5F5F7);
        SettingsRoot.Resources["SettingsSecondary"] = Brush(isLight ? 0xFF68686E : 0xFFAEAEB2);
        var accent = isLight ? 0xFF248A3Du : 0xFF30D158u;
        SettingsRoot.Resources["SettingsAccent"] = Brush(accent);
        SettingsRoot.Resources["SystemAccentColor"] = Color(accent);
        SettingsRoot.Resources["SystemAccentColorLight1"] = Color(Mix(accent, 0xFFFFFFFFu, 0.25));
        SettingsRoot.Resources["SystemAccentColorLight2"] = Color(Mix(accent, 0xFFFFFFFFu, 0.45));
        SettingsRoot.Resources["SystemAccentColorLight3"] = Color(Mix(accent, 0xFFFFFFFFu, 0.65));
        SettingsRoot.Resources["SystemAccentColorDark1"] = Color(Mix(accent, 0xFF000000u, 0.25));
        SettingsRoot.Resources["SystemAccentColorDark2"] = Color(Mix(accent, 0xFF000000u, 0.45));
        SettingsRoot.Resources["SystemAccentColorDark3"] = Color(Mix(accent, 0xFF000000u, 0.65));
        SettingsRoot.Resources["SettingsDivider"] = Brush(isLight ? 0x26000000u : 0x26FFFFFFu);
        SettingsRoot.Resources["SettingsCard"] = Brush(isLight ? 0x0D808080u : 0x14FFFFFFu);
        var neutral = Brush(isLight ? 0xFFE5E5EAu : 0xFF3A3A3Cu);
        var knob = Brush(isLight ? 0xFFFFFFFFu : 0xFFF5F5F7u);
        SettingsRoot.Resources["AccentFillColorDefaultBrush"] = Brush(accent);
        SettingsRoot.Resources["AccentFillColorSecondaryBrush"] = Brush(isLight ? 0xFF1F7A36u : 0xFF28C74F);
        SettingsRoot.Resources["AccentFillColorTertiaryBrush"] = Brush(isLight ? 0xFF19662Du : 0xFF22B847u);
        SettingsRoot.Resources["SliderTrackValueFill"] = Brush(accent);
        SettingsRoot.Resources["SliderTrackValueFillPointerOver"] = Brush(accent);
        SettingsRoot.Resources["SliderTrackValueFillPressed"] = Brush(accent);
        SettingsRoot.Resources["SliderTrackValueFillDisabled"] = neutral;
        SettingsRoot.Resources["SliderTrackFill"] = neutral;
        SettingsRoot.Resources["SliderThumbBackground"] = knob;
        SettingsRoot.Resources["SliderThumbBackgroundPointerOver"] = knob;
        SettingsRoot.Resources["SliderThumbBackgroundPressed"] = knob;
        SettingsRoot.Resources["ToggleSwitchFillOn"] = Brush(accent);
        SettingsRoot.Resources["ToggleSwitchFillOnPointerOver"] = Brush(accent);
        SettingsRoot.Resources["ToggleSwitchFillOnPressed"] = Brush(accent);
        SettingsRoot.Resources["ToggleSwitchKnobFillOn"] = knob;
        SettingsRoot.Resources["ToggleSwitchKnobFillOff"] = knob;
        SettingsRoot.Resources["ToggleSwitchFillOff"] = neutral;
        SettingsRoot.Resources["ToggleSwitchFillOffPointerOver"] = neutral;
        SettingsRoot.Resources["ToggleSwitchFillOffPressed"] = neutral;
        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonForegroundColor = Color(isLight ? 0xFF1C1C1Eu : 0xFFF5F5F7u);
        titleBar.ButtonHoverBackgroundColor = Color(isLight ? 0xFFE5E5EAu : 0xFF3A3A3Cu);
        titleBar.ButtonPressedBackgroundColor = Color(isLight ? 0xFFD1D1D6u : 0xFF48484Au);
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (SettingsRoot.RequestedTheme == ElementTheme.Default)
        {
            ApplyTheme(0);
        }
    }

    private static Microsoft.UI.Xaml.Media.SolidColorBrush Brush(uint color)
        => new() { Color = Color(color) };

    private static uint Mix(uint from, uint to, double t)
    {
        double fromR = (from >> 16) & 0xFF, toR = (to >> 16) & 0xFF;
        double fromG = (from >> 8) & 0xFF, toG = (to >> 8) & 0xFF;
        double fromB = from & 0xFF, toB = to & 0xFF;
        uint R = (uint)Math.Round(fromR + (toR - fromR) * t);
        uint G = (uint)Math.Round(fromG + (toG - fromG) * t);
        uint B = (uint)Math.Round(fromB + (toB - fromB) * t);
        return 0xFF000000u | (R << 16) | (G << 8) | B;
    }

    private static Windows.UI.Color Color(uint color)
        => Windows.UI.Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color);
}
