Tabii kanka. Bence bunu **direkt Cursor / Claude Code / Gemini / Codex gibi coding agent'a verebileceğin**, tasarım + teknik mimari + davranışları net anlatan bir prompt olarak hazırlayalım.

Aşağıdaki promptu direkt kullanabilirsin:

---

# PROJECT PROMPT — Windows Dynamic Island Media Controller

## Project Name

**Pulse — Dynamic Island Media Controller for Windows**

Build a polished, production-quality Windows desktop utility that brings an **Apple Dynamic Island-inspired media control experience to Windows**.

The application should detect currently playing media on Windows and display a minimal, animated, pill-shaped floating interface at the **top-center of the screen**.

The goal is NOT to copy Apple's Dynamic Island pixel-for-pixel. Instead, use the same **design philosophy**: minimal, elegant, fluid, contextual, unobtrusive and highly polished.

---

# 1. CORE CONCEPT

The application runs silently in the Windows background.

When no media is playing:

```text
                 ●
```

A very small dark pill should remain almost invisible.

When Spotify or another supported media application starts playing:

```text
              ┌─────────────────────────┐
              │  ◉  MAGAZIN       ❚❚   │
              └─────────────────────────┘
```

The pill smoothly expands using a natural spring animation.

When the user clicks the Dynamic Island, expand it into a larger media controller:

```text
        ╭────────────────────────────────────╮
        │                                    │
        │          ALBUM ART                 │
        │                                    │
        │                                    │
        │  MAGAZIN                           │
        │  UZI                               │
        │                                    │
        │  1:24 ━━━━━━━●━━━━━━━━ 3:42        │
        │                                    │
        │       ↶15s   ❚❚   15s↷            │
        │                                    │
        ╰────────────────────────────────────╯
```

After a period of inactivity, collapse it back into the compact pill.

---

# 2. IMPORTANT DESIGN DIRECTION

The UI should feel like:

**Apple + iOS + Dynamic Island + Liquid Glass + modern Windows utility**

Do NOT create a generic Windows application.

Do NOT use traditional Windows desktop panels, old-fashioned buttons, standard forms or excessive cards.

The interface should feel premium and futuristic while remaining extremely simple.

### Visual principles

- Dark-first design
- Black / near-black surfaces
- Subtle transparency
- Acrylic / Mica / glass effect where appropriate
- Large rounded corners
- Thin subtle borders
- Soft shadows
- Minimal icons
- Smooth spring animations
- Very high-quality spacing
- Typography hierarchy
- No unnecessary elements
- No visual clutter
- No excessive gradients
- No excessive glow
- No unnecessary neon effects

The application should look expensive and polished.

---

# 3. TECHNOLOGY

Use a native Windows technology stack.

### Required

- **C#**
- **WinUI 3**
- **Windows App SDK**
- **XAML**
- MVVM architecture
- Windows.Media.Control APIs
- GlobalSystemMediaTransportControlsSession
- GlobalSystemMediaTransportControlsSessionManager
- Windows notification/tray integration where appropriate

Use modern async/await patterns.

Keep the application lightweight and memory-efficient.

Do not use Electron.

Do not use a web wrapper.

Do not use Flutter.

Do not build the application as a browser-based overlay.

The application must be a real native Windows application.

---

# 4. MEDIA SESSION INTEGRATION

Use Windows' media session APIs to detect the currently active media session.

The application should be able to detect applications such as:

- Spotify
- YouTube / Chrome
- Microsoft Edge
- YouTube Music
- VLC
- Windows Media Player
- other applications exposing Windows media transport controls

Do not hardcode Spotify as the only media source.

Create a generic media abstraction layer.

Example:

```text
IMediaSessionService
        ↓
WindowsMediaSessionService
        ↓
CurrentMediaSession
```

The media model should contain:

```text
Title
Artist
Album
AlbumArt
PlaybackStatus
Position
Duration
CanPlay
CanPause
CanSkipNext
CanSkipPrevious
CanSeek
SourceApplication
```

---

# 5. MEDIA CONTROLS

The expanded Dynamic Island must provide:

### Basic controls

- Previous
- Play
- Pause
- Next

### Seeking

This is one of the MOST IMPORTANT features.

Create a smooth interactive seek bar.

Example:

```text
1:24 ━━━━━━━━━●━━━━━━━━━━━━ 3:42
```

The user must be able to drag the position.

When the user drags the seek bar:

```text
Current Position
       ↓
User changes position
       ↓
Media session seek
       ↓
Update UI
```

Do not fake the seek bar.

Use the actual media session playback position when supported.

If seeking is unavailable for the current media source, gracefully disable the seek interaction.

---

# 6. QUICK SEEK BUTTONS

Add:

```text
↶ 15 sec
15 sec ↷
```

Clicking the left button:

```text
currentPosition - 15 seconds
```

Clicking the right button:

```text
currentPosition + 15 seconds
```

Clamp the value between:

```text
0
```

and:

```text
duration
```

If the current media session does not support seeking, hide or disable these controls.

---

# 7. ALBUM ART

Display the currently playing media artwork.

Album artwork should:

- Be downloaded/read asynchronously
- Be cached
- Have rounded corners
- Fade smoothly when changing
- Avoid blocking the UI thread
- Have a subtle scale/fade transition when the song changes

When the track changes:

```text
Old artwork
    ↓
fade / scale
    ↓
New artwork
    ↓
smoothly appear
```

Never abruptly replace the artwork.

---

# 8. DYNAMIC ISLAND STATES

Create multiple visual states.

### STATE 1 — Idle

```text
             ●
```

Minimal presence.

---

### STATE 2 — Media Playing

```text
        ┌─────────────────────────┐
        │ 🖼  MAGAZIN       ❚❚    │
        └─────────────────────────┘
```

Compact mode.

Show:

- small artwork
- title
- play/pause

---

### STATE 3 — Hover

When the mouse moves over the Island:

Smoothly expand slightly.

```text
        ┌──────────────────────────────┐
        │ 🖼  MAGAZIN      ❚❚    →    │
        └──────────────────────────────┘
```

Do not immediately jump between sizes.

Use spring interpolation.

---

### STATE 4 — Expanded

On click:

```text
╭────────────────────────────────────╮
│                                    │
│            ALBUM ART               │
│                                    │
│ MAGAZIN                            │
│ UZI                                │
│                                    │
│ 1:24 ━━━━━━━●━━━━━━━━ 3:42         │
│                                    │
│       ↶15s  ❚❚  15s↷              │
│                                    │
╰────────────────────────────────────╯
```

---

### STATE 5 — Track Changed

Automatically show the new song.

Animate:

```text
Artwork
Title
Artist
Progress
```

Do not require the user to reopen the Island.

---

# 9. ANIMATIONS

Animations are extremely important.

Do not use simple:

```text
display: none
```

style transitions.

Use smooth native animations.

Examples:

### Expand

```text
Scale
Opacity
Width
Height
Corner Radius
```

animate together.

### Collapse

Reverse the animation naturally.

### Play → Pause

Animate the icon transition.

### Song change

Animate album art and metadata.

### Seek

The progress indicator must feel responsive and smooth.

Avoid excessive animations.

The application should feel like Apple's system UI: **fast, subtle and intentional.**

---

# 10. WINDOW BEHAVIOR

The Dynamic Island should behave like a floating system utility.

Requirements:

- Always on top when active
- Does not steal focus unnecessarily
- Does not appear in the taskbar as a normal application window
- Can run in the system tray
- Starts minimized/background
- Supports Windows startup
- Does not interrupt fullscreen applications
- Does not interfere with mouse input outside the Island
- Automatically hides/collapses when inactive

The Island should remain centered horizontally near the top of the primary display.

Use safe margins.

Do not place it directly against the screen edge.

---

# 11. MULTI-MONITOR SUPPORT

Support multiple monitors.

The Island should appear on the monitor where the user is currently active.

If this is difficult for the first MVP, implement primary-monitor support first but structure the code so multi-monitor support can be added without rewriting the architecture.

---

# 12. SYSTEM TRAY

Add a tray icon.

Right-click menu:

```text
Pulse

Media Controller
───────────────
Launch at startup
Settings
Pause notifications
About
Exit
```

Left click should reopen/show the Island.

---

# 13. SETTINGS

Create a minimal settings window.

Settings:

### Appearance

```text
Theme
○ Dark
○ Light
○ System
```

### Behavior

```text
☑ Launch at startup
☑ Show when media starts
☑ Auto-collapse
☑ Show seek controls
```

### Animation

```text
Animation intensity
○ Minimal
● Normal
○ Dynamic
```

### Position

```text
Top offset
Island size
```

Do not make the settings interface complicated.

---

# 14. RESPONSIVENESS

The Island must work correctly at different:

- resolutions
- DPI scaling
- Windows display scaling
- screen sizes

Test:

```text
1920×1080
2560×1440
3840×2160
125% scaling
150% scaling
200% scaling
```

Do not hardcode pixel positions that break under scaling.

---

# 15. PERFORMANCE

The application must be lightweight.

Target:

- Very low CPU usage while idle
- Low memory usage
- No unnecessary polling loops
- Prefer event-driven media session updates
- Do not continuously download album art
- Cache artwork
- Avoid unnecessary UI re-rendering

Do not use a 100ms infinite polling loop if Windows events can be used instead.

---

# 16. ERROR HANDLING

The application must gracefully handle:

- Spotify closing
- Media application crashing
- No active media session
- Media session becoming unavailable
- Artwork unavailable
- Duration unavailable
- Seek unavailable
- Network unavailable
- Permission issues
- Multiple media sessions
- Unsupported playback controls

Never crash because Spotify or another media application closes.

---

# 17. ACCESSIBILITY

Support:

- Keyboard navigation
- Tooltips
- Accessible labels
- Sufficient text contrast
- Reduced-motion option where possible

Example:

```text
Previous track
Play
Pause
Next track
Seek backward 15 seconds
Seek forward 15 seconds
```

---

# 18. PROJECT ARCHITECTURE

Use a clean architecture similar to:

```text
Pulse/
│
├── App/
│   ├── App.xaml
│   └── App.xaml.cs
│
├── Core/
│   ├── Models/
│   │   └── MediaTrack.cs
│   │
│   ├── Interfaces/
│   │   └── IMediaSessionService.cs
│   │
│   └── Services/
│       ├── WindowsMediaSessionService.cs
│       ├── ArtworkCacheService.cs
│       └── SettingsService.cs
│
├── UI/
│   ├── IslandWindow/
│   ├── Components/
│   │   ├── CompactIsland.xaml
│   │   ├── ExpandedIsland.xaml
│   │   ├── MediaControls.xaml
│   │   └── SeekBar.xaml
│   │
│   └── Settings/
│
├── ViewModels/
│   ├── IslandViewModel.cs
│   └── SettingsViewModel.cs
│
└── Assets/
```

Use MVVM.

Keep media logic separate from UI.

---

# 19. DESIGN SYSTEM

Create reusable design tokens.

Example:

```text
IslandBackground
IslandBorder
PrimaryText
SecondaryText
MutedText
CornerRadius
IslandShadow
AnimationDuration
```

Do not scatter hardcoded values throughout XAML.

The UI should be easy to customize later.

---

# 20. IMPORTANT UX RULE

The application should **never feel annoying**.

The Dynamic Island must not constantly pop open.

Example:

User starts Spotify:

```text
small notification
      ↓
Island expands
      ↓
shows track
      ↓
collapses
```

User does nothing:

```text
compact state
```

User intentionally interacts:

```text
expanded controller
```

The user should always feel in control.

---

# 21. MVP PRIORITY

Do NOT attempt to build every feature simultaneously.

Build in this order:

### Phase 1

```text
Windows project
↓
WinUI 3
↓
Floating Island window
↓
Spotify/media session detection
```

### Phase 2

```text
Track title
Artist
Artwork
Play/Pause
Next
Previous
```

### Phase 3

```text
Current position
Duration
Seek bar
Seek
+15 sec
-15 sec
```

### Phase 4

```text
Dynamic Island animations
Compact state
Expanded state
Track-change animation
```

### Phase 5

```text
System tray
Startup
Settings
Theme
```

### Phase 6

```text
Performance optimization
Error handling
Multi-monitor
Accessibility
Installer
```

---

# 22. FINAL QUALITY REQUIREMENT

This must NOT look like a student demo.

The final application should feel like a real commercial Windows utility.

Prioritize:

**Polish > feature quantity**

**Animation quality > excessive effects**

**Native Windows integration > hacks**

**Simple UX > complicated UI**

**Performance > unnecessary libraries**

**Apple-inspired elegance > literal Apple copying**

The final result should make someone look at Windows and think:

> "Why doesn't Windows have this built in?"

---

## FINAL DESIGN TARGET

The final experience should feel like:

**Dynamic Island + Apple Music + iOS Control Center + Windows native media integration**

but redesigned as an original Windows application called **Pulse**.

The application must be functional, not a static visual mockup.

Every visible control should be connected to real Windows media functionality where supported.

**Start with the MVP architecture and implement the working media-session functionality first. Then build the Dynamic Island UI on top of it.**