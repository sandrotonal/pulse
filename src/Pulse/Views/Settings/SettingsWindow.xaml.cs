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
        SettingsRoot.Resources["SettingsDivider"] = Brush(isLight ? 0x26000000u : 0x26FFFFFFu);
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

    private static Windows.UI.Color Color(uint color)
        => Windows.UI.Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color);
}
