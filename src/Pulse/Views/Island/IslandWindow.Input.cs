using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Pulse.Core.Animation;
using Pulse.Core.Models;
using Pulse.Core.Services;
using Pulse.Services.Glass;
using Pulse.Services.System;
using Pulse.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Pulse.Views.Island;

public sealed partial class IslandWindow
{
    private TimeSpan GetCollapseDelayInterval() =>
        TimeSpan.FromMilliseconds(Math.Clamp(_viewModel.AutoCollapseDelayMs, 300, 3000));

    private void RestartCollapseDelayTimer()
    {
        if (_isContextMenuOpen)
        {
            _collapseDelayTimer.Stop();
            return;
        }

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
    private bool IsCursorCurrentlyInsideIsland()
    {
        if (!GetCursorPos(out var pt) || !IsPointInsideVisibleIsland(pt.X, pt.Y))
        {
            return false;
        }

        // Do not claim pointer ownership when another window (such as Settings) sits in front of the island.
        var hitHwnd = WindowFromPoint(pt);
        if (hitHwnd == IntPtr.Zero)
        {
            return true;
        }

        const uint GA_ROOT = 2;
        return GetAncestor(hitHwnd, GA_ROOT) == _hwnd;
    }

    private void OnCollapseDelayTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (_isDragHovering || _isDraggingOut || _isContextMenuOpen || _isPickingShelfFile ||
            _viewModel.IsQuickNoteOpen || _viewModel.IsQuickLookOpen)
        {
            return;
        }

        if (!_isPointerOverSurface && _viewModel.IsExpanded && _viewModel.AutoCollapse && !_viewModel.IsScrubbing && !_viewModel.PreserveFocusPanel)
        {
            if (IsCursorCurrentlyInsideIsland())
            {
                _isPointerOverSurface = true;
                return;
            }

            _isInteractingWithControl = false;
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
        var nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_frostBusy ||
            nowTicks - _hostResizeGuardTicks < HostResizeGuardTicks ||
            nowTicks - _frostRestoreGuardTicks < HostResizeGuardTicks)
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
        if (!_viewModel.IsQuickNoteOpen && !_isPickingShelfFile)
        {
            _isInteractingWithControl = false;
        }
        _viewModel.OnPointerExited();

        if (_viewModel.IsExpanded && _viewModel.AutoCollapse && !_viewModel.IsScrubbing && !_viewModel.PreserveFocusPanel &&
            !_viewModel.IsQuickNoteOpen && !_viewModel.IsQuickLookOpen && !_isPickingShelfFile && !_isDragHovering && !_isDraggingOut)
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
        if (!_isPointerOverSurface && _viewModel.IsExpanded && _viewModel.AutoCollapse && !_viewModel.IsScrubbing && !_viewModel.PreserveFocusPanel)
        {
            _isInteractingWithControl = false;
            RestartCollapseDelayTimer();
        }
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

    private void ResetCompactPressFeedback()
    {
        _longPressTimer?.Stop();
        _longPressTriggered = false;
        AnimateSurfaceScale(1.0f, 1.0f, 140);
    }

    private void OnMediaSourcePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (IsNonPrimaryRelease(e))
        {
            ResetCompactPressFeedback();
            return;
        }

        ResetCompactPressFeedback();
        // Tapping the artwork/title opens the app that owns the media session (Spotify, browser...).
        e.Handled = true;
        _viewModel.ActivateMediaSourceApp();
    }

    private void OnEventContentPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (IsNonPrimaryRelease(e))
        {
            ResetCompactPressFeedback();
            return;
        }

        var handled = _viewModel.ActivateActiveEventTarget();
        if (handled)
        {
            ResetCompactPressFeedback();
        }
        e.Handled = handled;
    }

    private void OnCompactFocusPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (IsNonPrimaryRelease(e))
        {
            ResetCompactPressFeedback();
            return;
        }

        ResetCompactPressFeedback();
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

    private void OnContextFlyoutOpening(object sender, object e)
    {
        _isContextMenuOpen = true;
        _collapseDelayTimer.Stop();

        if (DoNotDisturbMenuItem is not null)
        {
            DoNotDisturbMenuItem.IsChecked = _viewModel.DoNotDisturb;
        }

        if (StartFocusContextItem is not null)
        {
            StartFocusContextItem.Text = $"Başlat ({_viewModel.FocusDefaultMinutes} Dk)";
        }

        if (MicMuteContextItem is not null)
        {
            MicMuteContextItem.Text = _viewModel.IsMicMuted ? "Mikrofonu Aç" : "Mikrofonu Sessize Al";
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

    private void OnContextFlyoutClosed(object sender, object e)
    {
        _isContextMenuOpen = false;
        if (IsCursorCurrentlyInsideIsland())
        {
            _isPointerOverSurface = true;
            _collapseDelayTimer.Stop();
            return;
        }

        if (!_isPointerOverSurface && _viewModel.IsExpanded && _viewModel.AutoCollapse && !_viewModel.PreserveFocusPanel && !_isDragHovering && !_isDraggingOut)
        {
            RestartCollapseDelayTimer();
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
    {
        _hoverExpandTimer?.Stop();
        _collapseDelayTimer.Stop();
        _isPointerOverSurface = false;
        _isInteractingWithControl = false;
        if (_viewModel.IsExpanded)
        {
            _viewModel.Collapse();
        }
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

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
        if (sender is FrameworkElement el)
        {
            AnimateButtonScale(el, 0.92f);
            el.PointerCaptureLost -= OnButtonPointerCaptureLost;
            el.PointerCaptureLost += OnButtonPointerCaptureLost;
            el.PointerCanceled -= OnButtonPointerCanceled;
            el.PointerCanceled += OnButtonPointerCanceled;
            el.Unloaded -= OnButtonUnloaded;
            el.Unloaded += OnButtonUnloaded;
        }
        // Do not set e.Handled = true: allow ButtonBase to process Click & Command naturally.
    }

    private void ReleaseButtonInteraction(object sender)
    {
        _isInteractingWithControl = false;
        if (sender is FrameworkElement el)
        {
            AnimateButtonScale(el, 1.0f);
            el.PointerCaptureLost -= OnButtonPointerCaptureLost;
            el.PointerCanceled -= OnButtonPointerCanceled;
            el.Unloaded -= OnButtonUnloaded;
        }
    }

    private void OnButtonPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        ReleaseButtonInteraction(sender);

        if (IsNonPrimaryRelease(e))
        {
            return;
        }

        if (!_isPointerOverSurface && _viewModel.IsExpanded && _viewModel.AutoCollapse && !_viewModel.PreserveFocusPanel)
        {
            RestartCollapseDelayTimer();
        }
        // Do not set e.Handled = true: allow ButtonBase to fire Click & Command.
    }

    private void OnButtonPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        => ReleaseButtonInteraction(sender);

    private void OnButtonPointerCanceled(object sender, PointerRoutedEventArgs e)
        => ReleaseButtonInteraction(sender);

    private void OnButtonUnloaded(object sender, RoutedEventArgs e)
        => ReleaseButtonInteraction(sender);

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

}
