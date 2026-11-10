using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pulse.Core.Services;
using Pulse.Services.Glass;
using Pulse.ViewModels;
using Windows.Graphics;

namespace Pulse.Views.Settings;

public sealed partial class SettingsWindow : Window
{
    private const uint DwmwaUseImmersiveDarkMode = 20;
    private readonly IntPtr _hwnd;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        SettingsRoot.DataContext = viewModel;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        FitToWorkArea();
        ConfigureTitleBarChrome();

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // Non-fatal if icon asset is unavailable.
        }

        ApplyTheme(viewModel.ThemeIndex);

        viewModel.PropertyChanged += OnSettingsPropertyChanged;
        SettingsRoot.ActualThemeChanged += OnActualThemeChanged;
        Closed += (_, _) =>
        {
            viewModel.PropertyChanged -= OnSettingsPropertyChanged;
            SettingsRoot.ActualThemeChanged -= OnActualThemeChanged;
            _ = FlushAndDisposeAsync(viewModel);
        };
        Activated += async (_, _) =>
        {
            try
            {
                await viewModel.InitializeAsync();
                ApplyTheme(viewModel.ThemeIndex);
            }
            catch (Exception ex)
            {
                CrashLogger.Write("SettingsWindow.Activated", ex);
            }
        };
    }

    private void ConfigureTitleBarChrome()
    {
        var titleBar = AppWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.BackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.InactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        SetTitleBar(TitleBarDragRegion);
    }

    private static async Task FlushAndDisposeAsync(SettingsViewModel viewModel)
    {
        try
        {
            await viewModel.FlushPendingSaveAsync();
        }
        catch (Exception ex)
        {
            CrashLogger.Write("SettingsWindow.FlushAndDisposeAsync", ex);
        }
        finally
        {
            viewModel.Dispose();
        }
    }

    private void FitToWorkArea()
    {
        var dpiScale = Math.Max(1u, GetDpiForWindow(_hwnd)) / 96.0;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var margin = (int)Math.Round(16 * dpiScale);

        // Keep the footer on-screen even at 125%/150% scaling or when Windows
        // restores the window lower than the primary work area.
        var width = Math.Min(
            (int)Math.Round(520 * dpiScale),
            Math.Max(1, workArea.Width - margin * 2));
        var height = Math.Min(
            (int)Math.Round(700 * dpiScale),
            Math.Max(1, workArea.Height - margin * 2));

        var x = workArea.X + (workArea.Width - width) / 2;
        var y = workArea.Y + Math.Max(margin, (workArea.Height - height) / 2);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SettingsViewModel.ThemeIndex) && sender is SettingsViewModel viewModel)
        {
            ApplyTheme(viewModel.ThemeIndex);
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || SettingsRoot.DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        button.IsEnabled = false;
        try
        {
            await viewModel.SaveNowAsync();
        }
        catch (Exception ex)
        {
            CrashLogger.Write("SettingsWindow.OnSaveClick", ex);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void OnRestoreDefaultsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || SettingsRoot.DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        if (SettingsRoot.XamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = SettingsRoot.XamlRoot,
            RequestedTheme = SettingsRoot.ActualTheme,
            Title = "Varsayılana dönülsün mü?",
            Content = "Tüm ayarlar ilk kurulum değerlerine sıfırlanacak. Bu işlem geri alınamaz.",
            PrimaryButtonText = "Sıfırla",
            CloseButtonText = "Vazgeç",
            DefaultButton = ContentDialogButton.Primary
        };

        button.IsEnabled = false;
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await viewModel.RestoreDefaultsAsync();
        }
        catch (Exception ex)
        {
            CrashLogger.Write("SettingsWindow.OnRestoreDefaultsClick", ex);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void ApplyTheme(int themeIndex)
    {
        var requestedTheme = themeIndex switch
        {
            1 => ElementTheme.Dark,
            2 => ElementTheme.Light,
            _ => ElementTheme.Default
        };

        // ResolveLight state cleanly from OS when System (0) is chosen so scalars & caption chrome stay synced.
        var isLight = themeIndex switch
        {
            1 => false,
            2 => true,
            _ => DesktopFrostService.IsSystemLightTheme()
        };

        var accent = isLight ? 0xFF1E7E34u : 0xFF30D158u;
        SettingsRoot.Resources["SystemAccentColor"] = DesktopFrostService.ToColor(accent);
        SettingsRoot.Resources["SystemAccentColorLight1"] = DesktopFrostService.ToColor(LiquidGlassEngine.BlendRgb(accent, 0xFFFFFFFFu, 0.25));
        SettingsRoot.Resources["SystemAccentColorLight2"] = DesktopFrostService.ToColor(LiquidGlassEngine.BlendRgb(accent, 0xFFFFFFFFu, 0.45));
        SettingsRoot.Resources["SystemAccentColorLight3"] = DesktopFrostService.ToColor(LiquidGlassEngine.BlendRgb(accent, 0xFFFFFFFFu, 0.65));
        SettingsRoot.Resources["SystemAccentColorDark1"] = DesktopFrostService.ToColor(LiquidGlassEngine.BlendRgb(accent, 0xFF000000u, 0.25));
        SettingsRoot.Resources["SystemAccentColorDark2"] = DesktopFrostService.ToColor(LiquidGlassEngine.BlendRgb(accent, 0xFF000000u, 0.45));
        SettingsRoot.Resources["SystemAccentColorDark3"] = DesktopFrostService.ToColor(LiquidGlassEngine.BlendRgb(accent, 0xFF000000u, 0.65));

        // Flip RequestedTheme AFTER ThemeDictionaries/SystemAccentColor are ready so WinUI's
        // native ThemeResource engine walks all bindings cleanly in a single pass.
        if (SettingsRoot.RequestedTheme != requestedTheme)
        {
            SettingsRoot.RequestedTheme = requestedTheme;
        }

        UpdateTitleBarChrome(isLight);
    }

    private void UpdateTitleBarChrome(bool isLight)
    {
        var titleBar = AppWindow.TitleBar;
        titleBar.BackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.InactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonForegroundColor = DesktopFrostService.ToColor(isLight ? 0xFF1C1C1Eu : 0xFFF5F5F7u);
        titleBar.ButtonInactiveForegroundColor = DesktopFrostService.ToColor(isLight ? 0xFF5A5A60u : 0xFFA6AAB4u);
        titleBar.ButtonHoverBackgroundColor = DesktopFrostService.ToColor(isLight ? 0x1A000000u : 0x1FFFFFFFu);
        titleBar.ButtonHoverForegroundColor = DesktopFrostService.ToColor(isLight ? 0xFF000000u : 0xFFFFFFFFu);
        titleBar.ButtonPressedBackgroundColor = DesktopFrostService.ToColor(isLight ? 0x29000000u : 0x33FFFFFFu);
        titleBar.ButtonPressedForegroundColor = DesktopFrostService.ToColor(isLight ? 0xFF000000u : 0xFFFFFFFFu);

        if (_hwnd != IntPtr.Zero)
        {
            var useDarkMode = isLight ? 0 : 1;
            _ = DwmSetWindowAttribute(_hwnd, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int));
        }
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (SettingsRoot.RequestedTheme == ElementTheme.Default)
        {
            ApplyTheme(0);
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref int pvAttribute, int cbAttribute);
}
