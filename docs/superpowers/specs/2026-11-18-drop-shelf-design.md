# Pulse Drop Shelf ("Raf") — Architectural Design Spec

**Date:** 2026-11-18  
**Target Version:** Pulse v2.2.0  
**Status:** Approved

---

## 1. Overview & User Experience

Drop Shelf ("Raf") brings a native macOS NotchDrop experience to Windows Dynamic Island:

- **4th Expanded Panel (`SelectedPageIndex = 3`, "Raf"):** Seamlessly integrated into the 4-dot island carousel (`Medya • Odak • Sistem • Raf`), with a subtle compact pill badge (`[icon] N`) whenever ≥1 item is staged on the shelf.
- **Universal Drag-In:** Dragging files, folders, images, or selected text/URLs from Explorer, browsers, Discord, VS Code, or any OLE-capable app onto the compact island magnetically springs it open to the Shelf panel.
- **Universal Drag-Out:** Dragging any individual card—or the **"Hepsini Sürükle" (Drag All)** handle—out of the island transfers native Windows OLE `StorageItem`s directly into any target app without stealing focus (`WS_EX_NOACTIVATE` preserved). Items remain on the shelf after drag-out until manually removed (`×`) or cleared (`Temizle`).
- **One-Click Power Actions:** `Hepsini Sürükle` (Drag All), `ZIP` (packages all staged files/folders into a timestamped zip archive and adds/copies it), `Kopyala` (places all staged items on the Windows Clipboard as file drop items), and `Temizle` (removes all items and cleans up temporary staged files).

---

## 2. Physics & Motion Mechanics

Built directly on Pulse's analytic `DampedSpringOscillator` and GPU Composition layer:

1. **Magnetic Capture (`DragEnter`):**
   - Immediately transitions the island to Expanded Shelf (`384×240`) using energetic under-damped spring momentum and expands `SetWindowRgn` on frame 1 so the OLE drop target never drops under the cursor.
   - Activates a subtle GPU pulsing drop-zone rim (`Scale 1.0 → 1.02`, dashed border highlight) while `_isDragHovering` is active.
2. **Drop Absorption (`Drop`):**
   - Executes a volume-preserving squash-and-rebound impulse on `Surface` (`ScaleX: 1.018, ScaleY: 0.965` for 85ms → spring rebound to `1.0, 1.0`).
   - Newly inserted shelf cards enter cleanly inside the horizontal shelf strip.
3. **Tactile Drag-Out (`DragStarting` / `DropCompleted`):**
   - Lowers dragged tile opacity (`0.45`) and suspends auto-collapse while `_isDraggingOut` is true.
   - Guarantees flag reset on `DropCompleted`, `DragLeave`, `Drop`, `WM_CAPTURECHANGED`, and `WM_KILLFOCUS` so the island can never lock open.
4. **Drag-Leave Grace (`DragLeave`):**
   - Uses a short 180ms anti-flicker grace timer before collapsing back to compact if the drag exits without dropping.

---

## 3. Architecture & File Layout

- **`src/Pulse/Core/Models/ShelfItem.cs`** — Immutable record (`Id`, `DisplayName`, `FilePath`, `Kind`, `ExtensionBadge`, `FileSizeBytes`, `IsTemporaryStaging`, `AddedAtUtc`) + `ShelfItemKind` enum (`File`, `Folder`, `Image`, `Text`).
- **`src/Pulse/Core/Interfaces/IDropShelfService.cs`** — Clean abstraction for shelf state, path deduplication, staging temp files, zip archiving, and lifecycle cleanup.
- **`src/Pulse/Core/Services/DropShelfService.cs`** — Pure, 100% unit-testable implementation (max 12 items, case-insensitive normalized path deduplication, safe temp staging under `%LOCALAPPDATA%\Pulse\ShelfTemp`, safe ZIP creation via `System.IO.Compression.ZipArchive`, safe deletion of temp files only—never touches original user files!).
- **`src/Pulse/Services/System/ShelfShellService.cs`** — WinRT/WinUI 3 bridge for extracting dropped `DataPackageView` items (StorageItems, Bitmap, Text/WebLink), populating outgoing `DataPackage` for `DragStarting` and `Clipboard.SetContent`, and generating lightweight thumbnails for image tiles.
- **`src/Pulse/ViewModels/IslandViewModel.cs`** — 4-page carousel (`0..3`), `ShelfItems` observable collection, shelf commands, compact badge bindings.
- **`src/Pulse/Views/Island/IslandWindow.xaml` & `.xaml.cs`** — 4-dot indicator (`PageDot3`), `ShelfExpandedPanel` with monochrome tonal hierarchy (`#1B1D21` card elevation + `rgba(255,255,255,0.08)` rim), compact shelf badge, and full OLE drag-in / drag-out handlers.
- **`tests/Pulse.Tests/Services/DropShelfServiceTests.cs`** — Unit tests covering add, dedup, capacity cap, temp text staging, safe remove (never deletes user originals, always deletes temp files), ZIP archive generation, and error handling.

---

## 4. Safety & Error Resilience

- **Original User Files Are Never Deleted:** Only files created by Pulse inside its isolated `%LOCALAPPDATA%\Pulse\ShelfTemp` staging folder (`IsTemporaryStaging == true`) are deleted on item removal, clear, or app exit.
- **Async Error Handling:** Every disk, ZIP, WinRT `DataPackage`, and clipboard operation is enclosed in `try-catch` blocks with contextual `CrashLogger.Write(...)` reporting.
- **No Lockups:** `_isDragHovering` and `_isDraggingOut` never block `WM_NCHITTEST` client promotion inside visible island bounds and always clear on capture loss.
