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

        if (_viewModel.IsHeadphonesConnected)
        {
            try
            {
                _headphoneBeaconHaloVisual.StopAnimation("Opacity");
                _headphoneBeaconHaloVisual.StopAnimation("Scale");
                _headphoneBeaconHaloVisual.Opacity = 0.35f;
                _headphoneBeaconHaloVisual.Scale = Vector3.One;
            }
            catch { }
        }

        if (ShelfDropBobIcon is not null)
        {
            try
            {
                ElementCompositionPreview.GetElementVisual(ShelfDropBobIcon).StopAnimation("Translation");
                ShelfDropBobIcon.Translation = Vector3.Zero;
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
        if (!_isRenderLoopSubscribed || _isDimensionAnimating || _isPageAnimating || _isDotAnimating || _isAmbientColorAnimating)
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

        if (_isAmbientColorAnimating)
        {
            StepAmbientColorTransition(dt);
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
                ResetDetachedVisualOpacity(null);
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
            ResetDetachedVisualOpacity(null);
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

        _lastSplitModeActive = _viewModel.IsSplitModeActive;
        _lastFocusTimerActive = _viewModel.IsFocusTimerActive;
        _lastNotificationActive = _viewModel.IsNotificationActive;
        _lastFileOpActive = _viewModel.IsFileOperationActive;

        if (!isExpanded)
        {
            _isInteractingWithControl = false;
        }

        var enteringExpanded = isExpanded && ExpandedView.Visibility != Visibility.Visible;
        if (enteringExpanded)
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

            if (_viewModel.SelectedPageIndex == 2)
            {
                _viewModel.RefreshSystemAudio();
            }

            // If expanded programmatically (hotkey / tray / second-launch signal) while the cursor
            // is away from the island, arm a generous auto-collapse grace timer so it never stays stuck open.
            if (!_isPointerOverSurface && _viewModel.AutoCollapse && !_viewModel.PreserveFocusPanel)
            {
                _collapseDelayTimer.Stop();
                _collapseDelayTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(3500.0, GetCollapseDelayInterval().TotalMilliseconds));
                _collapseDelayTimer.Start();
            }
        }

        bool isSplit = _viewModel.IsSplitModeActive && !isExpanded && !isCollapsing;

        var metrics = IslandSurfaceLayout.ComputeCompactMetrics(_viewModel.IslandScale);
        double targetWidth = isExpanded
            ? 384.0
            : IslandPresentationPolicy.ComputeCompactTargetWidth(
                state,
                hasTrack,
                isSplit,
                _viewModel.IsNotificationActive,
                _viewModel.IsFileOperationActive,
                _viewModel.IsFocusTimerActive,
                _viewModel.HasDeveloperLiveSplitBadge) * metrics.Scale;

        double targetHeight = isExpanded ? 240.0 : metrics.PillHeight;
        // Continuous island corners: large enough to read as an Apple surface
        // instead of a generic rounded card, still inside the content insets.
        double targetCornerRadius = isExpanded ? 56.0 : metrics.PillCornerRadius;

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
                ResetDetachedVisualOpacity(null);
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
                for (int i = 0; i < ExpandedPageCount; i++)
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

            if (!_isIslandHidden)
            {
                _pillMaskVisual.StopAnimation("Scale");
                _pillMaskVisual.Scale = Vector3.One;
                _pillMaskVisual.Opacity = 1;
            }
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
                ResetDetachedVisualOpacity(null);
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
                for (int i = 0; i < ExpandedPageCount; i++)
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
                ResetDetachedVisualOpacity(null);
                DetachedPill.Opacity = targetDetachedOpacity;
                DetachedPill.Visibility = isSplit && !_isIslandHidden ? Visibility.Visible : Visibility.Collapsed;
            }
            _compactVisual.Opacity = isExpanded ? 0 : 1;
            _expandedVisual.Opacity = isExpanded ? 1 : 0;
            ExpandedView.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
            UpdateWindowRegion(targetWidth, targetHeight, targetCornerRadius);
            if (isCollapsing && generation == _transitionGeneration && _viewModel.CurrentIslandState == IslandState.Collapsing)
            {
                _viewModel.CompleteCollapse();
            }
        }
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

    private void UpdateHeadphoneBeaconAnimation()
    {
        if (!_viewModel.IsHeadphonesConnected)
        {
            if (!_headphoneBeaconAnimating) return;
            _headphoneBeaconAnimating = false;

            try
            {
                _headphoneBeaconHaloVisual.StopAnimation("Opacity");
                _headphoneBeaconHaloVisual.StopAnimation("Scale");
            }
            catch { }

            return;
        }

        if (ReducedMotion)
        {
            _headphoneBeaconAnimating = false;
            ApplyStaticAmbientStatesIfReducedMotion();
            return;
        }

        if (_headphoneBeaconAnimating) return;
        _headphoneBeaconAnimating = true;

        try
        {
            var visual = _headphoneBeaconHaloVisual;
            var compositor = visual.Compositor;
            var easing = compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.42f, 0f), new Vector2(0.58f, 1f));

            visual.CenterPoint = new Vector3(
                (float)(HeadphoneBeaconHalo.Width / 2),
                (float)(HeadphoneBeaconHalo.Height / 2), 0f);

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
            scale.InsertKeyFrame(1f, new Vector3(1.20f, 1.20f, 1f), easing);
            visual.StartAnimation("Scale", scale);
        }
        catch
        {
            _headphoneBeaconAnimating = false;
        }
    }
}
