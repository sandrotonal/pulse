using System.ComponentModel;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Pulse.Core.Services;
using Pulse.Services.Glass;
using Pulse.ViewModels;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace Pulse.Views.Flyout;

public sealed partial class TaskbarFlyoutWindow : Window
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsOverlappedWindow = 0x00CF0000L;
    private const long WsPopup = unchecked((long)(uint)0x80000000);
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExTopmost = 0x00000008L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint MonitorDefaultToNearest = 2;
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly UISettings SystemUiSettings = new();

    private readonly MediaViewModel _viewModel;
    private readonly IntPtr _hwnd;
    private readonly Visual _surfaceVisual;
    private readonly Visual _backdropVisual;
    private DispatcherQueueTimer? _exitTimer;
    private Action? _pendingExitAction;
    private double _dpiScale = 1.0;
    private bool _isVisible;
    private bool _glassEnabled;
    private bool _hasFrostBitmap;
    private long _frostGeneration;

    // Window content dimensions (DIP)
    private const double FlyoutWidth = 320;
    private const double FlyoutHeight = 436;

    public TaskbarFlyoutWindow(MediaViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        FlyoutRoot.DataContext = viewModel;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _surfaceVisual = ElementCompositionPreview.GetElementVisual(Surface);
        _backdropVisual = ElementCompositionPreview.GetElementVisual(FlyoutBackdropHost);

        // Clip glass backdrop tightly inside Surface's 1px border so frost never bleeds past corners.
        var clipGeom = _backdropVisual.Compositor.CreateRoundedRectangleGeometry();
        clipGeom.Size = new Vector2((float)(FlyoutWidth - 2), (float)(FlyoutHeight - 2));
        clipGeom.CornerRadius = new Vector2(15f, 15f);
        _backdropVisual.Clip = _backdropVisual.Compositor.CreateGeometricClip(clipGeom);

        // Enable Translation property for Composition animations
        ElementCompositionPreview.SetIsTranslationEnabled(Surface, true);

        AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        ApplyWindowStyles();
        UpdateGlassState();
        ApplyTheme(_viewModel.Theme);

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        FlyoutRoot.ActualThemeChanged += OnFlyoutRootActualThemeChanged;
        Activated += OnWindowActivated;
        Closed += OnWindowClosed;

        // Start hidden — call ShowFlyout() explicitly when needed
        ShowWindow(_hwnd, 0); // SW_HIDE
        _isVisible = false;
    }

    public bool IsFlyoutVisible => _isVisible;

    private static bool ReducedMotion => !SystemUiSettings.AnimationsEnabled;

    private double MotionDurationScale
    {
        get
        {
            var intensityScale = _viewModel.AnimationIntensity switch
            {
                "Minimal" => 0.72,
                "Dynamic" => 1.18,
                _ => 1.0
            };
            var speedScale = Math.Clamp(_viewModel.MotionSpeed, 70, 140) / 100.0;
            return intensityScale / speedScale;
        }
    }

    private TimeSpan ScaledDuration(double baseMs, double minMs = 40.0)
        => TimeSpan.FromMilliseconds(Math.Max(minMs, baseMs * MotionDurationScale));

    public void ToggleFlyout()
    {
        if (_isVisible) HideFlyout();
        else ShowFlyout();
    }

    public void ShowFlyout()
    {
        if (_isVisible) return;
        _exitTimer?.Stop();
        _pendingExitAction = null;
        _isVisible = true;

        ApplyWindowStyles();
        var bounds = RepositionNearTray();
        UpdateGlassState();
        ApplyTheme(_viewModel.Theme);

        // Capture real desktop behind bounds BEFORE ShowWindow(SW_SHOW) for zero-blink Liquid Glass.
        if (_glassEnabled && bounds.Width >= 16 && bounds.Height >= 16)
        {
            var gen = ++_frostGeneration;
            var dpiScale = _dpiScale > 0 ? _dpiScale : 1.0;
            var rawBitmap = DesktopFrostService.CaptureScreenRegion(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            if (rawBitmap is not null)
            {
                _ = ApplyCapturedFrostAsync(rawBitmap, dpiScale, gen);
            }
        }

        try
        {
            _surfaceVisual.StopAnimation("Scale");
            _surfaceVisual.StopAnimation("Translation");
            _surfaceVisual.StopAnimation("Opacity");
        }
        catch
        {
        }

        if (ReducedMotion)
        {
            _surfaceVisual.Opacity = 1f;
            _surfaceVisual.Scale = Vector3.One;
            Surface.Translation = Vector3.Zero;
            ShowWindow(_hwnd, 5); // SW_SHOW
            SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpFrameChanged);
            return;
        }

        // Reset visual state before show
        _surfaceVisual.Opacity = 0;
        _surfaceVisual.Scale = new Vector3(0.92f, 0.92f, 1f);

        ShowWindow(_hwnd, 5); // SW_SHOW
        SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpFrameChanged);

        AnimateEntrance();
    }

    public void HideFlyout()
    {
        if (!_isVisible) return;
        _isVisible = false;

        if (ReducedMotion)
        {
            _exitTimer?.Stop();
            ShowWindow(_hwnd, 0); // SW_HIDE
            return;
        }

        AnimateExit(onComplete: () =>
        {
            if (!_isVisible)
            {
                ShowWindow(_hwnd, 0); // SW_HIDE
            }
        });
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MediaViewModel.Theme)
            or nameof(MediaViewModel.DynamicColorTint)
            or nameof(MediaViewModel.ArtworkAccentColor)
            or nameof(MediaViewModel.TransparencyLevel)
            or nameof(MediaViewModel.ArtworkTintStrength))
        {
            ApplyTheme(_viewModel.Theme);
        }
        else if (e.PropertyName == nameof(MediaViewModel.UseTransparency))
        {
            UpdateGlassState();
            ApplyTheme(_viewModel.Theme);
            if (_glassEnabled && _isVisible)
            {
                _ = RefreshFrostWhileVisibleAsync();
            }
        }
    }

    private void OnFlyoutRootActualThemeChanged(FrameworkElement sender, object args)
    {
        if (string.Equals(_viewModel.Theme, "System", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(_viewModel.Theme))
        {
            ApplyTheme(_viewModel.Theme);
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_exitTimer is not null)
        {
            _exitTimer.Stop();
            _exitTimer.Tick -= OnExitTimerTick;
        }
        _pendingExitAction = null;
        _frostGeneration++;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        FlyoutRoot.ActualThemeChanged -= OnFlyoutRootActualThemeChanged;
        Activated -= OnWindowActivated;
    }

    private void UpdateGlassState()
    {
        _glassEnabled = _viewModel.UseTransparency;
        if (!_glassEnabled)
        {
            _frostGeneration++;
            _hasFrostBitmap = false;
            if (FrostLayer is not null)
            {
                FrostLayer.Fill = null;
            }
        }

        var visible = _glassEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (FrostLayer is not null) FrostLayer.Visibility = visible;
        if (FrostTintLayer is not null) FrostTintLayer.Visibility = visible;
        if (FlyoutSheen is not null) FlyoutSheen.Visibility = visible;
    }

    private async Task ApplyCapturedFrostAsync(Bitmap rawBitmap, double dpiScale, long generation)
    {
        try
        {
            var pngBytes = await Task.Run(() =>
            {
                using (rawBitmap)
                {
                    return DesktopFrostService.ProcessCapturedFrostToPng(rawBitmap, dpiScale, blurRadius: 12, vibrancyBoost: 1.24);
                }
            });

            if (generation != _frostGeneration || !_glassEnabled || FrostLayer is null)
            {
                return;
            }

            var image = await DesktopFrostService.DecodePngToBitmapImageAsync(pngBytes);
            if (generation != _frostGeneration || !_glassEnabled || FrostLayer is null)
            {
                return;
            }

            FrostLayer.Fill = new ImageBrush
            {
                ImageSource = image,
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Top
            };
            _hasFrostBitmap = true;
            ApplyTheme(_viewModel.Theme);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("TaskbarFlyoutWindow.ApplyCapturedFrostAsync", ex);
        }
    }

    private async Task RefreshFrostWhileVisibleAsync()
    {
        if (!_glassEnabled || !_isVisible || _hwnd == IntPtr.Zero)
        {
            return;
        }

        var gen = ++_frostGeneration;
        try
        {
            var bounds = RepositionNearTray();
            if (bounds.Width < 16 || bounds.Height < 16)
            {
                return;
            }

            var dpiScale = _dpiScale > 0 ? _dpiScale : 1.0;
            Bitmap? rawBitmap = null;
            try
            {
                ShowWindow(_hwnd, 0); // SW_HIDE
                await Task.Delay(40);
                if (_isVisible && _glassEnabled && gen == _frostGeneration)
                {
                    rawBitmap = DesktopFrostService.CaptureScreenRegion(bounds.X, bounds.Y, bounds.Width, bounds.Height);
                }
            }
            finally
            {
                if (_isVisible)
                {
                    ShowWindow(_hwnd, 5); // SW_SHOW
                    SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
                }
            }

            if (rawBitmap is not null)
            {
                await ApplyCapturedFrostAsync(rawBitmap, dpiScale, gen);
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("TaskbarFlyoutWindow.RefreshFrostWhileVisibleAsync", ex);
        }
    }

    private void AnimateEntrance()
    {
        try
        {
            var compositor = _surfaceVisual.Compositor;
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));

            // Scale: 0.92 -> 1.0 with subtle overshoot
            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.Duration = ScaledDuration(280, minMs: 90);
            scaleAnim.InsertKeyFrame(0f, new Vector3(0.92f, 0.92f, 1f));
            scaleAnim.InsertKeyFrame(0.75f, new Vector3(1.014f, 1.014f, 1f), easing);
            scaleAnim.InsertKeyFrame(1f, Vector3.One, easing);

            // Translate: slide up 12px from bottom
            var translateAnim = compositor.CreateVector3KeyFrameAnimation();
            translateAnim.Duration = ScaledDuration(280, minMs: 90);
            translateAnim.InsertKeyFrame(0f, new Vector3(0, 12f, 0));
            translateAnim.InsertKeyFrame(1f, Vector3.Zero, easing);

            // Opacity: 0 -> 1
            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.Duration = ScaledDuration(180, minMs: 60);
            opacityAnim.InsertKeyFrame(0f, 0f);
            opacityAnim.InsertKeyFrame(1f, 1f, easing);

            // Anchor scale center at bottom (so it expands upward, like macOS dock)
            _surfaceVisual.CenterPoint = new Vector3((float)(FlyoutWidth / 2), (float)FlyoutHeight, 0);

            _surfaceVisual.StartAnimation("Scale", scaleAnim);
            _surfaceVisual.StartAnimation("Translation", translateAnim);
            _surfaceVisual.StartAnimation("Opacity", opacityAnim);
        }
        catch
        {
            _surfaceVisual.Opacity = 1;
            _surfaceVisual.Scale = Vector3.One;
        }
    }

    private void AnimateExit(Action onComplete)
    {
        try
        {
            var compositor = _surfaceVisual.Compositor;
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(1f, 1f));
            var exitDur = ScaledDuration(200, minMs: 70);

            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.Duration = exitDur;
            scaleAnim.InsertKeyFrame(1f, new Vector3(0.9f, 0.9f, 1f), easing);

            var translateAnim = compositor.CreateVector3KeyFrameAnimation();
            translateAnim.Duration = exitDur;
            translateAnim.InsertKeyFrame(1f, new Vector3(0, 10f, 0), easing);

            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.Duration = ScaledDuration(160, minMs: 55);
            opacityAnim.InsertKeyFrame(1f, 0f, easing);

            _surfaceVisual.CenterPoint = new Vector3((float)(FlyoutWidth / 2), (float)FlyoutHeight, 0);
            _surfaceVisual.StartAnimation("Scale", scaleAnim);
            _surfaceVisual.StartAnimation("Translation", translateAnim);
            _surfaceVisual.StartAnimation("Opacity", opacityAnim);

            if (_exitTimer is null)
            {
                _exitTimer = DispatcherQueue.CreateTimer();
                _exitTimer.IsRepeating = false;
                _exitTimer.Tick += OnExitTimerTick;
            }
            _exitTimer.Stop();
            _pendingExitAction = onComplete;
            _exitTimer.Interval = exitDur + TimeSpan.FromMilliseconds(15);
            _exitTimer.Start();
        }
        catch
        {
            onComplete();
        }
    }

    private void OnExitTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        var action = _pendingExitAction;
        _pendingExitAction = null;
        action?.Invoke();
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        // Auto-dismiss when user clicks outside
        if (args.WindowActivationState == WindowActivationState.Deactivated && _isVisible)
        {
            HideFlyout();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => HideFlyout();

    private void OnSeekBarScrubStarted(object? sender, double percent) => _viewModel.BeginScrub(percent);
    private void OnSeekBarScrubDelta(object? sender, double percent) => _viewModel.UpdateScrub(percent);
    private async void OnSeekBarScrubCompleted(object? sender, double percent) => await _viewModel.EndScrubAsync(percent);

    public void ApplyTheme(string theme)
    {
        var requestedTheme = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase)
            ? ElementTheme.Dark
            : string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
                ? ElementTheme.Light
                : ElementTheme.Default;
        if (FlyoutRoot.RequestedTheme != requestedTheme)
        {
            FlyoutRoot.RequestedTheme = requestedTheme;
        }

        var isLight = DesktopFrostService.ResolveIsLightTheme(theme);
        var hasFrostBitmap = _glassEnabled && _hasFrostBitmap && FrostLayer?.Fill is ImageBrush;
        var palette = LiquidGlassEngine.ComputeIslandPalette(
            isLightTheme: isLight,
            useTransparency: _glassEnabled,
            transparencyLevel: _viewModel.TransparencyLevel,
            dynamicColorTint: _viewModel.DynamicColorTint,
            artworkAccentColor: _viewModel.ArtworkAccentColor,
            artworkTintStrength: _viewModel.ArtworkTintStrength,
            hasFrostBitmap: hasFrostBitmap);

        DesktopFrostService.UpdateBrush(FlyoutRoot.Resources, "FlyoutBackground", palette.FallbackBackgroundArgb);
        DesktopFrostService.UpdateBrush(FlyoutRoot.Resources, "FlyoutBorder", palette.BorderRimTopArgb);
        DesktopFrostService.UpdateBrush(FlyoutRoot.Resources, "PrimaryText", palette.PrimaryTextArgb);
        DesktopFrostService.UpdateBrush(FlyoutRoot.Resources, "SecondaryText", palette.SecondaryTextArgb);
        DesktopFrostService.UpdateBrush(FlyoutRoot.Resources, "ControlBackground", palette.PillControlBackgroundArgb);
        DesktopFrostService.UpdateBrush(FlyoutRoot.Resources, "PlayButtonBackground", palette.PlayButtonBackgroundArgb);
        DesktopFrostService.UpdateBrush(FlyoutRoot.Resources, "PlayButtonForeground", palette.PlayButtonForegroundArgb);
        DesktopFrostService.UpdateBrush(FlyoutRoot.Resources, "Accent", palette.AccentArgb);
        DesktopFrostService.UpdateBrush(FlyoutRoot.Resources, "SeekAccent", palette.AccentArgb);

        Surface.Background = DesktopFrostService.CreateBrush(palette.SurfaceBackgroundArgb);
        Surface.BorderBrush = DesktopFrostService.CreateVerticalGradientBrush(palette.BorderRimTopArgb, palette.BorderRimBottomArgb);

        if (FrostTintLayer is not null)
        {
            FrostTintLayer.Fill = DesktopFrostService.CreateBrush(palette.FrostTintRgb);
            FrostTintLayer.Opacity = hasFrostBitmap ? palette.FrostTintOpacity : 0.0;
        }

        if (FlyoutSheenTopStop is not null)
        {
            FlyoutSheenTopStop.Color = DesktopFrostService.ToColor(palette.SheenTopArgb);
        }

        if (FlyoutSeekBar is not null)
        {
            FlyoutSeekBar.AccentBrush = DesktopFrostService.CreateBrush(palette.AccentArgb);
        }
    }

    private void ApplyWindowStyles()
    {
        var style = GetWindowLongPtr(_hwnd, GwlStyle).ToInt64();
        SetWindowLongPtr(_hwnd, GwlStyle, new IntPtr((style & ~WsOverlappedWindow) | WsPopup));

        var exStyle = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(exStyle | WsExToolWindow | WsExTopmost));

        // Remove DWM window border and disable rounded corners
        int dwmBorderNone = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(_hwnd, 34, ref dwmBorderNone, sizeof(int));
        int doNotRound = 1;
        DwmSetWindowAttribute(_hwnd, 33, ref doNotRound, sizeof(int));

        SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
    }

    private (int X, int Y, int Width, int Height) RepositionNearTray()
    {
        if (_hwnd == IntPtr.Zero) return (0, 0, 0, 0);

        IntPtr monitor = IntPtr.Zero;
        if (GetCursorPos(out var cursor))
            monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            monitor = MonitorFromWindow(_hwnd, MonitorDefaultToNearest);

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return (0, 0, 0, 0);

        _dpiScale = Math.Max(1u, GetDpiForWindow(_hwnd)) / 96.0;
        var workArea = info.WorkArea;

        var width = (int)Math.Round(FlyoutWidth * _dpiScale);
        var height = (int)Math.Round(FlyoutHeight * _dpiScale);
        var margin = (int)Math.Round(12.0 * _dpiScale);

        // Bottom-right corner above taskbar
        var x = workArea.Right - width - margin;
        var y = workArea.Bottom - height - margin;

        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(x, y));
        return (x, y, width, height);
    }

    // --- P/Invoke ---

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point pt);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Win32Point pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
    }
}
