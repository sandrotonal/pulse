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
    private Visual GetPanelVisual(int index) => index switch
    {
        0 => _mediaPanelVisual,
        1 => _focusPanelVisual,
        2 => _systemPanelVisual,
        3 => _shelfPanelVisual,
        _ => _mediaPanelVisual
    };

    private Grid GetPanelGrid(int index) => index switch
    {
        0 => MediaExpandedPanel,
        1 => FocusExpandedPanel,
        2 => SystemExpandedPanel,
        3 => ShelfExpandedPanel,
        _ => MediaExpandedPanel
    };

    private void OnPageDot0Click(object sender, RoutedEventArgs e) => NavigateToPage(0, 0 > _viewModel.SelectedPageIndex);
    private void OnPageDot1Click(object sender, RoutedEventArgs e) => NavigateToPage(1, 1 > _viewModel.SelectedPageIndex);
    private void OnPageDot2Click(object sender, RoutedEventArgs e) => NavigateToPage(2, 2 > _viewModel.SelectedPageIndex);
    private void OnPageDot3Click(object sender, RoutedEventArgs e) => NavigateToPage(3, 3 > _viewModel.SelectedPageIndex);

    private void OnSurfacePointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_isIslandHidden || !_viewModel.IsExpanded)
        {
            return;
        }

        var point = e.GetCurrentPoint(Surface);
        var delta = point.Properties.MouseWheelDelta;

        // When viewing a populated Shelf with horizontal overflow, route wheel smoothly to horizontal card scroll!
        if (_viewModel.SelectedPageIndex == 3 &&
            ShelfScrollViewer is { ScrollableWidth: > 1.0 })
        {
            var targetOffset = Math.Clamp(
                ShelfScrollViewer.HorizontalOffset - (delta * 0.65),
                0.0,
                ShelfScrollViewer.ScrollableWidth);
            if (Math.Abs(targetOffset - ShelfScrollViewer.HorizontalOffset) > 0.5)
            {
                _collapseDelayTimer.Stop();
                ShelfScrollViewer.ChangeView(targetOffset, null, null, disableAnimation: false);
                e.Handled = true;
                return;
            }
        }

        var step = _wheelAccumulator.Accumulate(delta);
        if (step == 0)
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
        if (step < 0)
        {
            int nextIndex = (currentIndex + 1) % ExpandedPageCount;
            NavigateToPage(nextIndex, isForward: true);
        }
        else
        {
            int prevIndex = (currentIndex - 1 + ExpandedPageCount) % ExpandedPageCount;
            NavigateToPage(prevIndex, isForward: false);
        }

        if (!_isPointerOverSurface && _viewModel.AutoCollapse && !_viewModel.PreserveFocusPanel && !_isDragHovering && !_isDraggingOut)
        {
            RestartCollapseDelayTimer();
        }
    }

    private void NavigateToPage(int targetIndex, bool isForward)
    {
        targetIndex = Math.Clamp(targetIndex, 0, ExpandedPageCount - 1);
        int fromIndex = _targetPageIndex;
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
        for (int i = 0; i < ExpandedPageCount; i++)
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

        for (int i = 0; i < ExpandedPageCount; i++)
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
            for (int i = 0; i < ExpandedPageCount; i++)
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

        for (int i = 0; i < ExpandedPageCount; i++)
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

        for (int i = 0; i < ExpandedPageCount; i++)
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

    private const double DotStripStartXDip = 6.5;

    private void UpdatePageDots(int activeIndex, bool animated)
    {
        double[] targets =
        [
            activeIndex == 0 ? 18.0 : 6.0,
            activeIndex == 1 ? 18.0 : 6.0,
            activeIndex == 2 ? 18.0 : 6.0,
            activeIndex == 3 ? 18.0 : 6.0
        ];
        double[] targetOpacities =
        [
            activeIndex == 0 ? 1.0 : 0.35,
            activeIndex == 1 ? 1.0 : 0.35,
            activeIndex == 2 ? 1.0 : 0.35,
            activeIndex == 3 ? 1.0 : 0.35
        ];

        if (WindowRoot.Resources.TryGetValue("PrimaryText", out var brushObj) && brushObj is Microsoft.UI.Xaml.Media.Brush brush)
        {
            for (int i = 0; i < ExpandedPageCount; i++)
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
            for (int i = 0; i < ExpandedPageCount; i++)
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

        for (int i = 0; i < ExpandedPageCount; i++)
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

        for (int i = 0; i < ExpandedPageCount; i++)
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
        for (int i = 0; i < ExpandedPageCount; i++)
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
            for (int i = 0; i < ExpandedPageCount; i++)
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
}
