# GPU-Composited Zero-Reflow Island Rendering Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or TDD cycle task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate per-frame XAML layout thrashing, preserve mid-flight spring momentum across all animations, and drive a deterministic `dt`-clamped 144Hz–360Hz Composition rendering pipeline in WinUI 3.

**Architecture:** Extract spring physics, `dt` clamping, and dynamic canvas/split geometry into pure `Pulse.Core.Animation` units covered by unit tests, then refactor `IslandWindow.xaml` and `IslandWindow.xaml.cs` to drive pill morphing via GPU `CompositionRoundedRectangleGeometry` and fixed inner view dimensions.

**Tech Stack:** .NET 9, WinUI 3 (`Microsoft.WindowsAppSDK` 2.5.1), `Microsoft.UI.Composition`, Win32 (`SetWindowRgn`, `WM_NCHITTEST`, `WM_DPICHANGED`), xUnit.

**Spec:** `docs/superpowers/specs/2026-11-10-gpu-composition-island-rendering-design.md`

---

### Task 1: Enhance `DampedSpringOscillator` & Add `FrameStepClock` (TDD)
- **Files:** `src/Pulse/Core/Animation/DampedSpringOscillator.cs`, `tests/Pulse.Tests/Animation/DampedSpringOscillatorTests.cs`
- [ ] Write failing tests for mid-flight velocity preservation, critically/overdamped velocity calculation, normalized tolerance (`[0..1]`), and `dt` clamping (`[0.0005, 0.033]`).
- [ ] Verify RED (`dotnet test`).
- [ ] Implement fixes in `DampedSpringOscillator.cs`.
- [ ] Verify GREEN (`dotnet test`).

### Task 2: Extract `IslandSurfaceLayout` Geometry & Hit-Test Calculator (TDD)
- **Files:** `src/Pulse/Core/Animation/IslandSurfaceLayout.cs`, `tests/Pulse.Tests/Animation/IslandSurfaceLayoutTests.cs`
- [ ] Write failing tests for centered pill bounds, split capsule offsets at scale 1.0 and 1.25, `560x300` host canvas, hit-testing inside/outside bounds, and expansion overshoot region padding.
- [ ] Verify RED (`dotnet test`).
- [ ] Implement `IslandSurfaceLayout.cs`.
- [ ] Verify GREEN (`dotnet test`).

### Task 3: Zero-Reflow XAML & GPU Composition Refactor (`IslandWindow.xaml` & `IslandWindow.xaml.cs`)
- **Files:** `src/Pulse/Views/Island/IslandWindow.xaml`, `src/Pulse/Views/Island/IslandWindow.xaml.cs`
- [ ] Lock `WindowRoot` & `HostWidthDip/HostHeightDip` to `560x300`; move visual backdrop & frost layers into `PillMaskHost` clipped by `_pillClipGeometry`.
- [ ] Lock `ExpandedView` to `384x240` with `BitmapCache` on panels; size `CompactView` only on state transitions; replace `AnimateDot` dependent Storyboard with GPU clip/opacity.
- [ ] Unify `CompositionTarget.Rendering` frame loop with clamped `dt`, active `AnimationProfile.Damping`, mid-flight velocity preservation, and zero idle CPU drain.
- [ ] Wire `IslandSurfaceLayout` into `UpdateWindowRegion`, `IsPointInsideVisibleIsland`, and handle `WM_DPICHANGED`/`WM_DISPLAYCHANGE`.

### Task 4: Build, Test & `pulse-ui-smoke` Live Verification
- [ ] Run `dotnet build Pulse.sln -c Release` (0 new warnings, 0 errors).
- [ ] Run `dotnet test tests\Pulse.Tests\Pulse.Tests.csproj -c Release --no-build`.
- [ ] Run `pulse-ui-smoke` (backup settings, verify `GetWindowRgn`, DPI, hover/expand/collapse/split, screenshot evidence, `crash.log`, restore settings).

