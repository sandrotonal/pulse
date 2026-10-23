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
    private readonly Pulse.Core.Animation.DampedSpringOscillator _springOscillator = new(dampingRatio: 0.82, responseSeconds: 0.34);
    private readonly SubclassProc _subclassProc;

    private long _lastWheelNavTicks;
    private const long WheelDebounceTicks = 200 * TimeSpan.TicksPerMillisecond;
    private int _targetPageIndex;
    private DispatcherQueueTimer? _pageTransitionCleanupTimer;
    private DispatcherQueueTimer? _hideAnimationTimer;
    private DispatcherQueueTimer? _topmostGuardTimer;
    private string? _lastPulseTrackKey;

    private double _dpiScale = 1.0;
    private bool _isIslandHidden;
    private bool _glassEnabled;
    private double _currentPillWidth = 216;
    private double _currentPillHeight = 40;
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
    private DispatcherQueueTimer? _volumeHudTimer;
    private bool _volumeHudFading;
    private Visual? _volumeHudVisual;
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
    private long _animStartTimeTicks;
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

        _volumeHudTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _volumeHudTimer.IsRepeating = false;
        _volumeHudTimer.Tick += OnVolumeHudTimerTick;

        _pageTransitionCleanupTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _pageTransitionCleanupTimer.Interval = TimeSpan.FromMilliseconds(220);
        _pageTransitionCleanupTimer.Tick += OnPageTransitionCleanupTimerTick;

        _surfaceVisual = ElementCompositionPreview.GetElementVisual(Surface);
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
        if (VolumeHud is not null)
        {
            _volumeHudVisual = ElementCompositionPreview.GetElementVisual(VolumeHud);
        }

        ElementCompositionPreview.SetIsTranslationEnabled(Surface, true);
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
        if (_hwnd == IntPtr.Zero)
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

        if (_isIslandHidden) UnhideIslandAnimated();
        ShowWindow(_hwnd, 4); // SW_SHOWNOACTIVATE
        ApplyWindowStyles();
        PositionHostWindow();
    }

    public void HideIslandAnimated(bool showAnchor = true)
    {
        if (_isIslandHidden) return;
        _isIslandHidden = true;
        _hideAnchorVisible = showAnchor;

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
                    HiddenAnchor.Visibility = _hideAnchorVisible ? Visibility.Visible : Visibility.Collapsed;
                    if (!_hideAnchorVisible)
                    {
                        ShowWindow(_hwnd, 0); // SW_HIDE: leave no artifact over fullscreen content
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
    private void PollFullscreenState()
    {
        var isFullscreen = false;
        try
        {
            if (SHQueryUserNotificationState(out var state) == 0)
            {
                isFullscreen = FullscreenNotificationState.IsFullscreen(state);
            }
        }
        catch
        {
            // Shell API is best-effort; a failure must never break the island.
        }

        var wasFullscreen = _fullscreenActive;
        _fullscreenActive = isFullscreen;

        if (wasFullscreen && !isFullscreen)
        {
            _userOverrodeFullscreenHide = false;
        }

        if (!_viewModel.AutoHideOnFullscreen)
        {
            if (_autoHiddenForFullscreen)
            {
                _autoHiddenForFullscreen = false;
                if (_isIslandHidden)
                {
                    ShowIsland();
                }
            }
            return;
        }

        if (isFullscreen && !_isIslandHidden && !_userOverrodeFullscreenHide)
        {
            _autoHiddenForFullscreen = true;
            HideIslandAnimated(showAnchor: false);
        }
        else if (!isFullscreen && _autoHiddenForFullscreen)
        {
            _autoHiddenForFullscreen = false;
            if (_isIslandHidden)
            {
                ShowIsland();
            }
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _fullscreenPollTimer?.Stop();
        _hoverExpandTimer?.Stop();
        _longPressTimer?.Stop();
        _volumeHudTimer?.Stop();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private const double BaseWindowTopGap = 14.0;

    private void PositionHostWindow()
    {
        ApplyHostWindowSize(_currentPillWidth, _currentPillHeight);
        UpdateWindowRegion(_currentPillWidth, _currentPillHeight, Surface.CornerRadius.TopLeft > 0 ? Surface.CornerRadius.TopLeft : 20);
    }

    private double _hostPillWidth = 216;
    private double _hostPillHeight = 40;

    /// <summary>
    /// Sizes/moves the host window to hug the current pill. A big fixed host window would
    /// swallow clicks in its transparent margins (WinUI never calls WM_NCHITTEST for
    /// pointer input), so the window must track the pill dimensions.
    /// </summary>
    // Fixed host footprint: big enough for the expanded card at every pill scale.
    // The window never resizes during transitions; a window region crops input/visual
    // to the current pill so clicks outside it fall through to the window below.
    private const double HostWidthDip = 424.0;
    private const double HostHeightDip = 260.0;

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

        var winWidth = (int)Math.Round(HostWidthDip * _dpiScale);
        var winHeight = (int)Math.Round(HostHeightDip * _dpiScale);

        var x = workArea.Left + Math.Max(0, (workArea.Right - workArea.Left - winWidth) / 2);
        var effectiveTopOffset = Math.Max(BaseWindowTopGap, _viewModel.TopOffset + BaseWindowTopGap);
        var y = workArea.Top + (int)Math.Round(effectiveTopOffset * _dpiScale);

        // Startup / setting changes only; transitions never resize the native window.
        QueueHostWindowResize(x, y, winWidth, winHeight);

        // Window moves under the cursor can synthesize pointer exit/enter pairs in WinUI;
        // suppress them briefly so hover state cannot oscillate with the resize itself.
        _hostResizeGuardTicks = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Crops the native window to the current pill with a Win32 region. Areas outside the
    /// region are not part of the window, so clicks pass through to whatever is below -
    /// while the window itself keeps a fixed size and never causes resize hitches.
    /// </summary>
    private void UpdateWindowRegion(double pillWidth, double pillHeight, double cornerRadius)
    {
        if (_hwnd == IntPtr.Zero) return;

        var dpi = _dpiScale > 0 ? _dpiScale : 1.0;
        var isSplit = _viewModel.IsSplitModeActive && !_viewModel.IsExpanded && !_viewModel.IsCollapsing;
        bool isDetachedActive = DetachedPill is not null && (DetachedPill.Visibility == Visibility.Visible || DetachedPill.Opacity > 0.01);

        if (_isDimensionAnimating && (isSplit || isDetachedActive))
        {
            // During animation into/out of split mode, keep a single spanning region [76..348]
            // so neither capsule is sliced by the gap before settling into position.
            var spanLeft = (int)Math.Round(76.0 * dpi);
            var spanRight = (int)Math.Round(348.0 * dpi);
            var spanHeight = (int)Math.Round(40.0 * dpi);
            var cornerD = (int)Math.Round(40.0 * dpi);

            var spanKey = (spanLeft, 0, spanRight + 1, spanHeight + 1, cornerD, 0, 0, 0, 0, 0);
            if (_lastWindowRegion == spanKey) return;
            _lastWindowRegion = spanKey;

            var spanRgn = CreateRoundRectRgn(spanLeft, 0, spanRight + 1, spanHeight + 1, cornerD, cornerD);
            if (spanRgn != IntPtr.Zero)
            {
                _ = SetWindowRgn(_hwnd, spanRgn, false);
            }
            return;
        }

        if (isSplit)
        {
            // Apple Split Island resting state: Leading pill (196px) + Gap (10px) + Trailing pill (66px) = Total 272px centered in HostWidthDip (424px).
            // Left margin = (424 - 272)/2 = 76px.
            // Surface bounds: [76, 272], DetachedPill bounds: [282, 348].
            var surfaceLeft = (int)Math.Round(76.0 * dpi);
            var surfaceRight = (int)Math.Round(272.0 * dpi);
            var surfaceHeight = (int)Math.Round(40.0 * dpi);
            var cornerDiameter = (int)Math.Round(40.0 * dpi);

            var detachedLeft = (int)Math.Round(282.0 * dpi);
            var detachedRight = (int)Math.Round(348.0 * dpi);

            var splitKey = (surfaceLeft, 0, surfaceRight + 1, surfaceHeight + 1, cornerDiameter,
                            detachedLeft, 0, detachedRight + 1, surfaceHeight + 1, cornerDiameter);
            if (_lastWindowRegion == splitKey) return;
            _lastWindowRegion = splitKey;

            var rgn = CreateRoundRectRgn(surfaceLeft, 0, surfaceRight + 1, surfaceHeight + 1, cornerDiameter, cornerDiameter);

            var detachedRgn = CreateRoundRectRgn(detachedLeft, 0, detachedRight + 1, surfaceHeight + 1, cornerDiameter, cornerDiameter);

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
                _ = SetWindowRgn(_hwnd, rgn, false);
            }
            return;
        }

        var surfaceOffset = Surface.Translation.X;
        var left = (int)Math.Round((((HostWidthDip - pillWidth) / 2.0) + surfaceOffset) * dpi);
        var top = 0;
        var width = (int)Math.Round(pillWidth * dpi);
        var height = (int)Math.Round(pillHeight * dpi);
        var cornerDiam = (int)Math.Round(Math.Max(2.0, cornerRadius * 2.0) * dpi);

        var normalKey = (left, top, left + width + 1, top + height + 1, cornerDiam, 0, 0, 0, 0, 0);
        if (_lastWindowRegion == normalKey) return;
        _lastWindowRegion = normalKey;

        var normalRgn = CreateRoundRectRgn(left, top, left + width + 1, top + height + 1, cornerDiam, cornerDiam);
        if (normalRgn != IntPtr.Zero)
        {
            _ = SetWindowRgn(_hwnd, normalRgn, false);
        }
    }

    private readonly object _hostResizeLock = new();
    private (int X, int Y, int W, int H, long Version)? _pendingHostResize;
    private long _hostResizeVersion;
    private bool _hostResizeWorkerRunning;

    private void QueueHostWindowResize(int x, int y, int width, int height)
    {
        lock (_hostResizeLock)
        {
            _hostResizeVersion++;
            _pendingHostResize = (x, y, width, height, _hostResizeVersion);
            if (_hostResizeWorkerRunning)
            {
                return;
            }

            _hostResizeWorkerRunning = true;
        }

        _ = Task.Run(ProcessHostResizes);
    }

    private async Task ProcessHostResizes()
    {
        while (true)
        {
            (int X, int Y, int W, int H, long Version) job;
            lock (_hostResizeLock)
            {
                if (_pendingHostResize is null)
                {
                    _hostResizeWorkerRunning = false;
                    return;
                }

                job = _pendingHostResize.Value;
                _pendingHostResize = null;
            }

            if (_hwnd != IntPtr.Zero)
            {
                _ = SetWindowPos(_hwnd, HwndTopmost, job.X, job.Y, job.W, job.H, SwpNoActivate);
            }

            // Let the owner thread breathe so bursts of resize requests coalesce.
            await Task.Delay(12).ConfigureAwait(false);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IslandViewModel.BatteryIsCharging))
        {
            UpdateBatteryChargingAnimation();
        }
        else if (e.PropertyName == nameof(MediaViewModel.CurrentIslandState))
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
    /// Applies the surface corner radius to every layer that shapes the pill. WinUI's
    /// Border.CornerRadius does not clip children, so the frost rectangle and its sheen
    /// must carry the same radius or their corners poke through the border during a morph.
    /// Concentric corners are what Apple's Live Activity guidance calls for.
    /// </summary>
    private void ApplySurfaceShape(double cornerRadius, double width, double height)
    {
        Surface.CornerRadius = new CornerRadius(cornerRadius);

        // A Border does not clip its children. While the card shrinks, the expanded
        // layout can overflow the capsule and draw outside it (badges, icons), so clip
        // the surface to the same rounded shape. This also keeps the corners continuous.
        if (width > 0 && height > 0)
        {
            if (_surfaceClipGeometry is null)
            {
                _surfaceClipGeometry = _surfaceVisual.Compositor.CreateRoundedRectangleGeometry();
                _surfaceVisual.Clip = _surfaceVisual.Compositor.CreateGeometricClip(_surfaceClipGeometry);
            }

            _surfaceClipGeometry.Size = new Vector2((float)width, (float)height);
            _surfaceClipGeometry.CornerRadius = new Vector2((float)cornerRadius, (float)cornerRadius);
        }

        if (FrostLayer is not null)
        {
            FrostLayer.RadiusX = cornerRadius;
            FrostLayer.RadiusY = cornerRadius;
        }

        if (FrostTintLayer is not null)
        {
            FrostTintLayer.RadiusX = cornerRadius;
            FrostTintLayer.RadiusY = cornerRadius;
        }

        if (FrostSheen is not null)
        {
            FrostSheen.RadiusX = cornerRadius;
            FrostSheen.RadiusY = cornerRadius;
            FrostSheen.Height = Math.Max(10.0, height * 0.45);
        }
    }

    private void AnimateSurfaceDimensions(
        double toWidth,
        double toHeight,
        double toCornerRadius,
        double toLeadingX,
        double toTrailingX,
        double toDetachedOpacity,
        double durationMs,
        Action? onCompleted = null)
    {
        if (ReducedMotion)
        {
            _isDimensionAnimating = false;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnDimensionRendering;
            Surface.Width = toWidth;
            Surface.Height = toHeight;
            ApplySurfaceShape(toCornerRadius, toWidth, toHeight);
            _currentPillWidth = toWidth;
            _currentPillHeight = toHeight;
            _regionCommitted = true;
            SetSplitPositions(toLeadingX, toTrailingX);
            if (DetachedPill is not null)
            {
                DetachedPill.Opacity = toDetachedOpacity;
                DetachedPill.Visibility = toDetachedOpacity > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            onCompleted?.Invoke();
            return;
        }

        _animStartWidth = Surface.Width > 0 ? Surface.Width : Surface.ActualWidth;
        if (_animStartWidth <= 0) _animStartWidth = 216.0;

        _animStartHeight = Surface.Height > 0 ? Surface.Height : Surface.ActualHeight;
        if (_animStartHeight <= 0) _animStartHeight = 40.0;

        _animStartCornerRadius = Surface.CornerRadius.TopLeft > 0 ? Surface.CornerRadius.TopLeft : 20.0;

        _animStartLeadingX = Surface.Translation.X;
        _animStartTrailingX = DetachedPill is not null ? DetachedPill.Translation.X : toTrailingX;
        _animStartDetachedOpacity = DetachedPill?.Opacity ?? 0.0;

        if (toDetachedOpacity > 0 && DetachedPill is not null)
        {
            DetachedPill.Visibility = Visibility.Visible;
            if (_animStartDetachedOpacity <= 0.05)
            {
                _animStartTrailingX = 66.0;
                SetSplitPositions(_animStartLeadingX, _animStartTrailingX);
            }
        }

        _animTargetWidth = toWidth;
        _animTargetHeight = toHeight;
        _animTargetCornerRadius = toCornerRadius;
        _animTargetLeadingX = toLeadingX;
        _animTargetTrailingX = toTrailingX;
        _animTargetDetachedOpacity = toDetachedOpacity;

        _animDurationMs = Math.Max(1.0, durationMs * AnimationProfile.DurationScale);
        _animStartTimeTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        _onAnimationCompleted = onCompleted;
        _regionCommitted = false;

        if (!_isDimensionAnimating)
        {
            _isDimensionAnimating = true;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnDimensionRendering;
        }
    }

    private void OnDimensionRendering(object? sender, object e)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double elapsedSec = (now - _animStartTimeTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
        double springElapsedSec = elapsedSec * MotionSpeedScale;

        var stateW = _springOscillator.Evaluate(springElapsedSec, _animStartWidth, _animTargetWidth);
        var stateH = _springOscillator.Evaluate(springElapsedSec, _animStartHeight, _animTargetHeight);
        var stateR = _springOscillator.Evaluate(springElapsedSec, _animStartCornerRadius, _animTargetCornerRadius);
        var stateLX = _springOscillator.Evaluate(springElapsedSec, _animStartLeadingX, _animTargetLeadingX);
        var stateTX = _springOscillator.Evaluate(springElapsedSec, _animStartTrailingX, _animTargetTrailingX);

        // A damped spring crosses its target and settles back. That soft overshoot is
        // desirable while expanding, but while shrinking it reads as a tiny tremor at
        // the end of the collapse. Clamp only shrinking dimensions so the expansion
        // keeps its elastic character.
        double width = stateW.Position;
        double height = stateH.Position;
        double cornerRadius = stateR.Position;
        if (_animTargetWidth < _animStartWidth) width = Math.Max(width, _animTargetWidth);
        if (_animTargetHeight < _animStartHeight) height = Math.Max(height, _animTargetHeight);
        if (_animTargetCornerRadius < _animStartCornerRadius) cornerRadius = Math.Max(cornerRadius, _animTargetCornerRadius);

        Surface.Width = width;
        Surface.Height = height;
        ApplySurfaceShape(cornerRadius, width, height);

        _currentPillWidth = width;
        _currentPillHeight = height;

        // Commit the final hit-test region while the spring is still moving, so the
        // single DWM region swap lands under motion instead of as a visible snap.
        // When shrinking, wait until the clamped surface actually reaches the target:
        // cropping a still-larger pill during the last spring frame reads as a tremor.
        var regionTolerance = _animTargetWidth < _animStartWidth ? 0.1 : 2.0;
        if (!_regionCommitted &&
            Math.Abs(width - _animTargetWidth) <= regionTolerance &&
            Math.Abs(height - _animTargetHeight) <= regionTolerance)
        {
            _regionCommitted = true;
            UpdateWindowRegion(_animTargetWidth, _animTargetHeight, _animTargetCornerRadius);
        }

        SetSplitPositions(stateLX.Position, stateTX.Position);

        if (DetachedPill is not null)
        {
            double durSec = Math.Max(0.001, _animDurationMs * 0.001);
            double progress = Math.Clamp(elapsedSec / durSec, 0.0, 1.0);
            DetachedPill.Opacity = _animStartDetachedOpacity + (_animTargetDetachedOpacity - _animStartDetachedOpacity) * progress;
        }

        if ((stateW.HasSettled && stateH.HasSettled && stateR.HasSettled && stateLX.HasSettled && stateTX.HasSettled) || elapsedSec >= (_animDurationMs * 0.001 * 1.5))
        {
            _isDimensionAnimating = false;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnDimensionRendering;

            Surface.Width = _animTargetWidth;
            Surface.Height = _animTargetHeight;
            ApplySurfaceShape(_animTargetCornerRadius, _animTargetWidth, _animTargetHeight);
            _currentPillWidth = _animTargetWidth;
            _currentPillHeight = _animTargetHeight;

            SetSplitPositions(_animTargetLeadingX, _animTargetTrailingX);

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
        if (ReducedMotion)
        {
            animated = false;
        }

        // A hover morph may still be running when the user clicks; its per-frame loop
        // would fight the new layout and its completion callback could shrink the host
        // window. Cancel it (and its callback) before applying the new state.
        if (_isDimensionAnimating)
        {
            _isDimensionAnimating = false;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnDimensionRendering;
            _onAnimationCompleted = null;
        }

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
            _expandedVisual.Opacity = 0;
        }

        double targetWidth;
        double targetHeight;
        double targetCornerRadius;

        bool isSplit = _viewModel.IsSplitModeActive && !isExpanded && !isCollapsing;
        double targetLeadingX = isSplit ? -38.0 : 0.0;
        double targetTrailingX = isSplit ? 103.0 : 66.0;
        double targetDetachedOpacity = isSplit ? 1.0 : 0.0;

        if (isExpanded)
        {
            targetWidth = 384;
            targetHeight = 240;
            // Continuous island corners: large enough to read as an Apple surface
            // instead of a generic rounded card, still inside the content insets.
            targetCornerRadius = 56;
        }
        else if (isSplit)
        {
            targetWidth = 196;
            targetHeight = 40;
            targetCornerRadius = 20;
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
            // The smart idle strip needs the full compact width. This also removes
            // the old 76 -> 86 hover jump when no media is playing.
            targetWidth = 216;
            targetHeight = 40;
            targetCornerRadius = 20;
        }
        else if (isHover)
        {
            targetWidth = hasTrack ? 252 : 216;
            targetHeight = 40;
            targetCornerRadius = 20;
        }
        else
        {
            targetWidth = 216;
            targetHeight = 40;
            targetCornerRadius = 20;
        }

        var scale = isExpanded ? 1.0 : _viewModel.IslandScale;
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
            ApplySurfaceShape(targetCornerRadius, targetWidth, targetHeight);
            _currentPillWidth = targetWidth;
            _currentPillHeight = targetHeight;
            SetSplitPositions(targetLeadingX, targetTrailingX);
            if (DetachedPill is not null)
            {
                DetachedPill.Opacity = targetDetachedOpacity;
                DetachedPill.Visibility = isSplit ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateWindowRegion(targetWidth, targetHeight, targetCornerRadius);

            _compactVisual.Opacity = isExpanded ? 0 : 1;
            CompactView.IsHitTestVisible = !isExpanded;
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

            _surfaceVisual.Scale = Vector3.One;
            _surfaceVisual.StopAnimation("Scale");
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
            var quickEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.6f, 1f));
            var durScale = AnimationProfile.DurationScale;

            _surfaceVisual.Scale = Vector3.One;
            _surfaceVisual.StopAnimation("Scale");

            var springEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));

            // Open the window region to the larger of current/target so the morph is never clipped.
            UpdateWindowRegion(
                Math.Max(_currentPillWidth, targetWidth),
                Math.Max(_currentPillHeight, targetHeight),
                Math.Max(Surface.CornerRadius.TopLeft, targetCornerRadius));

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

                AnimateSurfaceDimensions(targetWidth, targetHeight, targetCornerRadius, 0.0, 66.0, 0.0, 340);
            }
            else if (isCollapsing)
            {
                CompactView.IsHitTestVisible = true;
                ExpandedView.IsHitTestVisible = false;

                var expandedFade = compositor.CreateScalarKeyFrameAnimation();
                expandedFade.Duration = TimeSpan.FromMilliseconds(70 * durScale);
                expandedFade.InsertKeyFrame(0f, 1f);
                expandedFade.InsertKeyFrame(1f, 0f, quickEasing);

                // Park the heavy expanded tree as soon as it is fully transparent so the
                // remaining shrink frames only lay out the small compact content.
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

                // The compact layer is already in place under the expanded content. Reveal it
                // immediately; a fade-in from near-zero leaves the pill blank mid-shrink.
                _compactVisual.StopAnimation("Opacity");
                _compactVisual.Opacity = 1;

                AnimateSurfaceDimensions(targetWidth, targetHeight, targetCornerRadius, targetLeadingX, targetTrailingX, targetDetachedOpacity, 300, () =>
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

                UpdateWindowRegion(Math.Max(_currentPillWidth, targetWidth), Math.Max(_currentPillHeight, targetHeight), targetCornerRadius);
                AnimateSurfaceDimensions(targetWidth, targetHeight, targetCornerRadius, targetLeadingX, targetTrailingX, targetDetachedOpacity, 220, () => UpdateWindowRegion(targetWidth, targetHeight, targetCornerRadius));
            }
        }
        catch
        {
            Surface.Width = targetWidth;
            Surface.Height = targetHeight;
            ApplySurfaceShape(targetCornerRadius, targetWidth, targetHeight);
            SetSplitPositions(targetLeadingX, targetTrailingX);
            if (DetachedPill is not null)
            {
                DetachedPill.Opacity = targetDetachedOpacity;
                DetachedPill.Visibility = isSplit ? Visibility.Visible : Visibility.Collapsed;
            }
            _compactVisual.Opacity = isExpanded ? 0 : 1;
            _expandedVisual.Opacity = isExpanded ? 1 : 0;
            ExpandedView.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
            _currentPillWidth = targetWidth;
            _currentPillHeight = targetHeight;
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
            var compositor = _surfaceVisual.Compositor;
            _surfaceVisual.CenterPoint = new Vector3((float)(_currentPillWidth / 2.0), (float)(_currentPillHeight / 2.0), 0);

            var anim = compositor.CreateVector3KeyFrameAnimation();
            anim.Duration = TimeSpan.FromMilliseconds(durationMs);
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1.0f), new Vector2(0.3f, 1.0f));
            anim.InsertKeyFrame(1f, new Vector3(scaleX, scaleY, 1f), easing);

            _surfaceVisual.StartAnimation("Scale", anim);
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

        // The expanded media page owns the wheel: each notch changes system volume.
        // Other pages keep the existing wheel navigation.
        if (_viewModel.SelectedPageIndex == 0 && _viewModel.EnableScrollWheelVolume)
        {
            var steps = Math.Max(1, (int)Math.Round(Math.Abs(delta) / 120.0 * 2.0, MidpointRounding.AwayFromZero));
            var volume = _viewModel.AdjustMasterVolume(delta > 0 ? steps : -steps);
            ShowVolumeHud(volume, _viewModel.IsSystemMuted);
            e.Handled = true;
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

    private void ShowVolumeHud(int volume, bool muted)
    {
        if (VolumeHud is null || VolumeHudText is null || VolumeHudIcon is null)
        {
            return;
        }

        VolumeHudText.Text = muted ? "Sessiz" : $"%{volume}";
        VolumeHudIcon.Glyph = muted ? "\uE74F" : volume == 0 ? "\uE992" : "\uE995";
        VolumeHud.Visibility = Visibility.Visible;
        _volumeHudFading = false;
        _volumeHudTimer?.Stop();

        if (_volumeHudVisual is null)
        {
            VolumeHud.Opacity = 1;
        }
        else
        {
            try
            {
                _volumeHudVisual.StopAnimation("Opacity");
                _volumeHudVisual.StopAnimation("Scale");
                _volumeHudVisual.Opacity = 1f;
                _volumeHudVisual.CenterPoint = new Vector3(
                    (float)(VolumeHud.ActualWidth / 2.0),
                    (float)(VolumeHud.ActualHeight / 2.0), 0);

                var compositor = _volumeHudVisual.Compositor;
                var easing = compositor.CreateCubicBezierEasingFunction(
                    new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
                var pop = compositor.CreateVector3KeyFrameAnimation();
                pop.Duration = TimeSpan.FromMilliseconds(160);
                pop.InsertKeyFrame(0f, new Vector3(0.9f, 0.9f, 1f));
                pop.InsertKeyFrame(1f, Vector3.One, easing);
                _volumeHudVisual.StartAnimation("Scale", pop);
            }
            catch { }
        }

        if (_volumeHudTimer is not null)
        {
            _volumeHudTimer.Interval = TimeSpan.FromMilliseconds(900);
            _volumeHudTimer.Start();
        }
    }

    private void OnVolumeHudTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();

        if (!_volumeHudFading)
        {
            _volumeHudFading = true;
            if (_volumeHudVisual is not null)
            {
                try
                {
                    var fade = _volumeHudVisual.Compositor.CreateScalarKeyFrameAnimation();
                    fade.Duration = TimeSpan.FromMilliseconds(180);
                    fade.InsertKeyFrame(1f, 0f);
                    _volumeHudVisual.StartAnimation("Opacity", fade);
                }
                catch { }
            }

            sender.Interval = TimeSpan.FromMilliseconds(200);
            sender.Start();
            return;
        }

        _volumeHudFading = false;
        if (VolumeHud is not null)
        {
            VolumeHud.Visibility = Visibility.Collapsed;
            VolumeHud.Opacity = 0;
        }

        if (_volumeHudVisual is not null)
        {
            _volumeHudVisual.StopAnimation("Opacity");
            _volumeHudVisual.Opacity = 0f;
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
            _targetPageIndex = toIndex;
            _pageTransitionCleanupTimer?.Stop();

            // Settle all panels before starting a new transition
            for (int i = 0; i < 3; i++)
            {
                var g = GetPanelGrid(i);
                var v = GetPanelVisual(i);
                v.StopAnimation("Translation");
                v.StopAnimation("Opacity");
                g.Translation = Vector3.Zero;
                if (i == fromIndex)
                {
                    g.Visibility = Visibility.Visible;
                    v.Opacity = 1f;
                }
                else
                {
                    g.Visibility = Visibility.Collapsed;
                    v.Opacity = 0f;
                }
            }

            var compositor = _surfaceVisual.Compositor;
            var fadeEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.6f, 1f));
            var springEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));

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

            _pageTransitionCleanupTimer?.Start();
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

    private void OnPageTransitionCleanupTimerTick(DispatcherQueueTimer sender, object e)
    {
        sender.Stop();
        int target = _targetPageIndex;
        for (int i = 0; i < 3; i++)
        {
            var grid = GetPanelGrid(i);
            var visual = GetPanelVisual(i);
            if (i == target)
            {
                grid.Visibility = Visibility.Visible;
                grid.Translation = Vector3.Zero;
                visual.Opacity = 1f;
                visual.StopAnimation("Translation");
                visual.StopAnimation("Opacity");
            }
            else
            {
                grid.Visibility = Visibility.Collapsed;
                grid.Translation = Vector3.Zero;
                visual.Opacity = 0f;
                visual.StopAnimation("Translation");
                visual.StopAnimation("Opacity");
            }
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
                AnimateDot(dot, targetWidth, targetOpacity);
            }
            else
            {
                dot.Width = targetWidth;
                dot.Opacity = targetOpacity;
                if (WindowRoot.Resources.TryGetValue("PrimaryText", out var brushObj) && brushObj is Microsoft.UI.Xaml.Media.Brush brush)
                {
                    dot.Background = brush;
                }
            }
        }
    }

    private void AnimateDot(FrameworkElement dot, double targetWidth, double targetOpacity)
    {
        try
        {
            var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            var widthAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                To = targetWidth,
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EnableDependentAnimation = true,
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

            if (dot is Button btn && WindowRoot.Resources.TryGetValue("PrimaryText", out var brushObj) && brushObj is Microsoft.UI.Xaml.Media.Brush brush)
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

        Surface.Background = Brush(bgColor);
        Surface.BorderBrush = Brush(0x00000000);
        Surface.BorderThickness = new Thickness(0);
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

        double hostWidth = _hostPillWidth + 40.0;

        if (_isIslandHidden)
        {
            double anchorLeft = (HostWidthDip - 48.0) / 2.0;
            double anchorRight = anchorLeft + 48.0;
            double anchorTop = 0.0;
            double anchorBottom = 16.0;

            return localDipX >= anchorLeft && localDipX <= anchorRight &&
                   localDipY >= anchorTop && localDipY <= anchorBottom;
        }

        bool isSplit = _viewModel.IsSplitModeActive && !_viewModel.IsExpanded && !_viewModel.IsCollapsing;

        if (isSplit)
        {
            // Surface bounds: [78, 278], Detached bounds: [288, 346]
            bool insideSurface = localDipX >= 74.0 && localDipX <= 282.0 &&
                                 localDipY >= 0.0 && localDipY <= 44.0;
            bool insideDetached = localDipX >= 284.0 && localDipX <= 350.0 &&
                                  localDipY >= 0.0 && localDipY <= 44.0;
            return insideSurface || insideDetached;
        }

        double pillWidth = _currentPillWidth > 0 ? _currentPillWidth : Surface.ActualWidth;
        if (pillWidth <= 0) pillWidth = 216.0;

        double pillHeight = _currentPillHeight > 0 ? _currentPillHeight : Surface.ActualHeight;
        if (pillHeight <= 0) pillHeight = 40.0;

        double surfaceOffset = Surface.Translation.X;
        double leftDip = (HostWidthDip - pillWidth) / 2.0 + surfaceOffset;
        double rightDip = leftDip + pillWidth;
        double topDip = 0.0;
        double bottomDip = pillHeight;

        return localDipX >= (leftDip - 4.0) && localDipX <= (rightDip + 4.0) &&
               localDipY >= (topDip - 4.0) && localDipY <= (bottomDip + 4.0);
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
