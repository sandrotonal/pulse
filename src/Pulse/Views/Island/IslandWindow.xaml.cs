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
    private const uint MonitorDefaultToNearest = 2;
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
    private string? _lastPulseTrackKey;

    private double _dpiScale = 1.0;
    private bool _isIslandHidden;
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

        var scale = Math.Max(1.0, _viewModel.IslandScale);
        var hostWidthDip = Math.Max(420.0, 384.0 * scale + 24.0);
        var hostHeightDip = Math.Max(260.0, 220.0 * scale + 24.0);

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
                 or nameof(MediaViewModel.DynamicColorTint))
        {
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
            UpdateStateLayout(_viewModel.CurrentIslandState, animated: true);
        }
    }

    private void AnimateSurfaceDimensions(double toWidth, double toHeight, double toCornerRadius, double durationMs, Action? onCompleted = null)
    {
        _animStartWidth = Surface.Width > 0 ? Surface.Width : Surface.ActualWidth;
        if (_animStartWidth <= 0) _animStartWidth = 216.0;

        _animStartHeight = Surface.Height > 0 ? Surface.Height : Surface.ActualHeight;
        if (_animStartHeight <= 0) _animStartHeight = 40.0;

        _animStartCornerRadius = Surface.CornerRadius.TopLeft > 0 ? Surface.CornerRadius.TopLeft : 20.0;

        _animTargetWidth = toWidth;
        _animTargetHeight = toHeight;
        _animTargetCornerRadius = toCornerRadius;
        _animDurationMs = Math.Max(1.0, durationMs);
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

        // Apple fluid cubic ease-out: 1 - (1 - t)^3
        double inv = 1.0 - progress;
        double ease = 1.0 - (inv * inv * inv);

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
            targetHeight = 220;
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
            return;
        }

        try
        {
            var compositor = _surfaceVisual.Compositor;
            var springEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
            var quickEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.6f, 1f));

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
                compactFade.Duration = TimeSpan.FromMilliseconds(90);
                compactFade.InsertKeyFrame(1f, 0f, quickEasing);
                _compactVisual.StartAnimation("Opacity", compactFade);

                var expandedFade = compositor.CreateScalarKeyFrameAnimation();
                expandedFade.Duration = TimeSpan.FromMilliseconds(260);
                expandedFade.InsertKeyFrame(0f, 0f);
                expandedFade.InsertKeyFrame(0.15f, 0f);
                expandedFade.InsertKeyFrame(1f, 1f, springEasing);
                _expandedVisual.StartAnimation("Opacity", expandedFade);

                var expandTranslate = compositor.CreateVector3KeyFrameAnimation();
                expandTranslate.Duration = TimeSpan.FromMilliseconds(280);
                expandTranslate.InsertKeyFrame(0f, new Vector3(0, 8, 0));
                expandTranslate.InsertKeyFrame(1f, Vector3.Zero, springEasing);
                _expandedVisual.StartAnimation("Translation", expandTranslate);

                AnimateSurfaceDimensions(targetWidth, targetHeight, targetCornerRadius, 300);
            }
            else if (isCollapsing)
            {
                CompactView.IsHitTestVisible = true;
                ExpandedView.IsHitTestVisible = false;

                var expandedFade = compositor.CreateScalarKeyFrameAnimation();
                expandedFade.Duration = TimeSpan.FromMilliseconds(110);
                expandedFade.InsertKeyFrame(1f, 0f, quickEasing);
                _expandedVisual.StartAnimation("Opacity", expandedFade);

                var compactFade = compositor.CreateScalarKeyFrameAnimation();
                compactFade.Duration = TimeSpan.FromMilliseconds(220);
                compactFade.InsertKeyFrame(0f, 0f);
                compactFade.InsertKeyFrame(0.15f, 0f);
                compactFade.InsertKeyFrame(1f, 1f, springEasing);
                _compactVisual.StartAnimation("Opacity", compactFade);

                AnimateSurfaceDimensions(targetWidth, targetHeight, targetCornerRadius, 280, () =>
                {
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

                AnimateSurfaceDimensions(targetWidth, targetHeight, targetCornerRadius, 160);
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
        if (!_isPointerOverSurface && _viewModel.IsExpanded && _viewModel.AutoCollapse && !_viewModel.IsScrubbing && !_isInteractingWithControl)
        {
            _viewModel.Collapse();
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

        if (!_isPointerOverSurface && _viewModel.IsExpanded && _viewModel.AutoCollapse)
        {
            _collapseDelayTimer.Interval = TimeSpan.FromMilliseconds(600);
            _collapseDelayTimer.Start();
        }
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

        var artworkAccent = _viewModel.DynamicColorTint ? _viewModel.ArtworkAccentColor : 0xFF007AFFu;
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
        UpdatePageDots(_viewModel.SelectedPageIndex, animated: false);
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
