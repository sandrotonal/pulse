<div align="center">

# 🏝️ Pulse

**A Fluid, Apple iOS Dynamic Island & Media Dashboard for Windows 11**

[![NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
[![WinUI 3](https://img.shields.io/badge/WinUI-3.0-0078D4?style=for-the-badge&logo=windows11)](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
[![Windows App SDK](https://img.shields.io/badge/WindowsAppSDK-1.6%2B-0078D4?style=for-the-badge)](https://github.com/microsoft/windowsappsdk)
[![License](https://img.shields.io/badge/License-MIT-green.style=for-the-badge)](LICENSE)

*Pulse brings a sleek, interactive, micro-animated Dynamic Island experience to your Windows desktop with native system media control, live album art, track timeline seek support, system tray control, and fluid dark/light theme adaptability.*

---

</div>

## ✨ Key Features

- **🏝️ Dynamic Island UI**: Floating, top-aligned desktop widget with multi-state expansions (Idle, Compact, Expanded, Track Change, Settings).
- **🎵 Live Media Session Control**: Synchronizes automatically with active Windows System Media Transport Controls (GSMTC) for Spotify, Apple Music, YouTube, Web Browsers, VLC, etc.
- **🎨 Album Art & Backdrop FX**: Dynamic album artwork extraction, caching, and ultra-smooth blur backdrops.
- **⏱️ Interactive Timeline & Seeking**: Real-time progress bar with drag-to-seek support and live track time updates.
- **⚙️ Customization Engine**:
  - Custom animation speeds and curve presets (iOS Fluid Spring, Gentle, Snappy).
  - Theme switching (System, Dark, Light).
  - Configurable top offsets, island dimensions, and auto-collapse timeouts.
- **🔔 System Tray Integration**: Unobtrusive background operation with quick access menu, media controls, startup settings, and quick exit.
- **🖥️ Multi-Monitor & DPI Aware**: Seamlessly adapts to DPI scale factors and monitor layouts without clipping or blurred rendering.
- **🧪 Unit Tested Architecture**: Clean MVVM architecture with platform-decoupled state machine test coverage.

---

## 🛠️ Architecture & Technology Stack

- **Framework**: .NET 9.0 / C# 13
- **UI Platform**: WinUI 3 (Windows App SDK)
- **MVVM Pattern**: `CommunityToolkit.Mvvm`
- **Media API**: `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager`
- **Testing**: xUnit with Moq

---

## 🚀 Getting Started

### Prerequisites

1. **Windows 10/11** (Build 19041 or newer, recommended Build 26100+)
2. **.NET 9.0 SDK** ([Download .NET 9](https://dotnet.microsoft.com/download/dotnet/9.0))
3. **Windows Developer Mode** enabled (Required for running packaged WinUI 3 apps locally):
   - Go to **Settings > System > For developers** (or **Privacy & security > For developers**).
   - Turn **Developer Mode** ON.

### Building & Running

Clone the repository and build using the .NET CLI:

```powershell
# Clone the repository
git clone https://github.com/sandrotonal/pulse.git
cd pulse

# Build the solution
dotnet build Pulse.sln

# Run unit tests
dotnet test Pulse.sln

# Run the app
dotnet run --project src/Pulse/Pulse.csproj
```

---

## 📸 Usage & Controls

- **Click Island**: Toggle expanded playback view and track timeline.
- **Hover & Micro-interactions**: Hovering reveals quick media controls and track details.
- **Right Click / System Tray**: Access the settings panel, select active media session, or toggle Windows startup.

---

## 📁 Repository Structure

```
pulse/
├── src/
│   └── Pulse/                      # Main WinUI 3 Application
│       ├── Components/             # UI Views, Dynamic Island Controls
│       ├── Services/               # Media Session, System Tray, Settings
│       ├── ViewModels/             # MVVM ViewModels
│       ├── Helpers/                # Win32 Interop, Windowing & Animation helpers
│       └── Assets/                 # App icons and graphics
├── tests/
│   └── Pulse.Tests/                # xUnit Automated Tests
├── docs/                           # Architecture specs & design documentation
├── Pulse.sln                       # Visual Studio / .NET Solution
└── README.md
```

---

## 🤝 Contributing

Contributions are always welcome! Feel free to open issues, submit pull requests, or request new features.

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git checkout -b feature/AmazingFeature`)
5. Open a Pull Request

---

## 📄 License

Distributed under the MIT License. See `LICENSE` for more information.
