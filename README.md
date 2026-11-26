# Loop for Windows

Keyboard-driven window management for Windows, inspired by [Loop](https://github.com/MrKai77/Loop) for macOS.
Hold a trigger key combo, press a direction, and let go: the focused window snaps into place.

## Usage

Hold **Ctrl + Win + Alt** (the trigger; configurable) and press:

| Keys | Result |
|---|---|
| ← / → / ↑ / ↓ | Left / right / top / bottom half |
| Two perpendicular arrows (e.g. ↑ + ←) | That corner (top-left) |
| ← + →, or ↑ + ↓ | Center (keeps current size) |
| All four arrows | Corner-sized window, centered |
| Enter | Maximize / restore, exactly like the window's own maximize button |
| C | Center (keeps current size) |
| M | Minimize |
| ] / [ | Move to the next / previous display |
| 1 – 9 | Move to display *n* (numbered left to right, then top to bottom) |
| Esc | Cancel (while the trigger is held) |

**Preview, then apply.** While you hold the trigger, a translucent preview shows where the window will
go, and the window doesn't move until you release the trigger. Everything combines inside one hold:
press ← three times to step through sizes, or press ] and then ← for the left half of the next display.
Esc cancels. If you prefer the window to move on every key press, choose *Move instantly* in settings.

**Cycling.** Press the same layout again to cycle its size: ½ → ⅓ → ⅔ → ½ (configurable). Halves cycle
along their axis; corners and the centered corner size cycle their width. Moving or resizing the window
yourself resets the cycle.

**Chords.** Arrows pressed within 60 ms of each other count as one chord (configurable). Keys also
combine while held: press ← and then ↑ (keeping ← held) to go from the left half to the top-left corner.

**Displays.** Moving a window to another display keeps its layout: a left third stays a left third,
a maximized window stays maximized on the new display (and restores there), and a free-floating window
keeps its relative position and size.

**Maximize.** Enter toggles Windows' real maximized state, the same as the maximize/restore button, so
the app shows its restore icon and remembers its previous size. Arrow layouts on a maximized window start
from that previous size (← + → centers it at the size it had). Windows without a maximize button, such
as fixed-size dialogs, are stretched to fill the screen instead. Gaps don't apply to maximized windows.

**Animation.** Windows glide into place (about 180 ms, easing out), and the preview fades in and out
and glides between targets as you cycle sizes or build a chord. A new key press during a glide jumps it
to its end first. Loop skips the glide when it would look worse than a jump: when Windows' *Animation
effects* setting is off, for maximize/restore/minimize (Windows animates those itself), when a window
first has to come out of maximized or minimized, between displays with different scaling (the window
would rescale mid-flight), and for apps that repaint too slowly to keep up. Optionally, a ring at the
cursor lights up the direction you've chosen, like macOS Loop's radial menu.

Without the trigger, every key behaves normally.

## Settings

Right-click the tray icon and choose **Settings…** (or double-click the icon). You can change:

- the **trigger** modifiers (any combination of Ctrl, Win, Alt, Shift)
- **preview and apply on release**, or **move instantly**; show or hide the preview, and its color
- **cycle sizes** (e.g. `1/2, 1/3, 2/3`), the **gap** between windows, and the **chord window**
- the key for **every action**: click it and press the new key (the window has an action for each half,
  corner, display, and so on, not just the defaults above)
- **animation**: window glides and preview animation on/off, duration, easing, and the direction indicator
- **excluded apps**, by process name (e.g. `notepad`)
- **start at sign-in** (per-user Run key; no admin rights)

Changes apply as soon as you save. Settings live in `%APPDATA%\LoopForWindows\settings.json`. If that
file is broken, Loop starts with the defaults and says why in a tray notification.

## Building

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows 10/11.

```powershell
dotnet build
dotnet test                              # unit tests (pure logic, no UI)
dotnet run --project src/Loop.App        # start Loop (tray icon); add -- --settings to open settings
.\tests\smoke\Invoke-SmokeTest.ps1       # end-to-end check against a real window (steals focus ~1 min)
```

Environment variables:
- `LOOP_SETTINGS`: use a different settings file (the smoke test uses this so it never touches yours).
- `LOOP_LOG`: a file path to log every window action and its geometry (never key presses).

## Project layout

| Project | Purpose |
|---|---|
| `src/Loop.Core` | Pure logic: key state machine, chords, layout math and gaps, size cycling, gesture and preview planning, display ordering, placement planning, easing and animation policy, settings and their JSON format. No Win32. |
| `src/Loop.Platform` | Win32 interop: low-level keyboard hook (on its own thread), input injection, window and monitor APIs, the window animator, startup registration, and the service that wires them together. |
| `src/Loop.App` | WPF app: tray icon, preview overlay, direction indicator, settings window, single instance, Per-Monitor V2 DPI manifest. |
| `tests/Loop.Core.Tests` | xUnit tests for everything in Core. |
| `tests/smoke` | End-to-end smoke test using SendInput against a throwaway DPI-unaware window. |

**Threads.** The keyboard hook has a dedicated thread that only pumps messages and decides what each
key means. Windows skips a low-level hook that doesn't answer quickly and passes the key through, so the
hook must never wait behind UI work (rendering the preview, finishing an animation, a slow app's
`SetWindowPos`). Window moves and visuals run on the UI thread; animation frames run on a background
thread. With `LOOP_LOG` set, Loop logs whenever UI work waits more than 50 ms or takes more than 30 ms.

## Notes on mixed-DPI setups

A window's outer rectangle includes invisible resize borders, so a window snapped to a screen edge
pokes about 7px past it. When a monitor with a different scale sits past that edge, Windows' DPI
virtualization remaps the position of DPI-unaware windows (many older Win32 apps) using that monitor's
scale, and they land far off target. Loop avoids this by first moving the window with its right and
bottom edges pulled back inside the destination monitor, then growing it to full size without moving it
(see `PlacementPlanner`). If a different-DPI monitor sits directly *left of or above* the destination,
Loop relies on a second, corrective placement pass instead.

DPI-unaware windows on a fractionally scaled display (e.g. 125%) can end up 1px off when moved freely,
because Windows stores their size in scaled units. Loop's layouts (halves, corners, ...) are unaffected
on common resolutions.

## Roadmap

- [x] **Phase 0**: scaffold, tray, single instance, DPI awareness, CI
- [x] **Phase 1**: keyboard hook, halves/corners/maximize/center/minimize, invisible-border fix
- [x] **Phase 2**: chord grace period, size cycling (½, ⅓, ⅔), centering chords
- [x] **Phase 3**: multi-monitor (next/previous/numbered display, layout-preserving moves, mixed DPI, hotplug)
- [x] **Phase 4**: hold-to-preview overlay, apply on trigger release, Esc to cancel
- [x] **Phase 5**: settings UI (trigger, bindings, apply mode, preview, cycle sizes, gaps, exclusions, start at sign-in)
- [x] **Phase 6**: smooth transitions and animations (window glides, preview fades and glides,
  direction indicator, animation settings; keyboard hook moved to its own thread)
- [ ] **Phase 7**: elevated mode, restore previous size, installer
