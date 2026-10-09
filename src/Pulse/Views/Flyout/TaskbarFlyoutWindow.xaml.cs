using System.Runtime.InteropServices;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Pulse.ViewModels;
using Windows.Graphics;

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

    private readonly MediaViewModel _viewModel;
    private readonly IntPtr _hwnd;
    private readonly Visual _surfaceVisual;
    private double _dpiScale = 1.0;
    private bool _isVisible;

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
        ApplyTheme(_viewModel.Theme);

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MediaViewModel.Theme))
                ApplyTheme(_viewModel.Theme);
        };

        Activated += OnWindowActivated;

        // Start hidden — call ShowFlyout() explicitly when needed
        ShowWindow(_hwnd, 0); // SW_HIDE
        _isVisible = false;
    }

    public bool IsFlyoutVisible => _isVisible;

    public void ToggleFlyout()
    {
        if (_isVisible) HideFlyout();
        else ShowFlyout();
    }

    public void ShowFlyout()
    {
        if (_isVisible) return;
        _isVisible = true;

        ApplyWindowStyles();
        RepositionNearTray();

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

        AnimateExit(onComplete: () =>
        {
            ShowWindow(_hwnd, 0); // SW_HIDE
        });
    }

    private void AnimateEntrance()
    {
        try
        {
            var compositor = _surfaceVisual.Compositor;
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));

            // Scale: 0.92 → 1.0 with subtle overshoot
            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.Duration = TimeSpan.FromMilliseconds(280);
            scaleAnim.InsertKeyFrame(0f, new Vector3(0.92f, 0.92f, 1f));
            scaleAnim.InsertKeyFrame(0.75f, new Vector3(1.014f, 1.014f, 1f), easing);
            scaleAnim.InsertKeyFrame(1f, Vector3.One, easing);

            // Translate: slide up 12px from bottom
            var translateAnim = compositor.CreateVector3KeyFrameAnimation();
            translateAnim.Duration = TimeSpan.FromMilliseconds(280);
            translateAnim.InsertKeyFrame(0f, new Vector3(0, 12f, 0));
            translateAnim.InsertKeyFrame(1f, Vector3.Zero, easing);

            // Opacity: 0 → 1
            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.Duration = TimeSpan.FromMilliseconds(180);
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

            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.Duration = TimeSpan.FromMilliseconds(200);
            scaleAnim.InsertKeyFrame(1f, new Vector3(0.9f, 0.9f, 1f), easing);

            var translateAnim = compositor.CreateVector3KeyFrameAnimation();
            translateAnim.Duration = TimeSpan.FromMilliseconds(200);
            translateAnim.InsertKeyFrame(1f, new Vector3(0, 10f, 0), easing);

            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.Duration = TimeSpan.FromMilliseconds(160);
            opacityAnim.InsertKeyFrame(1f, 0f, easing);

            _surfaceVisual.CenterPoint = new Vector3((float)(FlyoutWidth / 2), (float)FlyoutHeight, 0);
            _surfaceVisual.StartAnimation("Scale", scaleAnim);
            _surfaceVisual.StartAnimation("Translation", translateAnim);
            _surfaceVisual.StartAnimation("Opacity", opacityAnim);

            // Wait for animation then call back
            var timer = this.DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(210);
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                onComplete();
            };
            timer.Start();
        }
        catch
        {
            onComplete();
        }
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
        var isLight = theme == "Light";
        FlyoutRoot.RequestedTheme = isLight ? ElementTheme.Light : ElementTheme.Dark;

        // Dark: deep charcoal with strong transparency — Apple Control Center style
        var bg        = isLight ? 0xF5FFFFFFu : 0xF0141416u;
        var border    = isLight ? 0x18000000u : 0x22FFFFFFu;
        var primary   = isLight ? 0xFF1C1C1Eu : 0xFFFFFFFFu;
        var secondary = isLight ? 0xFF8E8E93u : 0xA0EBEBEBu;
        var controlBg = isLight ? 0xFFE5E5EAu : 0x18FFFFFFu;
        var playBg    = isLight ? 0xFF1DB954u : 0xFFFFFFFFu;
        var playFg    = isLight ? 0xFFFFFFFFu : 0xFF000000u;
        var accent    = 0xFF1DB954u; // Spotify green
        var seekAcc   = 0xFF1DB954u;

        UpdateBrush("FlyoutBackground", bg);
        UpdateBrush("FlyoutBorder", border);
        UpdateBrush("PrimaryText", primary);
        UpdateBrush("SecondaryText", secondary);
        UpdateBrush("ControlBackground", controlBg);
        UpdateBrush("PlayButtonBackground", playBg);
        UpdateBrush("PlayButtonForeground", playFg);
        UpdateBrush("Accent", accent);
        UpdateBrush("SeekAccent", seekAcc);

        Surface.Background = Brush(bg);
        Surface.BorderBrush = Brush(border);
    }

    private void UpdateBrush(string key, uint color)
    {
        var c = ToColor(color);
        if (FlyoutRoot.Resources.TryGetValue(key, out var res) && res is Microsoft.UI.Xaml.Media.SolidColorBrush brush)
            brush.Color = c;
        else
            FlyoutRoot.Resources[key] = new Microsoft.UI.Xaml.Media.SolidColorBrush(c);
    }

    private static Microsoft.UI.Xaml.Media.SolidColorBrush Brush(uint color)
        => new(Microsoft.UI.Colors.Transparent) { Color = ToColor(color) };

    private static Windows.UI.Color ToColor(uint color)
        => Windows.UI.Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color);

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

    private void RepositionNearTray()
    {
        if (_hwnd == IntPtr.Zero) return;

        IntPtr monitor = IntPtr.Zero;
        if (GetCursorPos(out var cursor))
            monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            monitor = MonitorFromWindow(_hwnd, MonitorDefaultToNearest);

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;

        _dpiScale = Math.Max(1u, GetDpiForWindow(_hwnd)) / 96.0;
        var workArea = info.WorkArea;

        var width  = (int)Math.Round(FlyoutWidth  * _dpiScale);
        var height = (int)Math.Round(FlyoutHeight * _dpiScale);
        var margin = (int)Math.Round(12.0 * _dpiScale);

        // Bottom-right corner above taskbar
        var x = workArea.Right  - width  - margin;
        var y = workArea.Bottom - height - margin;

        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(x, y));
    }

    // ─── P/Invoke ────────────────────────────────────────────────────────────

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
