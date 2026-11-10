# ROLE & OBJECTIVE
Act as a Principal Windows Systems & Rendering Engineer. Your objective is to refactor the rendering, window management, and animation pacing pipeline of the `Pulse` Dynamic Island application to achieve a 0-stutter, 144Hz–360Hz hardware-accelerated frame rate (matching native Direct2D/C++ performance) while preserving 100% of the existing UI/UX design, typography, corner radii, colors, Split Mode, and MVVM data bindings.

CRITICAL EXECUTION RULE: Do NOT output raw code snippets in the chat for me to copy-paste. Directly read, edit, and write the changes into the workspace files (`src/Pulse/Views/Island/IslandWindow.xaml`, `src/Pulse/Views/Island/IslandWindow.xaml.cs`, `src/Pulse/Core/Animation/DampedSpringOscillator.cs`, and related tests in `tests/Pulse.Tests/`).

---

# ARCHITECTURAL BOTTLENECKS TO ELIMINATE
1. **OS Window (HWND) Resizing Bottleneck:** Resizing `Window.Width`, `Window.Height`, or `Window.Left` per animation frame forces the Windows Desktop Window Manager (DWM) to reallocate window surfaces and triggers full XAML layout passes, causing frame drops on high-refresh-rate monitors.
2. **Timer Jitter:** Using `DispatcherTimer` (~15.6ms resolution, UI-thread queued) causes uneven frame pacing and micro-stutters.
3. **XAML Layout Thrashing:** Animating outer container dimensions while inner views (`Compact`, `Expanded`, `Split`) have dynamic/auto layout recalculations forces `Measure` and `Arrange` passes on text, album art, and controls every millisecond.

---

# STEP-BY-STEP IMPLEMENTATION SPEC

### 1. Fixed Transparent Canvas Architecture (`IslandWindow.xaml` & `IslandWindow.xaml.cs`)
* Lock the `IslandWindow` OS HWND dimensions to a static maximum bounding box that comfortably fits all expanded and split states (e.g., `Width="560" Height="260"` or dynamically calculated once per monitor DPI change).
* Position the window once at startup/display-change at the top-center of the active work area. **Never** mutate `Window.Width`, `Window.Height`, or `Window.Left` during state transitions or spring animations.
* Keep the `Window` background `Transparent` (`AllowsTransparency="True"`, `WindowStyle="None"`).
* Place the visual pill container (and secondary split pill container) inside this fixed canvas aligned `HorizontalAlignment="Center"` and `VerticalAlignment="Top"`. Only the inner pill border(s) will animate their dimensions/transforms.

### 2. Dynamic Win32 Hit-Testing / Click-Through (`WM_NCHITTEST` Hook)
* Because the HWND is now a fixed 560x260 transparent canvas, transparent areas outside the visible black pill(s) must not block mouse clicks to underlying windows.
* In `IslandWindow.xaml.cs`, attach an `HwndSourceHook` inside `OnSourceInitialized`.
* Intercept `WM_NCHITTEST` (`0x0084`):
  - Extract screen coordinates `(x, y)` from `lParam` (handle signed 16-bit extraction properly for multi-monitor negative coordinates: `(short)(lParam.ToInt64() & 0xFFFF)` and `(short)((lParam.ToInt64() >> 16) & 0xFFFF)`).
  - Convert screen coordinates to the local coordinate space of the active pill container(s) (both main island border and split secondary pill if visible) using `PointFromScreen`.
  - If the cursor is outside the actual rendered bounds (`0 <= pt.X <= ActualWidth` and `0 <= pt.Y <= ActualHeight`) of both the primary pill and secondary split pill, set `handled = true` and return `new IntPtr(-1)` (`HTTRANSPARENT`).
  - If inside, allow normal hit-testing (`HTCLIENT`) so hover expansion, media clicks, drag-and-drop, and scroll wheel navigation work seamlessly.

### 3. VSync-Locked High-Refresh-Rate Frame Pacing (`CompositionTarget.Rendering`)
* Remove any `DispatcherTimer` or `Task.Delay` loops driving UI animations in `IslandWindow.xaml.cs`.
* Implement a deterministic VSync loop using `CompositionTarget.Rendering` paired with a high-resolution `System.Diagnostics.Stopwatch`:
  - Track `_lastTicks` on each render callback and compute exact `double dt = (double)(currentTicks - _lastTicks) / Stopwatch.Frequency;`.
  - Clamp `dt` to a safe range (`Math.Clamp(dt, 0.0005, 0.033)`) to prevent physics explosions when returning from Alt-Tab, sleep, or debugger pauses.
  - Step all active `DampedSpringOscillator` instances (`width`, `height`, `cornerRadius`, `splitGap`, `scale`, `opacity`) using this exact `dt`.
  - **Zero Idle CPU Drain:** As soon as all active spring oscillators report `IsAtRest` (settled within epsilon threshold), immediately unsubscribe from `CompositionTarget.Rendering` (`CompositionTarget.Rendering -= OnFrameRender`). Only re-subscribe when a state transition, hover, or split event changes a target value.

### 4. Eliminate Layout Thrashing & Enable GPU Caching (`IslandWindow.xaml`)
* Set `ClipToBounds="True"` (or bind a dynamic rounded `RectangleGeometry` clip) on the morphing pill container so inner content is cleanly masked during spring expansion/contraction.
* Prevent inner content (`Expanded` media dashboard, lyrics, focus timer, system notification views) from reflowing text or recalculating grid columns on every frame of the width/height spring:
  - Give inner expanded content templates a fixed target layout width/height (centered inside the morphing pill) rather than stretching dynamically from `120px` to `420px` every frame.
  - During state transitions, animate the inner content using GPU-accelerated `Opacity` and `RenderTransform` (`ScaleTransform` / `TranslateTransform` with `RenderTransformOrigin="0.5,0.5"`) while the outer black pill morphs its bounds via the spring oscillator.
* Apply `CacheMode="BitmapCache"` (with `RenderAtScale` matching DPI if applicable) to static/semi-static inner sub-views during motion so WPF/XAML composites them as GPU textures rather than re-rasterizing vectors and glyphs on every frame.

### 5. Verification & Unit Tests
* Inspect `src/Pulse/Core/Animation/DampedSpringOscillator.cs` to ensure velocity preservation on mid-flight target retargeting (interrupting an opening animation with a mouse-leave must smoothly redirect momentum without snapping or resetting velocity to zero).
* Run `dotnet build` and `dotnet test` across `Pulse.sln` (`tests/Pulse.Tests`) to verify all existing state machine, split mode, and oscillator unit tests pass with zero regressions.