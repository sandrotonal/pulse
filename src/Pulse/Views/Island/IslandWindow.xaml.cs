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
using Pulse.Core.Animation;
using Pulse.Core.Models;
using Pulse.Core.Services;
using Pulse.Core.Services.Weather;
using Pulse.Services.Glass;
using Pulse.Services.System;
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
    private const uint MonitorDefaultToPrimary = 1;
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
    private readonly Visual _pillMaskVisual;
    private readonly Visual _frostSheenVisual;
    private CompositionRoundedRectangleGeometry? _surfaceClipGeometry;
    private CompositionRoundedRectangleGeometry? _frostRimGeometry;
    private CompositionSpriteShape? _frostRimShape;
    private ShapeVisual? _frostRimVisual;
    private readonly Visual _compactVisual;
    private Visual _expandedVisual;
    private readonly Visual _wave1Visual;
    private readonly Visual _wave2Visual;
    private readonly Visual _wave3Visual;
    private readonly Visual _mediaPanelVisual;
    private readonly Visual _focusPanelVisual;
    private readonly Visual _systemPanelVisual;
    private readonly Visual _detachedVisual;
    private readonly Visual _batteryChargeHaloVisual;
    private readonly Border[] _dotVisualElements;
    private readonly Visual[] _dotVisuals = new Visual[3];
    private readonly CompositionRoundedRectangleGeometry[] _dotClipGeometries = new CompositionRoundedRectangleGeometry[3];
    private readonly DampedSpringOscillator _springMinimal = new(dampingRatio: 0.95, responseSeconds: 0.25);
    private readonly DampedSpringOscillator _springNormal = new(dampingRatio: 0.82, responseSeconds: 0.34);
    private readonly DampedSpringOscillator _springDynamic = new(dampingRatio: 0.60, responseSeconds: 0.40);
    private readonly DampedSpringOscillator _pageSpringMinimal = new(dampingRatio: 0.95, responseSeconds: 0.22);
    private readonly DampedSpringOscillator _pageSpringNormal = new(dampingRatio: 0.90, responseSeconds: 0.30);
    private readonly DampedSpringOscillator _pageSpringDynamic = new(dampingRatio: 0.72, responseSeconds: 0.34);
    private readonly DampedSpringOscillator _dotSpringMinimal = new(dampingRatio: 0.95, responseSeconds: 0.18);
    private readonly DampedSpringOscillator _dotSpringNormal = new(dampingRatio: 0.86, responseSeconds: 0.24);
    private readonly DampedSpringOscillator _dotSpringDynamic = new(dampingRatio: 0.68, responseSeconds: 0.28);
    private DampedSpringOscillator _activeDimensionSpring;
    private readonly SubclassProc _subclassProc;

    private bool _isRenderLoopSubscribed;
    private long _lastRenderTicks;

    private long _lastWheelNavTicks;
    private const long WheelDebounceTicks = 200 * TimeSpan.TicksPerMillisecond;
    private const float PageSlideDistance = 24f;
    private int _targetPageIndex;
    private bool _isPageAnimating;
    private double _pageAnimElapsedSec;
    private int _pageAnimFromIndex;
    private int _pageAnimToIndex;
    private readonly double[] _pageFromX = new double[3];
    private readonly double[] _pageToX = new double[3];
    private readonly double[] _pageVelX = new double[3];
    private readonly double[] _pageCurrX = new double[3];
    private readonly double[] _pageCurrVelX = new double[3];
    private readonly double[] _pageFromOpacity = new double[3];
    private readonly double[] _pageToOpacity = new double[3];
    private readonly double[] _pageVelOpacity = new double[3];
    private readonly double[] _pageCurrOpacity = new double[3];
    private readonly double[] _pageCurrVelOpacity = new double[3];
    private readonly double[] _pageFromScale = new double[3];
    private readonly double[] _pageToScale = new double[3];
    private readonly double[] _pageVelScale = new double[3];
    private readonly double[] _pageCurrScale = new double[3];
    private readonly double[] _pageCurrVelScale = new double[3];

    private bool _isDotAnimating;
    private double _dotAnimElapsedSec;
    private readonly double[] _dotStartWidth = [18.0, 6.0, 6.0];
    private readonly double[] _dotTargetWidth = [18.0, 6.0, 6.0];
    private readonly double[] _dotVelWidth = new double[3];
    private readonly double[] _dotCurrWidth = [18.0, 6.0, 6.0];
    private readonly double[] _dotCurrVelWidth = new double[3];
    private readonly double[] _dotStartOpacity = [1.0, 0.35, 0.35];
    private readonly double[] _dotTargetOpacity = [1.0, 0.35, 0.35];
    private readonly double[] _dotVelOpacity = new double[3];
    private readonly double[] _dotCurrOpacity = [1.0, 0.35, 0.35];
    private readonly double[] _dotCurrVelOpacity = new double[3];

    private DispatcherQueueTimer? _hideAnimationTimer;
    private DispatcherQueueTimer? _topmostGuardTimer;
    private DispatcherQueueTimer? _displaySettleTimer;
    private DispatcherQueueTimer? _unhideRecoveryTimer;
    private DispatcherQueueTimer? _frostRefreshTimer;
    private int _displaySettlePassesRemaining;
    private long _hideShowGeneration;
    private long _frostGeneration;
    private bool _hasFrostBitmap;
    private readonly FullscreenStabilityTracker _fullscreenTracker = new(exitConfirmationThreshold: 2);
    private const int DwmwaCloaked = 14;
    private string? _lastPulseTrackKey;

    private double _dpiScale = 1.0;
    private bool _isIslandHidden;
    private bool _glassEnabled;
    private double _currentPillWidth = 216;
    private double _currentPillHeight = 40;
    private double _currentCornerRadius = 20;
    private double _currentLeadingX;
    private double _currentTrailingX = 66;
    private double _currentDetachedOpacity;
    private double _currentExpandedScale = 1.0;
    private double _velPillWidth;
    private double _velPillHeight;
    private double _velCornerRadius;
    private double _velLeadingX;
    private double _velTrailingX;
    private double _velDetachedOpacity;
    private double _velExpandedScale;
    private bool _regionCommitted;
    private (int L1, int T1, int R1, int B1, int C1, int L2, int T2, int R2, int B2, int C2)? _lastWindowRegion;

    private bool _waveAnimating;
    private bool _batteryChargeAnimating;
    private readonly DispatcherQueueTimer _collapseDelayTimer;
    private DispatcherQueueTimer? _hoverExpandTimer;
    private DispatcherQueueTimer? _longPressTimer;
    private bool _longPressTriggered;
    private bool _hideAnchorVisible = true;
    private DispatcherQueueTimer? _fullscreenPollTimer;
    private bool _fullscreenActive;
    private bool _autoHiddenForFullscreen;
    private bool _userOverrodeFullscreenHide;
    private bool _isPointerOverSurface;
    private bool _isInteractingWithControl;
    private long _hostResizeGuardTicks;
    private static readonly long HostResizeGuardTicks = (long)(300 * (System.Diagnostics.Stopwatch.Frequency / 1000.0));
    private long _transitionGeneration;
    private double _animStartWidth;
    private double _animStartHeight;
    private double _animStartCornerRadius;
    private double _animTargetWidth;
    private double _animTargetHeight;
    private double _animTargetCornerRadius;
    private double _animStartLeadingX;
    private double _animTargetLeadingX;
    private double _animStartTrailingX;
    private double _animTargetTrailingX;
    private double _animStartDetachedOpacity;
    private double _animTargetDetachedOpacity;
    private bool _animStartDetachedActive;
    private bool _animTargetDetachedActive;
    private double _animStartExpandedScale = 1.0;
    private double _animTargetExpandedScale = 1.0;
    private double _animVelWidth;
    private double _animVelHeight;
    private double _animVelCornerRadius;
    private double _animVelLeadingX;
    private double _animVelTrailingX;
    private double _animVelDetachedOpacity;
    private double _animVelExpandedScale;
    private double _dimensionAnimElapsedSec;
    private double _animDurationMs;
    private bool _isDimensionAnimating;
    private Action? _onAnimationCompleted;
    private bool _lastSplitModeActive;
    private bool _lastFocusTimerActive;
    private bool _lastNotificationActive;
    private bool _lastFileOpActive;
    private double _appliedCompactScale = double.NaN;
    private readonly char[] _classNameBuffer = new char[64];
    private long _frostRestoreGuardTicks;
    private volatile bool _windowClosed;
    private bool _sessionLocked;
    private bool _sessionNotificationRegistered;
    private bool _initialPositionApplied;
    private IntPtr _lastMonitorHandle;
    private readonly IslandSurfaceLayout.WheelStepAccumulator _wheelAccumulator = new();
    private Microsoft.UI.Xaml.Media.RectangleGeometry? _expandedPanelsClip;
    private Microsoft.UI.Xaml.Media.ImageBrush? _detachedFrostBrush;
    private Microsoft.UI.Xaml.Media.TranslateTransform? _detachedFrostTransform;
    private PointerEventHandler? _sliderPressedHandler;
    private PointerEventHandler? _sliderReleasedHandler;

    public string HotkeyConflictWarning { get; private set; } = string.Empty;
    public event EventHandler? SettingsRequested;

    public IslandWindow(IslandViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        WindowRoot.DataContext = viewModel;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        _collapseDelayTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _collapseDelayTimer.Interval = GetCollapseDelayInterval();
        _collapseDelayTimer.Tick += OnCollapseDelayTimerTick;

        _hoverExpandTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _hoverExpandTimer.IsRepeating = false;
        _hoverExpandTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_viewModel.HoverExpandDelayMs, 0, 1000));
        _hoverExpandTimer.Tick += OnHoverExpandTimerTick;

        _longPressTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _longPressTimer.IsRepeating = false;
        _longPressTimer.Interval = TimeSpan.FromMilliseconds(500);
        _longPressTimer.Tick += OnLongPressTimerTick;

        _fullscreenPollTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _fullscreenPollTimer.Interval = TimeSpan.FromSeconds(1);
        _fullscreenPollTimer.Tick += (_, _) => PollFullscreenState();
        UpdateBackgroundPollTimerState();

        _activeDimensionSpring = _springNormal;
        _surfaceVisual = ElementCompositionPreview.GetElementVisual(Surface);
        _pillMaskVisual = ElementCompositionPreview.GetElementVisual(PillMaskHost);
        _frostSheenVisual = ElementCompositionPreview.GetElementVisual(FrostSheen);
        _compactVisual = ElementCompositionPreview.GetElementVisual(CompactView);
        _expandedVisual = ElementCompositionPreview.GetElementVisual(ExpandedView);
        _wave1Visual = ElementCompositionPreview.GetElementVisual(WaveBar1);
        _wave2Visual = ElementCompositionPreview.GetElementVisual(WaveBar2);
        _wave3Visual = ElementCompositionPreview.GetElementVisual(WaveBar3);
        _mediaPanelVisual = ElementCompositionPreview.GetElementVisual(MediaExpandedPanel);
        _focusPanelVisual = ElementCompositionPreview.GetElementVisual(FocusExpandedPanel);
        _systemPanelVisual = ElementCompositionPreview.GetElementVisual(SystemExpandedPanel);
        _detachedVisual = ElementCompositionPreview.GetElementVisual(DetachedPill);
        _batteryChargeHaloVisual = ElementCompositionPreview.GetElementVisual(BatteryChargeHalo);

        _dotVisualElements = [PageDotVisual0, PageDotVisual1, PageDotVisual2];
        for (int i = 0; i < 3; i++)
        {
            ElementCompositionPreview.SetIsTranslationEnabled(_dotVisualElements[i], true);
            _dotVisuals[i] = ElementCompositionPreview.GetElementVisual(_dotVisualElements[i]);
            var dotClip = _dotVisuals[i].Compositor.CreateRoundedRectangleGeometry();
            dotClip.CornerRadius = new Vector2(2.5f, 2.5f);
            _dotClipGeometries[i] = dotClip;
            _dotVisuals[i].Clip = _dotVisuals[i].Compositor.CreateGeometricClip(dotClip);
        }

        ElementCompositionPreview.SetIsTranslationEnabled(Surface, true);
        ElementCompositionPreview.SetIsTranslationEnabled(PillMaskHost, true);
        ElementCompositionPreview.SetIsTranslationEnabled(CompactView, true);
        ElementCompositionPreview.SetIsTranslationEnabled(ExpandedView, true);
        ElementCompositionPreview.SetIsTranslationEnabled(MediaExpandedPanel, true);
        ElementCompositionPreview.SetIsTranslationEnabled(FocusExpandedPanel, true);
        ElementCompositionPreview.SetIsTranslationEnabled(SystemExpandedPanel, true);
        ElementCompositionPreview.SetIsTranslationEnabled(DetachedPill, true);

        // Subclass window to intercept WM_NCHITTEST and WM_GETMINMAXINFO:
        _subclassProc = WndProc;
        SetWindowSubclass(_hwnd, _subclassProc, UIntPtr.Zero, IntPtr.Zero);

        // Global hotkeys: Win+Alt+I toggles the island, Win+Shift+F toggles the focus timer.
        ApplyHotkeyRegistration(_viewModel.EnableGlobalHotkeys);
        _sessionNotificationRegistered = WTSRegisterSessionNotification(_hwnd, NotifyForThisSession);

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
        EnsureSurfaceClipAndRim();
        ApplyGlass();
        ApplyTheme(_viewModel.Theme);
        ApplyCompactScale(_viewModel.IslandScale);
        UpdateIdleWeatherIcon(_viewModel.IdleWeatherIconKind);

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        WindowRoot.ActualThemeChanged += OnWindowRootActualThemeChanged;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            SystemUiSettings.AnimationsEnabledChanged += OnSystemAnimationsEnabledChanged;
        }
        Closed += OnWindowClosed;
        UpdateBatteryChargingAnimation();

        if (ExpandedPanelsHost is not null)
        {
            _expandedPanelsClip = new Microsoft.UI.Xaml.Media.RectangleGeometry();
            ExpandedPanelsHost.Clip = _expandedPanelsClip;
            ExpandedPanelsHost.SizeChanged += OnExpandedPanelsHostSizeChanged;
            UpdateExpandedPanelsClip();
        }

        if (FocusProgressTrack is not null)
        {
            FocusProgressTrack.SizeChanged += (_, _) => UpdateFocusProgressFill();
        }

        if (SystemVolumeSlider is not null)
        {
            _sliderPressedHandler = OnVolumeSliderPointerPressed;
            _sliderReleasedHandler = OnVolumeSliderPointerReleased;
            SystemVolumeSlider.AddHandler(UIElement.PointerPressedEvent, _sliderPressedHandler, handledEventsToo: true);
            SystemVolumeSlider.AddHandler(UIElement.PointerReleasedEvent, _sliderReleasedHandler, handledEventsToo: true);
            SystemVolumeSlider.AddHandler(UIElement.PointerCaptureLostEvent, _sliderReleasedHandler, handledEventsToo: true);
        }

        // Apply initial layout without animation
        UpdateStateLayout(_viewModel.CurrentIslandState, animated: false);

        // Guard against other apps/Win32 calls demoting the island out of the topmost band.
        _topmostGuardTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _topmostGuardTimer.Interval = TimeSpan.FromMilliseconds(3000);
        _topmostGuardTimer.Tick += (_, _) => ReassertTopmost();
        _topmostGuardTimer.Start();

        if (_viewModel.IsPlaying && _viewModel.ShowEqualizer)
        {
            StartEqualizerWave();
        }
    }

    public XamlRoot IslandXamlRoot => WindowRoot.XamlRoot;
    public ElementTheme IslandRequestedTheme => WindowRoot.ActualTheme;
    public bool IsIslandHidden => _isIslandHidden;

    /// <summary>Keeps the island above everything, even if another app demoted its z-order.</summary>
    public void ReassertTopmost()
    {
        if (_hwnd == IntPtr.Zero || (_isIslandHidden && !_hideAnchorVisible))
        {
            return;
        }

        _ = SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    public void ShowIsland()
    {
        // Showing the island during a fullscreen app is an explicit user override;
        // keep it visible until that fullscreen session ends.
        if (_fullscreenActive)
        {
            _userOverrodeFullscreenHide = true;
        }

        // Make the native HWND visible first so Composition keyframe/scoped batches advance on a live window.
        ShowWindow(_hwnd, 4); // SW_SHOWNOACTIVATE
        if (_isIslandHidden)
        {
            UnhideIslandAnimated();
        }
        else
        {
            _pillMaskVisual.StopAnimation("Opacity");
            _pillMaskVisual.StopAnimation("Scale");
            _pillMaskVisual.StopAnimation("Translation");
            _pillMaskVisual.Opacity = 1f;
            _pillMaskVisual.Scale = Vector3.One;
            PillMaskHost.Translation = Vector3.Zero;
            Surface.Visibility = Visibility.Visible;
            HiddenAnchor.Visibility = Visibility.Collapsed;
        }

        ApplyWindowStyles();
        PositionHostWindow();
        _lastWindowRegion = null;
        UpdateWindowRegion(_currentPillWidth, _currentPillHeight, _currentCornerRadius > 0 ? _currentCornerRadius : 20.0, forceRedraw: true);
        ReassertTopmost();
    }

    public void HideIslandAnimated(bool showAnchor = true)
    {
        if (_isIslandHidden) return;
        _isIslandHidden = true;
        _hideAnchorVisible = showAnchor;
        var gen = ++_hideShowGeneration;
        _hideAnimationTimer?.Stop();
        _unhideRecoveryTimer?.Stop();

        _hoverExpandTimer?.Stop();
        _longPressTimer?.Stop();
        _collapseDelayTimer.Stop();
        _isPointerOverSurface = false;
        _isInteractingWithControl = false;
        _longPressTriggered = false;

        if (!showAnchor)
        {
            StopAmbientAnimations();
        }

        if (ReducedMotion)
        {
            try
            {
                _pillMaskVisual.StopAnimation("Scale");
                _pillMaskVisual.StopAnimation("Opacity");
                _pillMaskVisual.StopAnimation("Translation");
                _pillMaskVisual.Opacity = 0f;
                _pillMaskVisual.Scale = Vector3.One;
                PillMaskHost.Translation = Vector3.Zero;
            }
            catch { }

            Surface.Visibility = Visibility.Collapsed;
            if (DetachedPill is not null)
            {
                _detachedVisual.StopAnimation("Opacity");
                DetachedPill.Visibility = Visibility.Collapsed;
            }

            HiddenAnchor.Visibility = _hideAnchorVisible ? Visibility.Visible : Visibility.Collapsed;
            if (!_hideAnchorVisible)
            {
                _lastWindowRegion = null;
                UpdateWindowRegion(_currentPillWidth, _currentPillHeight, _currentCornerRadius);
                ShowWindow(_hwnd, 0);
            }
            else
            {
                _lastWindowRegion = null;
                UpdateWindowRegion(_currentPillWidth, _currentPillHeight, _currentCornerRadius, forceRedraw: true);
            }
            return;
        }

        var hideDuration = ScaledDuration(180);
        try
        {
            var compositor = _pillMaskVisual.Compositor;
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(1f, 1f));

            _pillMaskVisual.CenterPoint = new Vector3((float)(IslandSurfaceLayout.DefaultHostWidthDip / 2.0), 0f, 0f);

            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.Duration = hideDuration;
            scaleAnim.InsertKeyFrame(1f, new Vector3(0.6f, 0.4f, 1f), easing);

            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.Duration = hideDuration;
            opacityAnim.InsertKeyFrame(1f, 0f, easing);

            var translateAnim = compositor.CreateVector3KeyFrameAnimation();
            translateAnim.Duration = hideDuration;
            translateAnim.InsertKeyFrame(1f, new Vector3(0f, -(float)IslandSurfaceLayout.SurfaceTopInsetDip, 0f), easing);

            _pillMaskVisual.StartAnimation("Scale", scaleAnim);
            _pillMaskVisual.StartAnimation("Opacity", opacityAnim);
            _pillMaskVisual.StartAnimation("Translation", translateAnim);

            if (DetachedPill is not null && DetachedPill.Visibility == Visibility.Visible)
            {
                _detachedVisual.StartAnimation("Opacity", opacityAnim);
            }
        }
        catch { }

        var timer = _hideAnimationTimer;
        if (timer is null)
        {
            timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (_isIslandHidden)
                {
                    Surface.Visibility = Visibility.Collapsed;
                    if (DetachedPill is not null)
                    {
                        DetachedPill.Visibility = Visibility.Collapsed;
                    }
                    HiddenAnchor.Visibility = _hideAnchorVisible ? Visibility.Visible : Visibility.Collapsed;
                    if (!_hideAnchorVisible)
                    {
                        _lastWindowRegion = null;
                        UpdateWindowRegion(_currentPillWidth, _currentPillHeight, _currentCornerRadius);
                        ShowWindow(_hwnd, 0); // SW_HIDE: leave no artifact over fullscreen content
                    }
                    else
                    {
                        UpdateWindowRegion(_currentPillWidth, _currentPillHeight, _currentCornerRadius, forceRedraw: true);
                    }
                }
            };
            _hideAnimationTimer = timer;
        }
        timer.Interval = hideDuration + TimeSpan.FromMilliseconds(15);
        timer.Start();
    }

    public void UnhideIslandAnimated()
    {
        if (!_isIslandHidden) return;
        _hideAnimationTimer?.Stop();
        _unhideRecoveryTimer?.Stop();
        _isIslandHidden = false;
        var gen = ++_hideShowGeneration;

        ShowWindow(_hwnd, 4); // SW_SHOWNOACTIVATE
        ApplyWindowStyles();
        PositionHostWindow();
        ReassertTopmost();

        HiddenAnchor.Visibility = Visibility.Collapsed;
        Surface.Visibility = Visibility.Visible;

        // Re-sync layout bounds & visibility cleanly before reveal so returning from fullscreen never shows stale widths.
        UpdateStateLayout(_viewModel.CurrentIslandState, animated: false);
        RestoreAmbientAnimations();

        _pillMaskVisual.StopAnimation("Scale");
        _pillMaskVisual.StopAnimation("Opacity");
        _pillMaskVisual.StopAnimation("Translation");

        _lastWindowRegion = null;
        UpdateWindowRegion(_currentPillWidth, _currentPillHeight, _currentCornerRadius, forceRedraw: true);

        if (ReducedMotion)
        {
            _pillMaskVisual.Opacity = 1f;
            _pillMaskVisual.Scale = Vector3.One;
            PillMaskHost.Translation = Vector3.Zero;
            if (_glassEnabled)
            {
                _ = RefreshFrostAsync(force: true);
            }
            return;
        }

        try
        {
            var compositor = _pillMaskVisual.Compositor;
            var duration = ScaledDuration(320);
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));

            _pillMaskVisual.CenterPoint = new Vector3((float)(IslandSurfaceLayout.DefaultHostWidthDip / 2.0), 0f, 0f);

            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.Duration = duration;
            scaleAnim.InsertKeyFrame(0f, new Vector3(0.6f, 0.4f, 1f));
            scaleAnim.InsertKeyFrame(0.7f, new Vector3(1.012f, 1.012f, 1f), easing);
            scaleAnim.InsertKeyFrame(1f, Vector3.One, easing);

            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.Duration = ScaledDuration(160);
            opacityAnim.InsertKeyFrame(0f, 0f);
            opacityAnim.InsertKeyFrame(1f, 1f, easing);

            var translateAnim = compositor.CreateVector3KeyFrameAnimation();
            translateAnim.Duration = duration;
            translateAnim.InsertKeyFrame(0f, new Vector3(0f, -(float)IslandSurfaceLayout.SurfaceTopInsetDip, 0f));
            translateAnim.InsertKeyFrame(1f, Vector3.Zero, easing);

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            _pillMaskVisual.StartAnimation("Scale", scaleAnim);
            _pillMaskVisual.StartAnimation("Opacity", opacityAnim);
            _pillMaskVisual.StartAnimation("Translation", translateAnim);
            batch.End();
            batch.Completed += (_, _) =>
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_windowClosed)
                    {
                        FinalizeUnhideSurface(gen);
                    }
                });
            };

            // Fallback timer ensures surface never stays hidden if DWM drops a composition batch on mode switch.
            if (_unhideRecoveryTimer is null)
            {
                _unhideRecoveryTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
                _unhideRecoveryTimer.IsRepeating = false;
                _unhideRecoveryTimer.Tick += (_, _) =>
                {
                    if (!_windowClosed)
                    {
                        FinalizeUnhideSurface(_hideShowGeneration);
                    }
                };
            }
            _unhideRecoveryTimer.Interval = duration + TimeSpan.FromMilliseconds(70);
            _unhideRecoveryTimer.Stop();
            _unhideRecoveryTimer.Start();
        }
        catch
        {
            FinalizeUnhideSurface(gen);
        }
    }

    private void FinalizeUnhideSurface(long generation)
    {
        if (_windowClosed || _isIslandHidden || generation != _hideShowGeneration)
        {
            return;
        }

        _unhideRecoveryTimer?.Stop();
        _pillMaskVisual.StopAnimation("Scale");
        _pillMaskVisual.StopAnimation("Opacity");
        _pillMaskVisual.StopAnimation("Translation");
        _pillMaskVisual.Opacity = 1f;
        _pillMaskVisual.Scale = Vector3.One;
        PillMaskHost.Translation = Vector3.Zero;
        ApplySurfaceShape(_currentCornerRadius, _currentPillWidth, _currentPillHeight);
        _lastWindowRegion = null;
        UpdateWindowRegion(_currentPillWidth, _currentPillHeight, _currentCornerRadius, forceRedraw: true);

        if (_glassEnabled)
        {
            _ = RefreshFrostAsync(force: true);
        }
    }

    private void StopAmbientAnimations()
    {
        StopEqualizerWave();
        if (_batteryChargeAnimating)
        {
            _batteryChargeAnimating = false;
            try
            {
                _batteryChargeHaloVisual.StopAnimation("Opacity");
                _batteryChargeHaloVisual.StopAnimation("Scale");
            }
            catch { }
        }
    }

    private void RestoreAmbientAnimations()
    {
        if (_viewModel.IsPlaying && _viewModel.ShowEqualizer)
        {
            StartEqualizerWave();
        }
        UpdateBatteryChargingAnimation();
    }

    public void ToggleHideIsland()
    {
        if (_isIslandHidden)
        {
            ShowIsland();
        }
        else
        {
            if (_fullscreenActive)
            {
                _userOverrodeFullscreenHide = true;
            }
            HideIslandAnimated();
        }
    }

    /// <summary>
    /// Polls the shell notification state. QUNS_BUSY, D3D fullscreen and
    /// presentation mode are the states Windows uses for fullscreen owners.
    /// </summary>
    /// <summary>
    /// Detects borderless/windowed fullscreen (most modern games): the foreground window
    /// covers the whole monitor on the island's monitor, excluding cloaked UWP hosts,
    /// transparent/no-activate overlays (NVIDIA, Game Bar, Vanguard), and desktop shells.
    /// </summary>
    private bool IsBorderlessFullscreenForeground()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == _hwnd) return false;

        var isVisible = IsWindowVisible(foreground);
        var isMinimized = IsIconic(foreground);
        var isMaximized = IsZoomed(foreground);

        var cloaked = 0;
        var cloakedResult = DwmGetWindowAttribute(foreground, DwmwaCloaked, ref cloaked, sizeof(int));
        var isCloaked = cloakedResult == 0 && cloaked != 0;

        var exStyle = GetWindowLongPtr(foreground, GwlExStyle).ToInt64();

        string? className = null;
        var length = GetClassName(foreground, _classNameBuffer, _classNameBuffer.Length);
        if (length > 0)
        {
            className = new string(_classNameBuffer, 0, length);
        }

        if (!GetWindowRect(foreground, out var windowRect)) return false;

        var fgMonitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        if (fgMonitor == IntPtr.Zero) return false;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(fgMonitor, ref info)) return false;

        var islandMonitor = MonitorFromWindow(_hwnd, MonitorDefaultToNearest);
        var isSameMonitor = islandMonitor == IntPtr.Zero || fgMonitor == islandMonitor;

        var candidate = new FullscreenWindowCandidate(
            Handle: foreground,
            OwnHandle: _hwnd,
            ClassName: className,
            IsVisible: isVisible,
            IsMinimized: isMinimized,
            IsMaximized: isMaximized,
            IsCloaked: isCloaked,
            ExStyle: exStyle,
            Left: windowRect.Left,
            Top: windowRect.Top,
            Right: windowRect.Right,
            Bottom: windowRect.Bottom,
            MonitorLeft: info.Monitor.Left,
            MonitorTop: info.Monitor.Top,
            MonitorRight: info.Monitor.Right,
            MonitorBottom: info.Monitor.Bottom,
            IsSameMonitorAsIsland: isSameMonitor);

        return FullscreenWindowEvaluator.IsBorderlessFullscreen(candidate);
    }

    private bool IsForegroundMonitorSameAsIsland()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || _hwnd == IntPtr.Zero)
        {
            return true;
        }

        var islandMonitor = MonitorFromWindow(_hwnd, MonitorDefaultToNearest);
        if (islandMonitor == IntPtr.Zero)
        {
            return true;
        }

        var fgMonitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        return fgMonitor == IntPtr.Zero || fgMonitor == islandMonitor;
    }

    private void UpdateBackgroundPollTimerState()
    {
        if (_fullscreenPollTimer is null || _windowClosed)
        {
            return;
        }

        var needsPoll = !_sessionLocked &&
                        (_viewModel.AutoHideOnFullscreen ||
                         string.Equals(_viewModel.PreferredMonitorDeviceName, DisplayMonitorHelper.FollowCursorDeviceToken, StringComparison.OrdinalIgnoreCase));
        if (needsPoll)
        {
            if (!_fullscreenPollTimer.IsRunning)
            {
                _fullscreenPollTimer.Start();
            }
        }
        else
        {
            _fullscreenPollTimer.Stop();
        }
    }

    private void ApplyHotkeyRegistration(bool enable)
    {
        if (_hwnd == IntPtr.Zero || _windowClosed)
        {
            HotkeyConflictWarning = string.Empty;
            return;
        }

        _ = UnregisterHotKey(_hwnd, HotkeyToggleIsland);
        _ = UnregisterHotKey(_hwnd, HotkeyToggleFocus);

        if (!enable)
        {
            HotkeyConflictWarning = string.Empty;
            return;
        }

        var conflicts = new List<string>(2);
        if (!RegisterHotKey(_hwnd, HotkeyToggleIsland, ModWin | ModAlt, VkI))
        {
            conflicts.Add("Win+Alt+I");
        }
        if (!RegisterHotKey(_hwnd, HotkeyToggleFocus, ModWin | ModShift, VkF))
        {
            conflicts.Add("Win+Shift+F");
        }

        HotkeyConflictWarning = conflicts.Count > 0
            ? $"{string.Join(" ve ", conflicts)} kısayolu başka bir uygulama tarafından kullanılıyor."
            : string.Empty;
    }

    private void PollFullscreenState()
    {
        if (_windowClosed || _sessionLocked)
        {
            return;
        }

        UpdateBackgroundPollTimerState();

        if (!_viewModel.AutoHideOnFullscreen)
        {
            _fullscreenTracker.Reset();
            _fullscreenActive = false;
            if (_autoHiddenForFullscreen)
            {
                _autoHiddenForFullscreen = false;
                if (_isIslandHidden)
                {
                    ShowIsland();
                }
                ScheduleDisplaySettle();
                _ = _viewModel.RefreshExternalStatesAsync();
            }
        }
        else
        {
            var rawIsFullscreen = false;
            try
            {
                if (SHQueryUserNotificationState(out var state) == 0 && FullscreenNotificationState.IsFullscreen(state))
                {
                    rawIsFullscreen = IsForegroundMonitorSameAsIsland();
                }

                // Exclusive fullscreen is not the only case: borderless/windowed fullscreen
                // games report QUNS_ACCEPTS_NOTIFICATIONS, so check the foreground window too.
                rawIsFullscreen = rawIsFullscreen || IsBorderlessFullscreenForeground();
            }
            catch
            {
                // Shell API is best-effort; a failure must never break the island.
            }

            var isFullscreen = _fullscreenTracker.Update(rawIsFullscreen);
            var wasFullscreen = _fullscreenActive;
            _fullscreenActive = isFullscreen;

            if (wasFullscreen && !isFullscreen)
            {
                _userOverrodeFullscreenHide = false;
            }

            if (isFullscreen && !_isIslandHidden && !_userOverrodeFullscreenHide)
            {
                _autoHiddenForFullscreen = true;
                HideIslandAnimated(showAnchor: false);
                return;
            }
            else if (!isFullscreen && (_autoHiddenForFullscreen || (wasFullscreen && _isIslandHidden)))
            {
                _autoHiddenForFullscreen = false;
                if (_isIslandHidden)
                {
                    ShowIsland();
                }
                ScheduleDisplaySettle();
                _ = _viewModel.RefreshExternalStatesAsync();
            }
        }

        if (string.Equals(_viewModel.PreferredMonitorDeviceName, DisplayMonitorHelper.FollowCursorDeviceToken, StringComparison.OrdinalIgnoreCase) &&
            !_isIslandHidden &&
            !_isPointerOverSurface &&
            !_viewModel.IsExpanded &&
            !_viewModel.IsCollapsing &&
            DisplayMonitorHelper.TryGetCursorMonitorHandle(out var cursorMonitor) &&
            cursorMonitor != _lastMonitorHandle)
        {
            PositionHostWindow();
            _lastWindowRegion = null;
            UpdateWindowRegion(_currentPillWidth, _currentPillHeight, _currentCornerRadius > 0 ? _currentCornerRadius : 20.0, forceRedraw: true);
            if (_glassEnabled)
            {
                ScheduleFrostRefresh(delayMs: 180);
            }
        }
    }

    private void ScheduleDisplaySettle()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        if (_displaySettleTimer is null)
        {
            _displaySettleTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _displaySettleTimer.IsRepeating = false;
            _displaySettleTimer.Tick += (_, _) =>
            {
                _displaySettleTimer?.Stop();
                if (_isIslandHidden && _autoHiddenForFullscreen)
                {
                    return;
                }

                PositionHostWindow();
                ApplyWindowStyles();
                _lastWindowRegion = null;
                UpdateWindowRegion(_currentPillWidth, _currentPillHeight, _currentCornerRadius > 0 ? _currentCornerRadius : 20.0, forceRedraw: true);
                ReassertTopmost();

                if (_displaySettlePassesRemaining > 1)
                {
                    _displaySettlePassesRemaining--;
                    if (_displaySettleTimer is not null)
                    {
                        _displaySettleTimer.Interval = TimeSpan.FromMilliseconds(600);
                        _displaySettleTimer.Start();
                    }
                }
                else
                {
                    _displaySettlePassesRemaining = 0;
                    if (_glassEnabled && !_isIslandHidden)
                    {
                        _ = RefreshFrostAsync(force: true);
                    }
                }
            };
        }

        _displaySettlePassesRemaining = 2;
        _displaySettleTimer.Stop();
        _displaySettleTimer.Interval = TimeSpan.FromMilliseconds(350);
        _displaySettleTimer.Start();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _windowClosed = true;
        _topmostGuardTimer?.Stop();
        _hideAnimationTimer?.Stop();
        _unhideRecoveryTimer?.Stop();
        _displaySettleTimer?.Stop();
        _frostRefreshTimer?.Stop();
        _collapseDelayTimer.Stop();
        _fullscreenPollTimer?.Stop();
        _hoverExpandTimer?.Stop();
        _longPressTimer?.Stop();
        _isDimensionAnimating = false;
        _isPageAnimating = false;
        _isDotAnimating = false;
        StopRenderLoop();
        WindowRoot.ActualThemeChanged -= OnWindowRootActualThemeChanged;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            SystemUiSettings.AnimationsEnabledChanged -= OnSystemAnimationsEnabledChanged;
        }
        if (ExpandedPanelsHost is not null)
        {
            ExpandedPanelsHost.SizeChanged -= OnExpandedPanelsHostSizeChanged;
        }
        if (SystemVolumeSlider is not null)
        {
            if (_sliderPressedHandler is not null)
            {
                SystemVolumeSlider.RemoveHandler(UIElement.PointerPressedEvent, _sliderPressedHandler);
            }
            if (_sliderReleasedHandler is not null)
            {
                SystemVolumeSlider.RemoveHandler(UIElement.PointerReleasedEvent, _sliderReleasedHandler);
                SystemVolumeSlider.RemoveHandler(UIElement.PointerCaptureLostEvent, _sliderReleasedHandler);
            }
        }
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        if (_hwnd != IntPtr.Zero)
        {
            if (_sessionNotificationRegistered)
            {
                _ = WTSUnRegisterSessionNotification(_hwnd);
                _sessionNotificationRegistered = false;
            }

            _ = UnregisterHotKey(_hwnd, HotkeyToggleIsland);
            _ = UnregisterHotKey(_hwnd, HotkeyToggleFocus);
            _ = RemoveWindowSubclass(_hwnd, _subclassProc, UIntPtr.Zero);
        }
    }

    private void PositionHostWindow(double? explicitDpiScale = null)
    {
        ApplyHostWindowSize(_currentPillWidth, _currentPillHeight, explicitDpiScale);
        UpdateWindowRegion(_currentPillWidth, _currentPillHeight, _currentCornerRadius > 0 ? _currentCornerRadius : 20.0);
    }

    private double _hostPillWidth = 216;
    private double _hostPillHeight = 40;

    /// <summary>
    /// Fixed transparent host footprint (560x306 DIP): includes a 3 DIP perimeter antialiasing
    /// margin around the 560x300 pill surface so GDI SetWindowRgn never slices DirectComposition's
    /// curved subpixel edges or top specular rim.
    /// </summary>
    private void ApplyHostWindowSize(double pillWidth, double pillHeight, double? explicitDpiScale = null)
    {
        if (_hwnd == IntPtr.Zero) return;

        int workLeft;
        int workTop;
        int workRight;
        int workBottom;

        if (DisplayMonitorHelper.TryResolveTargetMonitor(_viewModel.PreferredMonitorDeviceName, _hwnd, out var target))
        {
            _lastMonitorHandle = target.Handle;
            _dpiScale = explicitDpiScale is > 0.0 and var forcedDpi ? forcedDpi : target.DpiScale;
            workLeft = target.WorkLeft;
            workTop = target.WorkTop;
            workRight = target.WorkRight;
            workBottom = target.WorkBottom;
        }
        else
        {
            IntPtr monitor = !_initialPositionApplied
                ? MonitorFromWindow(_hwnd, MonitorDefaultToPrimary)
                : MonitorFromWindow(_hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
                monitor = MonitorFromPoint(default, MonitorDefaultToPrimary);

            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;

            _lastMonitorHandle = monitor;
            if (explicitDpiScale is > 0.0 and var forcedDpi)
            {
                _dpiScale = forcedDpi;
            }
            else if (GetDpiForMonitor(monitor, MdtEffectiveDpi, out uint monitorDpiX, out _) == 0 && monitorDpiX > 0)
            {
                _dpiScale = monitorDpiX / 96.0;
            }
            else
            {
                _dpiScale = Math.Max(1u, GetDpiForWindow(_hwnd)) / 96.0;
            }

            workLeft = info.WorkArea.Left;
            workTop = info.WorkArea.Top;
            workRight = info.WorkArea.Right;
            workBottom = info.WorkArea.Bottom;
        }

        _initialPositionApplied = true;
        _hostPillWidth = Math.Max(120.0, pillWidth);
        _hostPillHeight = Math.Max(36.0, pillHeight);

        var effectiveTopOffset = IslandSurfaceLayout.ComputeEffectiveTopOffsetDip(_viewModel.TopOffset);
        var hostBounds = IslandSurfaceLayout.ComputeHostWindowBounds(
            workLeft,
            workTop,
            workRight,
            workBottom,
            _dpiScale,
            effectiveTopOffset,
            _viewModel.HorizontalOffset);

        // Startup / DPI / display / settings changes only; transitions never resize the native window.
        // Always invoke SetWindowPos directly on the UI owner thread so DWM mode-switches never race.
        _ = SetWindowPos(_hwnd, HwndTopmost, hostBounds.X, hostBounds.Y, hostBounds.Width, hostBounds.Height, SwpNoActivate);

        // Window moves under the cursor can synthesize pointer exit/enter pairs in WinUI;
        // suppress them briefly so hover state cannot oscillate with the resize itself.
        _hostResizeGuardTicks = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    private static IntPtr CreatePaddedRoundRectRgn(IslandSurfaceLayout.PixelRgnRect r)
        => CreateRoundRectRgn(r.Left, r.Top, r.Right, r.Bottom, r.CornerDiameter, r.CornerDiameter);

    private void ApplyWindowRegionHandle(IntPtr regionHandle, bool forceRedraw)
    {
        if (regionHandle == IntPtr.Zero)
        {
            return;
        }

        if (_hwnd == IntPtr.Zero || _windowClosed || SetWindowRgn(_hwnd, regionHandle, forceRedraw) == 0)
        {
            _ = DeleteObject(regionHandle);
        }
    }

    /// <summary>
    /// Crops the native window around the current pill with an outward-padded Win32 region.
    /// Areas outside the region remain click-through, while DirectComposition owns the visible
    /// rounded edge with full hardware subpixel antialiasing inside the 3 DIP region margin.
    /// </summary>
    private void UpdateWindowRegion(double pillWidth, double pillHeight, double cornerRadius, bool includeOvershootPadding = false, bool forceRedraw = false)
    {
        if (_hwnd == IntPtr.Zero || _windowClosed) return;

        var dpi = _dpiScale > 0 ? _dpiScale : 1.0;
        int hostPixelW = Math.Max(1, (int)Math.Round(IslandSurfaceLayout.DefaultHostWidthDip * dpi));
        int hostPixelH = Math.Max(1, (int)Math.Round(IslandSurfaceLayout.DefaultHostWindowHeightDip * dpi));

        if (_isIslandHidden)
        {
            if (!_hideAnchorVisible)
            {
                _lastWindowRegion = null;
                ApplyWindowRegionHandle(CreateRectRgn(0, 0, 0, 0), false);
                return;
            }

            var anchorBounds = IslandSurfaceLayout.ComputeCenteredPillBounds(
                IslandSurfaceLayout.DefaultHostWidthDip,
                IslandSurfaceLayout.HiddenAnchorWidthDip,
                IslandSurfaceLayout.HiddenAnchorHeightDip);
            var anchorRgnRect = IslandSurfaceLayout.ComputePaddedPixelRegion(
                anchorBounds,
                IslandSurfaceLayout.SurfaceTopInsetDip,
                cornerRadiusDip: 6.0,
                IslandSurfaceLayout.RegionAntialiasMarginDip,
                dpi,
                hostPixelW,
                hostPixelH);

            var anchorKey = (anchorRgnRect.Left, anchorRgnRect.Top, anchorRgnRect.Right, anchorRgnRect.Bottom, anchorRgnRect.CornerDiameter, 0, 0, 0, 0, 0);
            if (_lastWindowRegion == anchorKey) return;
            _lastWindowRegion = anchorKey;

            ApplyWindowRegionHandle(CreatePaddedRoundRectRgn(anchorRgnRect), forceRedraw);
            return;
        }

        double effectiveWidth = includeOvershootPadding
            ? IslandSurfaceLayout.ComputeExpansionRegionWidth(pillWidth, pillWidth)
            : pillWidth;
        double effectiveHeight = includeOvershootPadding
            ? IslandSurfaceLayout.ComputeExpansionRegionHeight(pillHeight, pillHeight)
            : pillHeight;

        var isSplit = _viewModel.IsSplitModeActive && !_viewModel.IsExpanded && !_viewModel.IsCollapsing;
        bool isDetachedActive = DetachedPill is not null && (DetachedPill.Visibility == Visibility.Visible || _currentDetachedOpacity > 0.01);
        double detachedW = (DetachedPill is not null && DetachedPill.Width > 0)
            ? DetachedPill.Width
            : IslandSurfaceLayout.DetachedPillWidthDip;
        double detachedH = (DetachedPill is not null && DetachedPill.Height > 0)
            ? DetachedPill.Height
            : IslandSurfaceLayout.DetachedPillHeightDip;

        var metrics = IslandSurfaceLayout.ComputeCompactMetrics(_viewModel.IslandScale);
        double splitGap = metrics.SplitGap;

        if (_isDimensionAnimating && (isSplit || isDetachedActive || _animStartDetachedActive || _animTargetDetachedActive))
        {
            var envelopeBounds = IslandSurfaceLayout.ComputeTransitionEnvelopeBounds(
                IslandSurfaceLayout.DefaultHostWidthDip,
                IslandSurfaceLayout.DefaultHostHeightDip,
                _animStartWidth > 0 ? _animStartWidth : effectiveWidth,
                _animStartHeight > 0 ? _animStartHeight : effectiveHeight,
                _animStartLeadingX,
                _animTargetWidth > 0 ? _animTargetWidth : effectiveWidth,
                _animTargetHeight > 0 ? _animTargetHeight : effectiveHeight,
                _animTargetLeadingX,
                _animStartDetachedActive,
                _animStartTrailingX,
                _animTargetDetachedActive,
                _animTargetTrailingX,
                detachedW,
                detachedH,
                includeOvershootPadding);

            var spanRgnRect = IslandSurfaceLayout.ComputePaddedPixelRegion(
                envelopeBounds,
                IslandSurfaceLayout.SurfaceTopInsetDip,
                cornerRadius,
                IslandSurfaceLayout.RegionAntialiasMarginDip,
                dpi,
                hostPixelW,
                hostPixelH);

            var spanKey = (spanRgnRect.Left, spanRgnRect.Top, spanRgnRect.Right, spanRgnRect.Bottom, spanRgnRect.CornerDiameter, 0, 0, 0, 0, 0);
            if (_lastWindowRegion == spanKey) return;
            _lastWindowRegion = spanKey;

            ApplyWindowRegionHandle(CreatePaddedRoundRectRgn(spanRgnRect), forceRedraw);
            return;
        }

        if (isSplit)
        {
            double leadingW = Math.Max(1.0, pillWidth);
            double leadingH = Math.Max(1.0, pillHeight);
            var (leadOffX, trailOffX) = IslandSurfaceLayout.ComputeSplitOffsets(leadingW, detachedW, splitGap);
            var leadBounds = IslandSurfaceLayout.ComputeCenteredPillBounds(IslandSurfaceLayout.DefaultHostWidthDip, leadingW, leadingH, leadOffX);
            var detachBounds = IslandSurfaceLayout.ComputeCenteredPillBounds(IslandSurfaceLayout.DefaultHostWidthDip, detachedW, detachedH, trailOffX);

            var leadRgnRect = IslandSurfaceLayout.ComputePaddedPixelRegion(
                leadBounds,
                IslandSurfaceLayout.SurfaceTopInsetDip,
                cornerRadius,
                IslandSurfaceLayout.RegionAntialiasMarginDip,
                dpi,
                hostPixelW,
                hostPixelH);
            var detachRgnRect = IslandSurfaceLayout.ComputePaddedPixelRegion(
                detachBounds,
                IslandSurfaceLayout.SurfaceTopInsetDip,
                detachedH * 0.5,
                IslandSurfaceLayout.RegionAntialiasMarginDip,
                dpi,
                hostPixelW,
                hostPixelH);

            var splitKey = (
                leadRgnRect.Left, leadRgnRect.Top, leadRgnRect.Right, leadRgnRect.Bottom, leadRgnRect.CornerDiameter,
                detachRgnRect.Left, detachRgnRect.Top, detachRgnRect.Right, detachRgnRect.Bottom, detachRgnRect.CornerDiameter);
            if (_lastWindowRegion == splitKey) return;
            _lastWindowRegion = splitKey;

            var rgn = CreatePaddedRoundRectRgn(leadRgnRect);
            var detachedRgn = CreatePaddedRoundRectRgn(detachRgnRect);

            if (rgn != IntPtr.Zero && detachedRgn != IntPtr.Zero)
            {
                var combinedRgn = CreateRectRgn(0, 0, 0, 0);
                if (combinedRgn != IntPtr.Zero)
                {
                    _ = CombineRgn(combinedRgn, rgn, detachedRgn, RGN_OR);
                    _ = DeleteObject(rgn);
                    _ = DeleteObject(detachedRgn);
                    rgn = combinedRgn;
                }
                else
                {
                    _ = DeleteObject(detachedRgn);
                }
            }
            else if (detachedRgn != IntPtr.Zero)
            {
                rgn = detachedRgn;
            }

            ApplyWindowRegionHandle(rgn, forceRedraw);
            return;
        }

        double surfaceOffset = _isDimensionAnimating ? _currentLeadingX : 0.0;
        var normalBounds = IslandSurfaceLayout.ComputeCenteredPillBounds(
            IslandSurfaceLayout.DefaultHostWidthDip,
            effectiveWidth,
            effectiveHeight,
            surfaceOffset);
        var normalRgnRect = IslandSurfaceLayout.ComputePaddedPixelRegion(
            normalBounds,
            IslandSurfaceLayout.SurfaceTopInsetDip,
            cornerRadius,
            IslandSurfaceLayout.RegionAntialiasMarginDip,
            dpi,
            hostPixelW,
            hostPixelH);

        var normalKey = (normalRgnRect.Left, normalRgnRect.Top, normalRgnRect.Right, normalRgnRect.Bottom, normalRgnRect.CornerDiameter, 0, 0, 0, 0, 0);
        if (_lastWindowRegion == normalKey) return;
        _lastWindowRegion = normalKey;

        ApplyWindowRegionHandle(CreatePaddedRoundRectRgn(normalRgnRect), forceRedraw);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IslandViewModel.BatteryIsCharging))
        {
            if (!_isIslandHidden || _hideAnchorVisible)
            {
                UpdateBatteryChargingAnimation();
            }
        }
        else if (e.PropertyName == nameof(MediaViewModel.CurrentIslandState))
        {
            UpdateStateLayout(_viewModel.CurrentIslandState, animated: !_isIslandHidden);
        }
        else if (e.PropertyName is nameof(MediaViewModel.CurrentTrack) or nameof(MediaViewModel.IsPlaying))
        {
            if (_viewModel.IsPlaying && _viewModel.ShowEqualizer && (!_isIslandHidden || _hideAnchorVisible))
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
                !_viewModel.IsExpanded &&
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
            // Theme/accent changes only recolor the glass; the captured blur is
            // desktop-only and stays valid, so never re-capture here (no blink).
            ApplyTheme(_viewModel.Theme);
        }
        else if (e.PropertyName == nameof(MediaViewModel.UseTransparency))
        {
            ApplyGlass();
            ApplyTheme(_viewModel.Theme);
        }
        else if (e.PropertyName is nameof(MediaViewModel.TransparencyLevel) or nameof(MediaViewModel.ArtworkTintStrength))
        {
            // Live slider feedback: recolor the tint overlay, no frost re-capture.
            ApplyTheme(_viewModel.Theme);
        }
        else if (e.PropertyName is nameof(MediaViewModel.TopOffset)
                 or nameof(MediaViewModel.HorizontalOffset)
                 or nameof(MediaViewModel.PreferredMonitorDeviceName)
                 or nameof(MediaViewModel.IslandScale))
        {
            if (e.PropertyName == nameof(MediaViewModel.IslandScale))
            {
                ApplyCompactScale(_viewModel.IslandScale);
                UpdateStateLayout(_viewModel.CurrentIslandState, animated: false);
                PositionHostWindow();
            }
            else
            {
                if (e.PropertyName == nameof(MediaViewModel.PreferredMonitorDeviceName))
                {
                    _lastWindowRegion = null;
                    UpdateBackgroundPollTimerState();
                }

                PositionHostWindow();
                UpdateStateLayout(_viewModel.CurrentIslandState, animated: false);
            }

            if (_glassEnabled && !_isIslandHidden)
            {
                ScheduleFrostRefresh(delayMs: 240);
            }
        }
        else if (e.PropertyName is nameof(IslandViewModel.IdleWeatherIconKind) or nameof(IslandViewModel.IdleWeatherVisibility))
        {
            UpdateIdleWeatherIcon(_viewModel.IdleWeatherIconKind);
        }
        else if (e.PropertyName is nameof(MediaViewModel.EnableHoverExpand) or nameof(MediaViewModel.HoverAction))
        {
            if (!_viewModel.EnableHoverExpand || string.Equals(_viewModel.HoverAction, "ExpandCard", StringComparison.OrdinalIgnoreCase))
            {
                _hoverExpandTimer?.Stop();
                if (_viewModel.CurrentIslandState == IslandState.Hover)
                {
                    _viewModel.OnPointerExited();
                }
            }
        }
        else if (e.PropertyName == nameof(MediaViewModel.HoverExpandDelayMs))
        {
            if (_hoverExpandTimer is not null)
            {
                _hoverExpandTimer.Interval = TimeSpan.FromMilliseconds(
                    Math.Clamp(_viewModel.HoverExpandDelayMs, 0, 1000));
            }
        }
        else if (e.PropertyName == nameof(MediaViewModel.EnableGlobalHotkeys))
        {
            ApplyHotkeyRegistration(_viewModel.EnableGlobalHotkeys);
        }
        else if (e.PropertyName == nameof(MediaViewModel.AutoHideOnFullscreen))
        {
            UpdateBackgroundPollTimerState();
            PollFullscreenState();
        }
        else if (e.PropertyName == nameof(MediaViewModel.ShowEqualizer))
        {
            if (CompactWaveBars is not null)
            {
                bool showBars = _viewModel.CurrentIslandState == IslandState.Hover && _viewModel.ShowEqualizer;
                CompactWaveBars.Visibility = showBars ? Visibility.Visible : Visibility.Collapsed;
                CompactWaveBars.Opacity = showBars ? 1.0 : 0.0;
            }
            if (_viewModel.ShowEqualizer && _viewModel.IsPlaying && (!_isIslandHidden || _hideAnchorVisible))
            {
                StartEqualizerWave();
            }
            else
            {
                StopEqualizerWave();
            }
        }
        else if (e.PropertyName == nameof(IslandViewModel.IsSplitModeActive))
        {
            var isSplit = _viewModel.IsSplitModeActive;
            if (_lastSplitModeActive != isSplit)
            {
                _lastSplitModeActive = isSplit;
                UpdateStateLayout(_viewModel.CurrentIslandState, animated: true);
            }
        }
        else if (e.PropertyName == nameof(IslandViewModel.IsFocusTimerActive))
        {
            var isFocus = _viewModel.IsFocusTimerActive;
            if (_lastFocusTimerActive != isFocus)
            {
                _lastFocusTimerActive = isFocus;
                if (!_viewModel.IsSplitModeActive)
                {
                    UpdateStateLayout(_viewModel.CurrentIslandState, animated: true);
                }
            }
        }
        else if (e.PropertyName == nameof(IslandViewModel.IsNotificationActive))
        {
            var isNotif = _viewModel.IsNotificationActive;
            if (_lastNotificationActive != isNotif)
            {
                _lastNotificationActive = isNotif;
                if (isNotif && _viewModel.IsExpanded)
                {
                    _viewModel.Collapse();
                }
                UpdateStateLayout(_viewModel.CurrentIslandState, animated: true);
            }
        }
        else if (e.PropertyName == nameof(IslandViewModel.IsFileOperationActive))
        {
            var isFile = _viewModel.IsFileOperationActive;
            if (_lastFileOpActive != isFile)
            {
                _lastFileOpActive = isFile;
                UpdateStateLayout(_viewModel.CurrentIslandState, animated: true);
            }
        }
        else if (e.PropertyName is nameof(IslandViewModel.FocusProgress) or nameof(IslandViewModel.PreserveFocusPanel))
        {
            UpdateFocusProgressFill();
        }
    }

    private void ApplyCompactScale(double scale)
    {
        var m = IslandSurfaceLayout.ComputeCompactMetrics(scale);
        if (Math.Abs(_appliedCompactScale - m.Scale) < 0.001)
        {
            return;
        }

        _appliedCompactScale = m.Scale;

        CompactView.Padding = new Thickness(m.CompactHorizontalPadding, 0, m.CompactHorizontalPadding, 0);

        IdleContent.Margin = new Thickness(m.IdleInnerMarginH, 0, m.IdleInnerMarginH, 0);
        IdleSmartPanel.Spacing = m.IdleItemSpacing;
        IdleClockStack.MinWidth = m.IdleBlockMinWidth;
        IdleClockBlock.FontSize = m.IdleClockFontSize;
        IdleClockBlock.LineHeight = m.IdleClockLineHeight;
        IdleDateBlock.FontSize = m.IdleDateFontSize;
        IdleDateBlock.LineHeight = m.IdleDateLineHeight;
        IdleDivider.Width = m.IdleDividerWidth;
        IdleDivider.Height = m.IdleDividerHeight;
        IdleWeatherStack.MinWidth = m.IdleBlockMinWidth;
        IdleWeatherHeader.Height = m.IdleWeatherRowHeight;
        IdleWeatherHeader.Spacing = m.IdleWeatherHeaderSpacing;
        IdleWeatherIconBox.Width = m.IdleWeatherIconSize;
        IdleWeatherIconBox.Height = m.IdleWeatherIconSize;
        IdleWeatherTempBlock.FontSize = m.IdleWeatherTempFontSize;
        IdleWeatherTempBlock.LineHeight = m.IdleWeatherTempLineHeight;
        IdleWeatherCityBlock.FontSize = m.IdleWeatherCityFontSize;
        IdleWeatherCityBlock.LineHeight = m.IdleWeatherCityLineHeight;
        IdleWeatherCityBlock.MaxWidth = m.IdleWeatherCityMaxWidth;
        IdleBrandPanel.Spacing = m.IdleBrandSpacing;
        IdleBrandIconBorder.Width = m.IdleBrandIconSize;
        IdleBrandIconBorder.Height = m.IdleBrandIconSize;
        IdleBrandIconBorder.CornerRadius = new CornerRadius(m.IdleBrandIconCornerRadius);
        IdleBrandIconImage.Width = m.IdleBrandIconSize;
        IdleBrandIconImage.Height = m.IdleBrandIconSize;
        IdleBrandText.FontSize = m.IdleBrandFontSize;

        NotificationGlyphIcon.FontSize = m.EventGlyphSize;
        NotificationGlyphIcon.Margin = new Thickness(m.EventGlyphMarginLeft, 0, m.EventGlyphMarginRight, 0);
        NotificationTitleBlock.FontSize = m.EventTitleFontSize;
        NotificationSubtitleBlock.FontSize = m.EventSubtitleFontSize;
        NotificationDismissButton.Width = m.EventDismissButtonSize;
        NotificationDismissButton.Height = m.EventDismissButtonSize;
        NotificationDismissButton.CornerRadius = new CornerRadius(m.EventDismissButtonRadius);
        NotificationDismissButton.Margin = new Thickness(m.EventDismissButtonMarginLeft, 0, 0, 0);
        NotificationDismissGlyph.FontSize = m.EventDismissGlyphSize;

        FocusCompactGlyphIcon.FontSize = m.EventGlyphSize;
        FocusCompactGlyphIcon.Margin = new Thickness(m.EventGlyphMarginLeft, 0, m.EventGlyphMarginRight, 0);
        FocusCompactTimerBlock.FontSize = m.EventTitleFontSize;
        FocusCompactModeBlock.FontSize = m.EventSubtitleFontSize;
        FocusCompactToggleButton.Width = m.CompactActionButtonSize;
        FocusCompactToggleButton.Height = m.CompactActionButtonSize;
        FocusCompactToggleButton.CornerRadius = new CornerRadius(m.CompactActionButtonRadius);
        FocusCompactToggleButton.Margin = new Thickness(m.EventDismissButtonMarginLeft, 0, 0, 0);
        FocusCompactToggleGlyph.FontSize = m.FocusToggleGlyphSize;

        FileOpCompactGlyphIcon.FontSize = m.EventGlyphSize;
        FileOpCompactGlyphIcon.Margin = new Thickness(m.EventGlyphMarginLeft, 0, m.EventGlyphMarginRight, 0);
        FileOpCompactNameBlock.FontSize = m.FileOpNameFontSize;
        FileOpCompactProgressBar.Height = m.FileOpProgressHeight;
        FileOpCompactProgressBar.CornerRadius = new CornerRadius(m.FileOpProgressRadius);
        FileOpCompactStatusBlock.FontSize = m.FileOpStatusFontSize;
        FileOpCompactStatusBlock.Margin = new Thickness(m.FileOpStatusMarginLeft, 0, 0, 0);

        CompactArtBorder.Width = m.CompactArtSize;
        CompactArtBorder.Height = m.CompactArtSize;
        CompactArtBorder.CornerRadius = new CornerRadius(m.CompactArtCornerRadius);
        CompactArtBorder.Margin = new Thickness(0, 0, m.CompactArtMarginRight, 0);
        CompactArtInnerGrid.CornerRadius = new CornerRadius(m.CompactArtCornerRadius);
        CompactArtFallbackGlyph.FontSize = m.CompactArtFallbackGlyphSize;
        CompactTrackTitleBlock.FontSize = m.CompactTrackTitleFontSize;
        CompactTrackArtistBlock.FontSize = m.CompactTrackArtistFontSize;
        CompactTrackRightStack.Spacing = m.CompactTrackRightSpacing;
        CompactTrackRightStack.Margin = new Thickness(m.CompactTrackRightMarginLeft, 0, 0, 0);
        CompactWaveBars.Spacing = m.CompactWaveSpacing;

        var barRadius = new CornerRadius(m.CompactWaveBarCornerRadius);
        WaveBar1.Width = m.CompactWaveBarWidth;
        WaveBar1.Height = m.CompactWaveBarHeight;
        WaveBar1.CornerRadius = barRadius;
        WaveBar2.Width = m.CompactWaveBarWidth;
        WaveBar2.Height = m.CompactWaveBarHeight;
        WaveBar2.CornerRadius = barRadius;
        WaveBar3.Width = m.CompactWaveBarWidth;
        WaveBar3.Height = m.CompactWaveBarHeight;
        WaveBar3.CornerRadius = barRadius;

        if (_waveAnimating)
        {
            StopEqualizerWave();
            StartEqualizerWave();
        }

        CompactPlayPauseButton.Width = m.CompactActionButtonSize;
        CompactPlayPauseButton.Height = m.CompactActionButtonSize;
        CompactPlayPauseButton.CornerRadius = new CornerRadius(m.CompactActionButtonRadius);
        CompactPlayPauseGlyph.FontSize = m.CompactActionGlyphSize;

        if (DetachedPill is not null)
        {
            DetachedPill.Width = m.DetachedPillWidth;
            DetachedPill.Height = m.DetachedPillHeight;
            DetachedPill.CornerRadius = new CornerRadius(m.DetachedPillCornerRadius);
        }

        if (DetachedInnerGrid is not null)
        {
            DetachedInnerGrid.CornerRadius = new CornerRadius(Math.Max(1.0, m.DetachedPillCornerRadius - 1.0));
        }

        if (DetachedFrostSheen is not null)
        {
            DetachedFrostSheen.Height = m.DetachedFrostSheenHeight;
        }

        DetachedInnerStack.Spacing = m.DetachedStackSpacing;
        DetachedInnerStack.Padding = new Thickness(m.DetachedPaddingH, 0, m.DetachedPaddingH, 0);
        DetachedGlyphIcon.FontSize = m.DetachedGlyphSize;
        DetachedBadgeTextBlock.FontSize = m.DetachedBadgeFontSize;
    }

    private void UpdateIdleWeatherIcon(WeatherIconKind iconKind)
    {
        WeatherIconClearDay.Visibility = iconKind == WeatherIconKind.ClearDay ? Visibility.Visible : Visibility.Collapsed;
        WeatherIconClearNight.Visibility = iconKind == WeatherIconKind.ClearNight ? Visibility.Visible : Visibility.Collapsed;
        WeatherIconPartlyCloudyDay.Visibility = iconKind == WeatherIconKind.PartlyCloudyDay ? Visibility.Visible : Visibility.Collapsed;
        WeatherIconPartlyCloudyNight.Visibility = iconKind == WeatherIconKind.PartlyCloudyNight ? Visibility.Visible : Visibility.Collapsed;
        WeatherIconCloudy.Visibility = iconKind == WeatherIconKind.Cloudy ? Visibility.Visible : Visibility.Collapsed;
        WeatherIconFog.Visibility = iconKind == WeatherIconKind.Fog ? Visibility.Visible : Visibility.Collapsed;
        WeatherIconRain.Visibility = iconKind == WeatherIconKind.Rain ? Visibility.Visible : Visibility.Collapsed;
        WeatherIconHeavyRain.Visibility = iconKind == WeatherIconKind.HeavyRain ? Visibility.Visible : Visibility.Collapsed;
        WeatherIconSnow.Visibility = iconKind == WeatherIconKind.Snow ? Visibility.Visible : Visibility.Collapsed;
        WeatherIconThunderstorm.Visibility = iconKind == WeatherIconKind.Thunderstorm ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateFocusProgressFill()
    {
        if (FocusProgressTrack is null || FocusProgressFill is null) return;
        var trackWidth = FocusProgressTrack.ActualWidth;
        if (trackWidth <= 0)
        {
            trackWidth = 336; // fall back to XAML margin-derived width until layout settles
        }
        var progress = Math.Clamp(_viewModel.FocusProgress, 0, 1);
        FocusProgressFill.Width = Math.Max(0, progress * trackWidth);
    }

    /// <summary>Motion intensity from settings: damping + duration profile for spring animations.</summary>
    private (double Damping, double DurationScale) AnimationProfile
    {
        get
        {
            var (damping, scale) = _viewModel.AnimationIntensity switch
            {
                "Minimal" => (0.95, 0.72),
                "Dynamic" => (0.60, 1.18),
                _ => (0.82, 1.0)
            };
            return (damping, scale / MotionSpeedScale);
        }
    }

    /// <summary>70-140% user speed; higher values compress both durations and spring time.</summary>
    private double MotionSpeedScale => Math.Clamp(_viewModel.MotionSpeed, 70, 140) / 100.0;

    private TimeSpan ScaledDuration(double baseMs, double minMs = 30.0)
        => TimeSpan.FromMilliseconds(Math.Max(minMs, baseMs * AnimationProfile.DurationScale));

    private DampedSpringOscillator SelectPageSpring() => _viewModel.AnimationIntensity switch
    {
        "Minimal" => _pageSpringMinimal,
        "Dynamic" => _pageSpringDynamic,
        _ => _pageSpringNormal
    };

    private DampedSpringOscillator SelectDotSpring() => _viewModel.AnimationIntensity switch
    {
        "Minimal" => _dotSpringMinimal,
        "Dynamic" => _dotSpringDynamic,
        _ => _dotSpringNormal
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

    private void SetSplitPositions(double leadingX, double trailingX)
    {
        Surface.Translation = new Vector3((float)leadingX, 0, 0);
        if (DetachedPill is not null)
        {
            DetachedPill.Translation = new Vector3((float)trailingX, 0, 0);
            UpdateDetachedFrostTransform(trailingX);
        }
    }

    private void UpdateDetachedFrostTransform(double trailingX)
    {
        if (_detachedFrostTransform is null)
        {
            return;
        }

        double detachedW = DetachedPill is not null && DetachedPill.Width > 0
            ? DetachedPill.Width
            : IslandSurfaceLayout.DetachedPillWidthDip;
        double leftInSurface = ((IslandSurfaceLayout.DefaultHostWidthDip - detachedW) * 0.5) + trailingX;
        // Offset by 1 DIP BorderThickness inside DetachedPill so desktop frost stays pixel-locked.
        _detachedFrostTransform.X = -(leftInSurface + 1.0);
        _detachedFrostTransform.Y = -1.0;
    }

    private const float RimStrokeThicknessDip = 1.0f;
    private const float RimCenterInsetDip = RimStrokeThicknessDip * 0.5f;

    private void EnsureSurfaceClipAndRim()
    {
        var compositor = _pillMaskVisual.Compositor;
        if (_surfaceClipGeometry is null)
        {
            _surfaceClipGeometry = compositor.CreateRoundedRectangleGeometry();
            _pillMaskVisual.Clip = compositor.CreateGeometricClip(_surfaceClipGeometry);
        }

        if (_frostRimVisual is null && FrostRimHost is not null)
        {
            _frostRimGeometry = compositor.CreateRoundedRectangleGeometry();
            _frostRimShape = compositor.CreateSpriteShape(_frostRimGeometry);
            _frostRimShape.StrokeThickness = RimStrokeThicknessDip;
            _frostRimShape.StrokeBrush = DesktopFrostService.CreateCompositionVerticalGradient(
                compositor,
                0x46FFFFFFu,
                0x14FFFFFFu);

            _frostRimVisual = compositor.CreateShapeVisual();
            _frostRimVisual.Size = new Vector2(
                (float)IslandSurfaceLayout.DefaultHostWidthDip,
                (float)IslandSurfaceLayout.DefaultHostHeightDip);
            _frostRimVisual.Shapes.Add(_frostRimShape);
            ElementCompositionPreview.SetElementChildVisual(FrostRimHost, _frostRimVisual);
        }
    }

    /// <summary>
    /// Shapes the fixed 560x300 pill surface purely on the GPU using CompositionRoundedRectangleGeometry
    /// and frost sheen scaling, avoiding any per-frame XAML layout invalidation or Measure/Arrange pass.
    /// </summary>
    private void ApplySurfaceShape(double cornerRadius, double width, double height)
    {
        if (width > 0 && height > 0)
        {
            EnsureSurfaceClipAndRim();

            double clampedWidth = Math.Min(IslandSurfaceLayout.DefaultHostWidthDip, Math.Max(1.0, width));
            double clampedHeight = Math.Min(IslandSurfaceLayout.DefaultHostHeightDip, Math.Max(1.0, height));
            float offsetX = (float)((IslandSurfaceLayout.DefaultHostWidthDip - clampedWidth) / 2.0);
            float clampedRadius = (float)Math.Max(0.0, cornerRadius);

            _surfaceClipGeometry!.Offset = new Vector2(offsetX, 0f);
            _surfaceClipGeometry.Size = new Vector2((float)clampedWidth, (float)clampedHeight);
            _surfaceClipGeometry.CornerRadius = new Vector2(clampedRadius, clampedRadius);

            // Center the 1 DIP stroke half inside the geometric clip so outer antialiasing never gets cropped.
            if (_frostRimGeometry is not null)
            {
                float rimWidth = Math.Max(1f, (float)clampedWidth - (RimCenterInsetDip * 2f));
                float rimHeight = Math.Max(1f, (float)clampedHeight - (RimCenterInsetDip * 2f));
                float rimRadius = Math.Max(0f, clampedRadius - RimCenterInsetDip);
                _frostRimGeometry.Offset = new Vector2(offsetX + RimCenterInsetDip, RimCenterInsetDip);
                _frostRimGeometry.Size = new Vector2(rimWidth, rimHeight);
                _frostRimGeometry.CornerRadius = new Vector2(rimRadius, rimRadius);
            }
        }

        if (_frostSheenVisual is not null && height > 0)
        {
            float sheenScaleY = (float)Math.Max(10.0 / 18.0, (height * 0.45) / 18.0);
            _frostSheenVisual.Scale = new Vector3(1f, sheenScaleY, 1f);
        }
    }

    private void OnExpandedPanelsHostSizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateExpandedPanelsClip();

    private void UpdateExpandedPanelsClip()
    {
        if (_expandedPanelsClip is null || ExpandedPanelsHost is null)
        {
            return;
        }

        double w = Math.Max(0.0, ExpandedPanelsHost.ActualWidth);
        double h = Math.Max(0.0, ExpandedPanelsHost.ActualHeight);
        if (w <= 0 || h <= 0)
        {
            w = 528.0;
            h = 200.0;
        }

        _expandedPanelsClip.Rect = new Windows.Foundation.Rect(0, 0, w, h);
    }

    private void OnSystemAnimationsEnabledChanged(Windows.UI.ViewManagement.UISettings sender, object args)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_windowClosed)
            {
                return;
            }

            if (ReducedMotion)
            {
                StopAmbientAnimations();
            }
            else if (!_isIslandHidden || _hideAnchorVisible)
            {
                RestoreAmbientAnimations();
            }

            ApplyStaticAmbientStatesIfReducedMotion();
        });
    }

    private void ApplyStaticAmbientStatesIfReducedMotion()
    {
        if (!ReducedMotion)
        {
            return;
        }

        try
        {
            _wave1Visual.StopAnimation("Scale");
            _wave2Visual.StopAnimation("Scale");
            _wave3Visual.StopAnimation("Scale");
            _wave1Visual.Scale = new Vector3(1f, 0.55f, 1f);
            _wave2Visual.Scale = new Vector3(1f, 0.85f, 1f);
            _wave3Visual.Scale = new Vector3(1f, 0.45f, 1f);
        }
        catch { }

        if (_viewModel.BatteryIsCharging)
        {
            try
            {
                _batteryChargeHaloVisual.StopAnimation("Opacity");
                _batteryChargeHaloVisual.StopAnimation("Scale");
                _batteryChargeHaloVisual.Opacity = 0.35f;
                _batteryChargeHaloVisual.Scale = Vector3.One;
            }
            catch { }
        }
    }

    private void EnsureRenderLoopRunning()
    {
        if (_isRenderLoopSubscribed)
        {
            return;
        }

        _lastRenderTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        _isRenderLoopSubscribed = true;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnFrameRender;
    }

    private void StopRenderLoopIfIdle()
    {
        if (!_isRenderLoopSubscribed || _isDimensionAnimating || _isPageAnimating || _isDotAnimating)
        {
            return;
        }

        StopRenderLoop();
    }

    private void StopRenderLoop()
    {
        if (!_isRenderLoopSubscribed)
        {
            return;
        }

        _isRenderLoopSubscribed = false;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnFrameRender;
    }

    private void OnFrameRender(object? sender, object e)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double rawDt = (now - _lastRenderTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
        _lastRenderTicks = now;

        // Clamp dt to [0.5 ms .. 33 ms] so high-refresh (144Hz-360Hz) and transient stalls remain silky and deterministic.
        double dt = DampedSpringOscillator.ClampFrameDeltaSeconds(rawDt);

        if (_isDimensionAnimating)
        {
            StepDimensionAnimation(dt);
        }

        if (_isPageAnimating)
        {
            StepPageTransition(dt);
        }

        if (_isDotAnimating)
        {
            StepDotAnimation(dt);
        }

        StopRenderLoopIfIdle();
    }

    private DampedSpringOscillator SelectDimensionSpring() => _viewModel.AnimationIntensity switch
    {
        "Minimal" => _springMinimal,
        "Dynamic" => _springDynamic,
        _ => _springNormal
    };

    private void AnimateSurfaceDimensions(
        double toWidth,
        double toHeight,
        double toCornerRadius,
        double toLeadingX,
        double toTrailingX,
        double toDetachedOpacity,
        double toExpandedScale,
        double durationMs,
        Action? onCompleted = null)
    {
        if (ReducedMotion)
        {
            _isDimensionAnimating = false;
            _velPillWidth = 0;
            _velPillHeight = 0;
            _velCornerRadius = 0;
            _velLeadingX = 0;
            _velTrailingX = 0;
            _velDetachedOpacity = 0;
            _velExpandedScale = 0;

            _currentPillWidth = toWidth;
            _currentPillHeight = toHeight;
            _currentCornerRadius = toCornerRadius;
            _currentLeadingX = toLeadingX;
            _currentTrailingX = toTrailingX;
            _currentDetachedOpacity = toDetachedOpacity;
            _currentExpandedScale = toExpandedScale;
            _animStartDetachedActive = toDetachedOpacity > 0.01;
            _animTargetDetachedActive = toDetachedOpacity > 0.01;

            ApplySurfaceShape(toCornerRadius, toWidth, toHeight);
            _expandedVisual.StopAnimation("Scale");
            _expandedVisual.CenterPoint = new Vector3(192f, 20f, 0f);
            _expandedVisual.Scale = new Vector3((float)toExpandedScale, (float)toExpandedScale, 1f);
            _regionCommitted = true;
            SetSplitPositions(toLeadingX, toTrailingX);
            if (DetachedPill is not null)
            {
                DetachedPill.Opacity = toDetachedOpacity;
                DetachedPill.Visibility = toDetachedOpacity > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateWindowRegion(toWidth, toHeight, toCornerRadius);
            StopRenderLoopIfIdle();
            onCompleted?.Invoke();
            return;
        }

        bool wasAnimating = _isDimensionAnimating;
        _activeDimensionSpring = SelectDimensionSpring();

        _animStartWidth = _currentPillWidth > 0 ? _currentPillWidth : 216.0;
        _animStartHeight = _currentPillHeight > 0 ? _currentPillHeight : 40.0;
        _animStartCornerRadius = _currentCornerRadius > 0 ? _currentCornerRadius : 20.0;
        _animStartLeadingX = _currentLeadingX;
        _animStartTrailingX = _currentTrailingX;
        _animStartDetachedOpacity = _currentDetachedOpacity;
        _animStartExpandedScale = _currentExpandedScale;
        _animStartDetachedActive = _currentDetachedOpacity > 0.01 ||
            (DetachedPill is not null && DetachedPill.Visibility == Visibility.Visible);
        _animTargetDetachedActive = toDetachedOpacity > 0.01;

        double detachedTimeScale = toDetachedOpacity < _animStartDetachedOpacity ? 2.2 : 1.0;

        _animVelWidth = wasAnimating ? _velPillWidth : 0.0;
        _animVelHeight = wasAnimating ? _velPillHeight : 0.0;
        _animVelCornerRadius = wasAnimating ? _velCornerRadius : 0.0;
        _animVelLeadingX = wasAnimating ? _velLeadingX : 0.0;
        _animVelTrailingX = wasAnimating ? _velTrailingX : 0.0;
        _animVelDetachedOpacity = wasAnimating
            ? DampedSpringOscillator.NormalizeScaledVelocity(_velDetachedOpacity, detachedTimeScale)
            : 0.0;
        _animVelExpandedScale = wasAnimating ? _velExpandedScale : 0.0;

        if (toDetachedOpacity > 0 && DetachedPill is not null)
        {
            DetachedPill.Visibility = Visibility.Visible;
            if (!wasAnimating && _animStartDetachedOpacity <= 0.05)
            {
                double restTrailingX = DetachedPill.Width > 0 ? DetachedPill.Width : IslandSurfaceLayout.DetachedPillWidthDip;
                _animStartTrailingX = restTrailingX;
                _currentTrailingX = restTrailingX;
                SetSplitPositions(_animStartLeadingX, _animStartTrailingX);
            }
        }

        _animTargetWidth = toWidth;
        _animTargetHeight = toHeight;
        _animTargetCornerRadius = toCornerRadius;
        _animTargetLeadingX = toLeadingX;
        _animTargetTrailingX = toTrailingX;
        _animTargetDetachedOpacity = toDetachedOpacity;
        _animTargetExpandedScale = toExpandedScale;

        // Expanding out of split: let the trailing capsule fade where it stands instead of
        // drifting underneath the card that is growing under it.
        if (toDetachedOpacity < _animStartDetachedOpacity)
        {
            _animTargetTrailingX = _animStartTrailingX;
        }

        _animDurationMs = Math.Max(1.0, durationMs * AnimationProfile.DurationScale);
        _dimensionAnimElapsedSec = 0.0;
        _onAnimationCompleted = onCompleted;
        _regionCommitted = false;

        _expandedVisual.CenterPoint = new Vector3(192f, 20f, 0f);
        _isDimensionAnimating = true;
        EnsureRenderLoopRunning();
    }

    private void StepDimensionAnimation(double dt)
    {
        _dimensionAnimElapsedSec += dt;
        double springElapsedSec = _dimensionAnimElapsedSec * MotionSpeedScale;

        var stateW = _activeDimensionSpring.Evaluate(springElapsedSec, _animStartWidth, _animTargetWidth, _animVelWidth);
        var stateH = _activeDimensionSpring.Evaluate(springElapsedSec, _animStartHeight, _animTargetHeight, _animVelHeight);
        var stateR = _activeDimensionSpring.Evaluate(springElapsedSec, _animStartCornerRadius, _animTargetCornerRadius, _animVelCornerRadius);
        var stateLX = _activeDimensionSpring.Evaluate(springElapsedSec, _animStartLeadingX, _animTargetLeadingX, _animVelLeadingX);
        var stateTX = _activeDimensionSpring.Evaluate(springElapsedSec, _animStartTrailingX, _animTargetTrailingX, _animVelTrailingX);

        double detachedTimeScale = _animTargetDetachedOpacity < _animStartDetachedOpacity ? 2.2 : 1.0;
        double detachedElapsedSec = springElapsedSec * detachedTimeScale;
        var stateDO = _activeDimensionSpring.Evaluate(
            detachedElapsedSec,
            _animStartDetachedOpacity,
            _animTargetDetachedOpacity,
            _animVelDetachedOpacity,
            DampedSpringOscillator.NormalizedPositionTolerance,
            DampedSpringOscillator.NormalizedVelocityTolerance);
        var stateES = _activeDimensionSpring.Evaluate(
            springElapsedSec,
            _animStartExpandedScale,
            _animTargetExpandedScale,
            _animVelExpandedScale,
            DampedSpringOscillator.NormalizedPositionTolerance,
            DampedSpringOscillator.NormalizedVelocityTolerance);

        // Clamp only shrinking dimensions so collapse stops crisply without a post-target tremor,
        // while growing dimensions keep full elastic spring overshoot.
        double width = stateW.Position;
        double velWidth = stateW.Velocity;
        bool widthClamped = false;
        if (_animTargetWidth < _animStartWidth && width <= _animTargetWidth)
        {
            width = _animTargetWidth;
            velWidth = 0.0;
            widthClamped = true;
        }

        double height = stateH.Position;
        double velHeight = stateH.Velocity;
        bool heightClamped = false;
        if (_animTargetHeight < _animStartHeight && height <= _animTargetHeight)
        {
            height = _animTargetHeight;
            velHeight = 0.0;
            heightClamped = true;
        }

        double cornerRadius = stateR.Position;
        double velCornerRadius = stateR.Velocity;
        bool radiusClamped = false;
        if (_animTargetCornerRadius < _animStartCornerRadius && cornerRadius <= _animTargetCornerRadius)
        {
            cornerRadius = _animTargetCornerRadius;
            velCornerRadius = 0.0;
            radiusClamped = true;
        }

        width = Math.Clamp(width, 1.0, IslandSurfaceLayout.DefaultHostWidthDip);
        height = Math.Clamp(height, 1.0, IslandSurfaceLayout.DefaultHostHeightDip);
        cornerRadius = Math.Max(0.0, cornerRadius);

        bool doClamped = stateDO.Position < 0.0 || stateDO.Position > 1.0;
        double detachedOpacity = Math.Clamp(stateDO.Position, 0.0, 1.0);
        double expandedScale = Math.Clamp(stateES.Position, 0.85, 1.15);

        _currentPillWidth = width;
        _currentPillHeight = height;
        _currentCornerRadius = cornerRadius;
        _currentLeadingX = stateLX.Position;
        _currentTrailingX = stateTX.Position;
        _currentDetachedOpacity = detachedOpacity;
        _currentExpandedScale = expandedScale;

        _velPillWidth = velWidth;
        _velPillHeight = velHeight;
        _velCornerRadius = velCornerRadius;
        _velLeadingX = stateLX.Velocity;
        _velTrailingX = stateTX.Velocity;
        _velDetachedOpacity = doClamped
            ? 0.0
            : DampedSpringOscillator.ConvertToScreenVelocity(stateDO.Velocity, detachedTimeScale);
        _velExpandedScale = stateES.Velocity;

        ApplySurfaceShape(cornerRadius, width, height);
        SetSplitPositions(_currentLeadingX, _currentTrailingX);

        if (DetachedPill is not null && DetachedPill.Visibility == Visibility.Visible)
        {
            DetachedPill.Opacity = detachedOpacity;
        }

        if (_viewModel.IsExpanded || ExpandedView.Visibility == Visibility.Visible)
        {
            _expandedVisual.Scale = new Vector3((float)expandedScale, (float)expandedScale, 1f);
        }

        bool isExpanding = _animTargetWidth > _animStartWidth || _animTargetHeight > _animStartHeight;
        bool detachedDone = stateDO.HasSettled ||
            (_animTargetDetachedOpacity <= 0.0 && detachedOpacity <= 0.001) ||
            (_animTargetDetachedOpacity >= 1.0 && stateDO.Position >= 1.0);
        bool settled = (stateW.HasSettled || widthClamped) &&
                       (stateH.HasSettled || heightClamped) &&
                       (stateR.HasSettled || radiusClamped) &&
                       stateLX.HasSettled &&
                       stateTX.HasSettled &&
                       detachedDone &&
                       stateES.HasSettled;
        double safetyTimeoutSec = Math.Max(1.2, _animDurationMs * 0.001 * 3.0);

        bool splitStillMoving = _animStartDetachedActive != _animTargetDetachedActive ||
            Math.Abs(_currentLeadingX - _animTargetLeadingX) > 0.5 ||
            Math.Abs(_currentDetachedOpacity - _animTargetDetachedOpacity) > 0.02;

        if (!isExpanding && !splitStillMoving && !_regionCommitted &&
            Math.Abs(width - _animTargetWidth) <= 0.1 &&
            Math.Abs(height - _animTargetHeight) <= 0.1)
        {
            _regionCommitted = true;
            UpdateWindowRegion(_animTargetWidth, _animTargetHeight, _animTargetCornerRadius);
        }

        if (settled || _dimensionAnimElapsedSec >= safetyTimeoutSec)
        {
            _isDimensionAnimating = false;
            _velPillWidth = 0;
            _velPillHeight = 0;
            _velCornerRadius = 0;
            _velLeadingX = 0;
            _velTrailingX = 0;
            _velDetachedOpacity = 0;
            _velExpandedScale = 0;

            _currentPillWidth = _animTargetWidth;
            _currentPillHeight = _animTargetHeight;
            _currentCornerRadius = _animTargetCornerRadius;
            _currentLeadingX = _animTargetLeadingX;
            _currentTrailingX = _animTargetTrailingX;
            _currentDetachedOpacity = _animTargetDetachedOpacity;
            _currentExpandedScale = _animTargetExpandedScale;

            ApplySurfaceShape(_animTargetCornerRadius, _animTargetWidth, _animTargetHeight);
            SetSplitPositions(_animTargetLeadingX, _animTargetTrailingX);
            _expandedVisual.Scale = new Vector3((float)_animTargetExpandedScale, (float)_animTargetExpandedScale, 1f);

            if (DetachedPill is not null)
            {
                DetachedPill.Opacity = _animTargetDetachedOpacity;
                if (_animTargetDetachedOpacity <= 0.0)
                {
                    DetachedPill.Visibility = Visibility.Collapsed;
                }
            }

            UpdateWindowRegion(_animTargetWidth, _animTargetHeight, _animTargetCornerRadius);

            var callback = _onAnimationCompleted;
            _onAnimationCompleted = null;
            callback?.Invoke();
        }
    }

    private void UpdateStateLayout(IslandState state, bool animated)
    {
        // A page slide must not keep writing panel offsets after the panels are reset below.
        CancelPageTransition();

        if (ReducedMotion || _isIslandHidden)
        {
            animated = false;
        }

        // Discard stale completion callback from any superseded transition while preserving
        // active velocities so AnimateSurfaceDimensions can smoothly redirect momentum.
        _onAnimationCompleted = null;

        var generation = ++_transitionGeneration;
        var isExpanded = state == IslandState.Expanded;
        var isCollapsing = state == IslandState.Collapsing;
        var isHover = state == IslandState.Hover;
        var hasTrack = _viewModel.HasTrack;

        if (isExpanded && ExpandedView.Visibility != Visibility.Visible)
        {
            // The tree is parked while compact; wake it before the fade so the first
            // animation frame already has expanded content to show. A re-realized XAML
            // element can expose a fresh visual, so re-acquire it instead of animating
            // a stale reference (which would leave the card opaque but empty).
            ExpandedView.Visibility = Visibility.Visible;
            _expandedVisual = ElementCompositionPreview.GetElementVisual(ExpandedView);
            _expandedVisual.CenterPoint = new Vector3(192f, 20f, 0f);
            _expandedVisual.Opacity = 0;
            if (!_isDimensionAnimating)
            {
                _currentExpandedScale = 0.94;
                _velExpandedScale = 0;
            }
            _expandedVisual.Scale = new Vector3((float)_currentExpandedScale, (float)_currentExpandedScale, 1f);
        }

        bool isSplit = _viewModel.IsSplitModeActive && !isExpanded && !isCollapsing;

        double targetWidth = IslandPresentationPolicy.ComputeCompactTargetWidth(
            state,
            hasTrack,
            isSplit,
            _viewModel.IsNotificationActive,
            _viewModel.IsFileOperationActive,
            _viewModel.IsFocusTimerActive);

        var metrics = IslandSurfaceLayout.ComputeCompactMetrics(_viewModel.IslandScale);
        var scale = isExpanded ? 1.0 : metrics.Scale;
        double targetHeight = isExpanded ? 240.0 : metrics.PillHeight;
        // Continuous island corners: large enough to read as an Apple surface
        // instead of a generic rounded card, still inside the content insets.
        double targetCornerRadius = isExpanded ? 56.0 : metrics.PillCornerRadius;
        targetWidth *= scale;

        double detachedWidth = metrics.DetachedPillWidth;
        double detachedHeight = metrics.DetachedPillHeight;
        if (DetachedPill is not null && (Math.Abs(DetachedPill.Width - detachedWidth) > 0.01 || Math.Abs(DetachedPill.Height - detachedHeight) > 0.01))
        {
            DetachedPill.Width = detachedWidth;
            DetachedPill.Height = detachedHeight;
            DetachedPill.CornerRadius = new CornerRadius(metrics.DetachedPillCornerRadius);
            if (DetachedInnerGrid is not null)
            {
                DetachedInnerGrid.CornerRadius = new CornerRadius(Math.Max(1.0, metrics.DetachedPillCornerRadius - 1.0));
            }
        }

        var (splitLeadingX, splitTrailingX) = IslandSurfaceLayout.ComputeSplitOffsets(targetWidth, detachedWidth, metrics.SplitGap);
        double targetLeadingX = isSplit ? splitLeadingX : 0.0;
        double targetTrailingX = isSplit ? splitTrailingX : detachedWidth;
        double targetDetachedOpacity = isSplit ? 1.0 : 0.0;
        double targetExpandedScale = isExpanded ? 1.0 : 0.94;

        // Size CompactView only when entering/updating compact states (never per-frame and not during expansion).
        if (!isExpanded && (Math.Abs(CompactView.Width - targetWidth) > 0.01 || Math.Abs(CompactView.Height - targetHeight) > 0.01))
        {
            CompactView.Width = targetWidth;
            CompactView.Height = targetHeight;
        }

        if (CompactWaveBars is not null)
        {
            bool showBars = isHover && _viewModel.ShowEqualizer;
            CompactWaveBars.Visibility = showBars ? Visibility.Visible : Visibility.Collapsed;
            CompactWaveBars.Opacity = showBars ? 1.0 : 0.0;
        }

        if (!animated)
        {
            _isDimensionAnimating = false;
            _velPillWidth = 0;
            _velPillHeight = 0;
            _velCornerRadius = 0;
            _velLeadingX = 0;
            _velTrailingX = 0;
            _velDetachedOpacity = 0;
            _velExpandedScale = 0;

            _currentPillWidth = targetWidth;
            _currentPillHeight = targetHeight;
            _currentCornerRadius = targetCornerRadius;
            _currentLeadingX = targetLeadingX;
            _currentTrailingX = targetTrailingX;
            _currentDetachedOpacity = targetDetachedOpacity;
            _currentExpandedScale = isExpanded ? 1.0 : 0.94;
            _animStartDetachedActive = targetDetachedOpacity > 0.01;
            _animTargetDetachedActive = targetDetachedOpacity > 0.01;

            ApplySurfaceShape(targetCornerRadius, targetWidth, targetHeight);
            SetSplitPositions(targetLeadingX, targetTrailingX);
            if (DetachedPill is not null)
            {
                DetachedPill.Opacity = targetDetachedOpacity;
                DetachedPill.Visibility = isSplit && !_isIslandHidden ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateWindowRegion(targetWidth, targetHeight, targetCornerRadius);

            _compactVisual.StopAnimation("Opacity");
            _compactVisual.Opacity = isExpanded ? 0 : 1;
            CompactView.IsHitTestVisible = !isExpanded;
            _expandedVisual.StopAnimation("Opacity");
            _expandedVisual.StopAnimation("Scale");
            _expandedVisual.CenterPoint = new Vector3(192f, 20f, 0f);
            _expandedVisual.Scale = new Vector3((float)(isExpanded ? 1.0 : 0.94), (float)(isExpanded ? 1.0 : 0.94), 1f);
            _expandedVisual.Opacity = isExpanded ? 1 : 0;
            ExpandedView.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
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
                    v.StopAnimation("Translation");
                    v.StopAnimation("Opacity");
                    v.StopAnimation("Scale");
                    v.Scale = Vector3.One;
                    v.Opacity = (i == activeIndex) ? 1f : 0f;
                }
                UpdatePageDots(activeIndex, animated: false);
            }

            _pillMaskVisual.StopAnimation("Scale");
            _pillMaskVisual.Scale = Vector3.One;
            _pillMaskVisual.Opacity = 1;
            StopRenderLoopIfIdle();

            if (state == IslandState.Collapsing)
            {
                _viewModel.CompleteCollapse();
            }
            return;
        }

        // Skip redundant animation/HRGN overshoot if an idle transition lands back on identical geometry (e.g. post-collapse sync).
        bool alreadyAtTargetDimensions =
            !isExpanded &&
            !isCollapsing &&
            !_isDimensionAnimating &&
            ExpandedView.Visibility != Visibility.Visible &&
            Math.Abs(_currentPillWidth - targetWidth) < 0.2 &&
            Math.Abs(_currentPillHeight - targetHeight) < 0.2 &&
            Math.Abs(_currentCornerRadius - targetCornerRadius) < 0.2 &&
            Math.Abs(_currentLeadingX - targetLeadingX) < 0.2 &&
            Math.Abs(_currentDetachedOpacity - targetDetachedOpacity) < 0.01;

        if (alreadyAtTargetDimensions)
        {
            _compactVisual.StopAnimation("Opacity");
            _compactVisual.Opacity = 1;
            CompactView.IsHitTestVisible = true;
            ExpandedView.IsHitTestVisible = false;
            ApplySurfaceShape(targetCornerRadius, targetWidth, targetHeight);
            SetSplitPositions(targetLeadingX, targetTrailingX);
            if (DetachedPill is not null)
            {
                DetachedPill.Opacity = targetDetachedOpacity;
                DetachedPill.Visibility = isSplit && !_isIslandHidden ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateWindowRegion(targetWidth, targetHeight, targetCornerRadius);
            return;
        }

        try
        {
            var compositor = _pillMaskVisual.Compositor;
            var quickEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.6f, 1f));
            var durScale = AnimationProfile.DurationScale;

            _pillMaskVisual.StopAnimation("Scale");
            _pillMaskVisual.Scale = Vector3.One;

            var springEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));

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
                    v.StopAnimation("Translation");
                    v.StopAnimation("Opacity");
                    v.StopAnimation("Scale");
                    v.Scale = Vector3.One;
                    v.Opacity = (i == activeIndex) ? 1f : 0f;
                }
                UpdatePageDots(activeIndex, animated: false);

                _compactVisual.StopAnimation("Opacity");
                var compactFade = compositor.CreateScalarKeyFrameAnimation();
                compactFade.Duration = TimeSpan.FromMilliseconds(75 * durScale);
                compactFade.InsertKeyFrame(0f, Math.Clamp(_compactVisual.Opacity, 0f, 1f));
                compactFade.InsertKeyFrame(1f, 0f, quickEasing);
                _compactVisual.StartAnimation("Opacity", compactFade);

                float expandedStartOpacity = ExpandedView.Visibility == Visibility.Visible
                    ? Math.Clamp(_expandedVisual.Opacity, 0.15f, 1f)
                    : 0.15f;
                _expandedVisual.StopAnimation("Opacity");
                var expandedFade = compositor.CreateScalarKeyFrameAnimation();
                expandedFade.Duration = TimeSpan.FromMilliseconds(280 * durScale);
                expandedFade.InsertKeyFrame(0f, expandedStartOpacity);
                expandedFade.InsertKeyFrame(1f, 1f, springEasing);
                _expandedVisual.StartAnimation("Opacity", expandedFade);
                ExpandedView.Translation = Vector3.Zero;

                AnimateSurfaceDimensions(targetWidth, targetHeight, targetCornerRadius, 0.0, detachedWidth, 0.0, targetExpandedScale, 340);
                UpdateWindowRegion(
                    Math.Max(_currentPillWidth, targetWidth),
                    Math.Max(_currentPillHeight, targetHeight),
                    Math.Max(_currentCornerRadius, targetCornerRadius),
                    includeOvershootPadding: true);
            }
            else if (isCollapsing)
            {
                CompactView.IsHitTestVisible = true;
                ExpandedView.IsHitTestVisible = false;

                _expandedVisual.StopAnimation("Opacity");
                var expandedFade = compositor.CreateScalarKeyFrameAnimation();
                expandedFade.Duration = TimeSpan.FromMilliseconds(75 * durScale);
                expandedFade.InsertKeyFrame(0f, Math.Clamp(_expandedVisual.Opacity, 0f, 1f));
                expandedFade.InsertKeyFrame(1f, 0f, quickEasing);

                // Park the expanded tree once faded out so it consumes zero visual overhead when compact.
                var fadeBatch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
                _expandedVisual.StartAnimation("Opacity", expandedFade);
                fadeBatch.End();
                fadeBatch.Completed += (_, _) =>
                {
                    _ = DispatcherQueue.TryEnqueue(() =>
                    {
                        if (!_windowClosed && generation == _transitionGeneration && !_viewModel.IsExpanded)
                        {
                            ExpandedView.Visibility = Visibility.Collapsed;
                        }
                    });
                };

                // Smoothly crossfade compact content back in while the expanded layer fades out.
                _compactVisual.StopAnimation("Opacity");
                float compactStartOpacity = Math.Clamp(_compactVisual.Opacity, 0f, 1f);
                if (compactStartOpacity < 0.99f)
                {
                    var compactFadeIn = compositor.CreateScalarKeyFrameAnimation();
                    compactFadeIn.Duration = TimeSpan.FromMilliseconds(85 * durScale);
                    compactFadeIn.InsertKeyFrame(0f, compactStartOpacity);
                    compactFadeIn.InsertKeyFrame(1f, 1f, quickEasing);
                    _compactVisual.StartAnimation("Opacity", compactFadeIn);
                }
                else
                {
                    _compactVisual.Opacity = 1f;
                }

                AnimateSurfaceDimensions(targetWidth, targetHeight, targetCornerRadius, targetLeadingX, targetTrailingX, targetDetachedOpacity, targetExpandedScale, 300, () =>
                {
                    UpdateWindowRegion(targetWidth, targetHeight, targetCornerRadius);
                    if (generation == _transitionGeneration && _viewModel.CurrentIslandState == IslandState.Collapsing)
                    {
                        _viewModel.CompleteCollapse();
                    }
                });

                UpdateWindowRegion(
                    Math.Max(_currentPillWidth, targetWidth),
                    Math.Max(_currentPillHeight, targetHeight),
                    Math.Max(_currentCornerRadius, targetCornerRadius),
                    includeOvershootPadding: _animStartDetachedActive || _animTargetDetachedActive);
            }
            else
            {
                CompactView.IsHitTestVisible = true;
                ExpandedView.IsHitTestVisible = false;

                if (ExpandedView.Visibility == Visibility.Visible && _expandedVisual.Opacity > 0.01f)
                {
                    _expandedVisual.StopAnimation("Opacity");
                    var expandedFade = compositor.CreateScalarKeyFrameAnimation();
                    expandedFade.Duration = TimeSpan.FromMilliseconds(75 * durScale);
                    expandedFade.InsertKeyFrame(0f, Math.Clamp(_expandedVisual.Opacity, 0f, 1f));
                    expandedFade.InsertKeyFrame(1f, 0f, quickEasing);

                    var fadeBatch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
                    _expandedVisual.StartAnimation("Opacity", expandedFade);
                    fadeBatch.End();
                    fadeBatch.Completed += (_, _) =>
                    {
                        _ = DispatcherQueue.TryEnqueue(() =>
                        {
                            if (!_windowClosed && generation == _transitionGeneration && !_viewModel.IsExpanded)
                            {
                                ExpandedView.Visibility = Visibility.Collapsed;
                            }
                        });
                    };

                    _compactVisual.StopAnimation("Opacity");
                    var compactFadeIn = compositor.CreateScalarKeyFrameAnimation();
                    compactFadeIn.Duration = TimeSpan.FromMilliseconds(85 * durScale);
                    compactFadeIn.InsertKeyFrame(0f, Math.Clamp(_compactVisual.Opacity, 0f, 1f));
                    compactFadeIn.InsertKeyFrame(1f, 1f, quickEasing);
                    _compactVisual.StartAnimation("Opacity", compactFadeIn);
                }
                else
                {
                    _expandedVisual.StopAnimation("Opacity");
                    _expandedVisual.Opacity = 0;
                    _compactVisual.StopAnimation("Opacity");
                    _compactVisual.Opacity = 1;
                    ExpandedView.Visibility = Visibility.Collapsed;
                }

                bool growing = targetWidth > _currentPillWidth || targetHeight > _currentPillHeight;
                AnimateSurfaceDimensions(targetWidth, targetHeight, targetCornerRadius, targetLeadingX, targetTrailingX, targetDetachedOpacity, targetExpandedScale, 220, () => UpdateWindowRegion(targetWidth, targetHeight, targetCornerRadius));
                UpdateWindowRegion(
                    Math.Max(_currentPillWidth, targetWidth),
                    Math.Max(_currentPillHeight, targetHeight),
                    Math.Max(_currentCornerRadius, targetCornerRadius),
                    includeOvershootPadding: growing || _animStartDetachedActive || _animTargetDetachedActive);
            }
        }
        catch
        {
            _isDimensionAnimating = false;
            StopRenderLoopIfIdle();
            _currentPillWidth = targetWidth;
            _currentPillHeight = targetHeight;
            _currentCornerRadius = targetCornerRadius;
            _currentLeadingX = targetLeadingX;
            _currentTrailingX = targetTrailingX;
            _currentDetachedOpacity = targetDetachedOpacity;
            _currentExpandedScale = isExpanded ? 1.0 : 0.94;
            ApplySurfaceShape(targetCornerRadius, targetWidth, targetHeight);
            SetSplitPositions(targetLeadingX, targetTrailingX);
            if (DetachedPill is not null)
            {
                DetachedPill.Opacity = targetDetachedOpacity;
                DetachedPill.Visibility = isSplit && !_isIslandHidden ? Visibility.Visible : Visibility.Collapsed;
            }
            _compactVisual.Opacity = isExpanded ? 0 : 1;
            _expandedVisual.Opacity = isExpanded ? 1 : 0;
            ExpandedView.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
            UpdateWindowRegion(targetWidth, targetHeight, targetCornerRadius);
        }
    }

    private void PlayExpansionElasticBounce()
    {
        // Handled naturally by DampedSpringOscillator on Surface dimensions
    }

    // GPU-accelerated Equalizer wave animations running on the Compositor thread (zero CPU)
    private void StartEqualizerWave()
    {
        if (!_viewModel.ShowEqualizer) return;
        if (ReducedMotion)
        {
            _waveAnimating = false;
            ApplyStaticAmbientStatesIfReducedMotion();
            return;
        }
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
        if (ReducedMotion) return;
        try
        {
            var compositor = _pillMaskVisual.Compositor;
            var springEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.34f, 1.56f), new Vector2(0.64f, 1f));

            _pillMaskVisual.CenterPoint = new Vector3((float)(IslandSurfaceLayout.DefaultHostWidthDip / 2.0), (float)(_currentPillHeight / 2.0), 0f);

            // Keep horizontal scale delta comfortably inside RegionAntialiasMarginDip (3.0 DIP) so SetWindowRgn never clips.
            float safeScaleX = 1.0f + (float)Math.Min(0.022, 2.2 / Math.Max(120.0, _currentPillWidth));

            var pulse = compositor.CreateVector3KeyFrameAnimation();
            pulse.Duration = ScaledDuration(420, minMs: 120);
            pulse.InsertKeyFrame(0f, Vector3.One);
            pulse.InsertKeyFrame(0.35f, new Vector3(safeScaleX, 1.045f, 1f), springEasing);
            pulse.InsertKeyFrame(1f, Vector3.One, springEasing);

            _pillMaskVisual.StartAnimation("Scale", pulse);
        }
        catch { }
    }

    private void UpdateBatteryChargingAnimation()
    {
        if (!_viewModel.BatteryIsCharging)
        {
            if (!_batteryChargeAnimating) return;
            _batteryChargeAnimating = false;

            try
            {
                _batteryChargeHaloVisual.StopAnimation("Opacity");
                _batteryChargeHaloVisual.StopAnimation("Scale");
            }
            catch { }

            return;
        }

        if (ReducedMotion)
        {
            _batteryChargeAnimating = false;
            ApplyStaticAmbientStatesIfReducedMotion();
            return;
        }

        if (_batteryChargeAnimating) return;
        _batteryChargeAnimating = true;

        try
        {
            var visual = _batteryChargeHaloVisual;
            var compositor = visual.Compositor;
            var easing = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.42f, 0f), new Vector2(0.58f, 1f));

            visual.CenterPoint = new Vector3(
                (float)(BatteryChargeHalo.Width / 2),
                (float)(BatteryChargeHalo.Height / 2), 0f);

            var opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.Duration = TimeSpan.FromMilliseconds(1500);
            opacity.IterationBehavior = AnimationIterationBehavior.Forever;
            opacity.Direction = AnimationDirection.Alternate;
            opacity.InsertKeyFrame(0f, 0.12f);
            opacity.InsertKeyFrame(1f, 0.55f, easing);
            visual.StartAnimation("Opacity", opacity);

            var scale = compositor.CreateVector3KeyFrameAnimation();
            scale.Duration = TimeSpan.FromMilliseconds(1500);
            scale.IterationBehavior = AnimationIterationBehavior.Forever;
            scale.Direction = AnimationDirection.Alternate;
            scale.InsertKeyFrame(0f, new Vector3(0.85f, 0.85f, 1f));
            scale.InsertKeyFrame(1f, new Vector3(1.15f, 1.15f, 1f), easing);
            visual.StartAnimation("Scale", scale);
        }
        catch
        {
            _batteryChargeAnimating = false;
        }
    }

    private TimeSpan GetCollapseDelayInterval() =>
        TimeSpan.FromMilliseconds(Math.Clamp(_viewModel.AutoCollapseDelayMs, 300, 3000));

    private void RestartCollapseDelayTimer()
    {
        _collapseDelayTimer.Interval = GetCollapseDelayInterval();
        _collapseDelayTimer.Stop();
        _collapseDelayTimer.Start();
    }

    private bool IsNonPrimaryPress(PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint(Surface).Properties;
        return props.IsRightButtonPressed ||
               props.IsMiddleButtonPressed ||
               props.IsXButton1Pressed ||
               props.IsXButton2Pressed;
    }

    private bool IsNonPrimaryRelease(PointerRoutedEventArgs e)
    {
        var kind = e.GetCurrentPoint(Surface).Properties.PointerUpdateKind;
        return kind is Microsoft.UI.Input.PointerUpdateKind.RightButtonReleased
            or Microsoft.UI.Input.PointerUpdateKind.MiddleButtonReleased
            or Microsoft.UI.Input.PointerUpdateKind.XButton1Released
            or Microsoft.UI.Input.PointerUpdateKind.XButton2Released;
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
            RestartCollapseDelayTimer();
        }
    }

    private void ExecuteHoverAction()
    {
        if (_isIslandHidden || _viewModel.IsExpanded)
        {
            return;
        }

        if (string.Equals(_viewModel.HoverAction, "ExpandCard", StringComparison.OrdinalIgnoreCase))
        {
            ExpandFromCompact();
        }
        else
        {
            _viewModel.OnPointerEntered();
        }
    }

    private void ActivateCompactTarget()
    {
        if (_viewModel.ActivateActiveEventTarget())
        {
            return;
        }

        if (_viewModel.HasTrack)
        {
            _viewModel.ActivateMediaSourceApp();
        }
        else if (_viewModel.IsFocusTimerActive)
        {
            _viewModel.SelectPage(1);
            _viewModel.ToggleExpanded();
        }
    }

    private void ExecuteCompactAction(string action)
    {
        if (string.Equals(action, "ActivateApp", StringComparison.OrdinalIgnoreCase))
        {
            ActivateCompactTarget();
            return;
        }

        if (string.Equals(action, "PlayPause", StringComparison.OrdinalIgnoreCase))
        {
            if (_viewModel.CanTogglePlayback)
            {
                _viewModel.PlayPauseCommand.Execute(null);
                return;
            }
        }

        ExpandFromCompact();
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (System.Diagnostics.Stopwatch.GetTimestamp() - _hostResizeGuardTicks < HostResizeGuardTicks)
        {
            return;
        }

        _isPointerOverSurface = true;
        _collapseDelayTimer.Stop();

        if (_viewModel.IsExpanded || !_viewModel.EnableHoverExpand)
        {
            // Preserve the timer-reset side effect without scheduling a hover morph.
            _viewModel.OnPointerEntered();
            return;
        }

        var delay = Math.Clamp(_viewModel.HoverExpandDelayMs, 0, 1000);
        if (delay <= 0)
        {
            ExecuteHoverAction();
            return;
        }

        _hoverExpandTimer?.Stop();
        if (_hoverExpandTimer is not null)
        {
            _hoverExpandTimer.Interval = TimeSpan.FromMilliseconds(delay);
            _hoverExpandTimer.Start();
        }
    }

    private void OnHoverExpandTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (_isPointerOverSurface && !_viewModel.IsExpanded && _viewModel.EnableHoverExpand)
        {
            ExecuteHoverAction();
        }
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        // Frost capture briefly hides the window, which synthesizes a synthetic
        // PointerExited — ignore it so the island doesn't collapse mid-capture.
        if (_frostBusy || System.Diagnostics.Stopwatch.GetTimestamp() - _frostRestoreGuardTicks < HostResizeGuardTicks)
        {
            return;
        }

        // Window resize/move under the cursor can fake an exit; ignore quick echoes too.
        if (System.Diagnostics.Stopwatch.GetTimestamp() - _hostResizeGuardTicks < HostResizeGuardTicks)
        {
            return;
        }

        _hoverExpandTimer?.Stop();
        _longPressTimer?.Stop();
        _longPressTriggered = false;
        _isPointerOverSurface = false;
        _viewModel.OnPointerExited();

        if (_viewModel.IsExpanded && _viewModel.AutoCollapse && !_viewModel.IsScrubbing && !_isInteractingWithControl)
        {
            RestartCollapseDelayTimer();
        }
    }

    private void OnSurfacePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsNonPrimaryPress(e))
        {
            return;
        }

        _hoverExpandTimer?.Stop();

        if (!_viewModel.IsExpanded)
        {
            _longPressTriggered = false;
            _longPressTimer?.Stop();
            _longPressTimer?.Start();
            float safePressX = 1.0f + (float)Math.Min(0.016, 1.8 / Math.Max(120.0, _currentPillWidth));
            AnimateSurfaceScale(safePressX, 0.945f, 90);
        }
    }

    private void OnLongPressTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (!_isPointerOverSurface || _viewModel.IsExpanded)
        {
            return;
        }

        _longPressTriggered = true;
        float safeLongPressX = 1.0f + (float)Math.Min(0.018, 2.0 / Math.Max(120.0, _currentPillWidth));
        AnimateSurfaceScale(safeLongPressX, 1.025f, 120);

        if (string.Equals(_viewModel.CompactClickAction, "Expand", StringComparison.OrdinalIgnoreCase))
        {
            if (_viewModel.IsNotificationActive || _viewModel.IsFileOperationActive || _viewModel.HasTrack || _viewModel.IsFocusTimerActive)
            {
                ActivateCompactTarget();
            }
            else
            {
                ExpandFromCompact();
            }
        }
        else
        {
            ExpandFromCompact();
        }
    }

    private void OnSurfacePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _longPressTimer?.Stop();
        AnimateSurfaceScale(1.0f, 1.0f, 140);

        if (IsNonPrimaryRelease(e))
        {
            _longPressTriggered = false;
            return;
        }

        if (!_viewModel.IsExpanded && !_longPressTriggered)
        {
            e.Handled = true;
            ExecuteCompactAction(_viewModel.CompactClickAction);
        }

        _longPressTriggered = false;
    }

    private void OnSurfacePointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _longPressTimer?.Stop();
        _hoverExpandTimer?.Stop();
        _longPressTriggered = false;
        AnimateSurfaceScale(1.0f, 1.0f, 140);
    }

    private void OnSurfacePointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _longPressTimer?.Stop();
        _longPressTriggered = false;
    }

    private void ExpandFromCompact()
    {
        // Land on the page that matches what the island is currently showing.
        if (_viewModel.IsFocusTimerActive && !_viewModel.HasTrack)
        {
            _viewModel.SelectPage(1);
        }

        _viewModel.ToggleExpanded();
    }

    private void OnDetachedPillPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _collapseDelayTimer.Stop();
        AnimateElementScale(DetachedPill, 1.055f, 120);
    }

    private void OnDetachedPillPointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateElementScale(DetachedPill, 1.0f, 120);
    }

    private void OnDetachedPillPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsNonPrimaryPress(e))
        {
            return;
        }

        AnimateElementScale(DetachedPill, 0.92f, 80);
        e.Handled = true;
    }

    private void OnDetachedPillPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        AnimateElementScale(DetachedPill, 1.0f, 140);
        if (IsNonPrimaryRelease(e))
        {
            return;
        }

        _viewModel.OnDetachedPillClicked();
        e.Handled = true;
    }

    private void AnimateSurfaceScale(float scaleX, float scaleY, int durationMs)
    {
        if (ReducedMotion) return;
        try
        {
            var compositor = _pillMaskVisual.Compositor;
            _pillMaskVisual.CenterPoint = new Vector3((float)(IslandSurfaceLayout.DefaultHostWidthDip / 2.0), (float)(_currentPillHeight / 2.0), 0f);

            var anim = compositor.CreateVector3KeyFrameAnimation();
            anim.Duration = ScaledDuration(durationMs);
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1.0f), new Vector2(0.3f, 1.0f));
            anim.InsertKeyFrame(1f, new Vector3(scaleX, scaleY, 1f), easing);

            _pillMaskVisual.StartAnimation("Scale", anim);
        }
        catch { }
    }

    private void AnimateElementScale(FrameworkElement element, float targetScale, int durationMs)
    {
        if (ReducedMotion) return;
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.CenterPoint = new Vector3((float)(element.ActualWidth / 2.0), (float)(element.ActualHeight / 2.0), 0);
            var compositor = visual.Compositor;
            var anim = compositor.CreateVector3KeyFrameAnimation();
            anim.Duration = ScaledDuration(durationMs);
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1.0f), new Vector2(0.3f, 1.0f));
            anim.InsertKeyFrame(1f, new Vector3(targetScale, targetScale, 1f), easing);
            visual.StartAnimation("Scale", anim);
        }
        catch { }
    }

    private void OnMediaSourcePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (IsNonPrimaryRelease(e))
        {
            return;
        }

        // Tapping the artwork/title opens the app that owns the media session (Spotify, browser...).
        e.Handled = true;
        _viewModel.ActivateMediaSourceApp();
    }

    private void OnEventContentPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (IsNonPrimaryRelease(e))
        {
            return;
        }

        e.Handled = _viewModel.ActivateActiveEventTarget();
    }

    private void OnCompactFocusPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (IsNonPrimaryRelease(e))
        {
            return;
        }

        e.Handled = true;
        _viewModel.SelectPage(1);
        if (!_viewModel.IsExpanded)
        {
            _viewModel.ToggleExpanded();
        }
    }

    private void OnExpandedViewPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (IsNonPrimaryRelease(e))
        {
            return;
        }

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
        if (_isIslandHidden)
        {
            return;
        }

        var action = _viewModel.ScrollWheelAction;
        var isVolumeMode = string.Equals(action, "Volume", StringComparison.OrdinalIgnoreCase) ||
                           (string.Equals(action, "Smart", StringComparison.OrdinalIgnoreCase) &&
                            ((!_viewModel.IsExpanded && _viewModel.HasTrack) ||
                             (_viewModel.IsExpanded && _viewModel.SelectedPageIndex == 0 && _viewModel.HasTrack)));

        if (!isVolumeMode && !_viewModel.IsExpanded)
        {
            return;
        }

        var point = e.GetCurrentPoint(Surface);
        var delta = point.Properties.MouseWheelDelta;

        var step = _wheelAccumulator.Accumulate(delta);
        if (step == 0)
        {
            return;
        }

        _collapseDelayTimer.Stop();
        e.Handled = true;

        if (isVolumeMode)
        {
            _viewModel.AdjustMasterVolume(step * 4);
            if (_viewModel.IsExpanded && !_isPointerOverSurface && _viewModel.AutoCollapse && !_viewModel.PreserveFocusPanel)
            {
                RestartCollapseDelayTimer();
            }
            return;
        }

        var now = DateTime.UtcNow.Ticks;
        if (now - _lastWheelNavTicks < WheelDebounceTicks)
        {
            return;
        }

        _lastWheelNavTicks = now;

        int currentIndex = _viewModel.SelectedPageIndex;
        if (step < 0)
        {
            int nextIndex = (currentIndex + 1) % 3;
            NavigateToPage(nextIndex, isForward: true);
        }
        else
        {
            int prevIndex = (currentIndex - 1 + 3) % 3;
            NavigateToPage(prevIndex, isForward: false);
        }

        if (!_isPointerOverSurface && _viewModel.AutoCollapse && !_viewModel.PreserveFocusPanel)
        {
            RestartCollapseDelayTimer();
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
        _targetPageIndex = toIndex;
        _viewModel.SelectedPageIndex = toIndex;
        UpdatePageDots(toIndex, animated: !ReducedMotion);

        if (ReducedMotion)
        {
            FinishPageTransition();
            return;
        }

        int direction = isForward ? 1 : -1;
        bool wasAnimating = _isPageAnimating;

        // Capture live in-flight positions and velocities for every channel (X, Opacity, Scale)
        // so mid-flight page changes redirect smoothly instead of snapping.
        for (int i = 0; i < 3; i++)
        {
            bool isTarget = i == toIndex;
            bool wasPanelActive = wasAnimating && (i == _pageAnimFromIndex || i == _pageAnimToIndex || _pageCurrOpacity[i] > 0.01);
            if (wasPanelActive)
            {
                _pageFromX[i] = _pageCurrX[i];
                _pageVelX[i] = _pageCurrVelX[i];
                _pageFromOpacity[i] = _pageCurrOpacity[i];
                _pageVelOpacity[i] = _pageCurrVelOpacity[i];
                _pageFromScale[i] = _pageCurrScale[i];
                _pageVelScale[i] = _pageCurrVelScale[i];
            }
            else
            {
                _pageFromX[i] = isTarget ? direction * PageSlideDistance : 0;
                _pageVelX[i] = 0;
                _pageFromOpacity[i] = i == fromIndex ? 1 : 0;
                _pageVelOpacity[i] = 0;
                _pageFromScale[i] = isTarget ? 0.985 : 1;
                _pageVelScale[i] = 0;
            }

            _pageCurrX[i] = _pageFromX[i];
            _pageCurrVelX[i] = _pageVelX[i];
            _pageCurrOpacity[i] = _pageFromOpacity[i];
            _pageCurrVelOpacity[i] = _pageVelOpacity[i];
            _pageCurrScale[i] = _pageFromScale[i];
            _pageCurrVelScale[i] = _pageVelScale[i];

            _pageToX[i] = isTarget ? 0 : (i == fromIndex ? -direction * PageSlideDistance : direction * PageSlideDistance);
            _pageToOpacity[i] = isTarget ? 1 : 0;
            _pageToScale[i] = isTarget ? 1 : 0.985;
        }

        for (int i = 0; i < 3; i++)
        {
            var grid = GetPanelGrid(i);
            var visual = GetPanelVisual(i);
            bool visible = i == fromIndex || i == toIndex || (wasAnimating && _pageFromOpacity[i] > 0.01);
            grid.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            visual.StopAnimation("Translation");
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Scale");
            grid.Translation = new Vector3((float)_pageFromX[i], 0, 0);
            visual.Opacity = (float)Math.Clamp(_pageFromOpacity[i], 0, 1);
            float scale = (float)_pageFromScale[i];
            visual.Scale = new Vector3(scale, scale, 1);
        }

        _pageAnimFromIndex = fromIndex;
        _pageAnimToIndex = toIndex;
        _pageAnimElapsedSec = 0.0;
        _isPageAnimating = true;
        EnsureRenderLoopRunning();
    }

    private void CancelPageTransition()
    {
        if (!_isPageAnimating) return;
        _isPageAnimating = false;
        Array.Clear(_pageCurrVelX);
        Array.Clear(_pageCurrVelOpacity);
        Array.Clear(_pageCurrVelScale);
        if (!_viewModel.IsExpanded)
        {
            int active = _viewModel.SelectedPageIndex;
            for (int i = 0; i < 3; i++)
            {
                var grid = GetPanelGrid(i);
                var visual = GetPanelVisual(i);
                bool isTarget = i == active;
                _pageCurrX[i] = 0;
                _pageCurrOpacity[i] = isTarget ? 1.0 : 0.0;
                _pageCurrScale[i] = 1.0;
                grid.Translation = Vector3.Zero;
                visual.StopAnimation("Translation");
                visual.StopAnimation("Opacity");
                visual.StopAnimation("Scale");
                visual.Scale = Vector3.One;
                visual.Opacity = isTarget ? 1f : 0f;
                grid.Visibility = isTarget ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        StopRenderLoopIfIdle();
    }

    private void StepPageTransition(double dt)
    {
        if (!_isPageAnimating)
        {
            return;
        }

        if (!_viewModel.IsExpanded)
        {
            // A collapse owns the surface now; leave the panels to its reset.
            CancelPageTransition();
            return;
        }

        _pageAnimElapsedSec += dt;
        double elapsedSec = _pageAnimElapsedSec * MotionSpeedScale;
        var pageSpring = SelectPageSpring();
        bool settled = true;

        for (int i = 0; i < 3; i++)
        {
            var grid = GetPanelGrid(i);
            if (grid.Visibility != Visibility.Visible && i != _pageAnimFromIndex && i != _pageAnimToIndex)
            {
                continue;
            }

            var visual = GetPanelVisual(i);

            var x = pageSpring.Evaluate(elapsedSec, _pageFromX[i], _pageToX[i], _pageVelX[i]);
            var opacity = pageSpring.Evaluate(
                elapsedSec,
                _pageFromOpacity[i],
                _pageToOpacity[i],
                _pageVelOpacity[i],
                DampedSpringOscillator.NormalizedPositionTolerance,
                DampedSpringOscillator.NormalizedVelocityTolerance);
            var scale = pageSpring.Evaluate(
                elapsedSec,
                _pageFromScale[i],
                _pageToScale[i],
                _pageVelScale[i],
                DampedSpringOscillator.NormalizedPositionTolerance,
                DampedSpringOscillator.NormalizedVelocityTolerance);

            _pageCurrX[i] = x.Position;
            _pageCurrVelX[i] = x.Velocity;
            _pageCurrOpacity[i] = Math.Clamp(opacity.Position, 0.0, 1.0);
            _pageCurrVelOpacity[i] = opacity.Velocity;
            _pageCurrScale[i] = Math.Clamp(scale.Position, 0.90, 1.10);
            _pageCurrVelScale[i] = scale.Velocity;

            grid.Translation = new Vector3((float)x.Position, 0, 0);
            visual.Opacity = (float)_pageCurrOpacity[i];
            float s = (float)_pageCurrScale[i];
            visual.Scale = new Vector3(s, s, 1);

            if (i != _pageAnimToIndex && _pageCurrOpacity[i] <= 0.005)
            {
                grid.Visibility = Visibility.Collapsed;
            }

            settled &= x.HasSettled && opacity.HasSettled && scale.HasSettled;
        }

        if (settled || _pageAnimElapsedSec >= 1.5)
        {
            FinishPageTransition();
        }
    }

    private void FinishPageTransition()
    {
        _isPageAnimating = false;
        Array.Clear(_pageCurrVelX);
        Array.Clear(_pageCurrVelOpacity);
        Array.Clear(_pageCurrVelScale);
        StopRenderLoopIfIdle();

        for (int i = 0; i < 3; i++)
        {
            var grid = GetPanelGrid(i);
            var visual = GetPanelVisual(i);
            bool isTarget = i == _targetPageIndex;
            _pageCurrX[i] = 0;
            _pageCurrOpacity[i] = isTarget ? 1.0 : 0.0;
            _pageCurrScale[i] = 1.0;
            grid.Translation = Vector3.Zero;
            visual.StopAnimation("Translation");
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Scale");
            visual.Scale = Vector3.One;
            grid.Visibility = isTarget ? Visibility.Visible : Visibility.Collapsed;
            visual.Opacity = isTarget ? 1f : 0f;
        }
    }

    private const double DotStripStartXDip = 5.0;

    private void UpdatePageDots(int activeIndex, bool animated)
    {
        double[] targets =
        [
            activeIndex == 0 ? 18.0 : 6.0,
            activeIndex == 1 ? 18.0 : 6.0,
            activeIndex == 2 ? 18.0 : 6.0
        ];
        double[] targetOpacities =
        [
            activeIndex == 0 ? 1.0 : 0.35,
            activeIndex == 1 ? 1.0 : 0.35,
            activeIndex == 2 ? 1.0 : 0.35
        ];

        if (WindowRoot.Resources.TryGetValue("PrimaryText", out var brushObj) && brushObj is Microsoft.UI.Xaml.Media.Brush brush)
        {
            for (int i = 0; i < 3; i++)
            {
                if (_dotVisualElements[i] is not null)
                {
                    _dotVisualElements[i].Background = brush;
                }
            }
        }

        if (!animated)
        {
            _isDotAnimating = false;
            Array.Clear(_dotCurrVelWidth);
            Array.Clear(_dotCurrVelOpacity);
            double x = DotStripStartXDip;
            for (int i = 0; i < 3; i++)
            {
                _dotCurrWidth[i] = targets[i];
                _dotTargetWidth[i] = targets[i];
                _dotCurrOpacity[i] = targetOpacities[i];
                _dotTargetOpacity[i] = targetOpacities[i];
                ApplyDotVisualState(i, x, targets[i], targetOpacities[i]);
                x += targets[i] + 5.0;
            }
            StopRenderLoopIfIdle();
            return;
        }

        for (int i = 0; i < 3; i++)
        {
            _dotStartWidth[i] = _dotCurrWidth[i];
            _dotVelWidth[i] = _isDotAnimating ? _dotCurrVelWidth[i] : 0.0;
            _dotTargetWidth[i] = targets[i];

            _dotStartOpacity[i] = _dotCurrOpacity[i];
            _dotVelOpacity[i] = _isDotAnimating ? _dotCurrVelOpacity[i] : 0.0;
            _dotTargetOpacity[i] = targetOpacities[i];
        }

        _dotAnimElapsedSec = 0.0;
        _isDotAnimating = true;
        EnsureRenderLoopRunning();
    }

    private void StepDotAnimation(double dt)
    {
        if (!_isDotAnimating)
        {
            return;
        }

        _dotAnimElapsedSec += dt;
        double springElapsedSec = _dotAnimElapsedSec * MotionSpeedScale;
        var dotSpring = SelectDotSpring();
        bool settled = true;

        for (int i = 0; i < 3; i++)
        {
            var widthState = dotSpring.Evaluate(
                springElapsedSec,
                _dotStartWidth[i],
                _dotTargetWidth[i],
                _dotVelWidth[i],
                positionTolerance: 0.1,
                velocityTolerance: 0.25);
            var opacityState = dotSpring.Evaluate(
                springElapsedSec,
                _dotStartOpacity[i],
                _dotTargetOpacity[i],
                _dotVelOpacity[i],
                DampedSpringOscillator.NormalizedPositionTolerance,
                DampedSpringOscillator.NormalizedVelocityTolerance);

            _dotCurrWidth[i] = Math.Clamp(widthState.Position, 4.0, 22.0);
            _dotCurrVelWidth[i] = widthState.Velocity;
            _dotCurrOpacity[i] = Math.Clamp(opacityState.Position, 0.0, 1.0);
            _dotCurrVelOpacity[i] = opacityState.Velocity;

            settled &= widthState.HasSettled && opacityState.HasSettled;
        }

        double x = DotStripStartXDip;
        for (int i = 0; i < 3; i++)
        {
            ApplyDotVisualState(i, x, _dotCurrWidth[i], _dotCurrOpacity[i]);
            x += _dotCurrWidth[i] + 5.0;
        }

        if (settled || _dotAnimElapsedSec >= 1.2)
        {
            _isDotAnimating = false;
            Array.Clear(_dotCurrVelWidth);
            Array.Clear(_dotCurrVelOpacity);
            double snapX = DotStripStartXDip;
            for (int i = 0; i < 3; i++)
            {
                _dotCurrWidth[i] = _dotTargetWidth[i];
                _dotCurrOpacity[i] = _dotTargetOpacity[i];
                ApplyDotVisualState(i, snapX, _dotTargetWidth[i], _dotTargetOpacity[i]);
                snapX += _dotTargetWidth[i] + 5.0;
            }
        }
    }

    private void ApplyDotVisualState(int index, double offsetX, double width, double opacity)
    {
        if (_dotVisualElements[index] is not null)
        {
            _dotVisualElements[index].Translation = new Vector3((float)offsetX, 0f, 0f);
        }

        if (_dotClipGeometries[index] is not null)
        {
            _dotClipGeometries[index].Offset = Vector2.Zero;
            _dotClipGeometries[index].Size = new Vector2((float)Math.Max(2.0, width), 5f);
            _dotClipGeometries[index].CornerRadius = new Vector2(2.5f, 2.5f);
        }

        if (_dotVisuals[index] is not null)
        {
            _dotVisuals[index].Opacity = (float)Math.Clamp(opacity, 0.0, 1.0);
        }
    }

    private void OnContextFlyoutOpening(object sender, object e)
    {
        if (DoNotDisturbMenuItem is not null)
        {
            DoNotDisturbMenuItem.IsChecked = _viewModel.DoNotDisturb;
        }

        if (StartFocusContextItem is not null)
        {
            StartFocusContextItem.Text = $"Başlat ({_viewModel.FocusDefaultMinutes} Dk)";
        }

        var showDeveloperMenus = (GetKeyState(VkShift) & 0x8000) != 0;
        var devVisibility = showDeveloperMenus ? Visibility.Visible : Visibility.Collapsed;
        if (DeveloperMenuSeparator is not null)
        {
            DeveloperMenuSeparator.Visibility = devVisibility;
        }
        if (TestDemoSubMenu is not null)
        {
            TestDemoSubMenu.Visibility = devVisibility;
        }
        if (NotificationSimulatorSubMenu is not null)
        {
            NotificationSimulatorSubMenu.Visibility = devVisibility;
        }
    }

    private void OnDoNotDisturbToggleClick(object sender, RoutedEventArgs e)
    {
        if (DoNotDisturbMenuItem is not null)
        {
            _ = _viewModel.SetDoNotDisturbAsync(DoNotDisturbMenuItem.IsChecked);
        }
    }

    private void OnToggleExpandedClick(object sender, RoutedEventArgs e) => _viewModel.ToggleExpanded();
    private void OnCollapseClick(object sender, RoutedEventArgs e) => _viewModel.Collapse();
    private void OnHideIslandClick(object sender, RoutedEventArgs e) => HideIslandAnimated();

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
            RestartCollapseDelayTimer();
        }
    }

    private void OnVolumeSliderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsNonPrimaryPress(e))
        {
            return;
        }

        _isInteractingWithControl = true;
        _collapseDelayTimer.Stop();
    }

    private void OnVolumeSliderPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isInteractingWithControl = false;
        if (IsNonPrimaryRelease(e))
        {
            return;
        }

        if (!_isPointerOverSurface && _viewModel.IsExpanded && _viewModel.AutoCollapse && !_viewModel.PreserveFocusPanel)
        {
            RestartCollapseDelayTimer();
        }
    }

    private void OnButtonPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsNonPrimaryPress(e))
        {
            return;
        }

        _isInteractingWithControl = true;
        _collapseDelayTimer.Stop();
        if (sender is FrameworkElement el) AnimateButtonScale(el, 0.92f);
        e.Handled = true;
    }

    private void OnButtonPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isInteractingWithControl = false;
        if (sender is FrameworkElement el) AnimateButtonScale(el, 1.0f);
        if (IsNonPrimaryRelease(e))
        {
            return;
        }

        e.Handled = true;

        if (!_isPointerOverSurface && _viewModel.IsExpanded && _viewModel.AutoCollapse && !_viewModel.PreserveFocusPanel)
        {
            RestartCollapseDelayTimer();
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
            anim.Duration = ScaledDuration(targetScale < 1 ? 65 : 130);
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
        if (!_glassEnabled)
        {
            _frostRefreshTimer?.Stop();
            _frostGeneration++;
            _hasFrostBitmap = false;
            if (FrostLayer is not null)
            {
                FrostLayer.Fill = null;
            }
            if (DetachedFrostLayer is not null)
            {
                DetachedFrostLayer.Fill = null;
            }
            _detachedFrostBrush = null;
        }

        UpdateFrostVisibility();
        if (_glassEnabled)
        {
            // No periodic re-capture: each capture briefly hides the window, which
            // reads as a visible blink. The blur is captured on launch/glass enable,
            // debounced TopOffset/IslandScale updates, display settles, and fullscreen returns.
            _ = RefreshFrostAsync(force: true);
        }
    }

    private void ScheduleFrostRefresh(int delayMs = 240)
    {
        if (!_glassEnabled || _isIslandHidden)
        {
            return;
        }

        if (_frostRefreshTimer is null)
        {
            _frostRefreshTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _frostRefreshTimer.IsRepeating = false;
            _frostRefreshTimer.Tick += (_, _) =>
            {
                if (_glassEnabled && !_isIslandHidden)
                {
                    _ = RefreshFrostAsync(force: true);
                }
            };
        }

        _frostRefreshTimer.Stop();
        _frostRefreshTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(80, delayMs));
        _frostRefreshTimer.Start();
    }

    private void OnWindowRootActualThemeChanged(FrameworkElement sender, object args)
    {
        if (string.Equals(_viewModel.Theme, "System", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(_viewModel.Theme))
        {
            ApplyTheme(_viewModel.Theme);
            _viewModel.RefreshThemeResources();
        }
    }

    private void UpdateFrostVisibility()
    {
        var visible = _glassEnabled ? Visibility.Visible : Visibility.Collapsed;
        FrostLayer.Visibility = visible;
        if (FrostTintLayer is not null)
        {
            FrostTintLayer.Visibility = visible;
        }
        if (FrostSheen is not null)
        {
            FrostSheen.Visibility = visible;
        }
        if (DetachedFrostLayer is not null)
        {
            DetachedFrostLayer.Visibility = visible;
        }
        if (DetachedFrostTintLayer is not null)
        {
            DetachedFrostTintLayer.Visibility = visible;
        }
        if (DetachedFrostSheen is not null)
        {
            DetachedFrostSheen.Visibility = visible;
        }
        if (_glassEnabled)
        {
            UpdateFrostTint(ComputeCurrentPalette());
        }
    }

    private GlassSurfacePalette ComputeCurrentPalette()
    {
        var isLight = DesktopFrostService.ResolveIsLightTheme(_viewModel.Theme);
        var hasFrostBitmap = _glassEnabled && _hasFrostBitmap && FrostLayer.Fill is Microsoft.UI.Xaml.Media.ImageBrush;
        return LiquidGlassEngine.ComputeIslandPalette(
            isLightTheme: isLight,
            useTransparency: _glassEnabled,
            transparencyLevel: _viewModel.TransparencyLevel,
            dynamicColorTint: _viewModel.DynamicColorTint,
            artworkAccentColor: _viewModel.ArtworkAccentColor,
            artworkTintStrength: _viewModel.ArtworkTintStrength,
            hasFrostBitmap: hasFrostBitmap);
    }

    /// <summary>Recolors the live glass tint and specular sheen without touching the captured blur.</summary>
    private void UpdateFrostTint(GlassSurfacePalette palette)
    {
        var hasBitmap = _glassEnabled && _hasFrostBitmap && FrostLayer.Fill is Microsoft.UI.Xaml.Media.ImageBrush;
        if (FrostTintLayer is not null)
        {
            FrostTintLayer.Fill = DesktopFrostService.CreateBrush(palette.FrostTintRgb);
            FrostTintLayer.Opacity = hasBitmap ? palette.FrostTintOpacity : 0.0;
        }

        if (DetachedFrostTintLayer is not null)
        {
            DetachedFrostTintLayer.Fill = DesktopFrostService.CreateBrush(palette.FrostTintRgb);
            DetachedFrostTintLayer.Opacity = hasBitmap ? palette.FrostTintOpacity : 0.0;
        }

        if (FrostSheenTopStop is not null)
        {
            FrostSheenTopStop.Color = DesktopFrostService.ToColor(palette.SheenTopArgb);
        }

        if (DetachedFrostSheenTopStop is not null)
        {
            DetachedFrostSheenTopStop.Color = DesktopFrostService.ToColor(palette.SheenTopArgb);
        }
    }

    private bool _frostBusy;

    /// <summary>Samples the desktop area under the island, blurs + tints it into the frost brush.</summary>
    private async Task RefreshFrostAsync(bool force = false)
    {
        if (_windowClosed || !_glassEnabled || _isIslandHidden || _frostBusy || FrostLayer is null)
        {
            return;
        }

        if (!force && _isPointerOverSurface)
        {
            return;
        }

        _frostBusy = true;
        var generation = ++_frostGeneration;
        var windowWasTemporarilyHidden = false;
        try
        {
            GetClientRect(_hwnd, out var crc);
            var pt = new Win32Point();
            _ = ClientToScreen(_hwnd, ref pt);
            var dpiScale = _dpiScale > 0 ? _dpiScale : 1.0;
            var surfaceInsetPx = (int)Math.Round(IslandSurfaceLayout.SurfaceTopInsetDip * dpiScale);
            var screenX = pt.X;
            var screenY = pt.Y + surfaceInsetPx;
            var clientW = Math.Max(16, crc.Right - crc.Left);
            var remainingH = Math.Max(16, (crc.Bottom - crc.Top) - surfaceInsetPx);
            var targetSurfaceH = (int)Math.Round(IslandSurfaceLayout.DefaultHostHeightDip * dpiScale);
            var clientH = Math.Max(16, Math.Min(remainingH, targetSurfaceH));

            if (clientW < 16 || clientH < 16)
            {
                return;
            }

            // Hide briefly on the UI thread (owner thread of _hwnd) so the captured pixels contain only desktop content.
            windowWasTemporarilyHidden = SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpHideWindow | SwpNoActivate | SwpNoMove | SwpNoSize);
            await Task.Delay(45);

            byte[]? pngBytes = null;
            if (!_windowClosed && !_isIslandHidden)
            {
                pngBytes = await Task.Run(() =>
                {
                    using var bmp = DesktopFrostService.CaptureScreenRegion(screenX, screenY, clientW, clientH);
                    if (bmp is null)
                    {
                        return null;
                    }

                    return DesktopFrostService.ProcessCapturedFrostToPng(bmp, dpiScale, blurRadius: 11, vibrancyBoost: 1.24);
                });
            }

            if (windowWasTemporarilyHidden && !_windowClosed && !_isIslandHidden)
            {
                _frostRestoreGuardTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                _ = SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpShowWindow | SwpNoActivate | SwpNoMove | SwpNoSize);
                windowWasTemporarilyHidden = false;
            }

            if (_windowClosed || pngBytes is null || generation != _frostGeneration || _isIslandHidden || !_glassEnabled || FrostLayer is null)
            {
                return;
            }

            var image = await DesktopFrostService.DecodePngToBitmapImageAsync(pngBytes);
            if (_windowClosed || generation != _frostGeneration || _isIslandHidden || !_glassEnabled || FrostLayer is null)
            {
                return;
            }

            FrostLayer.Fill = new Microsoft.UI.Xaml.Media.ImageBrush
            {
                ImageSource = image,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.None,
                AlignmentX = Microsoft.UI.Xaml.Media.AlignmentX.Center,
                AlignmentY = Microsoft.UI.Xaml.Media.AlignmentY.Top
            };
            if (DetachedFrostLayer is not null)
            {
                _detachedFrostTransform ??= new Microsoft.UI.Xaml.Media.TranslateTransform();
                _detachedFrostBrush = new Microsoft.UI.Xaml.Media.ImageBrush
                {
                    ImageSource = image,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.None,
                    AlignmentX = Microsoft.UI.Xaml.Media.AlignmentX.Left,
                    AlignmentY = Microsoft.UI.Xaml.Media.AlignmentY.Top,
                    Transform = _detachedFrostTransform
                };
                DetachedFrostLayer.Fill = _detachedFrostBrush;
                UpdateDetachedFrostTransform(_currentTrailingX);
            }
            _hasFrostBitmap = true;
            ApplyTheme(_viewModel.Theme);
        }
        catch (Exception ex)
        {
            CrashLogger.Write("IslandWindow.RefreshFrostAsync", ex);
        }
        finally
        {
            if (windowWasTemporarilyHidden && !_windowClosed && !_isIslandHidden && _hwnd != IntPtr.Zero)
            {
                try
                {
                    _frostRestoreGuardTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                    _ = SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpShowWindow | SwpNoActivate | SwpNoMove | SwpNoSize);
                }
                catch
                {
                }
            }

            _frostBusy = false;
        }
    }

    public void ApplyTheme(string theme)
    {
        var palette = ComputeCurrentPalette();
        var requestedTheme = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase)
            ? ElementTheme.Dark
            : string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
                ? ElementTheme.Light
                : ElementTheme.Default;

        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "IslandBackground", palette.FallbackBackgroundArgb);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "IslandSolidSurface", 0xFF000000u | (palette.FallbackBackgroundArgb & 0x00FFFFFFu));
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "IslandBorder", palette.BorderRimTopArgb);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "PrimaryText", palette.PrimaryTextArgb);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SecondaryText", palette.SecondaryTextArgb);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "PillControlBackground", palette.PillControlBackgroundArgb);

        var sliderFill = palette.IsLightTheme ? 0xFF141518u : 0xFFFFFFFFu;
        var sliderFillPressed = palette.IsLightTheme ? 0xFF3A3A3Cu : 0xE6FFFFFFu;
        var sliderTrack = palette.IsLightTheme ? 0x24000000u : 0x26FFFFFFu;
        var sliderTrackHover = palette.IsLightTheme ? 0x33000000u : 0x36FFFFFFu;
        var sliderThumb = palette.IsLightTheme ? 0xFF141518u : 0xFFFFFFFFu;
        var sliderThumbPressed = palette.IsLightTheme ? 0xFF3A3A3Cu : 0xE6FFFFFFu;

        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SliderTrackValueFill", sliderFill);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SliderTrackValueFillPointerOver", sliderFill);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SliderTrackValueFillPressed", sliderFillPressed);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SliderTrackFill", sliderTrack);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SliderTrackFillPointerOver", sliderTrackHover);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SliderTrackFillPressed", sliderTrackHover);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SliderThumbBackground", sliderThumb);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SliderThumbBackgroundPointerOver", sliderThumb);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SliderThumbBackgroundPressed", sliderThumbPressed);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "PlayButtonBackground", palette.PlayButtonBackgroundArgb);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "PlayButtonForeground", palette.PlayButtonForegroundArgb);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "Accent", palette.AccentArgb);
        DesktopFrostService.UpdateBrush(WindowRoot.Resources, "SeekAccent", palette.AccentArgb);

        if (WindowRoot.RequestedTheme != requestedTheme)
        {
            WindowRoot.RequestedTheme = requestedTheme;
        }

        PillBackdrop.Background = DesktopFrostService.CreateBrush(palette.SurfaceBackgroundArgb);
        PillBackdrop.BorderBrush = DesktopFrostService.CreateBrush(0x00000000u);
        PillBackdrop.BorderThickness = new Thickness(0);

        if (_frostRimShape is not null)
        {
            _frostRimShape.StrokeBrush = DesktopFrostService.CreateCompositionVerticalGradient(
                _pillMaskVisual.Compositor,
                palette.BorderRimTopArgb,
                palette.BorderRimBottomArgb);
        }

        if (HiddenAnchorPill is not null)
        {
            HiddenAnchorPill.Background = DesktopFrostService.CreateBrush(palette.HiddenAnchorArgb);
        }

        var hasFrostBitmap = _glassEnabled && _hasFrostBitmap && FrostLayer.Fill is Microsoft.UI.Xaml.Media.ImageBrush;
        if (DetachedPill is not null)
        {
            DetachedPill.Background = DesktopFrostService.CreateBrush(
                hasFrostBitmap ? palette.SurfaceBackgroundArgb : palette.DetachedTintArgb);
            DetachedPill.BorderBrush = DesktopFrostService.CreateVerticalGradientBrush(palette.BorderRimTopArgb, palette.BorderRimBottomArgb);
            DetachedPill.BorderThickness = new Thickness(1);
            if (DetachedFrostSheenTopStop is not null)
            {
                DetachedFrostSheenTopStop.Color = DesktopFrostService.ToColor(palette.SheenTopArgb);
            }
        }

        WaveBar1.Background = DesktopFrostService.CreateBrush(palette.AccentArgb);
        WaveBar2.Background = DesktopFrostService.CreateBrush(palette.AccentArgb);
        WaveBar3.Background = DesktopFrostService.CreateBrush(palette.AccentArgb);
        if (ExpandedSeekBar is not null)
        {
            ExpandedSeekBar.AccentBrush = DesktopFrostService.CreateBrush(palette.AccentArgb);
            ExpandedSeekBar.TrackBackgroundBrush = DesktopFrostService.CreateBrush(palette.IsLightTheme ? 0x29000000u : 0x2DFFFFFFu);
            ExpandedSeekBar.ThumbBrush = DesktopFrostService.CreateBrush(palette.IsLightTheme ? 0xFF141518u : 0xFFFFFFFFu);
        }

        if (_isDotAnimating)
        {
            UpdatePageDotBrushes();
        }
        else
        {
            UpdatePageDots(_viewModel.SelectedPageIndex, animated: false);
        }

        // The glass tint is a live overlay on the captured blur, so track/theme/slider
        // changes recolor it instantly without a desktop re-capture (no blink).
        UpdateFrostTint(palette);
    }

    private void UpdatePageDotBrushes()
    {
        if (WindowRoot.Resources.TryGetValue("PrimaryText", out var brushObj) && brushObj is Microsoft.UI.Xaml.Media.Brush brush)
        {
            for (int i = 0; i < 3; i++)
            {
                if (_dotVisualElements[i] is not null)
                {
                    _dotVisualElements[i].Background = brush;
                }
            }
        }
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

    // Subclass WndProc to handle native hit-testing, DPI/display changes, and eliminate OS window minimum size constraints
    private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        const uint WM_GETMINMAXINFO = 0x0024;
        const uint WM_SETTINGCHANGE = 0x001A;
        const uint WM_WINDOWPOSCHANGING = 0x0046;
        const uint WM_DISPLAYCHANGE = 0x007E;
        const uint WM_NCHITTEST = 0x0084;
        const uint WM_WTSSESSION_CHANGE = 0x02B1;
        const uint WM_DPICHANGED = 0x02E0;
        const uint WM_HOTKEY = 0x0312;
        const uint SwpNoZOrder = 0x0004;
        const int WtsSessionLock = 0x7;
        const int WtsSessionUnlock = 0x8;

        if (uMsg == WM_WTSSESSION_CHANGE)
        {
            var status = wParam.ToInt32();
            if (status == WtsSessionLock)
            {
                _sessionLocked = true;
                _topmostGuardTimer?.Stop();
                _fullscreenPollTimer?.Stop();
                _viewModel.SetSessionLocked(true);
            }
            else if (status == WtsSessionUnlock)
            {
                _sessionLocked = false;
                if (!_windowClosed)
                {
                    _topmostGuardTimer?.Start();
                    UpdateBackgroundPollTimerState();
                    _viewModel.SetSessionLocked(false);
                    ScheduleDisplaySettle();
                }
            }
            return IntPtr.Zero;
        }

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

        if (uMsg == WM_DPICHANGED)
        {
            double dpiFromMsg = IslandSurfaceLayout.ExtractDpiScaleFromWParam(wParam);
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (_windowClosed) return;
                PositionHostWindow(dpiFromMsg);
                _lastWindowRegion = null;
                UpdateWindowRegion(_currentPillWidth, _currentPillHeight, _currentCornerRadius > 0 ? _currentCornerRadius : 20.0, forceRedraw: true);
                ScheduleDisplaySettle();
            });
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        if (uMsg == WM_DISPLAYCHANGE || (uMsg == WM_SETTINGCHANGE && wParam.ToInt32() == 0x002F))
        {
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (_windowClosed) return;
                PositionHostWindow();
                ScheduleDisplaySettle();
            });
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
                    ShowIsland();
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

            var (screenX, screenY) = IslandSurfaceLayout.ExtractSignedScreenCoordinates(lParam);

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
        double localDipY = ((screenY - winRect.Top) / _dpiScale) - IslandSurfaceLayout.SurfaceTopInsetDip;

        bool isSplit = _viewModel.IsSplitModeActive && !_viewModel.IsExpanded && !_viewModel.IsCollapsing;
        double pillWidth = _currentPillWidth > 0 ? _currentPillWidth : 216.0;
        double pillHeight = _currentPillHeight > 0 ? _currentPillHeight : 40.0;
        double detachedWidth = (DetachedPill is not null && DetachedPill.Width > 0)
            ? DetachedPill.Width
            : IslandSurfaceLayout.DetachedPillWidthDip;
        double detachedHeight = (DetachedPill is not null && DetachedPill.Height > 0)
            ? DetachedPill.Height
            : IslandSurfaceLayout.DetachedPillHeightDip;

        return IslandSurfaceLayout.IsPointInsideIsland(
            localDipX: localDipX,
            localDipY: localDipY,
            hostWidth: IslandSurfaceLayout.DefaultHostWidthDip,
            isHidden: _isIslandHidden,
            pillWidth: pillWidth,
            pillHeight: pillHeight,
            surfaceOffsetX: _currentLeadingX,
            isSplit: isSplit,
            detachedWidth: detachedWidth,
            detachedHeight: detachedHeight,
            detachedOffsetX: _currentTrailingX);
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
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

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
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int VkShift = 0x10;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private const int RGN_OR = 2;

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point pt);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Win32Point pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    private const uint NotifyForThisSession = 0;

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, uint dwFlags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private const int MdtEffectiveDpi = 0;

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

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
