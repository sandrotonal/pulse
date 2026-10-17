using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpHideWindow = 0x0080;
    private const uint MonitorDefaultToNearest = 2;
    private const uint ModWin = 0x0008;
    private const uint ModShift = 0x0004;
    private const uint ModAlt = 0x0001;
    private const int HotkeyToggleIsland = 0x5053;
    private const int HotkeyToggleFocus = 0x464F;
    private const uint VkI = 0x49;
    private const uint VkF = 0x46;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly IslandViewModel _viewModel;
    private readonly IntPtr _hwnd;
    private readonly Visual _surfaceVisual;
    private readonly Visual _compactVisual;
    private readonly Visual _expandedVisual;
    private readonly Visual _wave1Visual;
    private readonly Visual _wave2Visual;
    private readonly Visual _wave3Visual;
    private readonly Visual _mediaPanelVisual;
    private readonly Visual _focusPanelVisual;
    private readonly Visual _systemPanelVisual;
    private readonly SubclassProc _subclassProc;

    private long _lastWheelNavTicks;
    private const long WheelDebounceTicks = 200 * TimeSpan.TicksPerMillisecond;
    private DispatcherQueueTimer? _pageTransitionCleanupTimer;
    private DispatcherQueueTimer? _hideAnimationTimer;
    private DispatcherQueueTimer? _topmostGuardTimer;
    private string? _lastPulseTrackKey;

    private double _dpiScale = 1.0;
    private bool _isIslandHidden;
    private bool _glassEnabled;
    private double _currentPillWidth = 216;
    private double _currentPillHeight = 40;
    private bool _waveAnimating;
    private readonly DispatcherQueueTimer _collapseDelayTimer;
    private bool _isPointerOverSurface;
    private bool _isInteractingWithControl;
    private long _transitionGeneration;
    private double _animStartWidth;
    private double _animStartHeight;
    private double _animStartCornerRadius;
    private double _animTargetWidth;
    private double _animTargetHeight;
    private double _animTargetCornerRadius;
    private long _animStartTimeTicks;
    private double _animDurationMs;
    private bool _isDimensionAnimating;
    private Action? _onAnimationCompleted;

    public event EventHandler? SettingsRequested;
    public event EventHandler? TaskbarFlyoutRequested;

    public IslandWindow(IslandViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        WindowRoot.DataContext = viewModel;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        _collapseDelayTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _collapseDelayTimer.Interval = TimeSpan.FromMilliseconds(600);
        _collapseDelayTimer.Tick += OnCollapseDelayTimerTick;

        _surfaceVisual = ElementCompositionPreview.GetElementVisual(Surface);
        _compactVisual = ElementCompositionPreview.GetElementVisual(CompactView);
        _expandedVisual = ElementCompositionPreview.GetElementVisual(ExpandedView);
        _wave1Visual = ElementCompositionPreview.GetElementVisual(WaveBar1);
        _wave2Visual = ElementCompositionPreview.GetElementVisual(WaveBar2);
        _wave3Visual = ElementCompositionPreview.GetElementVisual(WaveBar3);
        _mediaPanelVisual = ElementCompositionPreview.GetElementVisual(MediaExpandedPanel);
        _focusPanelVisual = ElementCompositionPreview.GetElementVisual(FocusExpandedPanel);
        _systemPanelVisual = ElementCompositionPreview.GetElementVisual(SystemExpandedPanel);

        ElementCompositionPreview.SetIsTranslationEnabled(Surface, true);
        ElementCompositionPreview.SetIsTranslationEnabled(CompactView, true);
        ElementCompositionPreview.SetIsTranslationEnabled(ExpandedView, true);
        ElementCompositionPreview.SetIsTranslationEnabled(MediaExpandedPanel, true);
        ElementCompositionPreview.SetIsTranslationEnabled(FocusExpandedPanel, true);
        ElementCompositionPreview.SetIsTranslationEnabled(SystemExpandedPanel, true);

        // Subclass window to intercept WM_NCHITTEST and WM_GETMINMAXINFO:
        _subclassProc = WndProc;
        SetWindowSubclass(_hwnd, _subclassProc, UIntPtr.Zero, IntPtr.Zero);

        // Global hotkeys: Win+Alt+I toggles the island, Win+Shift+F toggles the focus timer.
        RegisterHotKey(_hwnd, HotkeyToggleIsland, ModWin | ModAlt, VkI);
        RegisterHotKey(_hwnd, HotkeyToggleFocus, ModWin | ModShift, VkF);

        AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        PositionHostWindow();
        ApplyWindowStyles();
        ApplyGlass();
        ApplyTheme(_viewModel.Theme);

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        if (FocusProgressTrack is not null)
        {
            FocusProgressTrack.SizeChanged += (_, _) => UpdateFocusProgressFill();
        }

        // Apply initial layout without animation
        UpdateStateLayout(_viewModel.CurrentIslandState, animated: false);

        // Guard against other apps/Win32 calls demoting the island out of the topmost band.
        _topmostGuardTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _topmostGuardTimer.Interval = TimeSpan.FromMilliseconds(3000);
        _topmostGuardTimer.Tick += (_, _) => ReassertTopmost();
        _topmostGuardTimer.Start();

        if (_viewModel.IsPlaying)
        {
            StartEqualizerWave();
        }
    }

    public XamlRoot IslandXamlRoot => WindowRoot.XamlRoot;
    public bool IsIslandHidden => _isIslandHidden;

    /// <summary>Keeps the island above everything, even if another app demoted its z-order.</summary>
    public void ReassertTopmost()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        _ = SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    public void ShowIsland()
    {
        if (_isIslandHidden) UnhideIslandAnimated();
        ShowWindow(_hwnd, 4); // SW_SHOWNOACTIVATE
        ApplyWindowStyles();
        PositionHostWindow();
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

        var timer = _hideAnimationTimer;
        if (timer is null)
        {
            timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(190);
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (_isIslandHidden)
                {
                    Surface.Visibility = Visibility.Collapsed;
                    HiddenAnchor.Visibility = Visibility.Visible;
                }
            };
            _hideAnimationTimer = timer;
        }
        timer.Start();
    }

    public void UnhideIslandAnimated()
    {
        if (!_isIslandHidden) return;
        _isIslandHidden = false;

        ReassertTopmost();
        HiddenAnchor.Visibility = Visibility.Collapsed;
        Surface.Visibility = Visibility.Visible;

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

    private const double BaseWindowTopGap = 14.0;

    private void PositionHostWindow()
    {
        ApplyHostWindowSize(_currentPillWidth, _currentPillHeight);
    }

    /// <summary>
    /// Sizes/moves the host window to hug the current pill. A big fixed host window would
    /// swallow clicks in its transparent margins (WinUI never calls WM_NCHITTEST for
    /// pointer input), so the window must track the pill dimensions.
    /// </summary>
    private void ApplyHostWindowSize(double pillWidth, double pillHeight)
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

        const double PadX = 20.0;
        const double PadBottom = 20.0;
        var hostWidthDip = Math.Max(120.0, pillWidth) + PadX * 2;
        var hostHeightDip = Math.Max(36.0, pillHeight) + PadBottom;

        WindowRoot.Width = hostWidthDip;
        WindowRoot.Height = hostHeightDip;

        var winWidth = (int)Math.Round(hostWidthDip * _dpiScale);
        var winHeight = (int)Math.Round(hostHeightDip * _dpiScale);

        var x = workArea.Left + Math.Max(0, (workArea.Right - workArea.Left - winWidth) / 2);
        var effectiveTopOffset = Math.Max(BaseWindowTopGap, _viewModel.TopOffset + BaseWindowTopGap);
        var y = workArea.Top + (int)Math.Round(effectiveTopOffset * _dpiScale);

        AppWindow.ResizeClient(new SizeInt32(winWidth, winHeight));
        AppWindow.Move(new PointInt32(x, y));
        SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);

        if (_glassEnabled)
        {
            _ = RefreshFrostAsync();
        }
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

            var trackKey = GetTrackKey(_viewModel.CurrentTrack);
            if (e.PropertyName == nameof(MediaViewModel.CurrentTrack) &&
                !_isIslandHidden &&
                trackKey is not null &&
                trackKey != _lastPulseTrackKey)
            {
                _lastPulseTrackKey = trackKey;
                PlayTrackChangePulse();
            }
        }
        else if (e.PropertyName is nameof(MediaViewModel.Theme) 
                 or nameof(MediaViewModel.ArtworkAccentColor) 
                 or nameof(MediaViewModel.DynamicColorTint)
                 or nameof(MediaViewModel.UseTransparency))
        {
            ApplyGlass();
            ApplyTheme(_viewModel.Theme);
        }
        else if (e.PropertyName is nameof(MediaViewModel.TopOffset) or nameof(MediaViewModel.IslandScale))
        {
            PositionHostWindow();
            UpdateStateLayout(_viewModel.CurrentIslandState, animated: false);
        }
        else if (e.PropertyName == nameof(MediaViewModel.ShowEqualizer))
        {
            if (CompactWaveBars is not null)
            {
                CompactWaveBars.Opacity = (_viewModel.CurrentIslandState == IslandState.Hover && _viewModel.ShowEqualizer) ? 1.0 : 0.0;
            }
        }
        else if (e.PropertyName is nameof(IslandViewModel.IsNotificationActive)
                 or nameof(IslandViewModel.IsFocusTimerActive)
                 or nameof(IslandViewModel.IsFileOperationActive))
        {
            // Apple Dynamic Island behaviour: an alert takes over the presentation.
            if (e.PropertyName == nameof(IslandViewModel.IsNotificationActive)
                && _viewModel.IsNotificationActive
                && _viewModel.IsExpanded)
            {
                _viewModel.Collapse();
            }
            UpdateStateLayout(_viewModel.CurrentIslandState, animated: true);
        }
        else if (e.PropertyName is nameof(IslandViewModel.FocusProgress) or nameof(IslandViewModel.PreserveFocusPanel))
        {
            UpdateFocusProgressFill();
        }
    }

    private Microsoft.UI.Xaml.Media.Animation.Storyboard? _focusFillStoryboard;

    private void UpdateFocusProgressFill()
    {
        if (FocusProgressTrack is null || FocusProgressFill is null) return;
        var trackWidth = FocusProgressTrack.ActualWidth;
        if (trackWidth <= 0)
        {
            trackWidth = 336; // fall back to XAML margin-derived width until layout settles
        }
        var progress = Math.Clamp(_viewModel.FocusProgress, 0, 1);
        var target = Math.Max(0, progress * trackWidth);

        // Smoothly glide the hairline between per-second ticks instead of jumping.
        _focusFillStoryboard?.Stop();
        var anim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(400),
            EnableDependentAnimation = true,
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
            }
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, FocusProgressFill);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, "Width");
        _focusFillStoryboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        _focusFillStoryboard.Children.Add(anim);
        _focusFillStoryboard.Begin();
    }

    /// <summary>Motion intensity from settings: damping + duration profile for spring animations.</summary>
    private (double Damping, double DurationScale) AnimationProfile => _viewModel.AnimationIntensity switch
    {
        "Minimal" => (0.95, 0.72),
        "Dynamic" => (0.60, 1.18),
        _ => (0.82, 1.0)
    };

    private static readonly Windows.UI.ViewManagement.UISettings SystemUiSettings = new();

    /// <summary>Respects the Windows "animation effects" accessibility setting.</summary>
    private static bool ReducedMotion => !SystemUiSettings.AnimationsEnabled;

    /// <summary>Normalized damped-spring easing curve evaluated over the animation duration.</summary>
    private static double SpringEase(double t, double damping)
    {
        if (t <= 0.0) return 0.0;
        if (t >= 1.0) return 1.0;

        const double Omega = 8.0;
        double zeta = Math.Clamp(damping, 0.2, 0.98);
        double omegaD = Omega * Math.Sqrt(1.0 - zeta * zeta);
        double decay = Math.Exp(-zeta * Omega * t);
        return 1.0 - decay * (Math.Cos(omegaD * t) + (zeta * Omega / omegaD) * Math.Sin(omegaD * t));
    }

    private void AnimateSurfaceDimensions(double toWidth, double toHeight, double toCornerRadius, double durationMs, Action? onCompleted = null)
    {
        if (ReducedMotion)
        {
            _isDimensionAnimating = false;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnDimensionRendering;
            Surface.Width = toWidth;
            Surface.Height = toHeight;
            Surface.CornerRadius = new CornerRadius(toCornerRadius);
            _currentPillWidth = toWidth;
            _currentPillHeight = toHeight;
            onCompleted?.Invoke();
            return;
        }
        _animStartWidth = Surface.Width > 0 ? Surface.Width : Surface.ActualWidth;
        if (_animStartWidth <= 0) _animStartWidth = 216.0;

        _animStartHeight = Surface.Height > 0 ? Surface.Height : Surface.ActualHeight;
        if (_animStartHeight <= 0) _animStartHeight = 40.0;

        _animStartCornerRadius = Surface.CornerRadius.TopLeft > 0 ? Surface.CornerRadius.TopLeft : 20.0;

        _animTargetWidth = toWidth;
        _animTargetHeight = toHeight;
        _animTargetCornerRadius = toCornerRadius;
        _animDurationMs = Math.Max(1.0, durationMs * AnimationProfile.DurationScale);
        _animStartTimeTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        _onAnimationCompleted = onCompleted;

        if (!_isDimensionAnimating)
        {
            _isDimensionAnimating = true;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnDimensionRendering;
        }
    }

    private void OnDimensionRendering(object? sender, object e)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double elapsedMs = (now - _animStartTimeTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        double progress = Math.Clamp(elapsedMs / _animDurationMs, 0.0, 1.0);

        // Damped harmonic spring: overshoot and settle according to the motion-intensity setting.
        double ease = SpringEase(progress, AnimationProfile.Damping);

        double currentW = _animStartWidth + (_animTargetWidth - _animStartWidth) * ease;
        double currentH = _animStartHeight + (_animTargetHeight - _animStartHeight) * ease;
        double currentR = _animStartCornerRadius + (_animTargetCornerRadius - _animStartCornerRadius) * ease;

        Surface.Width = currentW;
        Surface.Height = currentH;
        Surface.CornerRadius = new CornerRadius(currentR);

        _currentPillWidth = currentW;
        _currentPillHeight = currentH;

        if (progress >= 1.0)
        {
            _isDimensionAnimating = false;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnDimensionRendering;

            Surface.Width = _animTargetWidth;
            Surface.Height = _animTargetHeight;
            Surface.CornerRadius = new CornerRadius(_animTargetCornerRadius);
            _currentPillWidth = _animTargetWidth;
            _currentPillHeight = _animTargetHeight;

            var callback = _onAnimationCompleted;
            _onAnimationCompleted = null;
            callback?.Invoke();
        }
    }

    private void UpdateStateLayout(IslandState state, bool animated)
    {
        if (ReducedMotion)
        {
            animated = false;
        }

        var generation = ++_transitionGeneration;
        var isExpanded = state == IslandState.Expanded;
        var isCollapsing = state == IslandState.Collapsing;
        var isHover = state == IslandState.Hover;
        var hasTrack = _viewModel.HasTrack;

        double targetWidth;
        double targetHeight;
        double targetCornerRadius;

        if (isExpanded)
        {
            targetWidth = 384;
            targetHeight = 240;
            targetCornerRadius = 32;
        }
        else if (_viewModel.IsNotificationActive || _viewModel.IsFileOperationActive)
        {
            targetWidth = 268;
            targetHeight = 40;
            targetCornerRadius = 20;
        }
        else if (_viewModel.IsFocusTimerActive)
        {
            targetWidth = 220;
            targetHeight = 40;
            targetCornerRadius = 20;
        }
        else if (!hasTrack && state is IslandState.Idle)
        {
            targetWidth = 76;
            targetHeight = 32;
            targetCornerRadius = 16;
        }
        else if (isHover)
        {
            targetWidth = hasTrack ? 252 : 86;
            targetHeight = 40;
            targetCornerRadius = 20;
        }
        else
        {
            targetWidth = hasTrack ? 216 : 76;
            targetHeight = 40;
            targetCornerRadius = 20;
        }

        var scale = (isExpanded || isCollapsing) ? 1.0 : _viewModel.IslandScale;
        targetWidth *= scale;
        targetHeight *= scale;

        if (CompactWaveBars is not null)
        {
            CompactWaveBars.Opacity = (isHover && _viewModel.ShowEqualizer) ? 1.0 : 0.0;
        }

        if (!animated)
        {
            if (_isDimensionAnimating)
            {
                _isDimensionAnimating = false;
                Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnDimensionRendering;
            }

            Surface.Width = targetWidth;
            Surface.Height = targetHeight;
            Surface.CornerRadius = new CornerRadius(targetCornerRadius);
            _currentPillWidth = targetWidth;
            _currentPillHeight = targetHeight;
            ApplyHostWindowSize(targetWidth, targetHeight);

            _compactVisual.Opacity = isExpanded ? 0 : 1;
            CompactView.IsHitTestVisible = !isExpanded;
            _expandedVisual.Opacity = isExpanded ? 1 : 0;
            ExpandedView.IsHitTestVisible = isExpanded;

            if (isExpanded)
            {
                int activeIndex = _viewModel.SelectedPageIndex;
                for (int i = 0; i < 3; i++)
                {
                    var grid = GetPanelGrid(i);
                    grid.Visibility = (i == activeIndex) ? Visibility.Visible : Visibility.Collapsed;
                    grid.Translation = Vector3.Zero;
                    var v = GetPanelVisual(i);
                    v.Opacity = (i == activeIndex) ? 1f : 0f;
                    v.StopAnimation("Translation");
                }
                UpdatePageDots(activeIndex, animated: false);
            }

            _surfaceVisual.Scale = Vector3.One;
            _surfaceVisual.StopAnimation("Scale");
            _surfaceVisual.StopAnimation("Translation");
            Surface.Translation = Vector3.Zero;
            _surfaceVisual.Opacity = 1;

            if (state == IslandState.Collapsing)
            {
                _viewModel.CompleteCollapse();
            }
            return;
        }

        try
        {
            var compositor = _surfaceVisual.Compositor;
            var springEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
            var quickEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.6f, 1f));
            var durScale = AnimationProfile.DurationScale;

            // Grow the host window up-front so the animation is never clipped; shrink it
            // back to the exact pill bounds when the animation completes.
            ApplyHostWindowSize(Math.Max(_currentPillWidth, targetWidth), Math.Max(_currentPillHeight, targetHeight));

            _surfaceVisual.Scale = Vector3.One;
            _surfaceVisual.StopAnimation("Scale");
            _surfaceVisual.StopAnimation("Translation");
            Surface.Translation = Vector3.Zero;

            if (isExpanded)
            {
                CompactView.IsHitTestVisible = false;
                ExpandedView.IsHitTestVisible = true;

                int activeIndex = _viewModel.SelectedPageIndex;
                for (int i = 0; i < 3; i++)
                {
                    var grid = GetPanelGrid(i);
                    grid.Visibility = (i == activeIndex) ? Visibility.Visible : Visibility.Collapsed;
                    grid.Translation = Vector3.Zero;
                    var v = GetPanelVisual(i);
                    v.Opacity = (i == activeIndex) ? 1f : 0f;
                    v.StopAnimation("Translation");
                }
                UpdatePageDots(activeIndex, animated: false);

                // Coordinated cross-fade & reveal
                var compactFade = compositor.CreateScalarKeyFrameAnimation();
                compactFade.Duration = TimeSpan.FromMilliseconds(90 * durScale);
                compactFade.InsertKeyFrame(1f, 0f, quickEasing);
                _compactVisual.StartAnimation("Opacity", compactFade);

                var expandedFade = compositor.CreateScalarKeyFrameAnimation();
                expandedFade.Duration = TimeSpan.FromMilliseconds(260 * durScale);
                expandedFade.InsertKeyFrame(0f, 0f);
                expandedFade.InsertKeyFrame(0.15f, 0f);
                expandedFade.InsertKeyFrame(1f, 1f, springEasing);
                _expandedVisual.StartAnimation("Opacity", expandedFade);

                var expandTranslate = compositor.CreateVector3KeyFrameAnimation();
                expandTranslate.Duration = TimeSpan.FromMilliseconds(280 * durScale);
                expandTranslate.InsertKeyFrame(0f, new Vector3(0, 8, 0));
                expandTranslate.InsertKeyFrame(1f, Vector3.Zero, springEasing);
                _expandedVisual.StartAnimation("Translation", expandTranslate);

                AnimateSurfaceDimensions(targetWidth, targetHeight, targetCornerRadius, 300, () => ApplyHostWindowSize(targetWidth, targetHeight));

                if (_glassEnabled)
                {
                    _ = RefreshFrostAsync(force: true);
                }
            }
            else if (isCollapsing)
            {
                CompactView.IsHitTestVisible = true;
                ExpandedView.IsHitTestVisible = false;

                var expandedFade = compositor.CreateScalarKeyFrameAnimation();
                expandedFade.Duration = TimeSpan.FromMilliseconds(110 * durScale);
                expandedFade.InsertKeyFrame(1f, 0f, quickEasing);
                _expandedVisual.StartAnimation("Opacity", expandedFade);

                var compactFade = compositor.CreateScalarKeyFrameAnimation();
                compactFade.Duration = TimeSpan.FromMilliseconds(220 * durScale);
                compactFade.InsertKeyFrame(0f, 0f);
                compactFade.InsertKeyFrame(0.15f, 0f);
                compactFade.InsertKeyFrame(1f, 1f, springEasing);
                _compactVisual.StartAnimation("Opacity", compactFade);

                AnimateSurfaceDimensions(targetWidth, targetHeight, targetCornerRadius, 280, () =>
                {
                    ApplyHostWindowSize(targetWidth, targetHeight);
                    if (generation == _transitionGeneration && _viewModel.CurrentIslandState == IslandState.Collapsing)
                    {
                        _viewModel.CompleteCollapse();
                    }
                });
            }
            else
            {
                // Compact or Hover
                CompactView.IsHitTestVisible = true;
                ExpandedView.IsHitTestVisible = false;
                _expandedVisual.Opacity = 0;
                _compactVisual.Opacity = 1;

                AnimateSurfaceDimensions(targetWidth, targetHeight, targetCornerRadius, 160, () => ApplyHostWindowSize(targetWidth, targetHeight));
            }
        }
        catch
        {
            Surface.Width = targetWidth;
            Surface.Height = targetHeight;
            Surface.CornerRadius = new CornerRadius(targetCornerRadius);
            _compactVisual.Opacity = isExpanded ? 0 : 1;
            _expandedVisual.Opacity = isExpanded ? 1 : 0;
            _currentPillWidth = targetWidth;
            _currentPillHeight = targetHeight;
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

    // Quick scale pulse when the track changes (Apple micro-interaction)
    private static string? GetTrackKey(MediaTrack? track)
        => string.IsNullOrEmpty(track?.Title) ? null : $"{track.SourceApplicationId}|{track.Title}|{track.Artist}";

    private void PlayTrackChangePulse()
    {
        try
        {
            var compositor = _surfaceVisual.Compositor;
            var springEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.34f, 1.56f), new Vector2(0.64f, 1f));

            _surfaceVisual.CenterPoint = new Vector3((float)(_currentPillWidth / 2), (float)(_currentPillHeight / 2), 0);

            var pulse = compositor.CreateVector3KeyFrameAnimation();
            pulse.Duration = TimeSpan.FromMilliseconds(420);
            pulse.InsertKeyFrame(0f, Vector3.One);
            pulse.InsertKeyFrame(0.35f, new Vector3(1.05f, 1.05f, 1f), springEasing);
            pulse.InsertKeyFrame(1f, Vector3.One, springEasing);

            _surfaceVisual.StartAnimation("Scale", pulse);
        }
        catch { }
    }

    // Input handlers and 04 Auto-collapse sequence
    private void OnCollapseDelayTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (!_isPointerOverSurface && _viewModel.IsExpanded && _viewModel.AutoCollapse && !_viewModel.IsScrubbing && !_isInteractingWithControl && !_viewModel.PreserveFocusPanel)
        {
            _viewModel.Collapse();
        }
        else if (_viewModel.PreserveFocusPanel && _viewModel.IsExpanded)
        {
            // Running focus session keeps the island pinned until paused or page changed.
            sender.Interval = TimeSpan.FromMilliseconds(600);
            sender.Start();
        }
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOverSurface = true;
        _collapseDelayTimer.Stop();
        _viewModel.OnPointerEntered();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        // Frost capture briefly hides the window, which synthesizes a synthetic
        // PointerExited — ignore it so the island doesn't collapse mid-capture.
        if (_frostBusy) return;
        _isPointerOverSurface = false;
        _viewModel.OnPointerExited();

        if (_viewModel.IsExpanded && _viewModel.AutoCollapse && !_viewModel.IsScrubbing && !_isInteractingWithControl)
        {
            _collapseDelayTimer.Interval = TimeSpan.FromMilliseconds(600);
            _collapseDelayTimer.Start();
        }
    }

    private void OnSurfacePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_viewModel.IsExpanded)
        {
            e.Handled = true;
            // Land on the page that matches what the island is currently showing.
            if (_viewModel.IsFocusTimerActive && !_viewModel.HasTrack)
            {
                _viewModel.SelectPage(1);
            }
            _viewModel.ToggleExpanded();
        }
    }

    private void OnMediaSourcePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        // Tapping the artwork/title opens the app that owns the media session (Spotify, browser...).
        e.Handled = true;
        _viewModel.ActivateMediaSourceApp();
    }

    private void OnEventContentPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        _viewModel.ActivateActiveEventTarget();
    }

    private void OnCompactFocusPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        _viewModel.SelectPage(1);
        if (!_viewModel.IsExpanded)
        {
            _viewModel.ToggleExpanded();
        }
    }

    private void OnExpandedViewPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        // Prevent clicking inside the card content from closing the island
        e.Handled = true;
    }

    private Visual GetPanelVisual(int index) => index switch
    {
        0 => _mediaPanelVisual,
        1 => _focusPanelVisual,
        2 => _systemPanelVisual,
        _ => _mediaPanelVisual
    };

    private Grid GetPanelGrid(int index) => index switch
    {
        0 => MediaExpandedPanel,
        1 => FocusExpandedPanel,
        2 => SystemExpandedPanel,
        _ => MediaExpandedPanel
    };

    private void OnPageDot0Click(object sender, RoutedEventArgs e) => NavigateToPage(0, 0 > _viewModel.SelectedPageIndex);
    private void OnPageDot1Click(object sender, RoutedEventArgs e) => NavigateToPage(1, 1 > _viewModel.SelectedPageIndex);
    private void OnPageDot2Click(object sender, RoutedEventArgs e) => NavigateToPage(2, 2 > _viewModel.SelectedPageIndex);

    private void OnSurfacePointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!_viewModel.IsExpanded)
        {
            return;
        }

        var point = e.GetCurrentPoint(Surface);
        var delta = point.Properties.MouseWheelDelta;

        if (Math.Abs(delta) < 30)
        {
            return;
        }

        var now = DateTime.UtcNow.Ticks;
        if (now - _lastWheelNavTicks < WheelDebounceTicks)
        {
            e.Handled = true;
            return;
        }

        _lastWheelNavTicks = now;
        e.Handled = true;

        _collapseDelayTimer.Stop();

        int currentIndex = _viewModel.SelectedPageIndex;
        if (delta < 0)
        {
            int nextIndex = (currentIndex + 1) % 3;
            NavigateToPage(nextIndex, isForward: true);
        }
        else
        {
            int prevIndex = (currentIndex - 1 + 3) % 3;
            NavigateToPage(prevIndex, isForward: false);
        }

        if (!_isPointerOverSurface && _viewModel.AutoCollapse)
        {
            _collapseDelayTimer.Interval = TimeSpan.FromMilliseconds(600);
            _collapseDelayTimer.Start();
        }
    }

    private void NavigateToPage(int targetIndex, bool isForward)
    {
        targetIndex = Math.Clamp(targetIndex, 0, 2);
        int fromIndex = _viewModel.SelectedPageIndex;
        if (fromIndex == targetIndex) return;

        AnimatePageTransition(fromIndex, targetIndex, isForward);
    }

    private void AnimatePageTransition(int fromIndex, int toIndex, bool isForward)
    {
        try
        {
            var compositor = _surfaceVisual.Compositor;
            var springEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
            var fadeEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.6f, 1f));

            var fromGrid = GetPanelGrid(fromIndex);
            var toGrid = GetPanelGrid(toIndex);
            var fromVisual = GetPanelVisual(fromIndex);
            var toVisual = GetPanelVisual(toIndex);

            toGrid.Visibility = Visibility.Visible;

            var outFade = compositor.CreateScalarKeyFrameAnimation();
            outFade.Duration = TimeSpan.FromMilliseconds(160);
            outFade.InsertKeyFrame(1f, 0f, fadeEasing);
            fromVisual.StartAnimation("Opacity", outFade);

            var outTranslate = compositor.CreateVector3KeyFrameAnimation();
            outTranslate.Duration = TimeSpan.FromMilliseconds(180);
            float outTargetX = isForward ? -26f : 26f;
            outTranslate.InsertKeyFrame(1f, new Vector3(outTargetX, 0, 0), springEasing);
            fromVisual.StartAnimation("Translation", outTranslate);

            var inFade = compositor.CreateScalarKeyFrameAnimation();
            inFade.Duration = TimeSpan.FromMilliseconds(220);
            inFade.InsertKeyFrame(0f, 0f);
            inFade.InsertKeyFrame(1f, 1f, springEasing);
            toVisual.StartAnimation("Opacity", inFade);

            var inTranslate = compositor.CreateVector3KeyFrameAnimation();
            inTranslate.Duration = TimeSpan.FromMilliseconds(220);
            float inStartX = isForward ? 26f : -26f;
            inTranslate.InsertKeyFrame(0f, new Vector3(inStartX, 0, 0));
            inTranslate.InsertKeyFrame(1f, Vector3.Zero, springEasing);
            toVisual.StartAnimation("Translation", inTranslate);

            _viewModel.SelectedPageIndex = toIndex;
            UpdatePageDots(toIndex, animated: true);

            _pageTransitionCleanupTimer?.Stop();
            _pageTransitionCleanupTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _pageTransitionCleanupTimer.Interval = TimeSpan.FromMilliseconds(240);
            _pageTransitionCleanupTimer.Tick += (s, e) =>
            {
                _pageTransitionCleanupTimer.Stop();
                for (int i = 0; i < 3; i++)
                {
                    var grid = GetPanelGrid(i);
                    var visual = GetPanelVisual(i);
                    if (i == toIndex)
                    {
                        grid.Visibility = Visibility.Visible;
                        grid.Translation = Vector3.Zero;
                        visual.Opacity = 1f;
                        visual.StopAnimation("Translation");
                    }
                    else
                    {
                        grid.Visibility = Visibility.Collapsed;
                        grid.Translation = Vector3.Zero;
                        visual.Opacity = 0f;
                        visual.StopAnimation("Translation");
                    }
                }
            };
            _pageTransitionCleanupTimer.Start();
        }
        catch
        {
            _viewModel.SelectedPageIndex = toIndex;
            for (int i = 0; i < 3; i++)
            {
                GetPanelGrid(i).Visibility = (i == toIndex) ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdatePageDots(toIndex, animated: false);
        }
    }

    private void UpdatePageDots(int activeIndex, bool animated)
    {
        var dots = new[] { PageDot0, PageDot1, PageDot2 };
        for (int i = 0; i < dots.Length; i++)
        {
            var dot = dots[i];
            if (dot is null) continue;
            bool isActive = (i == activeIndex);
            double targetWidth = isActive ? 18.0 : 6.0;
            double targetOpacity = isActive ? 1.0 : 0.35;

            if (animated)
            {
                AnimateDot(dot, targetWidth, targetOpacity, isActive);
            }
            else
            {
                dot.Width = targetWidth;
                dot.Opacity = targetOpacity;
                if (WindowRoot.Resources.TryGetValue(isActive ? "Accent" : "PrimaryText", out var brushObj) && brushObj is Microsoft.UI.Xaml.Media.Brush brush)
                {
                    dot.Background = brush;
                }
            }
        }
    }

    private void AnimateDot(FrameworkElement dot, double targetWidth, double targetOpacity, bool isActive)
    {
        try
        {
            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            var widthAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = targetWidth,
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(widthAnim, dot);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(widthAnim, "Width");
            sb.Children.Add(widthAnim);

            var opacityAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = targetOpacity,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(opacityAnim, dot);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(opacityAnim, "Opacity");
            sb.Children.Add(opacityAnim);

            sb.Begin();

            if (dot is Button btn && WindowRoot.Resources.TryGetValue(isActive ? "Accent" : "PrimaryText", out var brushObj) && brushObj is Microsoft.UI.Xaml.Media.Brush brush)
            {
                btn.Background = brush;
            }
        }
        catch
        {
            dot.Width = targetWidth;
            dot.Opacity = targetOpacity;
        }
    }

    private void OnToggleExpandedClick(object sender, RoutedEventArgs e) => _viewModel.ToggleExpanded();
    private void OnCollapseClick(object sender, RoutedEventArgs e) => _viewModel.Collapse();
    private void OnToggleMuteClick(object sender, RoutedEventArgs e) => _viewModel.ToggleMuteCommand.Execute(null);
    private void OnHideIslandClick(object sender, RoutedEventArgs e) => HideIslandAnimated();

    private void OnOpenTaskbarFlyoutClick(object sender, RoutedEventArgs e)
        => TaskbarFlyoutRequested?.Invoke(this, EventArgs.Empty);

    private void OnHiddenAnchorPointerEntered(object sender, PointerRoutedEventArgs e) => UnhideIslandAnimated();
    private void OnHiddenAnchorPointerReleased(object sender, PointerRoutedEventArgs e) => UnhideIslandAnimated();

    private void OnSettingsClick(object sender, RoutedEventArgs e)
        => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnSeekBarScrubStarted(object? sender, double percent)
    {
        _collapseDelayTimer.Stop();
        _viewModel.BeginScrub(percent);
    }

    private void OnSeekBarScrubDelta(object? sender, double percent) => _viewModel.UpdateScrub(percent);

    private async void OnSeekBarScrubCompleted(object? sender, double percent)
    {
        try
        {
            await _viewModel.EndScrubAsync(percent);
        }
        catch { }

        if (!_isPointerOverSurface && _viewModel.IsExpanded && _viewModel.AutoCollapse)
        {
            _collapseDelayTimer.Interval = TimeSpan.FromMilliseconds(600);
            _collapseDelayTimer.Start();
        }
    }

    private void OnButtonPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isInteractingWithControl = true;
        _collapseDelayTimer.Stop();
        if (sender is FrameworkElement el) AnimateButtonScale(el, 0.92f);
        e.Handled = true;
    }

    private void OnButtonPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isInteractingWithControl = false;
        if (sender is FrameworkElement el) AnimateButtonScale(el, 1.0f);
        e.Handled = true;

        if (!_isPointerOverSurface && _viewModel.IsExpanded && _viewModel.AutoCollapse && !_viewModel.PreserveFocusPanel)
        {
            _collapseDelayTimer.Interval = TimeSpan.FromMilliseconds(600);
            _collapseDelayTimer.Start();
        }
    }

    private void AnimateButtonScale(FrameworkElement element, float targetScale)
    {
        if (ReducedMotion)
        {
            return;
        }

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
    // Apple "Liquid Glass": Microsoft's DesktopAcrylicBackdrop conflicts with the
    // DWM transparency trick WinUIEx uses, so when glass is enabled we render our own
    // frost layer: sample the live desktop behind the island, blur it and tint it
    // in-app. Never touches SystemBackdrop (keeps the TransparentTintBackdrop wired
    // in XAML so the window stays alpha-capable).
    public void ApplyGlass()
    {
        _glassEnabled = _viewModel.UseTransparency;
        FrostLayer.Visibility = _glassEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (_glassEnabled)
        {
            EnsureFrostTimer();
            _ = RefreshFrostAsync();
        }
        else
        {
            _frostTimer?.Stop();
            FrostLayer.Fill = null;
        }
    }

    private DispatcherQueueTimer? _frostTimer;
    private bool _frostBusy;

    private void EnsureFrostTimer()
    {
        if (_frostTimer is not null) return;
        _frostTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _frostTimer.Interval = TimeSpan.FromMilliseconds(2500);
        _frostTimer.Tick += (_, _) => _ = RefreshFrostAsync();
    }

    /// <summary>Samples the desktop area under the island, blurs + tints it into the frost brush.</summary>
    private async Task RefreshFrostAsync(bool force = false)
    {
        if (!_glassEnabled || _isIslandHidden || _frostBusy || FrostLayer is null) return;
        // Periodic refreshes skip while the pointer is over the pill (no flicker under
        // the cursor); forced refreshes (state change / expand) always run.
        if (!force && _isPointerOverSurface) return;

        _frostBusy = true;
        try
        {
            var clientW = 0;
            var clientH = 0;
            var screenX = 0;
            var screenY = 0;
            RunOnDispatcher(() =>
            {
                // Sample exactly the pill's on-screen rect so the frost maps 1:1.
                try
                {
                    var tf = Surface.TransformToVisual(null);
                    var bounds = tf.TransformBounds(new Windows.Foundation.Rect(0, 0, Surface.ActualWidth, Surface.ActualHeight));
                    GetClientRect(_hwnd, out var crc);
                    var pt = new Win32Point();
                    _ = ClientToScreen(_hwnd, ref pt);
                    screenX = pt.X + (int)Math.Round(bounds.X * _dpiScale);
                    screenY = pt.Y + (int)Math.Round(bounds.Y * _dpiScale);
                    clientW = Math.Max(16, (int)Math.Round(bounds.Width * _dpiScale));
                    clientH = Math.Max(16, (int)Math.Round(bounds.Height * _dpiScale));
                }
                catch
                {
                    GetClientRect(_hwnd, out var crc);
                    var pt = new Win32Point();
                    _ = ClientToScreen(_hwnd, ref pt);
                    screenX = pt.X;
                    screenY = pt.Y;
                    clientW = Math.Max(1, crc.Right - crc.Left);
                    clientH = Math.Max(1, crc.Bottom - crc.Top);
                }
            });
            if (clientW < 16 || clientH < 16) return;

            var frost = await Task.Run(() =>
            {
                var bmp = CaptureDesktopSample(screenX, screenY, clientW, clientH);
                if (bmp is null) return null;

                var tint = CurrentGlassTint();
                var result = FrostAndTint(bmp, tint);
                bmp.Dispose();
                return result;
            });
            if (frost is null) return;

            byte[] pngBytes;
            using (var ms = new System.IO.MemoryStream())
            {
                frost.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                pngBytes = ms.ToArray();
            }
            frost.Dispose();

            RunOnDispatcher(() =>
            {
                var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(pngBytes);
                    _ = writer.StoreAsync().AsTask().GetAwaiter().GetResult();
                    _ = writer.FlushAsync().AsTask().GetAwaiter().GetResult();
                }

                var image = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                _ = image.SetSourceAsync(stream);
                FrostLayer.Fill = new Microsoft.UI.Xaml.Media.ImageBrush
                {
                    ImageSource = image,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill
                };
            });
        }
        catch
        {
            // Be resilient — worst case the frost simply shows the previous frame.
        }
        finally
        {
            _frostBusy = false;
        }
    }

    private void RunOnDispatcher(Action action)
    {
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        if (dispatcher.HasThreadAccess)
        {
            action();
            return;
        }
        using var evt = new System.Threading.ManualResetEvent(false);
        dispatcher.TryEnqueue(() =>
        {
            try { action(); }
            finally { _ = evt.Set(); }
        });
        _ = evt.WaitOne(200);
    }

    private uint CurrentGlassTint()
    {
        var artworkAccent = _viewModel.DynamicColorTint ? _viewModel.ArtworkAccentColor : 0xFF007AFFu;
        return _viewModel.Theme == "Light"
            ? 0xB8F2F5F7u
            : Blend(0xB20B1018u, artworkAccent, 0.20);
    }

    /// <summary>
    /// Captures the screen region behind the island. The window is briefly hidden
    /// so the sample contains only the real desktop beneath it (no self-feedback).
    /// </summary>
    private System.Drawing.Bitmap? CaptureDesktopSample(int x, int y, int width, int height)
    {
        try
        {
            _ = SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpHideWindow | SwpNoActivate | SwpNoMove | SwpNoSize);
            System.Threading.Thread.Sleep(45); // one DWM frame so the desktop shows through
        }
        catch
        {
            // ignore — capture below still runs; worst case includes the pill itself
        }

        System.Drawing.Bitmap? captured = null;
        try
        {
            captured = new System.Drawing.Bitmap(width, height);
            using var g = System.Drawing.Graphics.FromImage(captured);
            g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height), System.Drawing.CopyPixelOperation.SourceCopy);
        }
        catch
        {
        }
        finally
        {
            try
            {
                _ = SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpShowWindow | SwpNoActivate | SwpNoMove | SwpNoSize);
            }
            catch
            {
            }
        }

        return captured;
    }

    private static System.Drawing.Bitmap FrostAndTint(System.Drawing.Bitmap captured, uint tint)
    {
        var w = captured.Width;
        var h = captured.Height;

        // Progressive "frosted" blur: downscale hard, upscale smooth.
        var fSmall = Math.Max(4, w / 8);
        var fSmallH = Math.Max(4, h / 8);
        using var small = new System.Drawing.Bitmap(fSmall, fSmallH);
        using (var g = System.Drawing.Graphics.FromImage(small))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.DrawImage(captured, new System.Drawing.Rectangle(0, 0, fSmall, fSmallH),
                0, 0, w, h, System.Drawing.GraphicsUnit.Pixel);
        }

        var midW = Math.Max(8, w / 2);
        var midH = Math.Max(8, h / 2);
        using var mid = new System.Drawing.Bitmap(midW, midH);
        using (var g = System.Drawing.Graphics.FromImage(mid))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(small, 0, 0, midW, midH);
        }

        var frost = new System.Drawing.Bitmap(w, h);
        using (var g = System.Drawing.Graphics.FromImage(frost))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(mid, 0, 0, w, h);

            // Tint blend (the ApplyTheme glass palette colour)
            var tintBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(
                (byte)(tint >> 24), (byte)(tint >> 16), (byte)(tint >> 8), (byte)tint));
            g.FillRectangle(tintBrush, 0, 0, w, h);
            tintBrush.Dispose();

            // iOS-style specular sheen along the top edge
            using var sheen = new System.Drawing.Drawing2D.LinearGradientBrush(
                new System.Drawing.RectangleF(0, 0, w, Math.Max(1, h * 0.45f)),
                System.Drawing.Color.FromArgb(36, 255, 255, 255),
                System.Drawing.Color.FromArgb(0, 255, 255, 255),
                System.Drawing.Drawing2D.LinearGradientMode.Vertical);
            g.FillRectangle(sheen, 0, 0, w, Math.Max(1, (int)(h * 0.45f)));
        }

        return frost;
    }

    public void ApplyTheme(string theme)
    {
        var isLight = theme == "Light";
        WindowRoot.RequestedTheme = isLight ? ElementTheme.Light : ElementTheme.Dark;

        var artworkAccent = _viewModel.DynamicColorTint ? _viewModel.ArtworkAccentColor : 0xFF007AFFu;
        var accentColor  = artworkAccent;
        // Solid surfaces stay near-opaque; glass surfaces go translucent so acrylic really shows.
        var bgColor      = isLight
            ? (_glassEnabled ? 0xB8F2F5F7u : 0xF2F5F5F7u)
            : (_glassEnabled ? Blend(0x990B1018u, artworkAccent, 0.20) : Blend(0xF20B1018u, artworkAccent, 0.20));
        var borderColor  = isLight ? 0x22000000u : Blend(0x33FFFFFFu, artworkAccent, 0.22);
        var primaryText  = isLight ? 0xFF1C1C1Eu : 0xFFFFFFFFu;
        var secondaryTxt = isLight ? 0xFF8E8E93u : 0xFFA4ABB6u;
        var pillCtrlBg   = isLight ? 0x14000000u : Blend(0x24FFFFFFu, artworkAccent, 0.14);
        var playBg       = accentColor;
        var playFg       = 0xFFFFFFFFu;
        var seekAccent   = accentColor;

        UpdateBrush("IslandBackground", bgColor);
        UpdateBrush("IslandBorder", borderColor);
        UpdateBrush("PrimaryText", primaryText);
        UpdateBrush("SecondaryText", secondaryTxt);
        UpdateBrush("PillControlBackground", pillCtrlBg);
        var sliderFill = isLight ? 0xFF1C1C1Eu : 0xFFFFFFFFu;
        var sliderFillPressed = isLight ? 0xFF3A3A3Cu : 0xE6FFFFFFu;
        var sliderTrack = isLight ? 0x1F000000u : 0x26FFFFFFu;
        var sliderTrackHover = isLight ? 0x29000000u : 0x33FFFFFFu;
        var sliderThumb = isLight ? 0xFFFFFFFFu : 0xFFFFFFFFu;
        var sliderThumbPressed = isLight ? 0xFFE5E5EAu : 0xE6FFFFFFu;
        UpdateBrush("SliderTrackValueFill", sliderFill);
        UpdateBrush("SliderTrackValueFillPointerOver", sliderFill);
        UpdateBrush("SliderTrackValueFillPressed", sliderFillPressed);
        UpdateBrush("SliderTrackFill", sliderTrack);
        UpdateBrush("SliderTrackFillPointerOver", sliderTrackHover);
        UpdateBrush("SliderTrackFillPressed", sliderTrackHover);
        UpdateBrush("SliderThumbBackground", sliderThumb);
        UpdateBrush("SliderThumbBackgroundPointerOver", sliderThumb);
        UpdateBrush("SliderThumbBackgroundPressed", sliderThumbPressed);
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
        UpdatePageDots(_viewModel.SelectedPageIndex, animated: false);

        // Re-capture the frost snapshot so the glass tint matches the new theme right away.
        if (_glassEnabled)
        {
            _ = RefreshFrostAsync(force: true);
        }
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

    // Subclass WndProc to handle native hit-testing and eliminate OS window minimum size constraints
    private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        const uint WM_GETMINMAXINFO = 0x0024;
        const uint WM_NCHITTEST = 0x0084;
        const uint WM_HOTKEY = 0x0312;
        const uint WM_WINDOWPOSCHANGING = 0x0046;
        const uint SwpNoZOrder = 0x0004;

        if (uMsg == WM_WINDOWPOSCHANGING)
        {
            // Immune to z-order demotion: any real z-order change is forced back to topmost.
            var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((wp.flags & SwpNoZOrder) == 0)
            {
                wp.hwndInsertAfter = HwndTopmost;
                Marshal.StructureToPtr(wp, lParam, false);
            }
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        if (uMsg == WM_HOTKEY)
        {
            var hotkeyId = wParam.ToInt32();
            if (hotkeyId == HotkeyToggleIsland)
            {
                ToggleHideIsland();
                return IntPtr.Zero;
            }

            if (hotkeyId == HotkeyToggleFocus)
            {
                if (_isIslandHidden)
                {
                    UnhideIslandAnimated();
                }
                _viewModel.ToggleFocusTimerCommand.Execute(null);
                return IntPtr.Zero;
            }
        }

        if (uMsg == WM_GETMINMAXINFO)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            mmi.ptMinTrackSize.X = 1;
            mmi.ptMinTrackSize.Y = 1;
            Marshal.StructureToPtr(mmi, lParam, false);
            return IntPtr.Zero;
        }

        if (uMsg == WM_NCHITTEST)
        {
            // During active control drag/scrub or button press, maintain hit testing
            if (_isInteractingWithControl || _viewModel.IsScrubbing)
            {
                return DefSubclassProc(hWnd, uMsg, wParam, lParam);
            }

            int screenX = unchecked((short)(long)lParam);
            int screenY = unchecked((short)((long)lParam >> 16));

            if (!IsPointInsideVisibleIsland(screenX, screenY))
            {
                const int HTTRANSPARENT = -1;
                return new IntPtr(HTTRANSPARENT);
            }
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private bool IsPointInsideVisibleIsland(int screenX, int screenY)
    {
        if (_hwnd == IntPtr.Zero) return false;
        if (!GetWindowRect(_hwnd, out var winRect)) return false;

        if (screenX < winRect.Left || screenX > winRect.Right ||
            screenY < winRect.Top || screenY > winRect.Bottom)
        {
            return false;
        }

        if (_dpiScale <= 0) _dpiScale = 1.0;

        double localDipX = (screenX - winRect.Left) / _dpiScale;
        double localDipY = (screenY - winRect.Top) / _dpiScale;

        double hostWidth = WindowRoot.Width > 0 ? WindowRoot.Width : 420.0;

        if (_isIslandHidden)
        {
            double anchorLeft = (hostWidth - 48.0) / 2.0;
            double anchorRight = anchorLeft + 48.0;
            double anchorTop = 0.0;
            double anchorBottom = 16.0;

            return localDipX >= anchorLeft && localDipX <= anchorRight &&
                   localDipY >= anchorTop && localDipY <= anchorBottom;
        }

        double pillWidth = _currentPillWidth > 0 ? _currentPillWidth : Surface.ActualWidth;
        if (pillWidth <= 0) pillWidth = 216.0;

        double pillHeight = _currentPillHeight > 0 ? _currentPillHeight : Surface.ActualHeight;
        if (pillHeight <= 0) pillHeight = 40.0;

        double leftDip = (hostWidth - pillWidth) / 2.0;
        double rightDip = leftDip + pillWidth;
        double topDip = 0.0;
        double bottomDip = pillHeight;

        return localDipX >= (leftDip - 2.0) && localDipX <= (rightDip + 2.0) &&
               localDipY >= (topDip - 2.0) && localDipY <= (bottomDip + 2.0);
    }

    // P/Invoke
    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref Win32Point lpPoint);

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
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
    }
}
