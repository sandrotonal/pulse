<div align="center">

# Pulse

**A Fluid, Apple iOS Dynamic Island & Media Dashboard for Windows 11**

[![NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![WinUI 3](https://img.shields.io/badge/WinUI-3.0-0078D4?style=for-the-badge&logo=windows11)](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
[![Windows App SDK](https://img.shields.io/badge/WindowsAppSDK-2.5.1-0078D4?style=for-the-badge)](https://github.com/microsoft/windowsappsdk)
[![Tests](https://img.shields.io/badge/Tests-396%20Passing-30D158?style=for-the-badge)](tests/Pulse.Tests)
[![License](https://img.shields.io/badge/License-MIT-green?style=for-the-badge)](LICENSE)

*Pulse brings a sleek, GPU-animated Liquid Glass Dynamic Island experience to Windows 11 — combining native system media control, synced lyrics, adaptive album-art frost, a monochrome Pomodoro focus timer with Focus Auto-DND, a matching system control panel with one-tap Global Mic Mute, a NotchDrop-style cross-app Drop Shelf (with Screenshot-to-Shelf, Pinning, Quick Note, Quick Look, and offline Windows OCR), an opt-in **Git & GitHub Control Deck** (live branch/commit telemetry, Actions CI, Fetch/Pull, `+ PR Aç`, and 2-step PR Merge), real-time OS notifications, and a smart idle strip for time, date and weather.*

---

<p>
  <img src="docs/images/island-compact.png" width="340" alt="Pulse compact media pill capturing live Spotify playback with album art tint" />
</p>
<p><em><strong>Compact Media Pill:</strong> Live track artwork, artist/title metadata, play/pause toggle, and soft-blended artwork presentation without harsh borders inside a crisp GPU-clipped capsule.</em></p>

<br />

<p>
  <img src="docs/images/island-expanded-media.png" width="480" alt="Pulse expanded media dashboard with album artwork, seek bar, synced lyrics, and transport controls" />
</p>
<p><em><strong>Expanded Media Dashboard:</strong> Adaptive artwork frost tinting, interactive drag-to-seek timeline with floating hover/scrub timestamp preview tooltip, live synced <code>lrclib</code> lyrics (current + upcoming line), transport controls, and an AirPlay-style audio route switcher button. Tap the artwork to jump straight to the source app.</em></p>

<br />

<p>
  <img src="docs/images/island-expanded-focus.png" width="480" alt="Pulse monochrome Pomodoro focus timer panel showing live countdown and presets" />
</p>
<p><em><strong>Focus Timer Panel:</strong> Monochrome Pomodoro presets (25/50 min work, 5/15 min break), tabular live countdown digits, and a spring-animated progress hairline. Tap the countdown to configure custom durations from 1 to 180 minutes.</em></p>

<br />

<p>
  <img src="docs/images/island-expanded-system.png" width="480" alt="Pulse system controls panel with volume slider, battery percentage, audio switcher, and live headphone indicator" />
</p>
<p><em><strong>System &amp; Control Panel:</strong> Monochrome WASAPI master volume slider with mute toggle, one-tap Global Microphone Mute with live muted indicator dot, semantic battery telemetry (green charging pulse, red low-battery warning), active Audio Output Endpoint switcher capsule (`Kulaklık / Hoparlör`), and live animated Headphone connection status indicator.</em></p>

<br />

<p>
  <img src="docs/images/island-expanded-shelf.png" width="480" alt="Pulse Drop Shelf panel staging dragged files, screenshots, quick notes, and clipboard text with pinning, Quick Look, offline OCR, and ZIP packaging" />
</p>
<p><em><strong>Drop Shelf (Apple NotchDrop Experience):</strong> Drag and drop files, screenshots, or text snippets straight to the island—or let OS screenshots land there automatically. Includes item Pinning (protected from Clear), inline Quick Note staging, in-island Quick Look preview, offline Windows OCR text extraction from images, full OLE drag-out back to WhatsApp, Discord, VS Code, or Explorer, and one-tap ZIP archive creation.</em></p>

<br />

<p>
  <img src="docs/images/island-expanded-dev.png" width="480" alt="Pulse Git and GitHub developer workspace panel showing active branch, commit SHA chip, CI workflow status, Fetch/Pull sync, and Pull Requests with inline Merge" />
</p>
<p><em><strong>Developer Workspace (Git &amp; GitHub Control Deck):</strong> Opt-in 5th panel featuring authentic Git/GitHub vector iconography, live branch &amp; dirty-file counts, click-to-copy Commit SHA, GitHub Actions CI run status, safe one-click <code>Fetch</code> / <code>Pull (ff-only)</code>, direct <code>+ PR Aç</code> compare launch, open PR list with 2-step inline <code>Merge → Onayla?</code>, and Terminal / VS Code / GitHub quick launchers.</em></p>

<br />

<p>
  <img src="docs/images/island-expanded-dev-guide.png" width="480" alt="Pulse in-island 2x2 Git and GitHub quick usage guide card" />
</p>
<p><em><strong>In-Island Interactive Guide:</strong> Accessible anytime from the Dev panel (<code>?</code>) or empty state—a scannable 2×2 workflow overview covering repository linking, live commit/CI monitoring, sync/PR creation, and 2-step PR merging.</em></p>

<br />

<p>
  <img src="docs/images/island-split.png" width="360" alt="Pulse split island pairing compact media capsule with active focus timer capsule" />
</p>
<p><em><strong>Split Island:</strong> When media playback and a focus session run concurrently, the island morphs cleanly into paired media and timer capsules. Clicking the detached timer opens the Focus panel.</em></p>

<br />

<p>
  <img src="docs/images/settings-dark.png" width="560" alt="Pulse Settings window in Dark theme showing island behavior and idle display switches" />
</p>
<p><em><strong>Settings (Dark Theme):</strong> Live hover-expand controls, delay slider, auto-collapse, fullscreen auto-hide, equalizer waves, and independent idle strip toggles.</em></p>

<br />

<p>
  <img src="docs/images/settings-notifications.png" width="560" alt="Pulse Settings window in Light theme showing notification categories and theme selector" />
</p>
<p><em><strong>Settings (Light Theme &amp; Notifications):</strong> Full Light/Dark parity with per-category toggles for hardware volume HUD, power events, downloads, Windows toast mirroring, Bluetooth devices, focus chime, and theme/glass controls.</em></p>

---

</div>

## Key Features

- **Zero-Reflow GPU Composition Pipeline**: Smooth multi-state morphing (Idle, Compact, Hover, Expanded, Split, Track Change pulse) driven by `Microsoft.UI.Composition` damped harmonic springs without layout thrashing, framed inside a 3 DIP antialiased `SetWindowRgn` surface and precision rounded clip so pill corners remain sharp across DPI scales.
- **Liquid-Glass Frost Engine**: Toggleable frosted glassmorphism (`LiquidGlassEngine` + `DesktopFrostService`) with a 0–100% transparency slider that samples and blurs the live desktop behind the island, adds an optical Fresnel highlight rim, and blends album-art accent tinting cleanly across Dark, Light, and System themes.
- **Live Theme Switching (Dark / Light / System)**: Instant runtime `RequestedTheme` updates across the Island and Settings window without restarting the app.
- **Smart Idle Strip**: When no media is playing, the compact pill displays a live clock, Turkish date, and Open-Meteo local weather (15-minute refresh, automatic `ipwho.is` IP lookup or manually verified city). Clock, date, and weather can each be toggled independently.
- **Live Media Session Control**: Automatic integration with Windows System Media Transport Controls (GSMTC) for Spotify, Apple Music, YouTube, browsers, VLC, and more — featuring dynamic artwork caching, seekable progress bar with floating timestamp tooltip preview, ±15s skip controls, synced lyrics powered by `lrclib`, and active audio route switching.
- **Split Island**: Concurrent media playback and an active focus countdown automatically split into paired left/right capsules and merge seamlessly when either finishes.
- **Focus Timer (Pomodoro & Focus Ritual)**: Monochrome preset chips (`25/50` work, `5/15` break), custom duration picker (`1–180` min), tabular countdown digits, session auto-switching, optional **Focus Auto-DND** (automatically silences routine alerts during focus and restores DND afterward), and optional completion chime.
- **System & Control Panel**: Stepwise WASAPI master volume slider with neutral mute state, one-tap **Global Microphone Mute** (`eCapture`), live audio output endpoint detection & quick switcher flyout, live battery & AC charging detection (green while charging with subtle pulse, red when low), and instant screenshot capture saved locally and copied to the clipboard.
- **Drop Shelf (Apple NotchDrop Experience)**: Drag and drop files, screenshots, or text snippets directly to the top island, or enable **Screenshot-to-Shelf** so `Win+Shift+S` captures land automatically. Supports **Item Pinning** (survives Clear & capacity eviction), inline **Quick Note** input, in-island **Quick Look** preview (images & code/text), offline **`Windows.Media.Ocr`** text copy from images, OLE drag-and-drop into any app, and one-tap ZIP archive packaging.
- **Real OS Event Awareness**:
  - **Windows Toast Listener**: Surfaces WhatsApp, Discord, browser, and system toast notifications directly inside the island.
  - **Bluetooth Device Alerts**: Announces headset and peripheral connections/battery level via Bluetooth radio monitoring.
  - **Audio, Power, Downloads & Screenshots**: Live hardware volume HUD, AC/charging transitions, completed file detection in Downloads, and instant screenshot alert capsules.
- **Apple-Style Alert Takeover & Deep Links**: Transient alerts briefly take over the pill and hand back to ongoing playback/focus cleanly; tapping artwork opens the source media app, and tapping alert capsules opens their file or folder.
- **Fullscreen & Game Mode Recovery**: Automatically hides for exclusive and borderless fullscreen games (excluding normal maximized windows) and cleanly recovers island/playback state when exiting fullscreen.
- **Single-Instance Guard & Tray Integration**: Re-launching Pulse brings up the existing instance, `--settings` routes directly to a single Settings window, global hotkeys (`Win+Alt+I`, `Win+Shift+F`), unstealable topmost `WM_WINDOWPOSCHANGING` handling, Windows Startup registry management, and full Reduced Motion compliance.
- **Developer Workspace (Git & GitHub Control Deck)**: Opt-in 5th island panel with zero-CPU `.git` metadata monitoring (`FileSystemWatcher` + bounded `git --no-optional-locks` queries) and ETag-cached GitHub REST API integration (`GitHubWorkspaceService`). Displays live branch, working-tree delta & `↑/↓` divergence, click-to-copy commit SHA, GitHub Actions CI status, safe `Fetch` / `Pull (ff-only)` sync, `+ PR Aç` compare launcher, open Pull Requests with 2-step `Merge → Onayla?` confirmation, Windows Credential Manager encrypted PAT storage, and Terminal / VS Code / Explorer quick actions.
- **Coordinated Z-Order & Zero-Jitter Settings Sync**: Diff-based settings propagation eliminates unnecessary frost re-captures and layout resets when editing settings; Settings window remains reliably above the island (`SetCompanionAboveWindow`), with occlusion-aware pointer checks (`WindowFromPoint` + `GetAncestor`) preventing unintended hover/collapse loops.
- **Unit Tested Architecture**: Clean MVVM separation with platform-decoupled core state, animation layout, Liquid Glass math, developer Git/GitHub parsers, and weather services (**396 automated xUnit tests**).

---

## Architecture & Technology Stack

- **Framework**: .NET 9.0 / C# 13
- **UI Platform**: WinUI 3 (Windows App SDK 2.5.1, unpackaged & self-contained `win-x64`)
- **Rendering & Motion**: `Microsoft.UI.Composition` GPU spring & clip geometry pipeline, DWM transparent host window, `LiquidGlassEngine`, GDI frosted desktop sampling
- **Media & Lyrics**: `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager` (GSMTC), `lrclib` synced lyrics client
- **Notifications & System APIs**: `UserNotificationListener` (Windows toasts), Bluetooth radio/device watcher, WASAPI endpoint volume, `GetSystemPowerStatus`, `FileSystemWatcher`, Win32 `SetWindowRgn` & global hotkeys
- **Weather & Location**: Open-Meteo forecast & geocoding APIs, `ipwho.is` IP lookup, JSON disk caches
- **MVVM & Serialization**: `CommunityToolkit.Mvvm`, source-generated `System.Text.Json` contexts
- **Testing**: xUnit with Moq (396 unit tests)

---

## Screens Flow

While expanded, scroll the mouse wheel over the island panel to switch seamlessly between the pages (4 core pages + opt-in 5th Developer page):

1. **Media** — playback metadata, drag-to-seek timeline, synced lyrics, and transport controls.
2. **Focus Timer** — monochrome Pomodoro focus timer with presets and custom duration picker.
3. **System & Control** — monochrome master volume, global mic mute, semantic charging/low battery status, audio route switcher, and screenshot capture.
4. **Drop Shelf** — NotchDrop-style cross-app file & snippet staging, quick notes, Quick Look, offline OCR, drag-to-external-app, and one-tap ZIP archiving.
5. **Git & GitHub (Opt-in)** — live branch/commit telemetry, Actions CI, `Fetch`/`Pull`, `+ PR Aç`, 2-step PR Merge, and quick editor/terminal tools.

Clicking the collapsed pill expands the dashboard; clicking outside or pressing the collapse chevron returns to compact mode. Right-click the island or use the tray icon to expand the island, open Settings, trigger Focus shortcuts, or Hide/Show the island.

---

## Installation & Download (End Users)

No .NET 9 SDK, Visual Studio, or Windows App SDK installation is required. Pulse is distributed as a **100% standalone, unpackaged, self-contained single-file executable**.

### 1. Download
1. Head over to the [GitHub Releases](https://github.com/sandrotonal/pulse/releases) page.
2. Download the latest **`Pulse.exe`** from Assets.
3. Move `Pulse.exe` to a permanent location (e.g., `%LOCALAPPDATA%\Programs\Pulse\` or your preferred tools folder).

### 2. First Run & Windows SmartScreen
Because Pulse is a community-built open-source application without an expensive commercial code-signing certificate, Windows Defender SmartScreen may display an *"Unrecognized app"* warning on the very first launch:

1. Click **More info** (*Daha fazla bilgi*).
2. Click **Run anyway** (*Yine de çalıştır*).

> [!TIP]
> Pulse runs immediately in your desktop system tray and top-center screen edge. You can enable **"Windows ile birlikte başlat"** (Launch at Windows startup) directly from the **Settings** window (`Win+Alt+I` -> Gear icon).

---

## Developer Guide & Building from Source

### Prerequisites

1. **Windows 10/11** (Build 19041 or newer, recommended Windows 11 Build 26100+)
2. **.NET 9.0 SDK** ([Download .NET 9](https://dotnet.microsoft.com/download/dotnet/9.0))

### Building & Running

Clone the repository and build using the .NET CLI:

```powershell
# Clone the repository
git clone https://github.com/sandrotonal/pulse.git
cd pulse

# Build the solution (Release)
dotnet build Pulse.sln -c Release

# Run automated unit tests (396 tests)
dotnet test tests\Pulse.Tests\Pulse.Tests.csproj -c Release --no-build

# Run the app in development
dotnet run --project src/Pulse/Pulse.csproj
```

### Producing the Single-File Release Executable

To produce the standalone unpackaged `Pulse.exe` without external dependencies:

```powershell
dotnet publish src/Pulse/Pulse.csproj -c Release -r win-x64 -o ./publish
```

The resulting `publish/Pulse.exe` is completely self-contained and ready for end-user distribution.

---

## Usage & Controls

- **Mouse Wheel**: switch expanded pages (Media, Focus Timer, System & Control, Drop Shelf, and opt-in Git & GitHub).
- **Click Pill**: toggle the expanded dashboard.
- **Hotkeys**: `Win+Alt+I` shows/hides the island; `Win+Shift+F` starts/pauses the focus timer.
- **Click Artwork**: deep-link to the app currently owning GSMTC playback (Spotify, browser, VLC, etc.).
- **Click Alert Capsule**: open the associated target (screenshot image, Downloads folder, focus panel).
- **Hover**: quick preview with animated live equalizer bars and track details.
- **Right Click / System Tray**: Expand/collapse the island, Settings window, Focus shortcuts, Notification test triggers, Hide/Exit.
- **Theme & Glass**: configure Dark/Light/System theme, 0–100% Liquid Glass transparency, and 0–100% artwork tint strength live in Settings.

---

## Repository Structure

```text
pulse/
├── src/
│   └── Pulse/                      # Main WinUI 3 Application (.NET 9)
│       ├── Controls/               # Custom UI controls (PulseSeekBar)
│       ├── Core/                   # Platform-neutral Core library (Pulse.Core)
│       │   ├── Animation/          # Spring presets, Surface layout & SetWindowRgn pixel math
│       │   ├── Events/             # Pub/sub event models
│       │   ├── Interfaces/         # Service & platform abstractions
│       │   ├── Models/             # Settings, Media, Focus, System, Developer & Weather models
│       │   └── Services/           # State machine, LiquidGlassEngine, Developer (Git/GitHub), Downloads, Weather & Settings
│       ├── Services/               # Windows platform integrations
│       │   ├── Artwork/            # Album art extraction & disk cache
│       │   ├── Glass/              # DesktopFrostService behind-island blur sampling
│       │   ├── Lyrics/             # lrclib synced lyrics client
│       │   ├── Media/              # GSMTC media session & playback controller
│       │   └── System/             # Toast listener, Bluetooth, Startup, Screenshot, Tray, Credential Store & WASAPI audio
│       ├── ViewModels/             # MVVM ViewModels (Island, Settings, Media)
│       ├── Views/                  # Island & Settings windows
│       └── Assets/                 # App icons and graphics
├── tests/
│   └── Pulse.Tests/                # xUnit Automated Tests (396 tests)
├── docs/
│   └── images/                     # Live WinUI 3 application screenshots
├── Pulse.sln                       # Visual Studio / .NET Solution
└── README.md
```

---

## Contributing

Contributions are always welcome. Feel free to open issues, submit pull requests, or request new features.

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

---

## License

Distributed under the MIT License. See `LICENSE` for more information.
