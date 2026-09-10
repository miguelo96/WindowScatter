# WindowScatter

A Windows utility that brings macOS Exposé-style window management to your desktop. Trigger it with a hotkey or hot corner and all your open windows fan out into a live thumbnail overview. Click one to switch to it, press Escape to dismiss.

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![Framework](https://img.shields.io/badge/.NET-8.0-purple)
![WPF](https://img.shields.io/badge/UI-WPF-blueviolet)

---

## Features

- **Live DWM thumbnails** — windows are rendered in real-time using the Desktop Window Manager API, not static screenshots
- **Scatter animation** — windows animate from their original positions to the overview and back
- **Smart layout** — windows are arranged to avoid overlap while preserving spatial context (each thumbnail appears near where its real window actually is)
- **Keyboard navigation** — arrow keys, Tab, and Enter to navigate and select without touching the mouse
- **Hot corners** — move your cursor to a screen corner to trigger the overview automatically
- **Configurable hotkey** — default is `Win+W`, changeable in `settings.json`
- **Desktop capture background** — blurred live desktop snapshot as the overlay background, falls back to wallpaper if unavailable
- **Multi-monitor aware** — scatter view opens on whichever monitor your cursor is on

---

## Requirements

- Windows 10 or Windows 11
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (Desktop Runtime)

---

## Getting Started

### Build from source

```bash
git clone https://github.com/yourname/WindowScatter.git
cd WindowScatter
dotnet build -c Release
dotnet run
```

Or open `WindowScatter.csproj` in Visual Studio 2022+ and hit Run.

Or just download the zip folder on releases and run WindowScatter.exe.

### Run

The app runs silently in the background with no visible window. Once running:

1. Press **Win+W** (default) to trigger the scatter view
2. Click a thumbnail to switch to that window
3. Press **Escape** to dismiss without switching

---

## Configuration

Settings are stored in `settings.json` next to the executable. The file is created automatically on first run with defaults.

```json
{
  "Hotkey": "Win+W",
  "EnableHotCorners": true,
  "HotCornerPosition": "TopLeft",
  "HotCornerDelay": 500,
  "AnimationSpeed": 0.25
}
```

| Setting | Type | Default | Description |
|---|---|---|---|
| `Hotkey` | string | `"Win+W"` | Keyboard shortcut to trigger scatter. Supports modifiers: `Win`, `Ctrl`, `Alt`, `Shift` |
| `EnableHotCorners` | bool | `true` | Whether moving the cursor to a screen corner triggers scatter |
| `HotCornerPosition` | string | `"TopLeft"` | Which corner activates scatter. Options: `TopLeft`, `TopRight`, `BottomLeft`, `BottomRight` |
| `HotCornerDelay` | int | `500` | Milliseconds the cursor must stay in the corner before triggering |
| `AnimationSpeed` | double | `0.25` | Duration of scatter/return animation in seconds |

### Hotkey format

Modifiers and key separated by `+`. Examples:

```
Win+W
Ctrl+Alt+Tab
Win+Shift+E
```

---

## Keyboard Controls (while scatter is active)

| Key | Action |
|---|---|
| `Tab` / `Shift+Tab` | Cycle through windows forward / backward |
| `Arrow keys` | Navigate to the nearest window in that direction |
| `Enter` | Switch to the selected window |
| `Escape` | Dismiss scatter without switching |

---

## Project Structure

```
WindowScatter/
├── MainWindow.xaml / .cs          # App entry point, orchestrates all managers
├── WindowLayoutCalculator.cs      # Scatter layout algorithm (placement & sizing)
├── WindowAnimationManager.cs      # Timer-based animation loop (scatter & return)
├── ThumbnailManager.cs            # DWM thumbnail registration and canvas elements
├── WindowSwitcher.cs              # Handles restoring a window after selection
├── WindowEnumerator.cs            # Enumerates visible, scatter-eligible windows
├── DesktopCaptureManager.cs       # Live desktop background capture via DWM
├── HotCornerManager.cs            # Cursor polling for hot corner activation
├── MonitorHelper.cs               # Monitor detection and bounds from cursor position
├── WallpaperManager.cs            # Wallpaper fallback for background image
├── HotkeyParser.cs                # Parses hotkey strings into virtual key codes
├── AppSettings.cs                 # Settings model with JSON load/save
├── WindowInfo.cs                  # Data model for an enumerated window
├── Win32Interop.cs                # P/Invoke declarations (DWM, User32, etc.)
└── App.xaml / .cs                 # WPF application bootstrap
```

---

## How It Works

1. A low-level keyboard hook intercepts the configured hotkey system-wide
2. On trigger, `WindowEnumerator` lists all visible windows on the active monitor
3. `WindowLayoutCalculator` computes scatter positions, scaling windows down to ~15–50% of original size and distributing them across the screen without overlap, biased toward preserving each window's original screen region
4. `DesktopCaptureManager` captures a live DWM thumbnail of the desktop as the blurred background
5. `ThumbnailManager` registers a DWM thumbnail for each window and places transparent click-capture borders over them on a WPF Canvas
6. `WindowAnimationManager` runs an animation loop interpolating each thumbnail from its real position to its scatter position
7. On click or keyboard Enter, `WindowSwitcher` runs the reverse animation and calls `SetForegroundWindow` on the chosen handle

---

## Known Limitations

- Windows only — relies heavily on DWM and Win32 APIs
- Hot corner detection only tracks the primary monitor's dimensions (secondary monitor corners don't trigger)
- No settings UI — configuration requires manually editing `settings.json`
- No much visual

---

## License

MIT
