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
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace Pulse.Views.Island;

public sealed partial class IslandWindow : Window
{
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
    private const int ExpandedPageCount = IslandViewModel.PageCount;
    private int ActivePageCount => _viewModel.VisiblePageCount;

    private readonly Visual _mediaPanelVisual;
    private readonly Visual _focusPanelVisual;
    private readonly Visual _systemPanelVisual;
    private readonly Visual _shelfPanelVisual;
    private readonly Visual _devPanelVisual;
    private readonly Visual _detachedVisual;
    private readonly Visual _batteryChargeHaloVisual;
    private readonly Visual _headphoneBeaconHaloVisual;
    private readonly Border[] _dotVisualElements;
    private readonly Visual[] _dotVisuals = new Visual[ExpandedPageCount];
    private readonly CompositionRoundedRectangleGeometry[] _dotClipGeometries = new CompositionRoundedRectangleGeometry[ExpandedPageCount];
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
    private readonly double[] _pageFromX = new double[ExpandedPageCount];
    private readonly double[] _pageToX = new double[ExpandedPageCount];
    private readonly double[] _pageVelX = new double[ExpandedPageCount];
    private readonly double[] _pageCurrX = new double[ExpandedPageCount];
    private readonly double[] _pageCurrVelX = new double[ExpandedPageCount];
    private readonly double[] _pageFromOpacity = new double[ExpandedPageCount];
    private readonly double[] _pageToOpacity = new double[ExpandedPageCount];
    private readonly double[] _pageVelOpacity = new double[ExpandedPageCount];
    private readonly double[] _pageCurrOpacity = new double[ExpandedPageCount];
    private readonly double[] _pageCurrVelOpacity = new double[ExpandedPageCount];
    private readonly double[] _pageFromScale = new double[ExpandedPageCount];
    private readonly double[] _pageToScale = new double[ExpandedPageCount];
    private readonly double[] _pageVelScale = new double[ExpandedPageCount];
    private readonly double[] _pageCurrScale = new double[ExpandedPageCount];
    private readonly double[] _pageCurrVelScale = new double[ExpandedPageCount];

    private bool _isDotAnimating;
    private double _dotAnimElapsedSec;
    private readonly double[] _dotStartWidth = [18.0, 6.0, 6.0, 6.0, 6.0];
    private readonly double[] _dotTargetWidth = [18.0, 6.0, 6.0, 6.0, 6.0];
    private readonly double[] _dotVelWidth = new double[ExpandedPageCount];
    private readonly double[] _dotCurrWidth = [18.0, 6.0, 6.0, 6.0, 6.0];
    private readonly double[] _dotCurrVelWidth = new double[ExpandedPageCount];
    private readonly double[] _dotStartOpacity = [1.0, 0.35, 0.35, 0.35, 0.35];
    private readonly double[] _dotTargetOpacity = [1.0, 0.35, 0.35, 0.35, 0.35];
    private readonly double[] _dotVelOpacity = new double[ExpandedPageCount];
    private readonly double[] _dotCurrOpacity = [1.0, 0.35, 0.35, 0.35, 0.35];
    private readonly double[] _dotCurrVelOpacity = new double[ExpandedPageCount];

    // Drop Shelf ("Raf") drag-in / drag-out physics & overlay state
    private bool _isDragHovering;
    private bool _isDraggingOut;
    private bool _wasExpandedBeforeDrag;
    private bool _isPickingShelfFile;
    private bool _isSubmittingQuickNote;
    private DispatcherQueueTimer? _dragLeaveTimer;

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
    private readonly long _launchTicks = System.Diagnostics.Stopwatch.GetTimestamp();
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
    private bool _headphoneBeaconAnimating;
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
    private bool _isContextMenuOpen;
    private double _appliedCompactScale = double.NaN;
    private readonly char[] _classNameBuffer = new char[64];
    private readonly char[] _titleBuffer = new char[256];
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

    private Microsoft.UI.Xaml.Media.LinearGradientBrush? _ambientBrush;
    private Microsoft.UI.Xaml.Media.GradientStop? _ambientStartStop;
    private Microsoft.UI.Xaml.Media.GradientStop? _ambientMidStop;
    private Microsoft.UI.Xaml.Media.GradientStop? _ambientEndStop;

    private double _currentAmbientR;
    private double _currentAmbientG;
    private double _currentAmbientB;
    private double _currentAmbientAlpha;

    private double _targetAmbientR;
    private double _targetAmbientG;
    private double _targetAmbientB;
    private double _targetAmbientAlpha;

    private bool _isAmbientColorAnimating;
    private bool _isAmbientTargetDynamicMusic;

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
        _shelfPanelVisual = ElementCompositionPreview.GetElementVisual(ShelfExpandedPanel);
        _devPanelVisual = ElementCompositionPreview.GetElementVisual(DevExpandedPanel);
        _detachedVisual = ElementCompositionPreview.GetElementVisual(DetachedPill);
        _batteryChargeHaloVisual = ElementCompositionPreview.GetElementVisual(BatteryChargeHalo);
        _headphoneBeaconHaloVisual = ElementCompositionPreview.GetElementVisual(HeadphoneBeaconHalo);

        _dotVisualElements = [PageDotVisual0, PageDotVisual1, PageDotVisual2, PageDotVisual3, PageDotVisual4];
        for (int i = 0; i < ExpandedPageCount; i++)
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
        ElementCompositionPreview.SetIsTranslationEnabled(ShelfExpandedPanel, true);
        ElementCompositionPreview.SetIsTranslationEnabled(DevExpandedPanel, true);
        ElementCompositionPreview.SetIsTranslationEnabled(ShelfDropBobIcon, true);
        ElementCompositionPreview.SetIsTranslationEnabled(DetachedPill, true);

        _dragLeaveTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _dragLeaveTimer.IsRepeating = false;
        _dragLeaveTimer.Interval = TimeSpan.FromMilliseconds(180);
        _dragLeaveTimer.Tick += OnDragLeaveTimerTick;

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
        ApplyGlass(deferCapture: true);
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
        UpdateHeadphoneBeaconAnimation();

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

    private void ResetDetachedVisualOpacity(float? opacity = 1f)
    {
        if (DetachedPill is null)
        {
            return;
        }

        try
        {
            _detachedVisual.StopAnimation("Opacity");
            if (opacity.HasValue)
            {
                _detachedVisual.Opacity = opacity.Value;
            }
        }
        catch { }
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
            _viewModel.EventRouter.SetPresentationPaused(_sessionLocked);
            ResetDetachedVisualOpacity(1f);
            _pillMaskVisual.StopAnimation("Opacity");
            _pillMaskVisual.StopAnimation("Scale");
            _pillMaskVisual.StopAnimation("Translation");
            _pillMaskVisual.Opacity = 1f;
            _pillMaskVisual.Scale = Vector3.One;
            PillMaskHost.Translation = Vector3.Zero;
            Surface.Visibility = Visibility.Visible;
            if (DetachedPill is not null)
            {
                bool showDetached = _viewModel.IsSplitModeActive && !_viewModel.IsExpanded && !_viewModel.IsCollapsing;
                DetachedPill.Visibility = showDetached ? Visibility.Visible : Visibility.Collapsed;
                DetachedPill.Opacity = showDetached ? 1.0 : 0.0;
            }
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
        _viewModel.EventRouter.SetPresentationPaused(true);

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
                ResetDetachedVisualOpacity(1f);
                DetachedPill.Visibility = Visibility.Collapsed;
            }

            if (_viewModel.IsExpanded)
            {
                _viewModel.Collapse();
            }
            if (_viewModel.CurrentIslandState == IslandState.Collapsing)
            {
                _viewModel.CompleteCollapse();
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
                        ResetDetachedVisualOpacity(1f);
                        DetachedPill.Visibility = Visibility.Collapsed;
                    }

                    if (_viewModel.IsExpanded)
                    {
                        _viewModel.Collapse();
                    }
                    if (_viewModel.CurrentIslandState == IslandState.Collapsing)
                    {
                        _viewModel.CompleteCollapse();
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
                        _lastWindowRegion = null;
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

        if (_viewModel.IsExpanded && !_isPointerOverSurface && _viewModel.AutoCollapse && !_viewModel.PreserveFocusPanel)
        {
            _viewModel.Collapse();
        }
        if (_viewModel.CurrentIslandState == IslandState.Collapsing)
        {
            _viewModel.CompleteCollapse();
        }

        // Clear any leftover Composition hide opacity override before recalculating layout and resuming alerts.
        ResetDetachedVisualOpacity(1f);
        _viewModel.EventRouter.SetPresentationPaused(_sessionLocked);

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
            ResetDetachedVisualOpacity(1f);
            if (DetachedPill is not null)
            {
                bool showDetached = _viewModel.IsSplitModeActive && !_viewModel.IsExpanded && !_viewModel.IsCollapsing;
                DetachedPill.Visibility = showDetached ? Visibility.Visible : Visibility.Collapsed;
                DetachedPill.Opacity = showDetached ? 1.0 : 0.0;
            }
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
            if (DetachedPill is not null && DetachedPill.Visibility == Visibility.Visible)
            {
                _detachedVisual.StartAnimation("Opacity", opacityAnim);
            }
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

        if (_isDimensionAnimating)
        {
            ResetDetachedVisualOpacity((float)Math.Clamp(_currentDetachedOpacity, 0.0, 1.0));
        }
        else
        {
            ResetDetachedVisualOpacity(1f);
            if (DetachedPill is not null)
            {
                bool showDetached = _viewModel.IsSplitModeActive && !_viewModel.IsExpanded && !_viewModel.IsCollapsing;
                DetachedPill.Visibility = showDetached ? Visibility.Visible : Visibility.Collapsed;
                DetachedPill.Opacity = showDetached ? 1.0 : 0.0;
            }
        }

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
        if (_headphoneBeaconAnimating)
        {
            _headphoneBeaconAnimating = false;
            try
            {
                _headphoneBeaconHaloVisual.StopAnimation("Opacity");
                _headphoneBeaconHaloVisual.StopAnimation("Scale");
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
        UpdateHeadphoneBeaconAnimation();
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
            SetTextInputActivationEnabled(false);
            HideIslandAnimated();
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _windowClosed = true;
        _topmostGuardTimer?.Stop();
        _hideAnimationTimer?.Stop();
        _unhideRecoveryTimer?.Stop();
        _displaySettleTimer?.Stop();
        _frostRefreshTimer?.Stop();
        _dragLeaveTimer?.Stop();
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

    public Task<bool> ExportSurfaceSnapshotAsync(string outputPath)
    {
        var pad = 28.0;
        var hostW = IslandSurfaceLayout.DefaultHostWidthDip;
        var pillW = Math.Max(120.0, _currentPillWidth);
        var pillH = Math.Max(36.0, _currentPillHeight);

        double left = (hostW - pillW) / 2.0 + _currentLeadingX;
        double right = left + pillW;

        if (_viewModel.IsSplitModeActive && !_viewModel.IsExpanded && !_viewModel.IsCollapsing && DetachedPill is not null)
        {
            var detW = DetachedPill.Width > 0 ? DetachedPill.Width : IslandSurfaceLayout.DetachedPillWidthDip;
            var detH = DetachedPill.Height > 0 ? DetachedPill.Height : IslandSurfaceLayout.DetachedPillHeightDip;
            var detLeft = (hostW - detW) / 2.0 + _currentTrailingX;
            left = Math.Min(left, detLeft);
            right = Math.Max(right, detLeft + detW);
            pillH = Math.Max(pillH, detH);
        }

        var crop = (
            X: Math.Max(0, left - pad),
            Y: 0.0,
            Width: Math.Min(hostW - Math.Max(0, left - pad), (right - left) + pad * 2.0),
            Height: Math.Min(WindowRoot.ActualHeight > 0 ? WindowRoot.ActualHeight : 306.0, IslandSurfaceLayout.SurfaceTopInsetDip + pillH + pad));

        return DesktopFrostService.RenderElementToPngAsync(WindowRoot, outputPath, crop, compositeBackdropArgb: 0xFF12141Au);
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
        else if (e.PropertyName == nameof(IslandViewModel.IsHeadphonesConnected))
        {
            if (!_isIslandHidden || _hideAnchorVisible)
            {
                UpdateHeadphoneBeaconAnimation();
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
        else if (e.PropertyName == nameof(IslandViewModel.SelectedPageIndex))
        {
            if (_targetPageIndex != _viewModel.SelectedPageIndex)
            {
                NavigateToPage(_viewModel.SelectedPageIndex, _viewModel.SelectedPageIndex > _targetPageIndex);
            }
        }
        else if (e.PropertyName == nameof(IslandViewModel.VisiblePageCount))
        {
            UpdatePageDots(_viewModel.SelectedPageIndex, animated: false);
        }
        else if (e.PropertyName == nameof(IslandViewModel.IsQuickNoteOpen))
        {
            SetTextInputActivationEnabled(_viewModel.IsQuickNoteOpen);
            if (_viewModel.IsQuickNoteOpen)
            {
                _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                {
                    if (!_windowClosed && _viewModel.IsQuickNoteOpen && QuickNoteInputBox is not null)
                    {
                        QuickNoteInputBox.Focus(FocusState.Programmatic);
                    }
                });
            }
        }
        else if (e.PropertyName is nameof(MediaViewModel.Theme) 
                 or nameof(MediaViewModel.ArtworkAccentColor) 
                 or nameof(MediaViewModel.DynamicColorTint)
                 or nameof(IslandViewModel.ArtworkAccentColor)
                 or nameof(IslandViewModel.DynamicColorTint)
                 or nameof(IslandViewModel.HasArtwork)
                 or nameof(IslandViewModel.HasTrack)
                 or nameof(IslandViewModel.ArtworkImage))
        {
            // Theme/accent changes only recolor the glass; the captured blur is
            // desktop-only and stays valid, so never re-capture here (no blink).
            ApplyTheme(_viewModel.Theme);

            if (e.PropertyName is nameof(IslandViewModel.ArtworkImage) or nameof(MediaViewModel.ArtworkImage))
            {
                PlayArtworkTransitionAnimation();
            }
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
                UpdateStateLayout(_viewModel.CurrentIslandState, animated: !_isIslandHidden);
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
                    UpdateStateLayout(_viewModel.CurrentIslandState, animated: !_isIslandHidden);
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
                UpdateStateLayout(_viewModel.CurrentIslandState, animated: !_isIslandHidden);
            }
        }
        else if (e.PropertyName == nameof(IslandViewModel.IsFileOperationActive))
        {
            var isFile = _viewModel.IsFileOperationActive;
            if (_lastFileOpActive != isFile)
            {
                _lastFileOpActive = isFile;
                UpdateStateLayout(_viewModel.CurrentIslandState, animated: !_isIslandHidden);
            }
        }
        else if (e.PropertyName is nameof(IslandViewModel.FocusProgress) or nameof(IslandViewModel.PreserveFocusPanel))
        {
            UpdateFocusProgressFill();
        }
        else if (e.PropertyName == nameof(IslandViewModel.DoNotDisturb) && DoNotDisturbMenuItem is not null)
        {
            DoNotDisturbMenuItem.IsChecked = _viewModel.DoNotDisturb;
        }
        else if (e.PropertyName == nameof(MediaViewModel.FocusDefaultMinutes) && StartFocusContextItem is not null)
        {
            StartFocusContextItem.Text = $"Başlat ({_viewModel.FocusDefaultMinutes} Dk)";
        }
        else if (e.PropertyName == nameof(IslandViewModel.IsMicMuted) && MicMuteContextItem is not null)
        {
            MicMuteContextItem.Text = _viewModel.IsMicMuted ? "Mikrofonu Aç" : "Mikrofonu Sessize Al";
        }
        else if (e.PropertyName == nameof(MediaViewModel.AutoCollapseDelayMs) && _collapseDelayTimer.IsRunning)
        {
            RestartCollapseDelayTimer();
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
        CompactArtFallback.CornerRadius = new CornerRadius(m.CompactArtCornerRadius);
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
}
