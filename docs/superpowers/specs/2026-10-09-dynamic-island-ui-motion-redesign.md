# Pulse — Dynamic Island Motion System & Taskbar Flyout Design Spec

## 1. Executive Summary
This design specification details the complete UI/UX, motion system, interaction model, and system tray flyout redesign for Pulse.
Pulse transitions from a static rectangle with hardcoded canvas positions to a fluid, Apple iOS Dynamic Island-inspired continuous surface with spring-based morphing, reliable seekbar scrubbing, and a dedicated taskbar control flyout.

---

## 2. Architectural Blueprint

### 2.1 Component Structure
```text
Pulse/
├── src/Pulse/
│   ├── Controls/
│   │   └── PulseSeekBar.xaml / .cs         # Smooth scrubber with pointer tracking & preview
│   ├── Views/
│   │   ├── Island/
│   │   │   ├── IslandWindow.xaml / .cs      # Continuous morphing pill & card container
│   │   └── Flyout/
│   │       ├── TaskbarFlyoutWindow.xaml / .cs # Windows Action Center / Control Center flyout
│   ├── ViewModels/
│   │   ├── MediaViewModel.cs               # Real-time position ticker, scrubbing state, commands
│   │   └── FlyoutViewModel.cs              # (Shared or dedicated) flyout presentation logic
│   ├── Services/
│   │   ├── Media/
│   │   │   └── WindowsMediaSessionService.cs # SMTC bridge with resilient timeline sync
│   │   └── System/
│   │       └── TrayService.cs              # Tray icon click coordinator for flyout toggle
```

### 2.2 Principles
- **DRY & Single Responsibility**: Shared media models and commands; UI layer handles pure visual composition and gestures.
- **Continuous Object Morphing**: Single root container transforms between `Idle`, `Compact`, `Hover`, and `Expanded` states rather than abruptly toggling visibility or tearing down controls.
- **Zero Glitch Seeking**: Dedicated user scrubbing state prevents background SMTC timeline updates from overriding the thumb mid-drag.
- **Apple Dark Mode Elevation Hierarchy**: Compliant with `dark-mode-surface-hierarchy`:
  - Base: `#0F1012` deep obsidian (avoids pure black trap).
  - Surface: `rgba(28, 30, 36, 0.92)` with Acrylic blur.
  - Rim Highlight: `rgba(255, 255, 255, 0.08)` ultra-fine border.
  - Primary text `#FFFFFF`, secondary text `rgba(235, 235, 245, 0.65)`.
  - Light mode standard: `#F2F2F7` background with `#FFFFFF` translucent glass card and `rgba(0, 0, 0, 0.07)` rim.

---

## 3. Dynamic Island Motion & State System

### 3.1 States
1. **Idle**: Compact capsule (`84 x 36 px`), subtle green/accent status dot with muted "PULSE" typography.
2. **Compact**: Minimalist music pill (`210 x 40 px`), rounded mini album artwork (`28 x 28 px`, corner radius `8 px`), track title, play/pause micro button, and animated audio equalizer indicator.
3. **Hover**: Anticipation expansion (`scale 1.018x`, subtle width expansion to `220 px`, smooth spring settling in `160 ms`).
4. **Expanded**: Large interactive card (`380 x 176 px`, corner radius `28 px`), high-res album art (`52 x 52 px`), title, artist, volume/status, custom interactive seekbar, and rounded transport controls.
5. **TrackChanging**: Smooth cross-fade and directional text sliding (`old text slides up & fades out, new text slides in from bottom`).
6. **Collapsing**: Soft spring reverse transition back to `Compact` or `Idle` (`260 ms`).

### 3.2 Physics & Timing Parameters
- **Easing Curve**: Windows Composition Cubic Bezier `CubicBezierEasingFunction(0.16f, 1.0f, 0.3f, 1.0f)`.
- **Spring Overshoot**: Targeted `1.018x` to `1.025x` peak overshoot before natural settling.
- **Duration**:
  - Hover anticipation: `160 ms`.
  - Expand: `320 ms`.
  - Collapse: `260 ms`.
  - Track change: `280 ms`.
  - Button press micro-interaction: `75 ms` scale down (`0.94x`), `120 ms` release with subtle bounce.

---

## 4. Playback Scrubber & Seeking Engine (`PulseSeekBar`)

### 4.1 Root Cause & Solution
- **Previous Failure**: Standard WinUI 3 `Slider` captures pointer messages inside its private template thumb, causing `PointerReleased` on the slider element to be swallowed. In addition, no local timeline interpolation was executed, freezing the progress bar.
- **New Architecture**:
  - Dedicated interactive `PulseSeekBar` control:
    - Composed of background track, active progress fill, and an Apple-style floating thumb dot.
    - Explicit pointer events: `PointerPressed`, `PointerMoved`, `PointerReleased`, `PointerCaptureLost`.
    - Instant click-to-seek: clicking anywhere on the bar computes percentage instantly.
    - Drag preview: dragging highlights the bar and displays a floating time badge (`m:ss`).
    - Release execution: on pointer release/capture loss, calls `SeekToPercentAsync(...)` once and smoothly releases scrub lock.
  - **Local Interpolation Ticker**:
    - `MediaViewModel` runs a 250ms `DispatcherQueueTimer` while `PlaybackStatus == Playing`.
    - Updates `InterpolatedPosition` incrementally without waiting for delayed OS SMTC timeline events.
    - Pauses during active user scrub.

---

## 5. Taskbar Flyout Panel (`TaskbarFlyoutWindow`)

### 5.1 Presentation & Behavior
- Clicking the Pulse system tray icon in the Windows taskbar notification area toggles `TaskbarFlyoutWindow`.
- The window is borderless, acrylic-backed, and automatically anchors to the bottom-right corner of the active working area (above the taskbar and clock).
- Statically styled as an Apple Control Center media widget:
  - Header with source app badge (Spotify, Apple Music, Edge, etc.).
  - Large album cover with subtle ambient shadow.
  - Track title & artist with clean ellipsis formatting.
  - Interactive `PulseSeekBar` with duration timestamps.
  - Previous, Play/Pause, Next, -15s, +15s transport controls.
  - Auto-dismisses when clicking outside (deactivation) or when toggled from the tray.

---

## 6. Error Handling & Resilience
- All WinRT SMTC and Win32 interop calls wrapped in defensive `try/catch` blocks.
- Thread dispatching ensured via `DispatcherQueue.TryEnqueue` whenever background SMTC events fire.
- Monitored multi-display DPI scaling (`GetDpiForWindow`) dynamically applied to positioning and sizing.

---

## 7. Verification & Acceptance Criteria
1. **Dynamic Island Motion**: Fluid morphing between compact capsule and expanded card with spring easing and no clipping or layout jumps.
2. **Scrubber Functionality**: Clicking any point on the seekbar or dragging the thumb accurately seeks the playback in Spotify/system media. Real-time progress moves smoothly every second.
3. **Taskbar Flyout**: Left-clicking the taskbar tray icon opens the flyout directly above the notification tray with smooth entry animation; closing works on loss of focus.
4. **Surface Aesthetics**: Dark mode adopts `#0F1012` and subtle rim lighting; Light mode remains crisp, readable, and frosted.
