# GPU-Composited Zero-Reflow Island Rendering Design

**Date:** 2026-11-10
**Target:** `Pulse` (WinUI 3 / .NET 9 / Windows App SDK 2.5.1)

## 1. Context & Findings
`prompt.md` specifies a 144Hz–360Hz stutter-free rendering pipeline. Several concepts in `prompt.md` used WPF terminology (`AllowsTransparency`, `HwndSourceHook`, `PointFromScreen`), whereas `Pulse` is native WinUI 3 using `WinUIEx.TransparentTintBackdrop`, `SetWindowSubclass`, `SetWindowRgn`, and `Microsoft.UI.Composition`.

Codebase audit revealed four remaining bottlenecks in the current WinUI 3 pipeline:
1. **Per-frame XAML layout thrashing:** `OnDimensionRendering` mutates `Surface.Width`, `Surface.Height`, `CornerRadius`, and `Frost*` properties every frame, while `ExpandedView` has no fixed dimensions — triggering full `Measure`/`Arrange` passes at up to 360Hz.
2. **Velocity loss on mid-flight retargeting:** `AnimateSurfaceDimensions` resets spring velocity to `0.0` on interruption, `DampedSpringOscillator` hardcodes `0.0` velocity for `dampingRatio >= 1.0`, and `AnimationProfile.Damping` is never applied.
3. **Unclamped timing & dependent dot animation:** Frame stepping uses raw `now - start` without `dt` clamping (`[0.0005, 0.033]`), `DampedSpringOscillator` uses `0.25` settle tolerance on `[0..1]` opacity, and `AnimateDot` runs a CPU-bound `EnableDependentAnimation` Storyboard.
4. **Tight canvas & hardcoded split coordinates:** `HostWidthDip = 424` leaves minimal overshoot margin and hardcodes split hit-test bounds.

## 2. Architecture
1. **Fixed Transparent Canvas (`560x300` DIP):** `WindowRoot` and native HWND stay locked at `560x300` DIPs, repositioned only on startup, settings change, `WM_DPICHANGED`, or `WM_DISPLAYCHANGE`.
2. **Zero-Reflow Composition Pill (`PillMaskHost` + `CompositionRoundedRectangleGeometry`):**
   - `PillBackdrop`, `FrostLayer`, `FrostTintLayer`, `FrostSheen` live inside a fixed `560x300` container clipped exclusively on the GPU via `CompositionRoundedRectangleGeometry` (`Offset`, `Size`, `CornerRadius`).
   - `ExpandedView` is locked to `384x240` with `CacheMode="BitmapCache"` on panels and animates purely via GPU `Opacity` and `Scale`.
   - `CompactView` updates its fixed size once at state change (never per frame).
3. **Unified `dt`-Clamped VSync Spring Loop:**
   - Single `CompositionTarget.Rendering` handler steps both surface morph springs and page transition springs using `dt = Math.Clamp(rawDt, 0.0005, 0.033)`.
   - Full velocity preservation on mid-flight retargeting across all channels (`width`, `height`, `cornerRadius`, `leadingX`, `trailingX`, `detachedOpacity`, `expandedScale`, page `X/Opacity/Scale`).
   - Active `AnimationProfile.Damping` applied to spring physics; separate pixel vs normalized `[0..1]` tolerances; GPU-composited page dots; immediate unsubscribe when all springs settle.
4. **Unified Hit-Testing & Region Sync (`IslandSurfaceLayout`):**
   - Pure geometry calculator in `Pulse.Core.Animation` drives both `SetWindowRgn` (with overshoot headroom during expansion and deferred shrink snap on collapse) and `WM_NCHITTEST` (`HTTRANSPARENT` outside pill bounds).

