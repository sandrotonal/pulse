using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Pulse.Core.Models;
using Pulse.ViewModels;
using Windows.Graphics;

namespace Pulse.Views.Island;

public sealed partial class IslandWindow : Window
{
    // Win32 constants
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsOverlappedWindow = 0x00CF0000L;
    private const long WsPopup = unchecked((long)(uint)0x80000000);
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExTopmost = 0x00000008L;
    private const long WsExLayered = 0x00080000L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint MonitorDefaultToNearest = 2;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly MediaViewModel _viewModel;
    private readonly IntPtr _hwnd;
    private readonly DispatcherQueueTimer _waveTimer;
    private readonly Visual _surfaceVisual;
    private readonly Visual _compactVisual;
    private readonly Visual _expandedVisual;

    private double _dpiScale = 1.0;
    private int _wavePhase;
    private bool _isIslandHidden;
    private double _currentPillWidth = 216;
    private double _currentPillHeight = 36;

    // Animation batch tracking — prevents conflicting animations
    private CompositionScopedBatch? _morphBatch;

    public event EventHandler? SettingsRequested;
    public event EventHandler? TaskbarFlyoutRequested;

    public IslandWindow(MediaViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        WindowRoot.DataContext = viewModel;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        _surfaceVisual = ElementCompositionPreview.GetElementVisual(Surface);
        _compactVisual = ElementCompositionPreview.GetElementVisual(CompactView);
        _expandedVisual = ElementCompositionPreview.GetElementVisual(ExpandedView);

        // Enable use of Composition Translation property
        ElementCompositionPreview.SetIsTranslationEnabled(Surface, true);
        ElementCompositionPreview.SetIsTranslationEnabled(CompactView, true);
        ElementCompositionPreview.SetIsTranslationEnabled(ExpandedView, true);

        _waveTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _waveTimer.Interval = TimeSpan.FromMilliseconds(160);
        _waveTimer.Tick += OnWaveTimerTick;

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

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Apply initial layout without animation
        UpdateStateLayout(_viewModel.CurrentIslandState, animated: false);

        if (_viewModel.CurrentTrack?.PlaybackStatus == PlaybackStatus.Playing)
        {
            _waveTimer.Start();
        }
    }

    public XamlRoot IslandXamlRoot => WindowRoot.XamlRoot;
    public bool IsIslandHidden => _isIslandHidden;

    public void ShowIsland()
    {
        if (_isIslandHidden) UnhideIslandAnimated();
        ShowWindow(_hwnd, 4); // SW_SHOWNOACTIVATE
        ApplyWindowStyles();
        PositionAndResizeWindow(_currentPillWidth, _currentPillHeight);
    }

    public void HideIslandAnimated()
    {
        if (_isIslandHidden) return;
        _isIslandHidden = true;

        try
        {
            var compositor = _surfaceVisual.Compositor;
            var duration = TimeSpan.FromMilliseconds(200);
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(1f, 1f));

            // Animate out: scale down + fade
            _surfaceVisual.CenterPoint = new Vector3((float)(_currentPillWidth / 2), (float)(_currentPillHeight / 2), 0);

            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.Duration = duration;
            scaleAnim.InsertKeyFrame(1f, new Vector3(0.7f, 0.7f, 1f), easing);

            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.Duration = duration;
            opacityAnim.InsertKeyFrame(1f, 0f, easing);

            var translateAnim = compositor.CreateVector3KeyFrameAnimation();
            translateAnim.Duration = duration;
            translateAnim.InsertKeyFrame(1f, new Vector3(0, -20, 0), easing);

            _surfaceVisual.StartAnimation("Scale", scaleAnim);
            _surfaceVisual.StartAnimation("Opacity", opacityAnim);
            _surfaceVisual.StartAnimation("Translation", translateAnim);
        }
        catch { /* defensive */ }

        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(210);
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            if (_isIslandHidden)
            {
                Surface.Visibility = Visibility.Collapsed;
                HiddenAnchor.Visibility = Visibility.Visible;
                PositionAndResizeWindow(48, 8);
            }
        };
        timer.Start();
    }

    public void UnhideIslandAnimated()
    {
        if (!_isIslandHidden) return;
        _isIslandHidden = false;

        HiddenAnchor.Visibility = Visibility.Collapsed;
        Surface.Visibility = Visibility.Visible;
        PositionAndResizeWindow(_currentPillWidth, _currentPillHeight);

        try
        {
            var compositor = _surfaceVisual.Compositor;
            var duration = TimeSpan.FromMilliseconds(360);
            // Apple spring easing: fast ease-out with subtle overshoot
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));

            _surfaceVisual.CenterPoint = new Vector3((float)(_currentPillWidth / 2), (float)(_currentPillHeight / 2), 0);

            // Reset translation first
            _surfaceVisual.Properties.InsertVector3("Translation", new Vector3(0, -30, 0));

            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.Duration = duration;
            scaleAnim.InsertKeyFrame(0f, new Vector3(0.6f, 0.6f, 1f));
            scaleAnim.InsertKeyFrame(0.7f, new Vector3(1.04f, 1.04f, 1f), easing);
            scaleAnim.InsertKeyFrame(1f, Vector3.One, easing);

            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.Duration = TimeSpan.FromMilliseconds(180);
            opacityAnim.InsertKeyFrame(0f, 0f);
            opacityAnim.InsertKeyFrame(1f, 1f, easing);

            var translateAnim = compositor.CreateVector3KeyFrameAnimation();
            translateAnim.Duration = duration;
            translateAnim.InsertKeyFrame(0f, new Vector3(0, -30, 0));
            translateAnim.InsertKeyFrame(1f, Vector3.Zero, easing);

            _surfaceVisual.StartAnimation("Scale", scaleAnim);
            _surfaceVisual.StartAnimation("Opacity", opacityAnim);
            _surfaceVisual.StartAnimation("Translation", translateAnim);
        }
        catch
        {
            _surfaceVisual.Opacity = 1;
            _surfaceVisual.Scale = Vector3.One;
        }
    }

    public void ToggleHideIsland()
    {
        if (_isIslandHidden) UnhideIslandAnimated();
        else HideIslandAnimated();
    }

    private void PositionAndResizeWindow(double widthDip, double heightDip)
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

        var winWidth = (int)Math.Round(widthDip * _dpiScale);
        var winHeight = (int)Math.Round(heightDip * _dpiScale);

        WindowRoot.Width = widthDip;
        WindowRoot.Height = heightDip;

        var x = workArea.Left + Math.Max(0, (workArea.Right - workArea.Left - winWidth) / 2);
        var y = workArea.Top + (int)Math.Round(_viewModel.TopOffset * _dpiScale);

        AppWindow.Resize(new SizeInt32(winWidth, winHeight));
        AppWindow.Move(new PointInt32(x, y));
        SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaViewModel.CurrentIslandState))
        {
            UpdateStateLayout(_viewModel.CurrentIslandState, animated: true);
        }
        else if (e.PropertyName == nameof(MediaViewModel.CurrentTrack))
        {
            if (_viewModel.CurrentTrack?.PlaybackStatus == PlaybackStatus.Playing)
            {
                if (!_waveTimer.IsRunning) _waveTimer.Start();
            }
            else
            {
                _waveTimer.Stop();
                ResetWaveBars();
            }
        }
        else if (e.PropertyName == nameof(MediaViewModel.Theme))
        {
            ApplyTheme(_viewModel.Theme);
        }
    }

    private void UpdateStateLayout(IslandState state, bool animated)
    {
        var isExpanded = state == IslandState.Expanded;
        var isHover = state == IslandState.Hover;
        var hasTrack = _viewModel.HasTrack;

        double targetWidth;
        double targetHeight;
        double targetCornerRadius;

        if (isExpanded)
        {
            targetWidth = 380;
            targetHeight = 172;
            targetCornerRadius = 26;
        }
        else if (!hasTrack && state is IslandState.Idle)
        {
            targetWidth = 76;
            targetHeight = 32;
            targetCornerRadius = 16;
        }
        else if (isHover)
        {
            targetWidth = hasTrack ? 224 : 84;
            targetHeight = 38;
            targetCornerRadius = 19;
        }
        else
        {
            targetWidth = hasTrack ? 216 : 76;
            targetHeight = 36;
            targetCornerRadius = 18;
        }

        var scale = _viewModel.IslandScale;
        targetWidth *= scale;
        targetHeight *= scale;

        _currentPillWidth = targetWidth;
        _currentPillHeight = targetHeight;

        if (!animated)
        {
            PositionAndResizeWindow(targetWidth, targetHeight);
            Surface.Width = targetWidth;
            Surface.Height = targetHeight;
            Surface.CornerRadius = new CornerRadius(targetCornerRadius);
            _compactVisual.Opacity = isExpanded ? 0 : 1;
            CompactView.IsHitTestVisible = !isExpanded;
            _expandedVisual.Opacity = isExpanded ? 1 : 0;
            ExpandedView.IsHitTestVisible = isExpanded;
            _surfaceVisual.Scale = Vector3.One;
            _surfaceVisual.Opacity = 1;
            return;
        }

        // Cancel previous morph batch if still running
        _morphBatch?.End();
        _morphBatch = null;

        // ---- Apple-style morph via Compositor only (no XAML Storyboard conflicts) ----
        try
        {
            var compositor = _surfaceVisual.Compositor;
            var morphDuration = TimeSpan.FromMilliseconds(isExpanded ? 320 : 250);
            // Apple spring cubic bezier
            var springEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
            var quickEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.6f, 1f));

            // --- Pill shape morphing via implicit size animation on the XAML element ---
            // We set dimensions directly, which triggers the layout system cleanly.
            // Only Window resize needs timing coordination.
            Surface.CornerRadius = new CornerRadius(targetCornerRadius);

            if (isExpanded)
            {
                // Expand: resize window first, then animate content
                PositionAndResizeWindow(targetWidth, targetHeight);
                Surface.Width = targetWidth;
                Surface.Height = targetHeight;
            }

            // --- Surface scale spring bounce ---
            _surfaceVisual.CenterPoint = new Vector3((float)(targetWidth / 2), (float)(targetHeight / 2), 0);
            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.Duration = morphDuration;
            scaleAnim.InsertKeyFrame(0f, Vector3.One);
            if (isExpanded)
            {
                scaleAnim.InsertKeyFrame(0.35f, new Vector3(1.028f, 1.028f, 1f), springEasing);
                scaleAnim.InsertKeyFrame(1f, Vector3.One, springEasing);
            }
            else
            {
                scaleAnim.InsertKeyFrame(0.4f, new Vector3(0.974f, 0.974f, 1f), quickEasing);
                scaleAnim.InsertKeyFrame(1f, Vector3.One, springEasing);
            }
            _surfaceVisual.StartAnimation("Scale", scaleAnim);

            // --- Content cross-fade ---
            CompactView.IsHitTestVisible = !isExpanded;
            ExpandedView.IsHitTestVisible = isExpanded;

            var compactFade = compositor.CreateScalarKeyFrameAnimation();
            compactFade.Duration = TimeSpan.FromMilliseconds(isExpanded ? 100 : 200);
            compactFade.InsertKeyFrame(1f, isExpanded ? 0f : 1f, quickEasing);
            _compactVisual.StartAnimation("Opacity", compactFade);

            var expandedFade = compositor.CreateScalarKeyFrameAnimation();
            expandedFade.Duration = TimeSpan.FromMilliseconds(isExpanded ? 260 : 90);
            if (isExpanded) expandedFade.InsertKeyFrame(0.4f, 0f);
            expandedFade.InsertKeyFrame(1f, isExpanded ? 1f : 0f, springEasing);
            _expandedVisual.StartAnimation("Opacity", expandedFade);

            // --- Schedule window shrink AFTER collapse animation ---
            if (!isExpanded)
            {
                var collapseTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
                collapseTimer.Interval = TimeSpan.FromMilliseconds(260);
                collapseTimer.Tick += (s, e) =>
                {
                    collapseTimer.Stop();
                    if (!_viewModel.IsExpanded && !_isIslandHidden)
                    {
                        Surface.Width = targetWidth;
                        Surface.Height = targetHeight;
                        PositionAndResizeWindow(targetWidth, targetHeight);
                    }
                };
                collapseTimer.Start();
            }
        }
        catch
        {
            // Fallback: instant update
            Surface.Width = targetWidth;
            Surface.Height = targetHeight;
            Surface.CornerRadius = new CornerRadius(targetCornerRadius);
            _compactVisual.Opacity = isExpanded ? 0 : 1;
            _expandedVisual.Opacity = isExpanded ? 1 : 0;
            PositionAndResizeWindow(targetWidth, targetHeight);
        }

        if (state == IslandState.Collapsing)
        {
            _viewModel.CompleteCollapse();
        }
    }

    // ─── Wave bars ───────────────────────────────────────────────────────────

    private void OnWaveTimerTick(DispatcherQueueTimer sender, object args)
    {
        _wavePhase = (_wavePhase + 1) % 4;
        (WaveBar1.Height, WaveBar2.Height, WaveBar3.Height) = _wavePhase switch
        {
            0 => (12d, 6d, 14d),
            1 => (6d, 15d, 9d),
            2 => (14d, 8d, 13d),
            _ => (8d, 13d, 6d)
        };
    }

    private void ResetWaveBars()
    {
        WaveBar1.Height = 8;
        WaveBar2.Height = 12;
        WaveBar3.Height = 6;
    }

    // ─── Input handlers ──────────────────────────────────────────────────────

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => _viewModel.OnPointerEntered();
    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => _viewModel.OnPointerExited();

    private void OnSurfaceTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        _viewModel.ToggleExpanded();
    }

    private void OnToggleExpandedClick(object sender, RoutedEventArgs e) => _viewModel.ToggleExpanded();
    private void OnCollapseClick(object sender, RoutedEventArgs e) => _viewModel.Collapse();
    private void OnHideIslandClick(object sender, RoutedEventArgs e) => HideIslandAnimated();

    private void OnOpenTaskbarFlyoutClick(object sender, RoutedEventArgs e)
        => TaskbarFlyoutRequested?.Invoke(this, EventArgs.Empty);

    private void OnHiddenAnchorPointerEntered(object sender, PointerRoutedEventArgs e) => UnhideIslandAnimated();
    private void OnHiddenAnchorTapped(object sender, TappedRoutedEventArgs e) => UnhideIslandAnimated();

    private void OnSettingsClick(object sender, RoutedEventArgs e)
        => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnSeekBarScrubStarted(object? sender, double percent) => _viewModel.BeginScrub(percent);
    private void OnSeekBarScrubDelta(object? sender, double percent) => _viewModel.UpdateScrub(percent);
    private async void OnSeekBarScrubCompleted(object? sender, double percent) => await _viewModel.EndScrubAsync(percent);

    private void OnButtonPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement el) AnimateButtonScale(el, 0.91f);
    }

    private void OnButtonPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement el) AnimateButtonScale(el, 1.0f);
    }

    private void AnimateButtonScale(FrameworkElement element, float targetScale)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.CenterPoint = new Vector3((float)(element.ActualWidth / 2), (float)(element.ActualHeight / 2), 0);
            var compositor = visual.Compositor;
            var anim = compositor.CreateVector3KeyFrameAnimation();
            anim.Duration = TimeSpan.FromMilliseconds(targetScale < 1 ? 65 : 140);
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
            anim.InsertKeyFrame(1f, new Vector3(targetScale, targetScale, 1f), easing);
            visual.StartAnimation("Scale", anim);
        }
        catch { }
    }

    // ─── Theme ───────────────────────────────────────────────────────────────

    public void ApplyTheme(string theme)
    {
        var isLight = theme == "Light";
        WindowRoot.RequestedTheme = isLight ? ElementTheme.Light : ElementTheme.Dark;

        var bgColor      = isLight ? 0xF5F2F2F7u : 0xFF000000u; // OLED black in dark
        var borderColor  = isLight ? 0x20000000u : 0x28FFFFFFu; // very subtle
        var primaryText  = isLight ? 0xFF1C1C1Eu : 0xFFFFFFFFu;
        var secondaryTxt = isLight ? 0xFF8E8E93u : 0xA0EBEBEBu;
        var pillCtrlBg   = isLight ? 0xFFE5E5EAu : 0x18FFFFFFu;
        var playBg       = isLight ? 0xFF007AFFu : 0xFFFFFFFFu;
        var playFg       = isLight ? 0xFFFFFFFFu : 0xFF000000u;
        var accentColor  = isLight ? 0xFF34C759u : 0xFF30D158u;
        var seekAccent   = isLight ? 0xFF007AFFu : 0xFF0A84FFu;

        UpdateBrush("IslandBackground", bgColor);
        UpdateBrush("IslandBorder", borderColor);
        UpdateBrush("PrimaryText", primaryText);
        UpdateBrush("SecondaryText", secondaryTxt);
        UpdateBrush("PillControlBackground", pillCtrlBg);
        UpdateBrush("PlayButtonBackground", playBg);
        UpdateBrush("PlayButtonForeground", playFg);
        UpdateBrush("Accent", accentColor);
        UpdateBrush("SeekAccent", seekAccent);

        Surface.Background = Brush(bgColor);
        Surface.BorderBrush = Brush(borderColor);
        WaveBar1.Background = Brush(accentColor);
        WaveBar2.Background = Brush(accentColor);
        WaveBar3.Background = Brush(accentColor);
        if (ExpandedSeekBar is not null)
            ExpandedSeekBar.AccentBrush = Brush(seekAccent);
    }

    private void UpdateBrush(string key, uint color)
    {
        var c = ToColor(color);
        if (WindowRoot.Resources.TryGetValue(key, out var res) && res is Microsoft.UI.Xaml.Media.SolidColorBrush brush)
            brush.Color = c;
        else
            WindowRoot.Resources[key] = new Microsoft.UI.Xaml.Media.SolidColorBrush(c);
    }

    private static Microsoft.UI.Xaml.Media.SolidColorBrush Brush(uint color)
        => new(Microsoft.UI.Colors.Transparent) { Color = ToColor(color) };

    private static Windows.UI.Color ToColor(uint color)
        => Windows.UI.Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color);

    // ─── Window chrome / styles ──────────────────────────────────────────────

    private void ApplyWindowStyles()
    {
        // 1. Strip overlapped frame, make popup tool window (no taskbar entry)
        var style = GetWindowLongPtr(_hwnd, GwlStyle).ToInt64();
        SetWindowLongPtr(_hwnd, GwlStyle, new IntPtr((style & ~WsOverlappedWindow) | WsPopup));

        var exStyle = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
        // NOTE: Do NOT add WS_EX_TRANSPARENT — it makes the entire window click-through.
        // The XAML hit-test on Surface/buttons handles interactivity correctly.
        SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(
            exStyle | WsExToolWindow | WsExTopmost | WsExNoActivate));

        // 2. Remove DWM border line (attribute 34 = DWMWA_BORDER_COLOR, 0xFFFFFFFE = DWMWA_COLOR_NONE)
        int dwmBorderNone = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(_hwnd, 34, ref dwmBorderNone, sizeof(int));

        // 3. Disable rounded DWM corners (attribute 33 = DWMWA_WINDOW_CORNER_PREFERENCE, 1 = DWMWCP_DONOTROUND)
        int doNotRound = 1;
        DwmSetWindowAttribute(_hwnd, 33, ref doNotRound, sizeof(int));

        SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
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
