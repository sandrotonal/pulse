# Dynamic Island Motion System & Taskbar Flyout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transform Pulse into a premium, fluid Apple iOS Dynamic Island experience with responsive seekbar scrubbing and a dedicated Taskbar Control Flyout.

**Architecture:** 
1. Real-time playback position interpolation and scrubbing state machine in `MediaViewModel`.
2. Dedicated Apple-style interactive `PulseSeekBar` control handling pointer manipulation and instant seeks.
3. Fluid container morphing and spring physics in `IslandWindow` respecting Apple dark mode obsidian surface hierarchy.
4. Acrylic-backed `TaskbarFlyoutWindow` docked to the Windows notification tray.

**Tech Stack:** C# .NET 9, WinUI 3 (Windows App SDK), Windows.Media.Control SMTC, Windows Composition.

**Spec:** `docs/superpowers/specs/2026-10-09-dynamic-island-ui-motion-redesign.md`

## Global Constraints
- C# 13, strict type safety, no raw object passing without types.
- All async calls defensively wrapped in `try-catch`.
- Dark mode must adhere to `dark-mode-surface-hierarchy`: `#0F1012` base, `rgba(255, 255, 255, 0.08)` rim line, `#FFFFFF` / `rgba(235, 235, 245, 0.65)` typography.
- Zero-lag, non-blocking UI thread dispatching.

---

### Task 1: Playback Position Interpolation Engine & Continuous Scrubber State

**Files:**
- Modify: `src/Pulse/ViewModels/MediaViewModel.cs`
- Modify: `tests/Pulse.Tests/ViewModels/MediaViewModelTests.cs`

**Interfaces:**
- Produces:
  - `double SeekPercent`: normalized playback or scrub percentage [0-100].
  - `string PositionLabel`: live formatted position (mm:ss).
  - `bool IsScrubbing`: true when user is holding/dragging the seekbar.
  - `void BeginScrub(double percent)`: locks external SMTC timeline updates during user interaction.
  - `void UpdateScrub(double percent)`: previews timestamp and thumb position live.
  - `Task EndScrubAsync(double percent)`: dispatches position change to `IPlaybackController` and restores live ticker.

- [ ] **Step 1: Write unit tests for position interpolation and scrub locking**

Add tests to `tests/Pulse.Tests/ViewModels/MediaViewModelTests.cs` verifying:
1. `BeginScrub` sets `IsScrubbing = true` and updates `SeekPercent`.
2. External track updates do not overwrite `SeekPercent` while `IsScrubbing` is true.
3. `EndScrubAsync` calls playback seek and unlocks scrub state.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Pulse.Tests/Pulse.Tests.csproj`
Expected: FAIL (missing `BeginScrub`, `UpdateScrub`, etc.)

- [ ] **Step 3: Implement playback interpolation ticker and scrub state in `MediaViewModel.cs`**

1. Introduce `DispatcherQueueTimer _playbackTicker` (interval 250ms).
2. Ticker ticks when `CurrentTrack.PlaybackStatus == PlaybackStatus.Playing && !_isScrubbing`.
3. Safely advances current position by elapsed delta without exceeding track duration.
4. Implement `BeginScrub`, `UpdateScrub`, and `EndScrubAsync`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Pulse.Tests/Pulse.Tests.csproj`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Pulse/ViewModels/MediaViewModel.cs tests/Pulse.Tests/
git commit -m "feat(media): implement playback interpolation ticker and scrub lock in MediaViewModel"
```

---

### Task 2: Interactive Apple Scrubber Component (`PulseSeekBar`)

**Files:**
- Create: `src/Pulse/Controls/PulseSeekBar.xaml`
- Create: `src/Pulse/Controls/PulseSeekBar.xaml.cs`

**Interfaces:**
- Consumes: `MediaViewModel.SeekPercent`, `MediaViewModel.CurrentTrack.CanSeek`
- Produces:
  - DependencyProperties: `Value` (double), `CanSeek` (bool), `AccentBrush` (Brush).
  - Events: `ScrubStarted`, `ScrubDelta`, `ScrubCompleted`.
  - Visuals: 4px track expanding to 6px on hover, glowing capsule thumb, smooth pointer tracking.

- [ ] **Step 1: Create `PulseSeekBar.xaml`**

XAML layout with:
1. Hit-test area (`Height="24"`, transparent background for comfortable cursor interaction).
2. Background track (`Height="4"`, corner radius `2`, color `rgba(255, 255, 255, 0.14)`).
3. Active fill track with corner radius.
4. Floating thumb dot (`Width="12"`, `Height="12"`, corner radius `6`, white with soft shadow, opacity morphing on hover/drag).

- [ ] **Step 2: Create `PulseSeekBar.xaml.cs`**

Implement pointer event overrides:
1. `OnPointerPressed`: Capture pointer, calculate percentage from `e.GetCurrentPoint(this).Position.X / ActualWidth`, invoke `ScrubStarted`.
2. `OnPointerMoved`: If captured, calculate new percentage, invoke `ScrubDelta`.
3. `OnPointerReleased` & `OnPointerCaptureLost`: Release pointer capture, invoke `ScrubCompleted`.

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build src/Pulse/Pulse.csproj`
Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/Pulse/Controls/PulseSeekBar.*
git commit -m "feat(ui): add interactive PulseSeekBar control with smooth thumb scrubbing"
```

---

### Task 3: Dynamic Island Visual Hierarchy & Spring Motion Redesign

**Files:**
- Modify: `src/Pulse/Views/Island/IslandWindow.xaml`
- Modify: `src/Pulse/Views/Island/IslandWindow.xaml.cs`

**Interfaces:**
- Consumes: `MediaViewModel`, `PulseSeekBar`
- Produces:
  - Cohesive dark mode theme: `#0F1012` surface, `rgba(255, 255, 255, 0.08)` rim line.
  - Coordinated spring morphing: `Idle` (84x36) -> `Compact` (210x40) -> `Hover` (220x40, 1.018x scale) -> `Expanded` (380x176) with high-res artwork, track metadata, `PulseSeekBar`, and fluid transport buttons.
  - Zero abrupt layout jumps.

- [ ] **Step 1: Refactor `IslandWindow.xaml` layout**

1. Replace rigid canvas offsets with structured morphing containers:
   - `CompactContainer`: mini album art (`28x28`), title text, mini wave indicator, play/pause button.
   - `ExpandedContainer`: large album art (`54x54`), title, artist, `PulseSeekBar`, position & duration counters, transport buttons (-15, prev, play/pause, next, +15).
2. Style with iOS typography, soft pill buttons, and subtle hover glows.

- [ ] **Step 2: Implement coordinated composition springs in `IslandWindow.xaml.cs`**

1. Wire `PulseSeekBar` events (`ScrubStarted`, `ScrubDelta`, `ScrubCompleted`) to `_viewModel.BeginScrub`, `UpdateScrub`, and `EndScrubAsync`.
2. Configure Windows Composition `CubicBezierEasingFunction(0.16f, 1.0f, 0.3f, 1.0f)` for size, corner radius, and scale morphing.
3. Smooth spring overshoot on button clicks (`0.94x` on press, `1.02x` on release).

- [ ] **Step 3: Build and test execution**

Run: `dotnet build Pulse.sln`
Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/Pulse/Views/Island/
git commit -m "feat(island): redesign Dynamic Island with Apple spring physics and fluid morphing"
```

---

### Task 4: Windows Taskbar / Notification Flyout Window (`TaskbarFlyoutWindow`)

**Files:**
- Create: `src/Pulse/Views/Flyout/TaskbarFlyoutWindow.xaml`
- Create: `src/Pulse/Views/Flyout/TaskbarFlyoutWindow.xaml.cs`
- Modify: `src/Pulse/Services/System/TrayService.cs`
- Modify: `src/Pulse/App.xaml.cs`

**Interfaces:**
- Consumes: `MediaViewModel`, `TrayService.ShowRequested`
- Produces:
  - `TaskbarFlyoutWindow`: borderless, topmost, acrylic control center flyout docked right above the taskbar tray.
  - Toggled when clicking the Pulse icon in the taskbar notification tray.
  - Auto-dismisses on focus lost.

- [ ] **Step 1: Create `TaskbarFlyoutWindow.xaml`**

1. Apple Control Center inspired card (`Width="340"`, `Height="200"`, corner radius `20`, acrylic backdrop).
2. Top header: App source indicator (e.g., "Spotify", "Apple Music", "Media") with mini app icon/badge.
3. Large artwork, track title, artist.
4. Integrated `PulseSeekBar` with time indicators.
5. High-contrast, elegant transport buttons.

- [ ] **Step 2: Create `TaskbarFlyoutWindow.xaml.cs`**

1. Configure borderless popup window styles via Win32 `SetWindowLongPtr`.
2. Implement monitor & taskbar detection to position the window at the bottom-right corner of the working area (`WorkArea.Right - Width - margin`, `WorkArea.Bottom - Height - margin`).
3. Add slide-up entrance animation using Windows Composition.
4. Handle `Activated` / deactivation to close smoothly when clicking outside.

- [ ] **Step 3: Wire `TaskbarFlyoutWindow` in `App.xaml.cs`**

1. In `App.xaml.cs`, initialize `TaskbarFlyoutWindow`.
2. When `TrayService.ShowRequested` fires, toggle `TaskbarFlyoutWindow`.

- [ ] **Step 4: Build and test**

Run: `dotnet build Pulse.sln`
Expected: Build succeeded with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Pulse/Views/Flyout/ src/Pulse/Services/System/ src/Pulse/App.xaml.cs
git commit -m "feat(flyout): add Apple Control Center-inspired TaskbarFlyoutWindow for system tray"
```

---

### Task 5: End-to-End Verification, Performance & Release Audit

**Files:**
- Modify: All affected components if needed
- Test: `tests/Pulse.Tests/`

- [ ] **Step 1: Run complete test suite**

Run: `dotnet test Pulse.sln`
Expected: All tests pass.

- [ ] **Step 2: Full solution build**

Run: `dotnet build Pulse.sln -c Release`
Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Verification check**

Verify:
1. Dynamic Island morphing is fluid with zero layout jumps.
2. Scrubber moves in real-time during playback; click and drag seeks smoothly without stutter.
3. Clicking tray icon opens the Taskbar flyout docked above the clock/taskbar.
4. Dark mode matches obsidian `#0F1012` and subtle rim lighting.

- [ ] **Step 4: Final commit**

```bash
git add -A
git commit -m "chore: complete Dynamic Island motion system and Taskbar flyout implementation"
```
