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
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsBorder = 0x00800000L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExTopmost = 0x00000008L;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExWindowEdge = 0x00000100L;
    private const long WsExClientEdge = 0x00000200L;
    private const long WsExDlgModalFrame = 0x00000001L;
    private const long WsExStaticEdge = 0x00020000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint MonitorDefaultToNearest = 2;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly MediaViewModel _viewModel;
    private readonly IntPtr _hwnd;
    private readonly Visual _surfaceVisual;
    private readonly Visual _compactVisual;
    private readonly Visual _expandedVisual;
    private readonly Visual _wave1Visual;
    private readonly Visual _wave2Visual;
    private readonly Visual _wave3Visual;
    private readonly SubclassProc _subclassProc;

    private double _dpiScale = 1.0;
    private bool _isIslandHidden;
    private double _currentPillWidth = 216;
    private double _currentPillHeight = 38;
    private bool _waveAnimating;

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
        _wave1Visual = ElementCompositionPreview.GetElementVisual(WaveBar1);
        _wave2Visual = ElementCompositionPreview.GetElementVisual(WaveBar2);
        _wave3Visual = ElementCompositionPreview.GetElementVisual(WaveBar3);

        ElementCompositionPreview.SetIsTranslationEnabled(Surface, true);
        ElementCompositionPreview.SetIsTranslationEnabled(CompactView, true);
        ElementCompositionPreview.SetIsTranslationEnabled(ExpandedView, true);

        // Subclass window to intercept WM_GETMINMAXINFO:
        // Allows window to shrink to any small capsule dimensions without Windows OS clamping
        _subclassProc = WndProc;
        SetWindowSubclass(_hwnd, _subclassProc, UIntPtr.Zero, IntPtr.Zero);

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

        if (_viewModel.IsPlaying)
        {
            StartEqualizerWave();
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
            var duration = TimeSpan.FromMilliseconds(180);
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(1f, 1f));

            _surfaceVisual.CenterPoint = new Vector3((float)(_currentPillWidth / 2), 0, 0);

            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.Duration = duration;
            scaleAnim.InsertKeyFrame(1f, new Vector3(0.6f, 0.4f, 1f), easing);

            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.Duration = duration;
            opacityAnim.InsertKeyFrame(1f, 0f, easing);

            var translateAnim = compositor.CreateVector3KeyFrameAnimation();
            translateAnim.Duration = duration;
            translateAnim.InsertKeyFrame(1f, new Vector3(0, -18, 0), easing);

            _surfaceVisual.StartAnimation("Scale", scaleAnim);
            _surfaceVisual.StartAnimation("Opacity", opacityAnim);
            _surfaceVisual.StartAnimation("Translation", translateAnim);
        }
        catch { }

        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(190);
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
            var duration = TimeSpan.FromMilliseconds(320);
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));

            _surfaceVisual.CenterPoint = new Vector3((float)(_currentPillWidth / 2), 0, 0);

            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.Duration = duration;
            scaleAnim.InsertKeyFrame(0f, new Vector3(0.6f, 0.4f, 1f));
            scaleAnim.InsertKeyFrame(0.7f, new Vector3(1.025f, 1.025f, 1f), easing);
            scaleAnim.InsertKeyFrame(1f, Vector3.One, easing);

            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.Duration = TimeSpan.FromMilliseconds(160);
            opacityAnim.InsertKeyFrame(0f, 0f);
            opacityAnim.InsertKeyFrame(1f, 1f, easing);

            var translateAnim = compositor.CreateVector3KeyFrameAnimation();
            translateAnim.Duration = duration;
            translateAnim.InsertKeyFrame(0f, new Vector3(0, -18, 0));
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

        AppWindow.ResizeClient(new SizeInt32(winWidth, winHeight));
        AppWindow.Move(new PointInt32(x, y));
        SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaViewModel.CurrentIslandState))
        {
            UpdateStateLayout(_viewModel.CurrentIslandState, animated: true);
        }
        else if (e.PropertyName is nameof(MediaViewModel.CurrentTrack) or nameof(MediaViewModel.IsPlaying))
        {
            if (_viewModel.IsPlaying)
            {
                StartEqualizerWave();
            }
            else
            {
                StopEqualizerWave();
            }
        }
        else if (e.PropertyName == nameof(MediaViewModel.Theme))
        {
            ApplyTheme(_viewModel.Theme);
        }
        else if (e.PropertyName == nameof(MediaViewModel.ArtworkAccentColor))
        {
            ApplyTheme(_viewModel.Theme);
        }
        else if (e.PropertyName is nameof(MediaViewModel.TopOffset) or nameof(MediaViewModel.IslandScale))
        {
            UpdateStateLayout(_viewModel.CurrentIslandState, animated: true);
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
            targetWidth = 348;
            targetHeight = 222;
            targetCornerRadius = 30;
        }
        else if (!hasTrack && state is IslandState.Idle)
        {
            targetWidth = 76;
            targetHeight = 32;
            targetCornerRadius = 16;
        }
        else if (isHover)
        {
            targetWidth = hasTrack ? 248 : 96;
            targetHeight = 46;
            targetCornerRadius = 23;
        }
        else
        {
            targetWidth = hasTrack ? 216 : 76;
            targetHeight = 38;
            targetCornerRadius = 19;
        }

        var scale = isExpanded ? 1.0 : _viewModel.IslandScale;
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

        // Apple fluid spring morphing via Compositor
        try
        {
            var compositor = _surfaceVisual.Compositor;
            var morphDuration = TimeSpan.FromMilliseconds(isExpanded ? 260 : 170);
            var springEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
            var quickEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.6f, 1f));

            Surface.CornerRadius = new CornerRadius(targetCornerRadius);

            if (isExpanded)
            {
                // Expanding: resize window first to accommodate large card,
                // then spring-scale from compact dimensions downward
                PositionAndResizeWindow(targetWidth, targetHeight);
                Surface.Width = targetWidth;
                Surface.Height = targetHeight;

                _surfaceVisual.CenterPoint = new Vector3((float)(targetWidth / 2), 0, 0);

                var compactW = (hasTrack ? 216.0 : 76.0) * _viewModel.IslandScale;
                var compactH = (hasTrack ? 38.0 : 32.0) * _viewModel.IslandScale;
                var fromScaleX = (float)(compactW / targetWidth);
                var fromScaleY = (float)(compactH / targetHeight);

                var expandScaleAnim = compositor.CreateVector3KeyFrameAnimation();
                expandScaleAnim.Duration = morphDuration;
                expandScaleAnim.InsertKeyFrame(0f, new Vector3(fromScaleX, fromScaleY, 1f));
                expandScaleAnim.InsertKeyFrame(0.7f, new Vector3(1.02f, 1.02f, 1f), springEasing);
                expandScaleAnim.InsertKeyFrame(1f, Vector3.One, springEasing);
                _surfaceVisual.StartAnimation("Scale", expandScaleAnim);

                // Content cross-fade
                CompactView.IsHitTestVisible = false;
                ExpandedView.IsHitTestVisible = true;

                var compactFade = compositor.CreateScalarKeyFrameAnimation();
                compactFade.Duration = TimeSpan.FromMilliseconds(80);
                compactFade.InsertKeyFrame(1f, 0f, quickEasing);
                _compactVisual.StartAnimation("Opacity", compactFade);

                var expandedFade = compositor.CreateScalarKeyFrameAnimation();
                expandedFade.Duration = TimeSpan.FromMilliseconds(240);
                expandedFade.InsertKeyFrame(0f, 0f);
                expandedFade.InsertKeyFrame(0.3f, 0f);
                expandedFade.InsertKeyFrame(1f, 1f, springEasing);
                _expandedVisual.StartAnimation("Opacity", expandedFade);
            }
            else if (state is IslandState.Hover or IslandState.Compact or IslandState.Idle or IslandState.TrackChanged or IslandState.Notification)
            {
                PositionAndResizeWindow(targetWidth, targetHeight);
                Surface.Width = targetWidth;
                Surface.Height = targetHeight;
                CompactView.IsHitTestVisible = true;
                ExpandedView.IsHitTestVisible = false;
                _expandedVisual.Opacity = 0;
                _compactVisual.Opacity = 1;

                _surfaceVisual.CenterPoint = new Vector3((float)(targetWidth / 2), (float)(targetHeight / 2), 0);
                var settle = state == IslandState.Hover ? 1.018f : 0.992f;
                var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
                scaleAnim.Duration = TimeSpan.FromMilliseconds(170);
                scaleAnim.InsertKeyFrame(0.72f, new Vector3(settle, settle, 1f), springEasing);
                scaleAnim.InsertKeyFrame(1f, Vector3.One, springEasing);
                _surfaceVisual.StartAnimation("Scale", scaleAnim);
            }
            else
            {
                // Collapsing: animate scale up into the compact pill
                _surfaceVisual.CenterPoint = new Vector3((float)(Surface.ActualWidth / 2), 0, 0);

                var toScaleX = (float)(targetWidth / Math.Max(1.0, Surface.ActualWidth));
                var toScaleY = (float)(targetHeight / Math.Max(1.0, Surface.ActualHeight));

                var collapseScaleAnim = compositor.CreateVector3KeyFrameAnimation();
                collapseScaleAnim.Duration = morphDuration;
                collapseScaleAnim.InsertKeyFrame(1f, new Vector3(toScaleX, toScaleY, 1f), springEasing);
                _surfaceVisual.StartAnimation("Scale", collapseScaleAnim);

                CompactView.IsHitTestVisible = true;
                ExpandedView.IsHitTestVisible = false;

                var expandedFade = compositor.CreateScalarKeyFrameAnimation();
                expandedFade.Duration = TimeSpan.FromMilliseconds(90);
                expandedFade.InsertKeyFrame(1f, 0f, quickEasing);
                _expandedVisual.StartAnimation("Opacity", expandedFade);

                var compactFade = compositor.CreateScalarKeyFrameAnimation();
                compactFade.Duration = TimeSpan.FromMilliseconds(200);
                compactFade.InsertKeyFrame(0f, 0f);
                compactFade.InsertKeyFrame(1f, 1f, springEasing);
                _compactVisual.StartAnimation("Opacity", compactFade);

                // Resize window to compact capsule once animation completes
                var collapseTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
                collapseTimer.Interval = TimeSpan.FromMilliseconds(245);
                collapseTimer.Tick += (s, e) =>
                {
                    collapseTimer.Stop();
                    if (!_viewModel.IsExpanded && !_isIslandHidden)
                    {
                        Surface.Width = targetWidth;
                        Surface.Height = targetHeight;
                        _surfaceVisual.Scale = Vector3.One;
                        PositionAndResizeWindow(targetWidth, targetHeight);
                    }
                };
                collapseTimer.Start();
            }
        }
        catch
        {
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

    // GPU-accelerated Equalizer wave animations running on the Compositor thread (zero CPU)
    private void StartEqualizerWave()
    {
        if (_waveAnimating) return;
        _waveAnimating = true;

        try
        {
            AnimateWaveBar(_wave1Visual, WaveBar1, 0.35f, 1.0f, 480);
            AnimateWaveBar(_wave2Visual, WaveBar2, 0.20f, 1.0f, 360);
            AnimateWaveBar(_wave3Visual, WaveBar3, 0.40f, 1.0f, 620);
        }
        catch { }
    }

    private void StopEqualizerWave()
    {
        if (!_waveAnimating) return;
        _waveAnimating = false;

        try
        {
            _wave1Visual.StopAnimation("Scale");
            _wave2Visual.StopAnimation("Scale");
            _wave3Visual.StopAnimation("Scale");
            _wave1Visual.Scale = new Vector3(1f, 0.45f, 1f);
            _wave2Visual.Scale = new Vector3(1f, 0.65f, 1f);
            _wave3Visual.Scale = new Vector3(1f, 0.35f, 1f);
        }
        catch { }
    }

    private void AnimateWaveBar(Visual visual, FrameworkElement element, float minScale, float maxScale, int durationMs)
    {
        var compositor = visual.Compositor;
        visual.CenterPoint = new Vector3((float)(element.Width / 2), (float)element.Height, 0);

        var anim = compositor.CreateVector3KeyFrameAnimation();
        anim.Duration = TimeSpan.FromMilliseconds(durationMs);
        anim.IterationBehavior = AnimationIterationBehavior.Forever;
        anim.Direction = AnimationDirection.Alternate;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.42f, 0f), new Vector2(0.58f, 1f));

        anim.InsertKeyFrame(0f, new Vector3(1f, minScale, 1f));
        anim.InsertKeyFrame(1f, new Vector3(1f, maxScale, 1f), easing);

        visual.StartAnimation("Scale", anim);
    }

    // Input handlers
    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => _viewModel.OnPointerEntered();
    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => _viewModel.OnPointerExited();

    private void OnSurfacePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_viewModel.IsExpanded)
        {
            e.Handled = true;
            _viewModel.ToggleExpanded();
        }
    }

    private void OnExpandedViewPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        // Prevent clicking inside the card content from closing the island
        e.Handled = true;
    }

    private void OnToggleExpandedClick(object sender, RoutedEventArgs e) => _viewModel.ToggleExpanded();
    private void OnCollapseClick(object sender, RoutedEventArgs e) => _viewModel.Collapse();
    private void OnHideIslandClick(object sender, RoutedEventArgs e) => HideIslandAnimated();

    private void OnOpenTaskbarFlyoutClick(object sender, RoutedEventArgs e)
        => TaskbarFlyoutRequested?.Invoke(this, EventArgs.Empty);

    private void OnHiddenAnchorPointerEntered(object sender, PointerRoutedEventArgs e) => UnhideIslandAnimated();
    private void OnHiddenAnchorPointerReleased(object sender, PointerRoutedEventArgs e) => UnhideIslandAnimated();

    private void OnSettingsClick(object sender, RoutedEventArgs e)
        => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnSeekBarScrubStarted(object? sender, double percent) => _viewModel.BeginScrub(percent);
    private void OnSeekBarScrubDelta(object? sender, double percent) => _viewModel.UpdateScrub(percent);
    private async void OnSeekBarScrubCompleted(object? sender, double percent)
    {
        try
        {
            await _viewModel.EndScrubAsync(percent);
        }
        catch { }
    }

    private void OnButtonPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement el) AnimateButtonScale(el, 0.92f);
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
            anim.Duration = TimeSpan.FromMilliseconds(targetScale < 1 ? 65 : 130);
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
            anim.InsertKeyFrame(1f, new Vector3(targetScale, targetScale, 1f), easing);
            visual.StartAnimation("Scale", anim);
        }
        catch { }
    }

    // Theme Management
    public void ApplyTheme(string theme)
    {
        var isLight = theme == "Light";
        WindowRoot.RequestedTheme = isLight ? ElementTheme.Light : ElementTheme.Dark;

        var artworkAccent = _viewModel.ArtworkAccentColor;
        var accentColor  = isLight ? 0xFF007AFFu : artworkAccent;
        var bgColor      = isLight ? 0xF2F5F5F7u : Blend(0xF20B1018u, artworkAccent, 0.20);
        var borderColor  = isLight ? 0x22000000u : Blend(0x33FFFFFFu, artworkAccent, 0.22);
        var primaryText  = isLight ? 0xFF1C1C1Eu : 0xFFFFFFFFu;
        var secondaryTxt = isLight ? 0xFF8E8E93u : 0xFFA4ABB6u;
        var pillCtrlBg   = isLight ? 0xFFE5E5EAu : Blend(0x24FFFFFFu, artworkAccent, 0.14);
        var playBg       = isLight ? 0xFF007AFFu : accentColor;
        var playFg       = 0xFFFFFFFFu;
        var seekAccent   = accentColor;

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
        => new(ToColor(color));

    private static Windows.UI.Color ToColor(uint color)
        => Windows.UI.Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color);

    private static uint Blend(uint baseColor, uint overlayColor, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        var a = (byte)(baseColor >> 24);
        var r = (byte)Math.Round(((baseColor >> 16) & 0xFF) * (1 - amount) + ((overlayColor >> 16) & 0xFF) * amount);
        var g = (byte)Math.Round(((baseColor >> 8) & 0xFF) * (1 - amount) + ((overlayColor >> 8) & 0xFF) * amount);
        var b = (byte)Math.Round((baseColor & 0xFF) * (1 - amount) + (overlayColor & 0xFF) * amount);
        return ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    // Win32 Styles & Border/Shadow Stripping
    private void ApplyWindowStyles()
    {
        var style = GetWindowLongPtr(_hwnd, GwlStyle).ToInt64();
        // Strip overlapped frame, borders and caption
        var newStyle = (style & ~WsOverlappedWindow & ~WsCaption & ~WsThickFrame & ~WsBorder) | WsPopup;
        SetWindowLongPtr(_hwnd, GwlStyle, new IntPtr(newStyle));

        var exStyle = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
        // Clear all window edges and modal frames
        var newExStyle = (exStyle & ~WsExWindowEdge & ~WsExClientEdge & ~WsExDlgModalFrame & ~WsExStaticEdge)
            | WsExToolWindow | WsExTopmost | WsExNoActivate;
        SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(newExStyle));

        // Remove DWM border line
        int dwmBorderNone = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(_hwnd, 34, ref dwmBorderNone, sizeof(int)); // 34 = DWMWA_BORDER_COLOR

        // Disable rounded DWM outer corners
        int doNotRound = 1; // DWMWCP_DONOTROUND
        DwmSetWindowAttribute(_hwnd, 33, ref doNotRound, sizeof(int)); // 33 = DWMWA_WINDOW_CORNER_PREFERENCE

        SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
    }

    // Subclass WndProc to eliminate OS window minimum size constraints
    private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        const uint WM_GETMINMAXINFO = 0x0024;
        if (uMsg == WM_GETMINMAXINFO)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            mmi.ptMinTrackSize.X = 1;
            mmi.ptMinTrackSize.Y = 1;
            Marshal.StructureToPtr(mmi, lParam, false);
            return IntPtr.Zero;
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    // P/Invoke
    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

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
    private struct MINMAXINFO
    {
        public Win32Point ptReserved;
        public Win32Point ptMaxSize;
        public Win32Point ptMaxPosition;
        public Win32Point ptMinTrackSize;
        public Win32Point ptMaxTrackSize;
    }

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
