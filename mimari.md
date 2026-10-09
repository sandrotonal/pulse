Aynen kankacım. Şimdi **kod yazmaya başlamadan önce mimariyi kilitleyelim.** AI'a vereceğimiz ana mimari promptu da buna göre hazırlayalım.

Ben bu projeyi **UI → ViewModel → Services → Windows API** şeklinde ayırırdım.

# Pulse — Proje Mimarisi

```text
┌─────────────────────────────────────────────┐
│                  PULSE UI                   │
│                 WinUI 3 / XAML              │
│                                             │
│  Dynamic Island                             │
│  ├── Compact                                │
│  ├── Hover                                  │
│  ├── Expanded                               │
│  ├── Track Changed                          │
│  └── System Notification                    │
└──────────────────────┬──────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────┐
│                 VIEWMODELS                  │
│                    MVVM                     │
│                                             │
│  IslandViewModel                            │
│  MediaViewModel                             │
│  SettingsViewModel                          │
│  TrayViewModel                              │
└──────────────────────┬──────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────┐
│                  SERVICES                   │
│                                             │
│  MediaSessionService                       │
│  PlaybackService                            │
│  ArtworkService                             │
│  WindowService                              │
│  AnimationService                           │
│  SettingsService                            │
│  TrayService                                │
│  StartupService                             │
└──────────────────────┬──────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────┐
│              WINDOWS PLATFORM               │
│                                             │
│ Windows.Media.Control                       │
│ GlobalSystemMediaTransportControlsSession   │
│ Windows App SDK                             │
│ Windows Composition                         │
│ Windows Runtime                             │
└─────────────────────────────────────────────┘
```

## 1. Klasör yapısı

Ben direkt şöyle başlatırdım:

```text
Pulse/
│
├── Pulse.sln
│
├── src/
│   │
│   └── Pulse/
│       │
│       ├── App.xaml
│       ├── App.xaml.cs
│       │
│       ├── Core/
│       │   ├── Models/
│       │   │   ├── MediaTrack.cs
│       │   │   ├── MediaSessionState.cs
│       │   │   ├── PlaybackState.cs
│       │   │   └── AppSettings.cs
│       │   │
│       │   ├── Interfaces/
│       │   │   ├── IMediaSessionService.cs
│       │   │   ├── IArtworkService.cs
│       │   │   ├── ISettingsService.cs
│       │   │   └── IWindowService.cs
│       │   │
│       │   └── Constants/
│       │       └── AppConstants.cs
│       │
│       ├── Services/
│       │   ├── Media/
│       │   │   ├── WindowsMediaSessionService.cs
│       │   │   └── PlaybackController.cs
│       │   │
│       │   ├── Artwork/
│       │   │   └── ArtworkCacheService.cs
│       │   │
│       │   ├── Window/
│       │   │   └── IslandWindowService.cs
│       │   │
│       │   ├── Settings/
│       │   │   └── SettingsService.cs
│       │   │
│       │   ├── System/
│       │   │   ├── TrayService.cs
│       │   │   └── StartupService.cs
│       │   │
│       │   └── Animation/
│       │       └── IslandAnimationService.cs
│       │
│       ├── ViewModels/
│       │   ├── IslandViewModel.cs
│       │   ├── MediaViewModel.cs
│       │   ├── SettingsViewModel.cs
│       │   └── TrayViewModel.cs
│       │
│       ├── Views/
│       │   ├── Island/
│       │   │   ├── IslandWindow.xaml
│       │   │   ├── CompactIsland.xaml
│       │   │   ├── ExpandedIsland.xaml
│       │   │   └── MediaControls.xaml
│       │   │
│       │   └── Settings/
│       │       └── SettingsWindow.xaml
│       │
│       ├── Controls/
│       │   ├── MediaSeekBar.xaml
│       │   ├── AlbumArtwork.xaml
│       │   └── PlaybackButtons.xaml
│       │
│       ├── Resources/
│       │   ├── Styles/
│       │   ├── Themes/
│       │   ├── Icons/
│       │   └── Fonts/
│       │
│       └── Assets/
│
└── tests/
    └── Pulse.Tests/
```

---

# 2. En önemli katman: Media Service

Burada **Spotify'a özel kod yazmayacağız.**

Bu çok önemli.

Yanlış:

```text
SpotifyService
```

Doğru:

```text
IMediaSessionService
        ↓
WindowsMediaSessionService
```

Çünkü hedefimiz:

```text
Spotify
YouTube
YouTube Music
VLC
Edge
Chrome
Apple Music
Windows Media Player
```

gibi Windows Media Session kullanan uygulamaları desteklemek.

---

# 3. MediaTrack modeli

Tek bir standart modelimiz olacak:

```csharp
MediaTrack
{
    Title
    Artist
    Album
    Artwork
    Duration
    Position
    PlaybackStatus

    CanPlay
    CanPause
    CanNext
    CanPrevious
    CanSeek

    SourceApplication
}
```

UI sadece bunu bilecek.

Örneğin Spotify'dan:

```text
Spotify
MAGAZIN
UZI
3:42
```

geliyor.

UI'nin umurunda olmayacak bunun Spotify'dan mı, Chrome'dan mı geldiği.

---

# 4. Playback Controller

Media kontrolünü ayrı tutacağız.

```text
PlaybackController
```

şunlardan sorumlu:

```text
Play()
Pause()
TogglePlayPause()

Next()
Previous()

Seek(position)
SeekForward(15)
SeekBackward(15)
```

Böylece UI içerisinde:

```csharp
session.TryChangePlaybackPositionAsync(...)
```

gibi Windows API kodları **asla dolaşmayacak.**

UI sadece:

```text
PlaybackController.Seek(...)
```

diyecek.

Bu çok önemli.

---

# 5. ViewModel

UI ile backend arasında ViewModel olacak.

Örneğin:

```text
WindowsMediaSession
        ↓
MediaSessionService
        ↓
MediaViewModel
        ↓
Island UI
```

`MediaViewModel`:

```text
Title
Artist
Artwork
Position
Duration
IsPlaying
CanSeek
```

gibi property'leri expose edecek.

UI doğrudan Windows API'ye dokunmayacak.

---

# 6. Dynamic Island State Machine

Burası projenin **en önemli UI mimarilerinden biri.**

Island'ın rastgele açılıp kapanmasını istemiyoruz.

State:

```csharp
IslandState
{
    Idle,
    Compact,
    Hover,
    Expanded,
    TrackChanged,
    Notification
}
```

Akış:

```text
IDLE
 │
 │ music starts
 ▼
COMPACT
 │
 │ mouse hover
 ▼
HOVER
 │
 │ click
 ▼
EXPANDED
 │
 │ mouse leaves / timeout
 ▼
COMPACT
```

Şarkı değişirse:

```text
COMPACT
   ↓
TRACK_CHANGED
   ↓
COMPACT
```

Bu sayede animasyonlar ve davranışlar kontrol altında olacak.

---

# 7. Window mimarisi

Dynamic Island'ı normal Windows penceresi gibi düşünmeyeceğiz.

```text
IslandWindow
```

şunları yönetecek:

- Always on top
- Borderless
- Transparent
- Top-center position
- Click-through gerektiğinde
- Focus davranışı
- DPI
- Monitor detection
- Show / Hide
- Resize
- Position

UI:

```text
IslandWindow.xaml
```

Windows davranışı:

```text
IslandWindowService
```

olacak.

---

# 8. Artwork sistemi

Album art için:

```text
ArtworkService
       ↓
ArtworkCache
```

kullanacağız.

Aynı artwork tekrar gelirse tekrar indirmeye/işlemeye çalışmayacak.

Cache:

```text
AppData/
└── Pulse/
    └── Cache/
        └── Artwork/
```

---

# 9. Settings

Ayarları UI'ya gömmeyeceğiz.

```text
SettingsService
       ↓
AppSettings
```

Örneğin:

```json
{
  "StartWithWindows": true,
  "AutoCollapse": true,
  "ShowSeekControls": true,
  "Theme": "System",
  "AnimationIntensity": "Normal",
  "TopOffset": 24
}
```

---

# 10. System Tray

Uygulama arka planda çalışacak:

```text
Windows
   ↓
Pulse
   ↓
System Tray
```

Tray:

```text
Pulse
────────────
Ayarlar
Başlangıçta başlat
Bildirimleri duraklat
Hakkında
Çıkış
```

---

# 11. Event-driven mimari

**Bunu özellikle AI'a söyleyelim.**

Sürekli:

```csharp
while(true)
{
    await Task.Delay(100);
    CheckMedia();
}
```

yaptırmayacağız.

Bunun yerine mümkün olduğunca:

```text
Windows Media Session Event
          ↓
Service
          ↓
ViewModel
          ↓
UI
```

kullanacağız.

Bu:

- CPU kullanımını düşürür
- daha hızlı tepki verir
- daha profesyonel olur
- battery/performance açısından daha iyi olur

---

# 12. Dependency Flow

Kurallarımız:

```text
Views
  ↓
ViewModels
  ↓
Services
  ↓
Windows APIs
```

**Tersine bağımlılık olmayacak.**

Örneğin:

❌ `WindowsMediaSessionService → IslandWindow`

olmayacak.

Ama:

✅ `IslandViewModel → IMediaSessionService`

olabilir.

---

# 13. Test mimarisi

AI'ın yazdığı kodu direkt production'a koymayacağız.

Şu testleri yapacağız:

```text
MediaSessionServiceTests
PlaybackControllerTests
SettingsServiceTests
IslandStateTests
```

Özellikle:

```text
Seek(-15)
Seek(+15)
Seek(0)
Seek(Duration)
```

gibi edge-case'leri test edeceğiz.

---

# 14. AI'a vereceğimiz ANA MİMARİ PROMPT

Bunu direkt coding agent'a ver:

```text
You are building Pulse, a production-quality native Windows Dynamic Island media controller.

TECH STACK:
- C#
- WinUI 3
- Windows App SDK
- XAML
- MVVM
- Windows.Media.Control
- GlobalSystemMediaTransportControlsSession
- Windows Composition APIs

ARCHITECTURE:

Use strict layered architecture:

Views
↓
ViewModels
↓
Services
↓
Windows APIs

Do not allow UI code to directly communicate with Windows media APIs.

PROJECT LAYERS:

Core
- Models
- Interfaces
- Constants

Services
- Media
- Playback
- Artwork
- Window
- Animation
- Settings
- Tray
- Startup

ViewModels
- IslandViewModel
- MediaViewModel
- SettingsViewModel
- TrayViewModel

Views
- Island
- Settings

Controls
- SeekBar
- AlbumArtwork
- PlaybackButtons

MEDIA ARCHITECTURE:

Create IMediaSessionService.

Implement WindowsMediaSessionService using Windows.Media.Control.

Do NOT create a Spotify-specific architecture.

The application must work with any Windows application exposing a media session.

Create a normalized MediaTrack model containing:

Title
Artist
Album
Artwork
Duration
Position
PlaybackStatus
SourceApplication
CanPlay
CanPause
CanNext
CanPrevious
CanSeek

Create a separate PlaybackController responsible for:

Play
Pause
TogglePlayPause
Next
Previous
Seek
SeekForward
SeekBackward

The UI must never directly call Windows media APIs.

Use event-driven updates instead of aggressive polling.

DYNAMIC ISLAND:

Create an explicit state machine:

Idle
Compact
Hover
Expanded
TrackChanged
Notification

Implement smooth transitions between states.

The Island should be a borderless, transparent, always-on-top native WinUI window.

It should appear centered at the top of the active display.

Support DPI scaling and prepare the architecture for multi-monitor support.

ARTWORK:

Create an ArtworkService with caching.

Never block the UI thread while processing artwork.

SETTINGS:

Create AppSettings and SettingsService.

Persist settings in local application data.

SYSTEM:

Create TrayService and StartupService.

The application should run in the background and provide a system tray experience.

PERFORMANCE:

Avoid unnecessary polling loops.

Use asynchronous APIs.

Avoid blocking the UI thread.

Keep CPU and memory usage low while idle.

ERROR HANDLING:

The application must gracefully handle:

- no media session
- media application closing
- media session becoming unavailable
- missing artwork
- unsupported seeking
- unsupported playback controls
- invalid duration
- unavailable position
- multiple media sessions

Do not crash when Spotify or another media application closes.

CODE QUALITY:

Use dependency injection where appropriate.

Use interfaces for replaceable services.

Use MVVM.

Use async/await.

Use nullable reference types.

Keep classes focused on a single responsibility.

Do not create giant classes.

Do not put business logic inside XAML code-behind.

Do not hardcode configuration values throughout the project.

Create reusable design tokens for UI values.

IMPORTANT:

Do not build the UI first as a fake prototype.

First implement and verify the real Windows media-session layer.

Then implement ViewModels.

Then implement the Dynamic Island UI.

Then implement animations.

Then implement tray/settings/startup.

Finally perform performance optimization, error handling and production hardening.

The final result must be a real functional Windows application, not a static UI mockup.
```

**Bu mimariyi kilitleyelim kanka.** Bundan sonra AI'a kod yazdırırken de en önemli kuralımız şu olsun:

> **Önce çalışan altyapı → sonra UI → sonra animasyon → sonra polish.**

Böyle gidersek senin oluşturduğumuz görseldeki o **Pulse Dynamic Island** sadece güzel görünen bir mockup değil, gerçekten Spotify'ı kontrol eden kullanılabilir bir Windows uygulaması olur.