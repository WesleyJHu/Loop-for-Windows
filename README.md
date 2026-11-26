# Loop for Windows

Keyboard-driven window management for Windows, inspired by [Loop](https://github.com/MrKai77/Loop) for macOS.
Hold a trigger key combo and press a direction to snap the focused window.

## Usage

Hold **Ctrl + Win + Alt** (the trigger) and press:

| Keys | Result |
|---|---|
| ← / → / ↑ / ↓ | Left / right / top / bottom half |
| Two perpendicular arrows (e.g. ↑ + ←) | That corner (top-left) |
| ← + → | Full width (keeps current height and vertical position) |
| ↑ + ↓ | Full height (keeps current width and horizontal position) |
| Enter | Maximize (fill the screen's work area) |
| C | Center (keeps current size) |
| M | Minimize |

Keys combine while held: press ← then ↑ (keeping ← held) to go from the left half to the top-left corner.
Without the trigger, all keys behave normally. Right-click the tray icon to disable Loop or quit.

> Other window tools that react to window moves (AquaSnap, FancyZones, DisplayFusion, ...) can fight
> with Loop over window positions. Pause them while using Loop.

## Building

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows 10/11.

```powershell
dotnet build
dotnet test                              # unit tests (pure logic, no UI)
dotnet run --project src/Loop.App        # start Loop (tray icon)
.\tests\smoke\Invoke-SmokeTest.ps1       # end-to-end check against a real window (steals focus ~15s)
```

Set the `LOOP_LOG` environment variable to a file path to log every window action and its geometry
(never key presses).

## Project layout

| Project | Purpose |
|---|---|
| `src/Loop.Core` | Pure logic: key state machine, chord resolution, layout math, frame compensation. No Win32. |
| `src/Loop.Platform` | Win32 interop: low-level keyboard hook, input injection, window/monitor APIs. |
| `src/Loop.App` | WPF entry point: tray icon, single instance, Per-Monitor V2 DPI manifest. |
| `tests/Loop.Core.Tests` | xUnit tests for everything in Core. |
| `tests/smoke` | End-to-end smoke test using SendInput against a throwaway window. |

## Roadmap

- [x] **Phase 0**: scaffold, tray, single instance, DPI awareness, CI
- [x] **Phase 1**: keyboard hook, halves/corners/full width/full height/maximize/center/minimize, invisible-border fix
- [ ] **Phase 2**: cycling sizes on repeated presses (1/2, 1/3, 2/3)
- [ ] **Phase 3**: multi-monitor (next/previous/numbered display, mixed DPI, hotplug)
- [ ] **Phase 4**: hold-to-preview overlay, apply on trigger release, Esc to cancel
- [ ] **Phase 5**: settings UI (custom trigger and bindings, gaps, launch at startup, exclusions)
- [ ] **Phase 6**: elevated mode, restore previous size, installer
