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
    private readonly Visual _pillMaskVisual;
    private readonly Visual _frostSheenVisual;
    private CompositionRoundedRectangleGeometry? _surfaceClipGeometry;
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
    private readonly Button[] _dotButtons;
    private readonly DampedSpringOscillator _springMinimal = new(dampingRatio: 0.95, responseSeconds: 0.25);
    private readonly DampedSpringOscillator _springNormal = new(dampingRatio: 0.82, responseSeconds: 0.34);
    private readonly DampedSpringOscillator _springDynamic = new(dampingRatio: 0.60, responseSeconds: 0.40);
    private readonly DampedSpringOscillator _pageSpring = new(dampingRatio: 0.90, responseSeconds: 0.30);
    private readonly DampedSpringOscillator _dotSpring = new(dampingRatio: 0.86, responseSeconds: 0.24);
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
    private int _displaySettlePassesRemaining;
    private long _hideShowGeneration;
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
        _fullscreenPollTimer.Start();

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
        _dotButtons = [PageDot0, PageDot1, PageDot2];
        for (int i = 0; i < 3; i++)
        {
            ElementCompositionPreview.SetIsTranslationEnabled(_dotVisualElements[i], true);
            ElementCompositionPreview.SetIsTranslationEnabled(_dotButtons[i], true);
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
        Closed += OnWindowClosed;
        UpdateBatteryChargingAnimation();

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

        try
        {
            var compositor = _pillMaskVisual.Compositor;
            var duration = TimeSpan.FromMilliseconds(180);
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(1f, 1f));

            _pillMaskVisual.CenterPoint = new Vector3((float)(IslandSurfaceLayout.DefaultHostWidthDip / 2.0), 0f, 0f);

            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.Duration = duration;
            scaleAnim.InsertKeyFrame(1f, new Vector3(0.6f, 0.4f, 1f), easing);

            var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
            opacityAnim.Duration = duration;
            opacityAnim.InsertKeyFrame(1f, 0f, easing);

            var translateAnim = compositor.CreateVector3KeyFrameAnimation();
            translateAnim.Duration = duration;
            translateAnim.InsertKeyFrame(1f, new Vector3(0f, -18f, 0f), easing);

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
            timer.Interval = TimeSpan.FromMilliseconds(190);
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
            var duration = TimeSpan.FromMilliseconds(320);
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));

            _pillMaskVisual.CenterPoint = new Vector3((float)(IslandSurfaceLayout.DefaultHostWidthDip / 2.0), 0f, 0f);

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
            translateAnim.InsertKeyFrame(0f, new Vector3(0f, -18f, 0f));
            translateAnim.InsertKeyFrame(1f, Vector3.Zero, easing);

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            _pillMaskVisual.StartAnimation("Scale", scaleAnim);
            _pillMaskVisual.StartAnimation("Opacity", opacityAnim);
            _pillMaskVisual.StartAnimation("Translation", translateAnim);
            batch.End();
            batch.Completed += (_, _) =>
            {
                _ = DispatcherQueue.TryEnqueue(() => FinalizeUnhideSurface(gen));
            };

            // Fallback timer ensures surface never stays hidden if DWM drops a composition batch on mode switch.
            if (_unhideRecoveryTimer is null)
            {
                _unhideRecoveryTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
                _unhideRecoveryTimer.IsRepeating = false;
                _unhideRecoveryTimer.Interval = TimeSpan.FromMilliseconds(390);
                _unhideRecoveryTimer.Tick += (_, _) => FinalizeUnhideSurface(_hideShowGeneration);
            }
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
        if (_isIslandHidden || generation != _hideShowGeneration)
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
        if (_viewModel.IsPlaying)
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
        var classBuffer = new char[64];
        var length = GetClassName(foreground, classBuffer, classBuffer.Length);
        if (length > 0)
        {
            className = new string(classBuffer, 0, length);
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

    private void PollFullscreenState()
    {
        var rawIsFullscreen = false;
        try
        {
            if (SHQueryUserNotificationState(out var state) == 0)
            {
                rawIsFullscreen = FullscreenNotificationState.IsFullscreen(state);
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
            return;
        }

        if (isFullscreen && !_isIslandHidden && !_userOverrodeFullscreenHide)
        {
            _autoHiddenForFullscreen = true;
            HideIslandAnimated(showAnchor: false);
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
        _topmostGuardTimer?.Stop();
        _hideAnimationTimer?.Stop();
        _unhideRecoveryTimer?.Stop();
        _displaySettleTimer?.Stop();
        _collapseDelayTimer.Stop();
        _fullscreenPollTimer?.Stop();
        _hoverExpandTimer?.Stop();
        _longPressTimer?.Stop();
        _isDimensionAnimating = false;
        _isPageAnimating = false;
        _isDotAnimating = false;
        StopRenderLoop();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        if (_hwnd != IntPtr.Zero)
        {
            _ = UnregisterHotKey(_hwnd, HotkeyToggleIsland);
            _ = UnregisterHotKey(_hwnd, HotkeyToggleFocus);
            _ = RemoveWindowSubclass(_hwnd, _subclassProc, UIntPtr.Zero);
        }
    }

    private const double BaseWindowTopGap = 14.0;

    private void PositionHostWindow()
    {
        ApplyHostWindowSize(_currentPillWidth, _currentPillHeight);
        UpdateWindowRegion(_currentPillWidth, _currentPillHeight, _currentCornerRadius > 0 ? _currentCornerRadius : 20.0);
    }

    private double _hostPillWidth = 216;
    private double _hostPillHeight = 40;

    /// <summary>
    /// Fixed transparent host footprint (560x300 DIP): big enough for expanded/split capsules
    /// and dynamic spring overshoot at any pill scale. Transitions never resize the native HWND;
    /// SetWindowRgn + WM_NCHITTEST crop input/hit-testing to the visible pill bounds.
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

        _hostPillWidth = Math.Max(120.0, pillWidth);
        _hostPillHeight = Math.Max(36.0, pillHeight);

        var winWidth = (int)Math.Round(IslandSurfaceLayout.DefaultHostWidthDip * _dpiScale);
        var winHeight = (int)Math.Round(IslandSurfaceLayout.DefaultHostHeightDip * _dpiScale);

        var x = workArea.Left + Math.Max(0, (workArea.Right - workArea.Left - winWidth) / 2);
        var effectiveTopOffset = Math.Max(BaseWindowTopGap, _viewModel.TopOffset + BaseWindowTopGap);
        var y = workArea.Top + (int)Math.Round(effectiveTopOffset * _dpiScale);

        // Startup / DPI / display / settings changes only; transitions never resize the native window.
        // Always invoke SetWindowPos directly on the UI owner thread so DWM mode-switches never race.
        _ = SetWindowPos(_hwnd, HwndTopmost, x, y, winWidth, winHeight, SwpNoActivate);

        // Window moves under the cursor can synthesize pointer exit/enter pairs in WinUI;
        // suppress them briefly so hover state cannot oscillate with the resize itself.
        _hostResizeGuardTicks = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Crops the native window to the current pill with a Win32 region. Areas outside the
    /// region are not part of the window, so clicks pass through to whatever is below -
    /// while the window itself keeps a fixed size and never causes resize hitches.
    /// </summary>
    private void UpdateWindowRegion(double pillWidth, double pillHeight, double cornerRadius, bool includeOvershootPadding = false, bool forceRedraw = false)
    {
        if (_hwnd == IntPtr.Zero) return;

        var dpi = _dpiScale > 0 ? _dpiScale : 1.0;

        if (_isIslandHidden)
        {
            if (!_hideAnchorVisible)
            {
                _lastWindowRegion = null;
                var emptyRgn = CreateRectRgn(0, 0, 0, 0);
                if (emptyRgn != IntPtr.Zero)
                {
                    _ = SetWindowRgn(_hwnd, emptyRgn, false);
                }
                return;
            }

            var anchorBounds = IslandSurfaceLayout.ComputeCenteredPillBounds(
                IslandSurfaceLayout.DefaultHostWidthDip,
                IslandSurfaceLayout.HiddenAnchorWidthDip,
                IslandSurfaceLayout.HiddenAnchorHeightDip);
            int aLeft = (int)Math.Round(anchorBounds.Left * dpi);
            int aRight = (int)Math.Round(anchorBounds.Right * dpi);
            int aBottom = (int)Math.Round(anchorBounds.Bottom * dpi);
            int aCorner = (int)Math.Round(12.0 * dpi);

            var anchorKey = (aLeft, 0, aRight + 1, aBottom + 1, aCorner, 0, 0, 0, 0, 0);
            if (_lastWindowRegion == anchorKey) return;
            _lastWindowRegion = anchorKey;

            var anchorRgn = CreateRoundRectRgn(aLeft, 0, aRight + 1, aBottom + 1, aCorner, aCorner);
            if (anchorRgn != IntPtr.Zero)
            {
                _ = SetWindowRgn(_hwnd, anchorRgn, forceRedraw);
            }
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

        if (_isDimensionAnimating && (isSplit || isDetachedActive))
        {
            double splitTotalSpan = (196.0 * Math.Max(1.0, _viewModel.IslandScale))
                + IslandSurfaceLayout.SplitGapDip
                + detachedW;
            var regionWidth = Math.Min(IslandSurfaceLayout.DefaultHostWidthDip, Math.Max(effectiveWidth, splitTotalSpan + (includeOvershootPadding ? 16.0 : 0.0)));
            var regionHeight = Math.Min(IslandSurfaceLayout.DefaultHostHeightDip, Math.Max(effectiveHeight, detachedH));
            var spanBounds = IslandSurfaceLayout.ComputeCenteredPillBounds(IslandSurfaceLayout.DefaultHostWidthDip, regionWidth, regionHeight);
            var spanLeft = (int)Math.Round(spanBounds.Left * dpi);
            var spanRight = (int)Math.Round(spanBounds.Right * dpi);
            var spanBottom = (int)Math.Round(spanBounds.Bottom * dpi);
            var cornerD = (int)Math.Round(Math.Max(2.0, cornerRadius * 2.0) * dpi);

            var spanKey = (spanLeft, 0, spanRight + 1, spanBottom + 1, cornerD, 0, 0, 0, 0, 0);
            if (_lastWindowRegion == spanKey) return;
            _lastWindowRegion = spanKey;

            var spanRgn = CreateRoundRectRgn(spanLeft, 0, spanRight + 1, spanBottom + 1, cornerD, cornerD);
            if (spanRgn != IntPtr.Zero)
            {
                _ = SetWindowRgn(_hwnd, spanRgn, forceRedraw);
            }
            return;
        }

        if (isSplit)
        {
            double leadingW = Math.Max(1.0, pillWidth);
            double leadingH = Math.Max(1.0, pillHeight);
            var (leadOffX, trailOffX) = IslandSurfaceLayout.ComputeSplitOffsets(leadingW, detachedW);
            var leadBounds = IslandSurfaceLayout.ComputeCenteredPillBounds(IslandSurfaceLayout.DefaultHostWidthDip, leadingW, leadingH, leadOffX);
            var detachBounds = IslandSurfaceLayout.ComputeCenteredPillBounds(IslandSurfaceLayout.DefaultHostWidthDip, detachedW, detachedH, trailOffX);

            var surfaceLeft = (int)Math.Round(leadBounds.Left * dpi);
            var surfaceRight = (int)Math.Round(leadBounds.Right * dpi);
            var surfaceBottom = (int)Math.Round(leadBounds.Bottom * dpi);
            var cornerDiameter = (int)Math.Round(Math.Max(2.0, cornerRadius * 2.0) * dpi);

            var detachedLeft = (int)Math.Round(detachBounds.Left * dpi);
            var detachedRight = (int)Math.Round(detachBounds.Right * dpi);
            var detachedBottom = (int)Math.Round(detachBounds.Bottom * dpi);
            var detachedCornerDiameter = (int)Math.Round(Math.Max(2.0, detachedH) * dpi);

            var splitKey = (surfaceLeft, 0, surfaceRight + 1, surfaceBottom + 1, cornerDiameter,
                            detachedLeft, 0, detachedRight + 1, detachedBottom + 1, detachedCornerDiameter);
            if (_lastWindowRegion == splitKey) return;
            _lastWindowRegion = splitKey;

            var rgn = CreateRoundRectRgn(surfaceLeft, 0, surfaceRight + 1, surfaceBottom + 1, cornerDiameter, cornerDiameter);
            var detachedRgn = CreateRoundRectRgn(detachedLeft, 0, detachedRight + 1, detachedBottom + 1, detachedCornerDiameter, detachedCornerDiameter);

            if (rgn != IntPtr.Zero && detachedRgn != IntPtr.Zero)
            {
                var combinedRgn = CreateRectRgn(0, 0, 0, 0);
                CombineRgn(combinedRgn, rgn, detachedRgn, RGN_OR);
                DeleteObject(rgn);
                DeleteObject(detachedRgn);
                rgn = combinedRgn;
            }

            if (rgn != IntPtr.Zero)
            {
                _ = SetWindowRgn(_hwnd, rgn, forceRedraw);
            }
            return;
        }

        double surfaceOffset = _isDimensionAnimating ? _currentLeadingX : 0.0;
        var normalBounds = IslandSurfaceLayout.ComputeCenteredPillBounds(
            IslandSurfaceLayout.DefaultHostWidthDip,
            effectiveWidth,
            effectiveHeight,
            surfaceOffset);
        var left = (int)Math.Round(normalBounds.Left * dpi);
        var top = 0;
        var right = (int)Math.Round(normalBounds.Right * dpi);
        var bottom = (int)Math.Round(normalBounds.Bottom * dpi);
        var cornerDiam = (int)Math.Round(Math.Max(2.0, cornerRadius * 2.0) * dpi);

        var normalKey = (left, top, right + 1, bottom + 1, cornerDiam, 0, 0, 0, 0, 0);
        if (_lastWindowRegion == normalKey) return;
        _lastWindowRegion = normalKey;

        var normalRgn = CreateRoundRectRgn(left, top, right + 1, bottom + 1, cornerDiam, cornerDiam);
        if (normalRgn != IntPtr.Zero)
        {
            _ = SetWindowRgn(_hwnd, normalRgn, forceRedraw);
        }
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
            if (_viewModel.IsPlaying && (!_isIslandHidden || _hideAnchorVisible))
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
        else if (e.PropertyName is nameof(MediaViewModel.TopOffset) or nameof(MediaViewModel.IslandScale))
        {
            PositionHostWindow();
            UpdateStateLayout(_viewModel.CurrentIslandState, animated: false);
        }
        else if (e.PropertyName == nameof(MediaViewModel.HoverExpandDelayMs))
        {
            if (_hoverExpandTimer is not null)
            {
                _hoverExpandTimer.Interval = TimeSpan.FromMilliseconds(
                    Math.Clamp(_viewModel.HoverExpandDelayMs, 0, 1000));
            }
        }
        else if (e.PropertyName == nameof(MediaViewModel.AutoHideOnFullscreen))
        {
            PollFullscreenState();
        }
        else if (e.PropertyName == nameof(MediaViewModel.ShowEqualizer))
        {
            if (CompactWaveBars is not null)
            {
                CompactWaveBars.Opacity = (_viewModel.CurrentIslandState == IslandState.Hover && _viewModel.ShowEqualizer) ? 1.0 : 0.0;
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
            if (_surfaceClipGeometry is null)
            {
                _surfaceClipGeometry = _pillMaskVisual.Compositor.CreateRoundedRectangleGeometry();
                _pillMaskVisual.Clip = _pillMaskVisual.Compositor.CreateGeometricClip(_surfaceClipGeometry);
            }

            double clampedWidth = Math.Min(IslandSurfaceLayout.DefaultHostWidthDip, Math.Max(1.0, width));
            double clampedHeight = Math.Min(IslandSurfaceLayout.DefaultHostHeightDip, Math.Max(1.0, height));
            float offsetX = (float)((IslandSurfaceLayout.DefaultHostWidthDip - clampedWidth) / 2.0);
            float clampedRadius = (float)Math.Max(0.0, cornerRadius);

            _surfaceClipGeometry.Offset = new Vector2(offsetX, 0f);
            _surfaceClipGeometry.Size = new Vector2((float)clampedWidth, (float)clampedHeight);
            _surfaceClipGeometry.CornerRadius = new Vector2(clampedRadius, clampedRadius);
        }

        if (_frostSheenVisual is not null && height > 0)
        {
            float sheenScaleY = (float)Math.Max(10.0 / 18.0, (height * 0.45) / 18.0);
            _frostSheenVisual.Scale = new Vector3(1f, sheenScaleY, 1f);
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

            ApplySurfaceShape(toCornerRadius, toWidth, toHeight);
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

        _animVelWidth = wasAnimating ? _velPillWidth : 0.0;
        _animVelHeight = wasAnimating ? _velPillHeight : 0.0;
        _animVelCornerRadius = wasAnimating ? _velCornerRadius : 0.0;
        _animVelLeadingX = wasAnimating ? _velLeadingX : 0.0;
        _animVelTrailingX = wasAnimating ? _velTrailingX : 0.0;
        _animVelDetachedOpacity = wasAnimating ? _velDetachedOpacity : 0.0;
        _animVelExpandedScale = wasAnimating ? _velExpandedScale : 0.0;

        if (toDetachedOpacity > 0 && DetachedPill is not null)
        {
            DetachedPill.Visibility = Visibility.Visible;
            if (!wasAnimating && _animStartDetachedOpacity <= 0.05)
            {
                _animStartTrailingX = 66.0;
                _currentTrailingX = 66.0;
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

        double detachedElapsedSec = _animTargetDetachedOpacity < _animStartDetachedOpacity
            ? springElapsedSec * 2.2
            : springElapsedSec;
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
        if (_animTargetWidth < _animStartWidth && width <= _animTargetWidth)
        {
            width = _animTargetWidth;
            velWidth = 0.0;
        }

        double height = stateH.Position;
        double velHeight = stateH.Velocity;
        if (_animTargetHeight < _animStartHeight && height <= _animTargetHeight)
        {
            height = _animTargetHeight;
            velHeight = 0.0;
        }

        double cornerRadius = stateR.Position;
        double velCornerRadius = stateR.Velocity;
        if (_animTargetCornerRadius < _animStartCornerRadius && cornerRadius <= _animTargetCornerRadius)
        {
            cornerRadius = _animTargetCornerRadius;
            velCornerRadius = 0.0;
        }

        width = Math.Clamp(width, 1.0, IslandSurfaceLayout.DefaultHostWidthDip);
        height = Math.Clamp(height, 1.0, IslandSurfaceLayout.DefaultHostHeightDip);
        cornerRadius = Math.Max(0.0, cornerRadius);

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
        _velDetachedOpacity = stateDO.Velocity;
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
        bool settled = stateW.HasSettled && stateH.HasSettled && stateR.HasSettled && stateLX.HasSettled && stateTX.HasSettled && stateDO.HasSettled && stateES.HasSettled;
        double safetyTimeoutSec = Math.Max(1.2, _animDurationMs * 0.001 * 3.0);

        if (!isExpanding && !_regionCommitted &&
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
        double targetHeight = isExpanded ? 240 : 40;
        // Continuous island corners: large enough to read as an Apple surface
        // instead of a generic rounded card, still inside the content insets.
        double targetCornerRadius = isExpanded ? 56 : 20;

        var scale = isExpanded ? 1.0 : _viewModel.IslandScale;
        targetWidth *= scale;
        targetHeight *= scale;
        if (!isExpanded)
        {
            targetCornerRadius *= scale;
        }

        double detachedWidth = IslandSurfaceLayout.DetachedPillWidthDip * scale;
        double detachedHeight = IslandSurfaceLayout.DetachedPillHeightDip * scale;
        if (DetachedPill is not null && (Math.Abs(DetachedPill.Width - detachedWidth) > 0.01 || Math.Abs(DetachedPill.Height - detachedHeight) > 0.01))
        {
            DetachedPill.Width = detachedWidth;
            DetachedPill.Height = detachedHeight;
            DetachedPill.CornerRadius = new CornerRadius(20.0 * scale);
        }

        var (splitLeadingX, splitTrailingX) = IslandSurfaceLayout.ComputeSplitOffsets(targetWidth, detachedWidth);
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
            CompactWaveBars.Opacity = (isHover && _viewModel.ShowEqualizer) ? 1.0 : 0.0;
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

            ApplySurfaceShape(targetCornerRadius, targetWidth, targetHeight);
            SetSplitPositions(targetLeadingX, targetTrailingX);
            if (DetachedPill is not null)
            {
                DetachedPill.Opacity = targetDetachedOpacity;
                DetachedPill.Visibility = isSplit && !_isIslandHidden ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateWindowRegion(targetWidth, targetHeight, targetCornerRadius);

            _compactVisual.Opacity = isExpanded ? 0 : 1;
            CompactView.IsHitTestVisible = !isExpanded;
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
                    v.Opacity = (i == activeIndex) ? 1f : 0f;
                    v.StopAnimation("Translation");
                }
                UpdatePageDots(activeIndex, animated: false);
            }

            _pillMaskVisual.Scale = Vector3.One;
            _pillMaskVisual.StopAnimation("Scale");
            _pillMaskVisual.Opacity = 1;
            StopRenderLoopIfIdle();

            if (state == IslandState.Collapsing)
            {
                _viewModel.CompleteCollapse();
            }
            return;
        }

        try
        {
            var compositor = _pillMaskVisual.Compositor;
            var quickEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.6f, 1f));
            var durScale = AnimationProfile.DurationScale;

            _pillMaskVisual.Scale = Vector3.One;
            _pillMaskVisual.StopAnimation("Scale");

            var springEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));

            // Open the window region with spring overshoot headroom before starting the morph.
            UpdateWindowRegion(
                Math.Max(_currentPillWidth, targetWidth),
                Math.Max(_currentPillHeight, targetHeight),
                Math.Max(_currentCornerRadius, targetCornerRadius),
                includeOvershootPadding: true);

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
                    v.StopAnimation("Scale");
                    v.Scale = Vector3.One;
                    v.Opacity = (i == activeIndex) ? 1f : 0f;
                }
                UpdatePageDots(activeIndex, animated: false);

                var compactFade = compositor.CreateScalarKeyFrameAnimation();
                compactFade.Duration = TimeSpan.FromMilliseconds(75 * durScale);
                compactFade.InsertKeyFrame(1f, 0f, quickEasing);
                _compactVisual.StartAnimation("Opacity", compactFade);

                var expandedFade = compositor.CreateScalarKeyFrameAnimation();
                expandedFade.Duration = TimeSpan.FromMilliseconds(280 * durScale);
                expandedFade.InsertKeyFrame(0f, 0.15f);
                expandedFade.InsertKeyFrame(1f, 1f, springEasing);
                _expandedVisual.StartAnimation("Opacity", expandedFade);
                ExpandedView.Translation = Vector3.Zero;

                AnimateSurfaceDimensions(targetWidth, targetHeight, targetCornerRadius, 0.0, detachedWidth, 0.0, targetExpandedScale, 340);
            }
            else if (isCollapsing)
            {
                CompactView.IsHitTestVisible = true;
                ExpandedView.IsHitTestVisible = false;

                var expandedFade = compositor.CreateScalarKeyFrameAnimation();
                expandedFade.Duration = TimeSpan.FromMilliseconds(70 * durScale);
                expandedFade.InsertKeyFrame(0f, _expandedVisual.Opacity);
                expandedFade.InsertKeyFrame(1f, 0f, quickEasing);

                // Park the expanded tree once faded out so it consumes zero visual overhead when compact.
                var fadeBatch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
                _expandedVisual.StartAnimation("Opacity", expandedFade);
                fadeBatch.End();
                fadeBatch.Completed += (_, _) =>
                {
                    _ = DispatcherQueue.TryEnqueue(() =>
                    {
                        if (generation == _transitionGeneration && !_viewModel.IsExpanded)
                        {
                            ExpandedView.Visibility = Visibility.Collapsed;
                        }
                    });
                };

                // Keep compact content crisp and visible during collapse.
                _compactVisual.StopAnimation("Opacity");
                _compactVisual.Opacity = 1;

                AnimateSurfaceDimensions(targetWidth, targetHeight, targetCornerRadius, targetLeadingX, targetTrailingX, targetDetachedOpacity, targetExpandedScale, 300, () =>
                {
                    UpdateWindowRegion(targetWidth, targetHeight, targetCornerRadius);
                    if (generation == _transitionGeneration && _viewModel.CurrentIslandState == IslandState.Collapsing)
                    {
                        _viewModel.CompleteCollapse();
                    }
                });
            }
            else
            {
                CompactView.IsHitTestVisible = true;
                ExpandedView.IsHitTestVisible = false;
                _expandedVisual.Opacity = 0;
                _compactVisual.Opacity = 1;
                ExpandedView.Visibility = Visibility.Collapsed;

                AnimateSurfaceDimensions(targetWidth, targetHeight, targetCornerRadius, targetLeadingX, targetTrailingX, targetDetachedOpacity, targetExpandedScale, 220, () => UpdateWindowRegion(targetWidth, targetHeight, targetCornerRadius));
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
            var compositor = _pillMaskVisual.Compositor;
            var springEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.34f, 1.56f), new Vector2(0.64f, 1f));

            _pillMaskVisual.CenterPoint = new Vector3((float)(IslandSurfaceLayout.DefaultHostWidthDip / 2.0), (float)(_currentPillHeight / 2.0), 0f);

            var pulse = compositor.CreateVector3KeyFrameAnimation();
            pulse.Duration = TimeSpan.FromMilliseconds(420);
            pulse.InsertKeyFrame(0f, Vector3.One);
            pulse.InsertKeyFrame(0.35f, new Vector3(1.05f, 1.05f, 1f), springEasing);
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
            _viewModel.OnPointerEntered();
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
        if (_isPointerOverSurface && !_viewModel.IsExpanded)
        {
            _viewModel.OnPointerEntered();
        }
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        // Frost capture briefly hides the window, which synthesizes a synthetic
        // PointerExited — ignore it so the island doesn't collapse mid-capture.
        if (_frostBusy) return;

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
            _collapseDelayTimer.Interval = TimeSpan.FromMilliseconds(600);
            _collapseDelayTimer.Start();
        }
    }

    private void OnSurfacePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _hoverExpandTimer?.Stop();

        if (!_viewModel.IsExpanded)
        {
            _longPressTriggered = false;
            _longPressTimer?.Stop();
            _longPressTimer?.Start();
            AnimateSurfaceScale(1.035f, 0.945f, 90);
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
        AnimateSurfaceScale(1.025f, 1.025f, 120);
        ExpandFromCompact();
    }

    private void OnSurfacePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _longPressTimer?.Stop();
        AnimateSurfaceScale(1.0f, 1.0f, 140);

        if (!_viewModel.IsExpanded && !_longPressTriggered)
        {
            e.Handled = true;
            ExpandFromCompact();
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
        AnimateElementScale(DetachedPill, 1.08f, 120);
    }

    private void OnDetachedPillPointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateElementScale(DetachedPill, 1.0f, 120);
    }

    private void OnDetachedPillPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        AnimateElementScale(DetachedPill, 0.92f, 80);
        e.Handled = true;
    }

    private void OnDetachedPillPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        AnimateElementScale(DetachedPill, 1.0f, 140);
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
            anim.Duration = TimeSpan.FromMilliseconds(durationMs);
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
            anim.Duration = TimeSpan.FromMilliseconds(durationMs);
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1.0f), new Vector2(0.3f, 1.0f));
            anim.InsertKeyFrame(1f, new Vector3(targetScale, targetScale, 1f), easing);
            visual.StartAnimation("Scale", anim);
        }
        catch { }
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

        _collapseDelayTimer.Stop();

        var now = DateTime.UtcNow.Ticks;
        if (now - _lastWheelNavTicks < WheelDebounceTicks)
        {
            e.Handled = true;
            return;
        }

        _lastWheelNavTicks = now;
        e.Handled = true;

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
            bool wasPanelActive = wasAnimating && (i == _pageAnimFromIndex || i == _pageAnimToIndex);
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
            bool visible = i == fromIndex || i == toIndex;
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
        bool settled = true;

        for (int i = 0; i < 3; i++)
        {
            if (i != _pageAnimFromIndex && i != _pageAnimToIndex)
            {
                continue;
            }

            var grid = GetPanelGrid(i);
            var visual = GetPanelVisual(i);

            var x = _pageSpring.Evaluate(elapsedSec, _pageFromX[i], _pageToX[i], _pageVelX[i]);
            var opacity = _pageSpring.Evaluate(
                elapsedSec,
                _pageFromOpacity[i],
                _pageToOpacity[i],
                _pageVelOpacity[i],
                DampedSpringOscillator.NormalizedPositionTolerance,
                DampedSpringOscillator.NormalizedVelocityTolerance);
            var scale = _pageSpring.Evaluate(
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
            visual.Scale = Vector3.One;
            grid.Visibility = isTarget ? Visibility.Visible : Visibility.Collapsed;
            visual.Opacity = isTarget ? 1f : 0f;
        }
    }

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

        // Position transparent hit buttons once per page selection (zero per-frame layout).
        double btnX = 0.0;
        for (int i = 0; i < 3; i++)
        {
            var btn = _dotButtons[i];
            if (btn is not null)
            {
                if (Math.Abs(btn.Width - targets[i]) > 0.01)
                {
                    btn.Width = targets[i];
                }
                btn.Translation = new Vector3((float)btnX, 0f, 0f);
            }
            btnX += targets[i] + 5.0;
        }

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
            double x = 0.0;
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
        bool settled = true;

        for (int i = 0; i < 3; i++)
        {
            var widthState = _dotSpring.Evaluate(
                springElapsedSec,
                _dotStartWidth[i],
                _dotTargetWidth[i],
                _dotVelWidth[i],
                positionTolerance: 0.1,
                velocityTolerance: 0.25);
            var opacityState = _dotSpring.Evaluate(
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

        double x = 0.0;
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
            double snapX = 0.0;
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

    private void OnToggleExpandedClick(object sender, RoutedEventArgs e) => _viewModel.ToggleExpanded();
    private void OnCollapseClick(object sender, RoutedEventArgs e) => _viewModel.Collapse();
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
        UpdateFrostVisibility();
        if (_glassEnabled)
        {
            // No periodic re-capture: each capture briefly hides the window, which
            // reads as a visible blink. The blur is captured on launch/glass enable;
            // track and slider changes only recolor the tint overlay above it.
            _ = RefreshFrostAsync(force: true);
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
        if (_glassEnabled)
        {
            UpdateFrostTint();
        }
    }

    /// <summary>Recolors the live glass tint without touching the captured blur.</summary>
    private void UpdateFrostTint()
    {
        if (FrostTintLayer is null)
        {
            return;
        }

        FrostTintLayer.Fill = Brush(CurrentGlassTint());
        FrostTintLayer.Opacity = FrostAlpha() / 255.0;
    }

    private bool _frostBusy;

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
                // Capture the whole fixed host footprint once. The brush renders with
                // Stretch=None and top/center alignment, so every pill size reads a 1:1
                // crop of the same snapshot instead of re-scaling an old capture.
                GetClientRect(_hwnd, out var crc);
                var pt = new Win32Point();
                _ = ClientToScreen(_hwnd, ref pt);
                screenX = pt.X;
                screenY = pt.Y;
                clientW = Math.Max(16, crc.Right - crc.Left);
                clientH = Math.Max(16, crc.Bottom - crc.Top);
            });
            if (clientW < 16 || clientH < 16) return;

            var dpiScale = _dpiScale > 0 ? _dpiScale : 1.0;
            var frost = await Task.Run(() =>
            {
                var bmp = CaptureDesktopSample(screenX, screenY, clientW, clientH);
                if (bmp is null) return null;

                var result = BlurDesktop(bmp);
                bmp.Dispose();

                var dip = ScaleToDip(result, dpiScale);
                if (!ReferenceEquals(dip, result))
                {
                    result.Dispose();
                }
                return dip;
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
                if (_isIslandHidden || !_glassEnabled || FrostLayer is null)
                {
                    return;
                }

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
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.None,
                    AlignmentX = Microsoft.UI.Xaml.Media.AlignmentX.Center,
                    AlignmentY = Microsoft.UI.Xaml.Media.AlignmentY.Top
                };
                UpdateFrostTint();
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

    private static bool IsSystemLightTheme()
    {
        try
        {
            var background = SystemUiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
            return (background.R * 0.299 + background.G * 0.587 + background.B * 0.114) > 128;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Resolves the Settings "System" option to the real OS theme instead of forcing dark.</summary>
    private static bool IsLightTheme(string theme) =>
        theme == "Light" || (theme != "Dark" && IsSystemLightTheme());

    /// <summary>Surface alpha from the transparency slider; 0xF2 = solid, 0x5E = most transparent.</summary>
    private uint SurfaceAlpha()
    {
        if (!_glassEnabled) return 0xF2u;
        var level = Math.Clamp(_viewModel.TransparencyLevel, 0, 100);
        return (uint)Math.Round(0xF2 - (0xF2 - 0x5E) * level / 100.0);
    }

    /// <summary>Frost stays a little denser than the surface so text keeps its contrast.</summary>
    private uint FrostAlpha()
    {
        if (!_glassEnabled) return 0xF2u;
        return Math.Min(0xF2u, SurfaceAlpha() + 0x1E);
    }

    private uint CurrentGlassTint()
    {
        var artworkAccent = _viewModel.DynamicColorTint ? _viewModel.ArtworkAccentColor : 0xFF007AFFu;
        var tintAmount = Math.Clamp(_viewModel.ArtworkTintStrength, 0, 100) / 100.0;
        var baseColor = IsLightTheme(_viewModel.Theme) ? 0xFFF2F5F7u : 0xFF0B1018u;
        // Opaque RGB: FrostTintLayer applies the transparency as its Opacity.
        return Blend(baseColor, artworkAccent, tintAmount);
    }

    /// <summary>
    /// Captures the screen region behind the island. The window is briefly hidden
    /// so the sample contains only the real desktop beneath it (no self-feedback).
    /// </summary>
    private System.Drawing.Bitmap? CaptureDesktopSample(int x, int y, int width, int height)
    {
        if (_isIslandHidden)
        {
            return null;
        }

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
            if (!_isIslandHidden)
            {
                captured = new System.Drawing.Bitmap(width, height);
                using var g = System.Drawing.Graphics.FromImage(captured);
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height), System.Drawing.CopyPixelOperation.SourceCopy);
            }
        }
        catch
        {
        }
        finally
        {
            if (!_isIslandHidden)
            {
                try
                {
                    _ = SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpShowWindow | SwpNoActivate | SwpNoMove | SwpNoSize);
                }
                catch
                {
                }
            }
        }

        return captured;
    }

    private static System.Drawing.Bitmap BlurDesktop(System.Drawing.Bitmap captured)
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
        }

        return frost;
    }

    /// <summary>Converts a physical-pixel snapshot to Dip size so Stretch=None maps 1:1.</summary>
    private static System.Drawing.Bitmap ScaleToDip(System.Drawing.Bitmap source, double dpiScale)
    {
        if (dpiScale <= 0 || Math.Abs(dpiScale - 1.0) < 0.01)
        {
            return source;
        }

        var width = Math.Max(16, (int)Math.Round(source.Width / dpiScale));
        var height = Math.Max(16, (int)Math.Round(source.Height / dpiScale));
        var scaled = new System.Drawing.Bitmap(width, height);
        using var g = System.Drawing.Graphics.FromImage(scaled);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(source, 0, 0, width, height);
        return scaled;
    }

    public void ApplyTheme(string theme)
    {
        var isLight = IsLightTheme(theme);
        WindowRoot.RequestedTheme = isLight ? ElementTheme.Light : ElementTheme.Dark;

        var artworkAccent = _viewModel.DynamicColorTint ? _viewModel.ArtworkAccentColor : 0xFF007AFFu;
        var tintAmount   = Math.Clamp(_viewModel.ArtworkTintStrength, 0, 100) / 100.0;
        var accentColor  = artworkAccent;
        // Glass is used in both compact and expanded states; the transparency slider
        // controls how much desktop shows through, and the artwork accent tints it.
        var bgAlpha      = SurfaceAlpha();
        var bgColor      = isLight
            ? Blend((bgAlpha << 24) | 0xF2F5F7u, artworkAccent, tintAmount)
            : Blend((bgAlpha << 24) | 0x0B1018u, artworkAccent, tintAmount);
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

        PillBackdrop.Background = Brush(bgColor);
        PillBackdrop.BorderBrush = Brush(0x00000000);
        PillBackdrop.BorderThickness = new Thickness(0);
        if (DetachedPill is not null)
        {
            DetachedPill.Background = Brush(bgColor);
            DetachedPill.BorderBrush = Brush(0x00000000);
            DetachedPill.BorderThickness = new Thickness(0);
        }
        WaveBar1.Background = Brush(accentColor);
        WaveBar2.Background = Brush(accentColor);
        WaveBar3.Background = Brush(accentColor);
        if (ExpandedSeekBar is not null)
            ExpandedSeekBar.AccentBrush = Brush(seekAccent);
        UpdatePageDots(_viewModel.SelectedPageIndex, animated: false);

        // The glass tint is a live overlay on the captured blur, so track/theme/slider
        // changes recolor it instantly without a desktop re-capture (no blink).
        UpdateFrostTint();
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

    // Subclass WndProc to handle native hit-testing, DPI/display changes, and eliminate OS window minimum size constraints
    private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        const uint WM_GETMINMAXINFO = 0x0024;
        const uint WM_SETTINGCHANGE = 0x001A;
        const uint WM_WINDOWPOSCHANGING = 0x0046;
        const uint WM_DISPLAYCHANGE = 0x007E;
        const uint WM_NCHITTEST = 0x0084;
        const uint WM_DPICHANGED = 0x02E0;
        const uint WM_HOTKEY = 0x0312;
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

        if (uMsg == WM_DPICHANGED || uMsg == WM_DISPLAYCHANGE || (uMsg == WM_SETTINGCHANGE && wParam.ToInt32() == 0x002F))
        {
            _ = DispatcherQueue.TryEnqueue(() =>
            {
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
        double localDipY = (screenY - winRect.Top) / _dpiScale;

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
