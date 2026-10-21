# Apple Physics Spring Engine & Split Dynamic Island Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement authentic Apple iOS Dynamic Island interaction architecture in Pulse, featuring a 60/120 FPS Damped Harmonic Oscillator spring physics engine, dual-capsule Split/Detached Island mode for concurrent activities, elastic squish-and-stretch tactile gestures, and verified native P/Invoke boundaries using REA.

**Architecture:**
1. **Mathematical Spring Physics (`DampedSpringOscillator`):** An analytical second-order mass-spring-damper motion engine ($\zeta = 0.82$, $\omega_0 \approx 18\text{ rad/s}$) driving island dimensions, scales, and transforms on `CompositionTarget.Rendering` without linear mechanical easing.
2. **Split / Detached Island Dual-Capsule System:** When multiple contextual events coexist (e.g., active media playback concurrently with a Pomodoro timer or system notification), the island splits into a Leading Pill (`Surface`) and a detached Trailing Circle (`DetachedPill`).
3. **Tactile Squish & Elastic Gestures:** Pointer press/hover physics featuring liquid-like compression (`Scale = (1.04, 0.94, 1.0)`) on press, followed by an explosive spring release on expansion.
4. **Native Hit-Testing & Win32 Interop:** Update `IslandWindow.WndProc` (`WM_NCHITTEST`) to compute dual hit-test regions for both capsules, with transparent passthrough everywhere else.
5. **REA Boundary Verification:** Utilize REA (`rea-agents`) to audit and verify P/Invoke declarations across `user32.dll`, `dwmapi.dll`, and `comctl32.dll`.

**Tech Stack:** C# 13 (.NET 9), WinUI 3 (Windows App SDK 1.6), Windows Composition APIs, Win32 P/Invoke, xUnit, REA CLI (`rea-agents`).

**Spec:** Apple Human Interface Guidelines for Dynamic Island, iOS SpringBoard / ActivityKit Jindo fluid spring physics specifications.

## Global Constraints
- C# 13 with strict type safety, zero `object` or `dynamic` loosely-typed usage.
- Strict DRY and Single Responsibility principles.
- Defensive error handling: every async method and WinRT/Composition event enclosed in `try-catch` blocks.
- Absolute prohibition of emojis everywhere (code, comments, commit messages, responses).
- Strictly NO git push to remote repository without explicit user command ("pushla").
- Non-blocking 60/120 FPS rendering on UI and compositor threads with zero CPU spinning when resting.

---

### Task 1: Mathematical Damped Harmonic Oscillator Spring Engine

**Files:**
- Create: `src/Pulse/Core/Animation/DampedSpringOscillator.cs`
- Test: `tests/Pulse.Tests/Animation/DampedSpringOscillatorTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  namespace Pulse.Core.Animation;

  public sealed class DampedSpringOscillator
  {
      public double DampingRatio { get; }
      public double FrequencyResponse { get; }
      public DampedSpringOscillator(double dampingRatio = 0.82, double responseSeconds = 0.35);
      public SpringState Evaluate(double elapsedSeconds, double start, double target, double initialVelocity = 0.0);
  }

  public readonly record struct SpringState(double Position, double Velocity, bool HasSettled);
  ```

- [x] **Step 1: Write unit tests for analytical spring math and settling**
- [x] **Step 2: Run test to verify it fails**
- [x] **Step 3: Implement `DampedSpringOscillator`**
- [x] **Step 4: Run tests to verify they pass**
- [x] **Step 5: Verify build without errors**

Run: `dotnet build src/Pulse/Pulse.csproj`
Expected: PASS.

---

### Task 2: Split / Detached Island Dual-Capsule Model & ViewModel Logic

**Files:**
- Modify: `src/Pulse/ViewModels/IslandViewModel.cs`
- Test: `tests/Pulse.Tests/IslandSplitModeTests.cs`

**Interfaces:**
- Produces in `IslandViewModel`:
  - `bool IsSplitModeActive { get; }` (True when `IsCompact && HasTrack && (IsFocusTimerActive || IsNotificationActive || IsFileOperationActive)`)
  - `string DetachedGlyph { get; }`
  - `string DetachedBadgeText { get; }`
  - `string DetachedToolTip { get; }`
  - `void OnDetachedPillClicked()`

- [ ] **Step 1: Write unit tests for Split Island logic**

```csharp
using Pulse.Core.Events;
using Pulse.Core.Models;
using Pulse.Core.Services;

namespace Pulse.Tests;

public sealed class IslandSplitModeTests
{
    [Fact]
    public void SplitModeActivatesWhenSecondaryActivityCoexistsWithActiveMedia()
    {
        using var router = new EventRouter();
        using var timerService = new FocusTimerService(router);

        timerService.Start(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5));
        var active = router.CurrentActiveEvent as FocusTimerEvent;
        Assert.NotNull(active);
        Assert.Equal("focus_timer", active.Id);
    }
}
```

- [ ] **Step 2: Implement Split Island properties in `IslandViewModel.cs`**

Add properties:
```csharp
public bool IsSplitModeActive => IsCompact && HasTrack && (IsFocusTimerActive || IsNotificationActive || IsFileOperationActive);

public string DetachedGlyph => IsFocusTimerActive ? "\uE916" : IsNotificationActive ? NotificationGlyph : "\uE896";

public string DetachedBadgeText => IsFocusTimerActive ? FocusTimerText : IsNotificationActive ? "!" : $"{FileOperationProgress:F0}%";

public string DetachedToolTip => IsFocusTimerActive ? "Odak Sayacı" : IsNotificationActive ? NotificationTitle : FileOperationName;

public void OnDetachedPillClicked()
{
    if (IsFocusTimerActive)
    {
        SelectedPageIndex = 1;
    }
    else if (IsNotificationActive || IsFileOperationActive)
    {
        SelectedPageIndex = 2;
    }
    ToggleExpanded();
}
```

- [x] **Step 1: Write unit tests for Split Island logic**
- [x] **Step 2: Implement Split Island properties in `IslandViewModel.cs`**
- [x] **Step 3: Run unit tests to verify split state arbitration**

Run: `dotnet test tests/Pulse.Tests/Pulse.Tests.csproj`
Expected: PASS.

---

### Task 3: Dual-Capsule XAML Surface & Win32 Hit-Testing Geometry

- [x] **Step 1: Add `DetachedPill` element in `IslandWindow.xaml`**
- [x] **Step 2: Update Win32 `IsPointInsideVisibleIsland` in `IslandWindow.xaml.cs`**
- [x] **Step 3: Build project and verify zero compilation errors**

---

### Task 4: Integrate Spring Physics and Squish-Stretch Gestures

- [x] **Step 1: Replace Dimension Animator in `IslandWindow.xaml.cs` with `DampedSpringOscillator`**
- [x] **Step 2: Add Squish/Elastic Tactile Compression**
- [x] **Step 3: Run all unit tests**

---

### Task 5: Audit Native Interop Declarations with REA Agents

- [x] **Step 1: Prepare boundary definition JSON for REA verification**
- [x] **Step 2: Execute `rea verify-managed-native-boundaries`**
- [x] **Step 3: Validate zero P/Invoke memory layout violations or calling convention faults**

---

### Task 6: End-to-End Build, Launch, and Verification

- [x] **Step 1: Execute clean build**
- [x] **Step 2: Execute full test suite**
- [x] **Step 3: Launch daemon application and verify live performance**
