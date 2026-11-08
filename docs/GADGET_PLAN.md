# Gadget Classic plan — align SystemInfo with System Monitor II image

Reference image (Windows 7 sidebar gadget "System Monitor II"):
* Vertical 140–150px dark-navy column, metallic inset borders, 1px steel separators.
* Title bar: "System Monitor II" (white condensed), subtitle "Mem Usage 38%" (cyan 12pt).
* Mem section: grid Used 5768 MB / Free 1031 MB / Total 6991 MB / 10.12 GB / 16.95 GB, then RAM 35% Page 40% with thin blue bars.
* CPU section: "Intel® Core™ i7-3770 @ 3.40GHz", "Clock: 3401MHz", "CPU Usage 93%" large white.
* Per-core bars (9 cores shown): `[67<] [86>] [95>] [95>] [90%] [90%] [P 95%] [83%] [P 94%]` — small 6px bars, red→green scale.
* Gauge: needle + numeric TDP 44W, PWR 56/77W.
* System: "2121 threads in 158 proc. / Processor Queue Length: 0 / Uptime: 14:25:04 / Record: 23° at 14:25:31".
* Footer icon row: HP clock, Bat, PS, Windows logo, "© 2017 by loop HT v2.4.1".

## Current vs target
| Area | Current | Target |
|---|---|---|
| Layout | Freeform 320px boxes floating on 340px board | Fixed gadget column 150–180px, stacked sections, no free drag (or drag whole gadget only) |
| Chrome | Rounded cards + DropShadow + pattern | Flat metallic inset, 1px `#2A3A4A` separators, square window with 6px radius, steel header gradient |
| Type | Segoe UI 11–12pt | Condensed/Segoe UI 9–10pt, caps for labels, cyan for values |
| Bars | Rounded green→red heat, 4–12px tall | 5–7px segmented blue-cyan, red top tick, white % label right |
| Gauge+history | Optional header graph | Fixed needle gauge (0–100%) + 60-sample spark at footer |
| Footer | ⚙/—/✕ only | HP/Lock/PS/Win + version + uptime/record block |
| Themes | Midnight/Aurora/Ember/Paper | Add `GadgetDark/Light` family that exactly matches above; others remain |

## Plan — Gadget skin (additive, not a rewrite)
1. **Gadget theme tokens** (`ThemeService`):
   - Add `GadgetDark` (`WindowTop #1A2A3A → #0A1624`, `Box #0E1E30`, `Header #162C42`, `Border #2A3A4A`, `Accent #4FC3F7`, `GraphPalette` blue→cyan→green).
   - Add `GadgetLight` (same hues on `#E8EEF4`).
   - Register in definitions + Normalize whitelist.
2. **Gadget window preset** (`MainWindow.Theme` menu → "Gadget"):
   - Forces `Width 180 Height 720`, `Topmost`, `WindowLocked` suggestion, single BoxCanvas scroll hidden.
   - Creates/collapses boxes to one column 164px wide stacked `Y = 8 + i*224` (already snapped).
   - Hide borders/shadow via new ` GadgetSkin` bool on `WindowLayout` (Box Elev=False, CornerRadius=4, HeaderGradient=true).
3. **Section mapping** (use existing useful defaults):
   - Mem: `Memory (Load)` + `Memory Used/Available` + `Virtual Memory` → grid Used/Free/Total (`FormatBytes`).
   - CPU: `CPU Total` + `Core Max` (labelled "CPU Usage"), `Clock` (MHz), per-core in tooltip/dialog (not all 16 rows).
   - TDP/PWR: `Package Power` + GPU `Package`.
   - System: `Threads` (`Process.Threads`), `Processes` (count), `Uptime` (`Environment.TickCount64`), `Record` (max temp seen).
   - Footer: clock icon (uptime), battery (% + AC), lock, Windows start, version.
4. **Chrome**: Title `System Monitor II` (editable via Settings), gradient header, inset separator (`BorderThickness 0,1,0,0` + highlight), segmented bar (5 ticks), needle gauge (reuse `DrawGraph` needle via `Arc`? MVP = large % + spark).
5. **Save state**: already `layout.json` (`Left/Top/Width/Height`, `Boxes[X/Y/Width/Height/SensorIds/Modes/Visible/Locked]`, `ThemeMode/Scheme`, `Font/Bar/GraphScale`, `SearchFilter`, `TempAlert`, `GraphZoom`). Add explicit:
   - Right-click → **Save current state** → writes `layout.json` + `profiles/gadget-{timestamp}.json`.
   - Right-click → **Restore / Reset** (existing Profiles → Load/Reset).
   - Auto-save on every move/resize/picker/theme toggle (already in `SaveLayout()`).
6. **Tray icon**: enable `UseWindowsForms`, add `TrayService` (`System.Windows.Forms.NotifyIcon` + `ContextMenuStrip`):
   - **Tray menu** (right-click, not left): `Open SystemInfo`, `Settings…`, `Theme (submenu)`, `Profiles (submenu)`, `Save current state`, `Hide/Show`, `Exit`. These are exactly the same handlers as the main context menu; window lock/size and picker-only toggles are **not** duplicated in tray (senseless there).
   - `MinimizeToTray` (already flag) → `✕` hides to tray instead of exit; `--minimized` starts hidden; balloon on alert >85°C.
   - `NotifyIcon.Text = "SystemInfo — System Monitor II"`, double-click restores.
7. **Right-click menus sync:** extract `BuildContextMenu()` to shared builder used by both window and tray (single source of truth, so checkmarks never diverge).

## Implementation order
1. P0 tokens + Gadget themes (1h) → screenshot.
2. P1 save-state menu + Profiles already, tray skeleton (2h).
3. P2 preset layout (180px column, header/footer) + section mapping (3h).
4. P3 gauge/needle + segmented bars (2h).

## What is NOT needed
* No LHM changes (Mem/CPU/GPU sensors already mapped).
* No `.gadget` packaging (Win10/11 has no sidebar).
* No tray actions for per-sensor Text/Bar/Graph (that lives in per-box `＋ Sensors`).

Next: pick P0–P2 and I will build them.
