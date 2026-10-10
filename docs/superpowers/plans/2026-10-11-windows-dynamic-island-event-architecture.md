# Windows Dynamic Island Event Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transform Pulse into an intelligent, event-driven Windows Dynamic Island that seamlessly handles media playback, file operations, system notifications, focus timer, and clipboard previews on a single contextual surface with priority routing.

**Architecture:** 
1. Normalized event model (`IslandEvent`, `EventPriority`, `EventCategory`) in `Pulse.Core.Events`.
2. Central `EventRouter` arbitrating incoming events, priority queues, and collision handling.
3. Universal `IslandStateManager` mapping the highest priority event to the Dynamic Island surface without visual jitter or window resizing.
4. Independent, modular services (`FileOperationService`, `SystemEventService`, `FocusTimerService`, `ClipboardService`) publishing to the event router.
5. Cohesive WinUI 3 UI surface maintaining Apple dark glass obsidian hierarchy and fluid pointer state machine.

**Tech Stack:** C# .NET 9, WinUI 3 (Windows App SDK 1.6), Windows.Media.Control, Windows.System.Power, CoreAudio, Windows Composition APIs.

**Spec:** User Prompt "PULSE — WINDOWS DYNAMIC ISLAND Complete Product Implementation Specification"

## Global Constraints
- C# 13, strict type safety, zero `object` or `any` loosely-typed usage.
- Strict DRY and Single Responsibility principles.
- Defensive error handling: every async operation and WinRT event listener enclosed in robust `try-catch` blocks.
- Absolute prohibition of emojis everywhere (code, comments, commit messages, responses).
- Single stable host window architecture with native `WM_NCHITTEST` `HTTRANSPARENT` hit testing outside visible island.
- No continuous polling loops for timers or UI updates.

---

### Task 1: Central Event Model & Priority Definition

**Files:**
- Create: `src/Pulse/Core/Events/EventPriority.cs`
- Create: `src/Pulse/Core/Events/EventCategory.cs`
- Create: `src/Pulse/Core/Events/IslandEvent.cs`
- Create: `src/Pulse/Core/Events/MediaEvent.cs`
- Create: `src/Pulse/Core/Events/SystemNotificationEvent.cs`
- Create: `src/Pulse/Core/Events/FileOperationEvent.cs`
- Create: `src/Pulse/Core/Events/FocusTimerEvent.cs`
- Create: `src/Pulse/Core/Events/ClipboardEvent.cs`
- Test: `tests/Pulse.Tests/Events/IslandEventTests.cs`

**Interfaces:**
- Produces:
  - `enum EventPriority { Routine = 1, Informational = 2, Progress = 3, Warning = 4, ActiveMedia = 5, UserInteraction = 6 }`
  - `enum EventCategory { Media, FileOperation, Bluetooth, Audio, Battery, Network, Screenshot, Download, FocusTimer, Clipboard }`
  - `abstract record IslandEvent(string Id, EventPriority Priority, EventCategory Category, DateTimeOffset Timestamp, TimeSpan? DisplayDuration, bool CanDismiss)`
  - Concrete event records with specialized payload properties.

- [ ] **Step 1: Write unit tests for Event Model contracts and sorting**
- [ ] **Step 2: Run test to verify it fails**
- [ ] **Step 3: Implement Event models in `Pulse.Core`**
- [ ] **Step 4: Run tests to verify they pass**
- [ ] **Step 5: Commit**

---

### Task 2: Central Event Router & Arbitration Engine

**Files:**
- Create: `src/Pulse/Core/Interfaces/IEventRouter.cs`
- Create: `src/Pulse/Core/Services/EventRouter.cs`
- Test: `tests/Pulse.Tests/Services/EventRouterTests.cs`

**Interfaces:**
- Consumes: `IslandEvent`, `EventPriority`
- Produces:
  - `IEventRouter`:
    - `void Publish(IslandEvent @event)`
    - `void Dismiss(string eventId)`
    - `void LockUserInteraction()`
    - `void UnlockUserInteraction()`
    - `event EventHandler<IslandEvent?> ActiveEventChanged`
    - `IslandEvent? CurrentActiveEvent { get; }`

- [ ] **Step 1: Write unit tests for EventRouter priorities and collision handling**
  - Verify higher priority events preempt lower priority events.
  - Verify `LockUserInteraction` suppresses routine notifications while user is interacting with expanded player.
  - Verify temporary notifications automatically expire after `DisplayDuration` and restore the underlying media/idle event.
  - Verify event coalescing for rapid progress updates with matching ID.
- [ ] **Step 2: Run tests to verify they fail**
- [ ] **Step 3: Implement `EventRouter` with thread-safe priority queue and expiration timers**
- [ ] **Step 4: Run tests to verify they pass**
- [ ] **Step 5: Commit**

---

### Task 3: Focus Timer Service & State Integration

**Files:**
- Create: `src/Pulse/Core/Interfaces/IFocusTimerService.cs`
- Create: `src/Pulse/Services/Focus/FocusTimerService.cs`
- Test: `tests/Pulse.Tests/Services/FocusTimerServiceTests.cs`

**Interfaces:**
- Consumes: `IEventRouter`
- Produces:
  - `IFocusTimerService`:
    - `TimeSpan RemainingTime { get; }`
    - `bool IsRunning { get; }`
    - `bool IsBreak { get; }`
    - `void Start(TimeSpan workDuration, TimeSpan breakDuration)`
    - `void Pause()`
    - `void Resume()`
    - `void Reset()`
    - `event EventHandler<FocusTimerEvent> Tick`

- [ ] **Step 1: Write unit tests for FocusTimerService**
- [ ] **Step 2: Run tests to verify failure**
- [ ] **Step 3: Implement FocusTimerService using wall-clock delta calculation**
- [ ] **Step 4: Run tests to verify they pass**
- [ ] **Step 5: Commit**

---

### Task 4: System Event Center (Battery, Audio, Bluetooth)

**Files:**
- Create: `src/Pulse/Core/Interfaces/ISystemEventService.cs`
- Create: `src/Pulse/Services/System/SystemEventService.cs`
- Test: `tests/Pulse.Tests/Services/SystemEventServiceTests.cs`

**Interfaces:**
- Consumes: `IEventRouter`, Windows Runtime `PowerManager`, CoreAudio
- Produces:
  - `ISystemEventService`:
    - `Task InitializeAsync()`
    - Event handlers for battery status changes, volume/mute notifications, bluetooth connection events.

- [ ] **Step 1: Write unit tests for system event normalization**
- [ ] **Step 2: Implement `SystemEventService` with graceful fallback for hardware APIs**
- [ ] **Step 3: Verify tests pass**
- [ ] **Step 4: Commit**

---

### Task 5: File Operation Center (Progress & Completion)

**Files:**
- Create: `src/Pulse/Core/Interfaces/IFileOperationService.cs`
- Create: `src/Pulse/Services/Files/FileOperationService.cs`
- Test: `tests/Pulse.Tests/Services/FileOperationServiceTests.cs`

**Interfaces:**
- Consumes: `IEventRouter`
- Produces:
  - `IFileOperationService`:
    - `void ReportProgress(string operationId, string fileName, long transferredBytes, long totalBytes, string? destinationPath)`
    - `void ReportCompleted(string operationId, string fileName, string destinationPath)`
    - `void ReportFailed(string operationId, string fileName, string errorMessage)`

- [ ] **Step 1: Write unit tests for FileOperationService and event throttling**
- [ ] **Step 2: Implement `FileOperationService`**
- [ ] **Step 3: Verify tests pass**
- [ ] **Step 4: Commit**

---

### Task 6: Unified Island ViewModel & Surface Adapter

**Files:**
- Modify: `src/Pulse/ViewModels/MediaViewModel.cs`
- Create: `src/Pulse/ViewModels/IslandViewModel.cs`
- Modify: `src/Pulse/Views/Island/IslandWindow.xaml`
- Modify: `src/Pulse/Views/Island/IslandWindow.xaml.cs`
- Modify: `src/Pulse/App.xaml.cs`

**Interfaces:**
- Consumes: `IEventRouter`, `IMediaSessionService`, `ISettingsService`
- Produces:
  - `IslandViewModel` coordinating contextual presentations (Media, File Progress, System Notification, Focus Timer) on the single `Surface` border.

- [ ] **Step 1: Create `IslandViewModel` delegating to `MediaViewModel` while handling non-media events**
- [ ] **Step 2: Update `IslandWindow.xaml` with contextual notification pills/cards**
- [ ] **Step 3: Wire `EventRouter` in `App.xaml.cs`**
- [ ] **Step 4: Verify with full solution build and test run**
- [ ] **Step 5: Commit and push**
