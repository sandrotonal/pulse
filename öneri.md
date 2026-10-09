PULSE — DYNAMIC ISLAND UI/UX + MOTION SYSTEM REDESIGN

IMPORTANT:
The current implementation is functional, but the UI currently looks like a static music player.

The uploaded/reference screenshot represents the current state.

DO NOT simply redesign the player visually.

The main goal of this task is to completely improve the MOTION SYSTEM, TRANSITIONS, INTERACTION MODEL and DYNAMIC ISLAND BEHAVIOR.

The final result must feel like a premium native Apple/iOS-inspired interface.

It should feel alive, fluid, responsive and dynamic.

Do NOT copy Apple's UI pixel-for-pixel.
Use Apple's interaction principles and motion language as inspiration while keeping Pulse visually original.

==================================================
1. CURRENT PROBLEM
==================================================

The current implementation has these problems:

- UI feels static
- transitions are abrupt
- expanding/collapsing feels mechanical
- no proper Dynamic Island state system
- controls appear/disappear instead of morphing
- album artwork changes abruptly
- seek bar movement feels mechanical
- buttons lack micro-interactions
- no spring physics
- no coordinated animation between elements
- layout changes cause visual jumps
- no continuous relationship between compact and expanded states
- interface does not feel like a single living component

FIX ALL OF THESE.

==================================================
2. CORE PRINCIPLE
==================================================

The Dynamic Island must NOT behave like:

CLOSED UI
↓
OPEN UI

Instead it must behave like ONE CONTINUOUS OBJECT that transforms between states.

Every state transition must preserve visual continuity.

The user should feel that the same surface is physically expanding, contracting and transforming.

Never abruptly replace one layout with another.

==================================================
3. DYNAMIC ISLAND STATE MACHINE
==================================================

Create an explicit state machine.

States:

Idle
Compact
Hover
Expanded
TrackChanging
PlaybackChanged
SystemNotification
Collapsing

Example:

Idle
 ↓
Compact
 ↓
Hover
 ↓
Expanded
 ↓
Collapsing
 ↓
Compact

Track change:

Compact
 ↓
TrackChanging
 ↓
Compact

System event:

Compact
 ↓
SystemNotification
 ↓
Compact

Do not implement these states as unrelated UI components.

They must share the same visual container and transition between each other.

==================================================
4. COMPACT STATE
==================================================

The compact state should be extremely minimal.

Example:

┌──────────────────────────────┐
│  album  TITLE          ||    │
└──────────────────────────────┘

Requirements:

- small album artwork
- title
- artist if space allows
- play/pause
- subtle activity indicator
- black/dark glass surface
- highly rounded capsule
- subtle border
- subtle shadow
- very small footprint

It should feel like a Dynamic Island rather than a normal Windows window.

==================================================
5. HOVER INTERACTION
==================================================

When the mouse approaches the Island:

DO NOT instantly resize it.

Use a subtle anticipation animation.

Example:

Compact
 ↓
slight scale 1.0 → 1.015
 ↓
width expands slightly
 ↓
controls become visible
 ↓
hover state

The animation must use spring-like motion.

No linear mechanical transitions.

The Island should react immediately to the pointer but settle naturally.

==================================================
6. EXPANDED STATE
==================================================

When clicked, transform the existing Island into the expanded media controller.

Do NOT:

destroy compact UI
create a new panel
fade compact UI out
fade expanded UI in

Instead:

Morph the same surface.

The compact album artwork should smoothly expand into the large artwork.

The title should smoothly move into its new position.

The playback button should remain visually connected.

The container should grow from the compact capsule into the expanded rounded rectangle.

The transformation should feel physically continuous.

==================================================
7. MORPHING ANIMATION
==================================================

Animate these properties together:

- width
- height
- position
- corner radius
- opacity
- scale
- blur
- shadow
- artwork size
- artwork position
- title position
- subtitle position
- playback controls
- seek bar
- secondary controls

Do not animate everything independently.

Use coordinated choreography.

Example timing:

0ms
→ container begins expanding

40ms
→ artwork begins growing

80ms
→ title moves into expanded position

100ms
→ controls begin appearing

140ms
→ seek bar appears

180ms
→ secondary controls settle

250-400ms
→ final spring settlement

The whole transition should feel like ONE animation.

==================================================
8. SPRING PHYSICS
==================================================

Avoid basic linear/ease animations for major UI transitions.

Use spring-based animation where possible.

The motion should have:

- fast response
- soft overshoot
- natural settling
- no excessive bounce

Recommended conceptual parameters:

Mass:
1

Stiffness:
high

Damping:
medium/high

The animation must feel:

FAST + SOFT + RESPONSIVE

NOT:

SLOW + BOUNCY + CARTOONISH

==================================================
9. ANIMATION TIMING
==================================================

Use different timing depending on interaction.

User interaction:

150–300ms

Major expansion:

300–450ms

Track change:

350–600ms

System notification:

250–400ms

Collapse:

250–350ms

Micro interaction:

80–160ms

Do NOT make every animation 500ms.

The UI should feel instant.

==================================================
10. ALBUM ART ANIMATION
==================================================

Album artwork must never simply change.

When the track changes:

OLD ART
 ↓
subtle scale down
 ↓
fade
 ↓
NEW ART
 ↓
scale from ~0.96 to 1
 ↓
settle

The new artwork should appear naturally.

Do not use a harsh crossfade.

If possible, use a smooth shared-element style transition.

==================================================
11. TRACK CHANGE ANIMATION
==================================================

When the current song changes:

Example:

MAGAZIN
UZI

becomes:

HER YERİM YARA
BLOK3

Do not replace the text instantly.

Animate:

Old title
 ↓
slight upward movement
 ↓
fade out
 ↓
new title enters from below
 ↓
settle

Artist should follow the same choreography but with slightly delayed timing.

The animation must be subtle.

==================================================
12. PLAY / PAUSE MICRO INTERACTION
==================================================

The play/pause button must not simply swap icons.

Create a smooth icon morph.

Play:

▶

Pause:

Ⅱ

Transition:

▶
 ↓
morph
 ↓
Ⅱ

The button itself should have:

- subtle scale
- slight spring response
- pressed state
- hover state

On click:

scale:

1.0
→
0.94
→
1.02
→
1.0

Keep it subtle.

==================================================
13. SEEK BAR
==================================================

The seek bar must feel extremely smooth.

Current problem:

The thumb feels like a normal HTML/UI slider.

Fix it.

Requirements:

- smooth thumb movement
- animated progress
- larger hit area than visible thumb
- subtle hover expansion
- pressed state
- smooth dragging
- no jitter
- no lag

When dragging:

thumb becomes slightly larger.

Example:

Normal:
●

Hover:
◉

Pressed:
● slightly larger

The progress track should respond continuously to pointer movement.

Do not wait until mouse release to update the visual position.

==================================================
14. SEEK ±15 BUTTONS
==================================================

When clicking -15:

Show a small temporary visual feedback:

↶ 15

The icon/text should briefly animate.

Example:

scale
1
→
1.08
→
1

and fade.

Same for +15.

Do NOT create intrusive notifications.

==================================================
15. HOVER STATES
==================================================

Every interactive element must have a proper hover state.

Buttons:

Normal
Hover
Pressed
Disabled

Hover should NOT dramatically change colors.

Use:

- slight brightness
- subtle scale
- subtle background
- slight shadow
- micro movement

Keep it Apple-like.

==================================================
16. GLASS EFFECT
==================================================

The Island should feel like a premium glass surface.

Use:

- translucent dark background
- subtle backdrop blur
- soft inner highlight
- very subtle border
- soft shadow

Avoid:

- excessive transparency
- excessive neon
- heavy glow
- strong gradients
- cheap glassmorphism appearance

The surface should feel physical and premium.

==================================================
17. SHADOW SYSTEM
==================================================

Do not use a static heavy shadow.

Create a soft layered shadow.

When the Island expands:

shadow gradually increases.

When it collapses:

shadow decreases.

This reinforces the perception that the object is physically growing.

==================================================
18. BACKGROUND / DEPTH
==================================================

The Island should visually separate itself from the desktop.

Use subtle depth:

Desktop
 ↓
soft shadow
 ↓
glass surface
 ↓
content

Do not make the background blur too strong.

==================================================
19. RESPONSIVE LAYOUT
==================================================

The expanded player must not use fixed coordinates for every element.

Use responsive layout rules.

When width changes:

- artwork scales
- text reflows
- controls remain centered
- seek bar expands/contracts
- spacing adapts

The UI must work at:

1920×1080
2560×1440
3840×2160

and Windows scaling:

100%
125%
150%
200%

==================================================
20. PERFORMANCE
==================================================

THIS IS CRITICAL.

Animations must remain smooth.

Target:

60 FPS minimum.

Prefer:

- compositor-friendly animations
- GPU accelerated transforms
- opacity
- scale
- translation
- composition animations

Avoid expensive layout recalculation during animations.

Do NOT continuously rebuild the entire UI.

Do NOT recreate the Dynamic Island window during every state change.

Do NOT use aggressive polling.

Do NOT use unnecessary timers.

Do NOT perform heavy artwork processing on the UI thread.

Do NOT block the UI thread.

Use async operations where appropriate.

==================================================
21. IMPORTANT WINUI RULE
==================================================

Because this is WinUI 3:

Prefer Windows Composition APIs / compositor animations for high-frequency visual animations where appropriate.

Use XAML animations for simple UI transitions.

Use composition animations for:

- scale
- translation
- opacity
- transforms
- high-frequency interactions

Avoid causing unnecessary layout passes.

==================================================
22. WINDOW BEHAVIOR
==================================================

The Dynamic Island should feel like part of the operating system.

It must:

- stay on top
- not steal focus unnecessarily
- not appear as a normal taskbar application
- remain lightweight
- support transparent/borderless presentation
- respond immediately to interaction
- remain stable while the user interacts with other applications

When inactive, collapse naturally.

==================================================
23. AUTO COLLAPSE
==================================================

After the user stops interacting:

Expanded
 ↓
wait short delay
 ↓
collapse animation
 ↓
Compact

Do not instantly disappear.

Use a smooth contraction.

The expanded content should visually compress back into the compact Island.

==================================================
24. SYSTEM NOTIFICATION MODE
==================================================

The same Island should handle temporary system events.

Examples:

Bluetooth:

Bluetooth icon
Philips TAH4205
Connected

Volume:

🔊
78%

Screenshot:

Screenshot saved

These must appear through the same Island surface.

Do NOT create separate notification windows with completely different designs.

The Island itself transforms into the notification state.

==================================================
25. DO NOT BREAK FUNCTIONALITY
==================================================

IMPORTANT:

Do NOT rewrite the existing working media-session architecture unless necessary.

Do NOT remove:

- Spotify support
- play/pause
- previous
- next
- seek
- artwork
- playback position
- duration
- media detection

Improve the UI and animation architecture around the existing functionality.

If refactoring is necessary, preserve all existing behavior.

==================================================
26. CODE QUALITY
==================================================

Create a dedicated animation/state architecture.

Recommended:

IslandStateManager
IslandAnimationService
IslandViewModel

The ViewModel controls state.

The AnimationService controls visual transitions.

The media service controls media.

Do not mix these responsibilities.

Example:

MediaSessionService
        ↓
IslandViewModel
        ↓
IslandStateManager
        ↓
IslandAnimationService
        ↓
WinUI Composition / XAML

==================================================
27. FINAL UX TARGET
==================================================

The final experience should NOT look like:

"Windows music player"

It should feel like:

"An operating-system-level Dynamic Island."

The user should be able to:

Start Spotify
 ↓
Island appears smoothly

Hover
 ↓
Island subtly reacts

Click
 ↓
Island expands organically

Drag seek bar
 ↓
Immediate smooth response

Change song
 ↓
Artwork + title morph smoothly

Pause
 ↓
Button morph animation

Move away
 ↓
Island gently collapses

System event
 ↓
Same Island transforms into notification

Everything must feel like ONE coherent motion system.

==================================================
28. FINAL QUALITY CHECK
==================================================

Before considering the implementation complete, verify:

[ ] No abrupt UI replacement
[ ] No flickering
[ ] No layout jumps
[ ] No animation stutter
[ ] No unnecessary polling
[ ] No UI thread blocking
[ ] 60 FPS target
[ ] Smooth compact → expanded transition
[ ] Smooth expanded → compact transition
[ ] Smooth track change
[ ] Smooth artwork transition
[ ] Smooth play/pause morph
[ ] Smooth seek interaction
[ ] Smooth hover states
[ ] Smooth system notifications
[ ] Correct DPI behavior
[ ] Correct multi-resolution behavior
[ ] Existing media functionality remains intact

MOST IMPORTANT:

Do not just make the UI prettier.

Rebuild the interaction and motion system so that the entire Dynamic Island behaves as one physically coherent, responsive, animated surface.

The final result should feel close to the interaction quality of modern iOS system UI while remaining an original Windows application.