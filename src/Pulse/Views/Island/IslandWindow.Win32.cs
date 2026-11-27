using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Pulse.Core.Animation;
using Pulse.Core.Models;
using Pulse.Core.Services;
using Pulse.Services.Glass;
using Pulse.Services.System;

namespace Pulse.Views.Island;

public sealed partial class IslandWindow
{
    private double _hostPillWidth = 216;
    private double _hostPillHeight = 40;

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
    private const uint SwpNoZOrder = 0x0004;
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
    private const int VkShift = 0x10;
    private const int RGN_OR = 2;
    private const uint NotifyForThisSession = 0;
    private const int MdtEffectiveDpi = 0;
    private static readonly IntPtr HwndTopmost = new(-1);
    private IntPtr _companionAboveHwnd;

    /// <summary>
    /// Registers an open companion window (such as Settings) that must remain above the island
    /// in Z-order while visible, instead of being overtaken by ReassertTopmost.
    /// </summary>
    public void SetCompanionAboveWindow(IntPtr hwnd)
    {
        _companionAboveHwnd = hwnd;
        if (!_windowClosed)
        {
            ReassertTopmost();
        }
    }

    private IntPtr GetTargetZOrderInsertAfter(out bool skipZOrderChange)
    {
        var companion = _companionAboveHwnd;
        if (companion != IntPtr.Zero &&
            companion != _hwnd &&
            IsWindow(companion) &&
            IsWindowVisible(companion) &&
            !IsIconic(companion))
        {
            var exStyle = GetWindowLongPtr(companion, GwlExStyle).ToInt64();
            if ((exStyle & WsExTopmost) == 0)
            {
                // Companion temporarily yielded topmost (e.g., native FolderPicker dialog is open).
                skipZOrderChange = true;
                return IntPtr.Zero;
            }

            skipZOrderChange = false;
            return companion;
        }

        // Clear stale handle if the companion window closed or minimized.
        if (companion != IntPtr.Zero && (!IsWindow(companion) || !IsWindowVisible(companion)))
        {
            _companionAboveHwnd = IntPtr.Zero;
        }

        skipZOrderChange = false;
        return HwndTopmost;
    }

    private string? TryGetProcessNameFast(uint pid)
    {
        if (pid == 0)
        {
            return null;
        }

        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var size = _processPathBuffer.Length;
            if (QueryFullProcessImageName(handle, 0, _processPathBuffer, ref size) && size > 0)
            {
                var fullPath = new string(_processPathBuffer, 0, size);
                return Path.GetFileNameWithoutExtension(fullPath);
            }
        }
        finally
        {
            _ = CloseHandle(handle);
        }

        return null;
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private readonly char[] _processPathBuffer = new char[512];

    /// <summary>Keeps the island above desktop apps without overtaking an open Settings window.</summary>
    public void ReassertTopmost()
    {
        if (_hwnd == IntPtr.Zero || (_isIslandHidden && !_hideAnchorVisible))
        {
            return;
        }

        var insertAfter = GetTargetZOrderInsertAfter(out var skipZOrder);
        if (skipZOrder)
        {
            return;
        }

        _ = SetWindowPos(_hwnd, insertAfter, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    /// <summary>Guarantees the native HWND is shown without disrupting active Composition animations.</summary>
    public void EnsureNativeWindowVisible()
    {
        if (_hwnd == IntPtr.Zero || _windowClosed)
        {
            return;
        }

        if (_fullscreenActive)
        {
            _userOverrodeFullscreenHide = true;
        }

        if (!_isIslandHidden)
        {
            _ = ShowWindow(_hwnd, 4); // SW_SHOWNOACTIVATE
            ReassertTopmost();
        }
    }

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

        string? processName = null;
        string? windowTitle = null;
        try
        {
            _ = GetWindowThreadProcessId(foreground, out var pid);
            processName = TryGetProcessNameFast(pid);
        }
        catch { }

        try
        {
            var titleLen = GetWindowText(foreground, _titleBuffer, _titleBuffer.Length);
            if (titleLen > 0)
            {
                windowTitle = new string(_titleBuffer, 0, titleLen);
            }
        }
        catch { }

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
            ProcessName: processName,
            WindowTitle: windowTitle,
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

    private bool IsForegroundScreenshotOverlay()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == _hwnd) return false;

        string? className = null;
        var length = GetClassName(foreground, _classNameBuffer, _classNameBuffer.Length);
        if (length > 0)
        {
            className = new string(_classNameBuffer, 0, length);
        }

        string? processName = null;
        string? windowTitle = null;
        try
        {
            _ = GetWindowThreadProcessId(foreground, out var pid);
            processName = TryGetProcessNameFast(pid);
        }
        catch { }

        try
        {
            var titleLen = GetWindowText(foreground, _titleBuffer, _titleBuffer.Length);
            if (titleLen > 0)
            {
                windowTitle = new string(_titleBuffer, 0, titleLen);
            }
        }
        catch { }

        var candidate = new FullscreenWindowCandidate(
            Handle: foreground,
            OwnHandle: _hwnd,
            ClassName: className,
            ProcessName: processName,
            WindowTitle: windowTitle,
            IsVisible: true,
            IsMinimized: false,
            IsMaximized: false,
            IsCloaked: false,
            ExStyle: 0,
            Left: 0,
            Top: 0,
            Right: 0,
            Bottom: 0,
            MonitorLeft: 0,
            MonitorTop: 0,
            MonitorRight: 0,
            MonitorBottom: 0,
            IsSameMonitorAsIsland: true);

        return FullscreenWindowEvaluator.IsScreenshotOverlay(candidate);
    }

    private bool IsForegroundMonitorSameAsIsland()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || _hwnd == IntPtr.Zero)
        {
            return false;
        }

        var islandMonitor = MonitorFromWindow(_hwnd, MonitorDefaultToNearest);
        if (islandMonitor == IntPtr.Zero)
        {
            return true;
        }

        var fgMonitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        return fgMonitor == IntPtr.Zero || fgMonitor == islandMonitor;
    }

    private bool IsExclusiveD3DFullscreenRunning()
    {
        try
        {
            return SHQueryUserNotificationState(out var state) == 0 &&
                   state == FullscreenNotificationState.RunningD3DFullScreen &&
                   IsForegroundMonitorSameAsIsland();
        }
        catch
        {
            return false;
        }
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
                if (!IsForegroundScreenshotOverlay())
                {
                    if (SHQueryUserNotificationState(out var state) == 0)
                    {
                        if (state == FullscreenNotificationState.RunningD3DFullScreen)
                        {
                            rawIsFullscreen = IsForegroundMonitorSameAsIsland();
                        }
                        else if (FullscreenNotificationState.IsFullscreen(state))
                        {
                            // Busy (2) or PresentationMode (4) can occur during Windows boot, user logon,
                            // or Focus Assist/DND. Never auto-hide for desktop/taskbar/explorer.
                            // Only hide if an actual foreground window is verified covering the monitor.
                            rawIsFullscreen = IsBorderlessFullscreenForeground();
                        }
                        else
                        {
                            rawIsFullscreen = IsBorderlessFullscreenForeground();
                        }
                    }
                    else
                    {
                        rawIsFullscreen = IsBorderlessFullscreenForeground();
                    }
                }
            }
            catch
            {
                // Shell API is best-effort; a failure must never break the island.
            }

            // During initial startup grace period (first 15 seconds), protect the island from
            // disappearing during Windows logon/boot transitions unless an exclusive D3D game is active.
            var isStartupGracePeriod = (System.Diagnostics.Stopwatch.GetTimestamp() - _launchTicks) <
                                       (15 * System.Diagnostics.Stopwatch.Frequency);
            if (isStartupGracePeriod && !IsExclusiveD3DFullscreenRunning())
            {
                rawIsFullscreen = false;
            }

            var isFullscreen = _fullscreenTracker.Update(rawIsFullscreen);
            var wasFullscreen = _fullscreenActive;
            _fullscreenActive = isFullscreen;

            if (wasFullscreen && !isFullscreen)
            {
                _userOverrodeFullscreenHide = false;
            }

            if (isFullscreen && !_isIslandHidden && !_userOverrodeFullscreenHide &&
                !_viewModel.IsExpanded && !_isPointerOverSurface && !_isInteractingWithControl && !_viewModel.IsScrubbing)
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

    private void PositionHostWindow(double? explicitDpiScale = null)
    {
        ApplyHostWindowSize(_currentPillWidth, _currentPillHeight, explicitDpiScale);
        UpdateWindowRegion(_currentPillWidth, _currentPillHeight, _currentCornerRadius > 0 ? _currentCornerRadius : 20.0);
    }

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
        var insertAfter = GetTargetZOrderInsertAfter(out var skipZOrder);
        var flags = SwpNoActivate | (skipZOrder ? SwpNoZOrder : 0);
        _ = SetWindowPos(_hwnd, insertAfter, hostBounds.X, hostBounds.Y, hostBounds.Width, hostBounds.Height, flags);

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

        bool isExpandedMode = _viewModel.IsExpanded || (ExpandedView is not null && ExpandedView.Visibility == Visibility.Visible);
        if (isExpandedMode && !_viewModel.IsCollapsing)
        {
            pillWidth = Math.Max(pillWidth, 384.0);
            pillHeight = Math.Max(pillHeight, 240.0);
            cornerRadius = Math.Max(cornerRadius, 56.0);
        }

        if (_isIslandHidden && Surface.Visibility != Visibility.Visible)
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

    // Win32 Styles & Border/Shadow Stripping
    private IntPtr _previousForegroundHwndBeforeInput;
    private bool _isTextInputActivationEnabled;

    /// <summary>
    /// Temporarily allows Win32 window activation while an in-island TextBox (Quick Note) is open
    /// so Windows routes keyboard input into the WinUI input site, then restores WS_EX_NOACTIVATE.
    /// </summary>
    private void SetTextInputActivationEnabled(bool enabled)
    {
        if (_hwnd == IntPtr.Zero || _windowClosed || _isTextInputActivationEnabled == enabled)
        {
            return;
        }

        _isTextInputActivationEnabled = enabled;
        try
        {
            var exStyle = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
            if (enabled)
            {
                var currentFg = GetForegroundWindow();
                if (currentFg != IntPtr.Zero && currentFg != _hwnd)
                {
                    _previousForegroundHwndBeforeInput = currentFg;
                }

                SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(exStyle & ~WsExNoActivate));
                SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
                Activate();
                _ = SetForegroundWindow(_hwnd);
            }
            else
            {
                SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(exStyle | WsExNoActivate));
                var insertAfter = GetTargetZOrderInsertAfter(out var skipZOrder);
                SetWindowPos(_hwnd, insertAfter, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged | (skipZOrder ? SwpNoZOrder : 0));

                var prev = _previousForegroundHwndBeforeInput;
                _previousForegroundHwndBeforeInput = IntPtr.Zero;
                if (prev != IntPtr.Zero && prev != _hwnd && GetForegroundWindow() == _hwnd && IsWindow(prev) && IsWindowVisible(prev))
                {
                    _ = SetForegroundWindow(prev);
                }
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("IslandWindow.SetTextInputActivationEnabled", ex);
        }
    }

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

        var insertAfter = GetTargetZOrderInsertAfter(out var skipZOrder);
        SetWindowPos(_hwnd, insertAfter, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged | (skipZOrder ? SwpNoZOrder : 0));
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
                _viewModel.EventRouter.SetPresentationPaused(true);
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
                    _viewModel.EventRouter.SetPresentationPaused(_isIslandHidden);
                    ScheduleDisplaySettle();
                }
            }
            return IntPtr.Zero;
        }

        if (uMsg == WM_WINDOWPOSCHANGING)
        {
            // Keep topmost above general apps, and never interfere with WinUI's internal Z-order chain
            // while a companion Settings window (or its modal dialog) is active.
            var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((wp.flags & SwpNoZOrder) == 0)
            {
                var insertAfter = GetTargetZOrderInsertAfter(out var skipZOrder);
                if (skipZOrder || insertAfter != HwndTopmost)
                {
                    wp.flags |= SwpNoZOrder;
                }
                else
                {
                    wp.hwndInsertAfter = HwndTopmost;
                }
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

        const uint WM_MOUSEACTIVATE = 0x0021;
        const uint WM_KILLFOCUS = 0x0008;
        const uint WM_CAPTURECHANGED = 0x0215;
        if (uMsg == WM_MOUSEACTIVATE)
        {
            const int MA_ACTIVATE = 1;
            const int MA_NOACTIVATE = 3;
            return new IntPtr((_isTextInputActivationEnabled || _viewModel.IsQuickNoteOpen) ? MA_ACTIVATE : MA_NOACTIVATE);
        }

        if (uMsg == WM_KILLFOCUS || uMsg == WM_CAPTURECHANGED)
        {
            if (!_viewModel.IsQuickNoteOpen && !_isPickingShelfFile)
            {
                _isInteractingWithControl = false;
            }
            if (!_isContextMenuOpen && !_isPointerOverSurface && _viewModel.IsExpanded && _viewModel.AutoCollapse &&
                !_viewModel.PreserveFocusPanel && !_viewModel.IsQuickNoteOpen && !_viewModel.IsQuickLookOpen &&
                !_isPickingShelfFile && !_isDragHovering && !_isDraggingOut)
            {
                RestartCollapseDelayTimer();
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
            var (screenX, screenY) = IslandSurfaceLayout.ExtractSignedScreenCoordinates(lParam);

            if (!_viewModel.IsScrubbing && !IsPointInsideVisibleIsland(screenX, screenY))
            {
                const int HTTRANSPARENT = -1;
                return new IntPtr(HTTRANSPARENT);
            }

            var hit = DefSubclassProc(hWnd, uMsg, wParam, lParam);
            // If the point is inside the visible island bounds but DefSubclassProc fell through to transparent/nowhere,
            // promote it to HTCLIENT so clicks never pass through the island.
            if (hit.ToInt64() == -1 || hit.ToInt64() == 0)
            {
                const int HTCLIENT = 1;
                return new IntPtr(HTCLIENT);
            }
            return hit;
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

        bool isExpandedMode = _viewModel.IsExpanded || (ExpandedView is not null && ExpandedView.Visibility == Visibility.Visible);
        bool isSplit = _viewModel.IsSplitModeActive && !isExpandedMode && !_viewModel.IsCollapsing;
        double pillWidth = _currentPillWidth > 0 ? _currentPillWidth : 216.0;
        double pillHeight = _currentPillHeight > 0 ? _currentPillHeight : 40.0;
        if (isExpandedMode)
        {
            pillWidth = Math.Max(pillWidth, 384.0);
            pillHeight = Math.Max(pillHeight, 240.0);
        }

        double detachedWidth = (DetachedPill is not null && DetachedPill.Width > 0)
            ? DetachedPill.Width
            : IslandSurfaceLayout.DetachedPillWidthDip;
        double detachedHeight = (DetachedPill is not null && DetachedPill.Height > 0)
            ? DetachedPill.Height
            : IslandSurfaceLayout.DetachedPillHeightDip;

        bool isEffectivelyHidden = _isIslandHidden && (Surface is null || Surface.Visibility != Visibility.Visible);

        return IslandSurfaceLayout.IsPointInsideIsland(
            localDipX: localDipX,
            localDipY: localDipY,
            hostWidth: IslandSurfaceLayout.DefaultHostWidthDip,
            isHidden: isEffectivelyHidden,
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

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, [Out] char[] lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point pt);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Win32Point pt);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Win32Point pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowText(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, uint dwFlags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

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
