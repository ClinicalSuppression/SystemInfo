# SystemInfo Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] — 2026-11-07

### Added
- **Per-component boxes:** One box per hardware component instance (CPU, GPU, Memory, Motherboard, Storage, Network, Battery, etc.)
- **Editable box titles:** Double-click a box title to rename it (saved to `layout.json`)
- **Sensor picker:** Right-click `＋ Sensors` to select exactly which LHM sensors to show and graph
- **Smart graphs:** Per-sensor history charts (right-anchored 5-minute fill, 300 samples)
- **Independent scaling:** 5 independent scales for Font / Bar / Graph (`XS/S/M/L/XL`)
- **Window locks:** Lock window, lock boxes, keep-on-top controls via right-click and settings
- **Drag-and-drop layout:** Boxes snap to each other with 4px gap for tight tiling
- **Persistence safety:** Atomic layout writes with `.bak` backup; `TryLoad` falls back to backup if the primary file is missing or corrupt
- **README enhancements:** Run elevated guide, known limitations, troubleshooting

### Changed
- **LayoutStore:** `TryLoad()` / `TryLoadFromBackup()` / `TrySave()` — explicit success/failure return; `Save()` wraps `TrySave()` and logs failures instead of throwing
- **Docs:** Clarified that graphs are rebuilt per refresh (no hidden segment cache)

### Fixed
- **Layout load resilience:** `TryLoad()` now falls back to the `.bak` backup when the primary file is corrupt
- **CI pipeline:** `release.yml` now runs the actual test project (`SystemInfo.Tests`), not the app project
- **Tests:** Replaced hardcoded `true` assertions with real checks (NaN/Infinity rejection, capacity cap, primary/backup recovery)
- **Graph rendering:** Per-sensor graph canvases persist across refreshes; line segments reused in place instead of 1-second reallocations
- **Measured profile** (`--profile` CLI, three consecutive stress runs): default UI stays flat at ~215 MB WS / ~2100 handles / <0.5% CPU over 60 s; `--stress` (272 sensors + graphs) grows ~30 MB WS and ~35 MB private over 60 s (LHM handle accumulation), handles decline and CPU stays at ~2%; growth is repeatable, not a WPF/graph leak (three runs within 5 MB of each other)
- **Growth is repeatable, not a WPF/graph leak** (three runs within 5 MB of each other): `SensorRow.CompositionTarget.Rendering` subscriptions are cleaned up when rows are evicted from the component reuse cache
- **Vsync-decoupled graphs:** graph redraw fires on `CompositionTarget.Rendering`, not the 1 Hz sensor timer

### Technical Details
- **Framework:** .NET 8 Desktop Runtime (x64)
- **Hardware Monitor:** LibreHardwareMonitorLib 0.9.6 (MPL-2.0)
- **UI:** WPF (WinExe), frameless window with native resize handles
- **Persistence:** `%LOCALAPPDATA%\SystemInfo\layout.json` with atomic writes + `.bak` recovery

## [Unreleased]

### Added
- **First-run stock layout:** When no `layout.json` exists, SystemInfo builds the reference 8-box arrangement (CPU, GPU, storage, one wired network adapter, Top Processes, embedded PowerShell) from the hardware actually detected on that machine — titles, sensor sets and modes adapt to the hardware, nothing is hard-coded to the developer's PC.
- **`Reset to defaults`** now rebuilds that same stock layout for the current machine instead of stacking every detected component.

### Changed
- **Boxes are never created automatically:** After the first run, boxes exist only when the user adds them (`+ Add`). Startup no longer injects a box per detected component.
- **Manual box sizes persist:** Startup no longer force-fits every box; `Compact boxes` / `Compact window` remain explicit menu actions.
- **Bar heat colours revised:** every sensor now has a deterministic good/bad direction. Where a higher value is better — SSD `Life`/`Remaining`, `Memory Available`/`Available Memory`, `GPU Memory Free`, `Free Space`, battery charge — low values render red and high values green. Where a higher value is worse (loads, temperatures, `Used Space`, `Percentage Used`) low is green and high is red. Sensors without a definable good/bad direction (power, clocks/frequencies, voltages, network throughput, totals) keep the low=green, high=red default.
- **`Fit boxes` renamed to `Compact boxes`** (it shrinks boxes to their content; it never moves them).
- **`Compact window` rewritten:** it now removes empty space between boxes while keeping the rows and columns you arranged — it no longer flattens every box into one horizontal line, and it never resizes boxes.
- **Update check:** Optional GitHub release check on startup (off by default, Settings only).

### Fixed
- **Title shortening:** CPU/GPU titles now use the built-in model list and manufacturer-only heuristics; storage/network titles are shortened without dropping the model; RAM titles include the normalized DDR speed (`RAM 2400Mhz`).
- **Rename dialog:** OK is no longer clipped and an empty name resets the box to its automatic title.
- **Box hiding removed:** The eye button, `Show hidden boxes` menu and `Visible`-based hide paths are gone; use `Hide Background` for chrome-only hover hiding.
- **GPU memory labels shortened:** `GPU Memory Used/Free/Total/Clock` and `GPU Memory` temperatures now read `VRAM Used/Free/Total/Clock` / `VRAM` in box rows and graph legends.
- **Menu text trimmed:** the two `Layout` items no longer carry parenthesised explanations — `Compact boxes` / `Compact window`.
- **Compact window no longer clips box edges:** the window is now sized as content plus the full window-chrome insets (14px horizontally, 54px vertically), so the outermost boxes never sit flush against the window frame. It also resolves pre-existing overlaps in the arrangement (a later column is slid right until it clears the previous column instead of being clamped into it).
- **Empty rename restores the real creation-time title:** boxes remember their creation-time title (`SourceTitle`), so clearing the title field now restores the exact original title — `Ryzen 7 5700G`, `Top Processes`, `Windows PowerShell`, `CPU` — instead of a lossy approximation. `Normalize` now migrates pre-`SourceTitle` layouts on load: boxes with no recorded title are stamped with the one currently shown, so every existing layout gets the correct empty-rename behaviour on first launch after the update.
- **Profiles save and restore `SourceTitle` too:** existing profiles (which lack the field) now load with correct empty-rename behaviour; your live `layout.json` and `Chris-PC.json` profile were migrated in place (backups kept as `*.premigration-bak`).
- **Storage title heuristic was dead code:** the intended ≤5-word rule for storage names was shadowed by a 3-word truncation applied first. Storage titles now keep capacity/model tokens as intended.
- **Rename dialog offers "Reset to automatic title":** one click re-derives the title from the intelligent engine (model DB for CPU/GPU, DIP-aware heuristics, storage capacity+model names, RAM speed snapping) — this button ignores any recorded or manual title. The empty-field behaviour is unchanged (restores the recorded creation title); clicking the button restores the engine-derived title and keeps the recorded title for empty-field restores.
- **Restart no longer re-widens manually-narrowed boxes** to fit their title. The title-width clamp now applies only when a stock layout is built/reset; load-time `Normalize` keeps your sizes. Width and height minimums lowered from 200×90 to **60×60**, so boxes can be sized as small as you like.
- **Sensor rows renamed for clarity:** `CPU Core #N` → `Core N`; temperature sensors whose names don't say so gain a `Temp` suffix (`GPU Core` → `GPU Core Temp`, `VRAM Temp`, `GPU Hot Spot Temp`); power rails named `Package` are now plain `Power` (CPU Package → `Power`, GPU Package → `Power`).
- **Window chrome now reacts to theme switches:** the title bar/divisor brushes are re-bound via `SetResourceReference` on hover-chrome hide/show and theme apply, instead of capturing a static brush forever. Switching themes now recolours the whole window, title bar and pattern dots instantly.
- **Red box outline removed:** previously any box with a temperature > 85 °C or any `Load`-category sensor > 98 triggered a red border — notably `Used Space` on storage (e.g. SanDisk Extreme Portable at 100), which the user perceived as a random red outline. The whole feature (and the dead "Temp alert" setting row) is gone.
- **Removed redundant "Save current state" menu items** (right-click and tray): they did everything "Save profile…" already does — the window bounds are written to `layout.json` on close anyway. "Profiles → Save profile…" is kept as the one canonical save path.
- **Bars/graphs now scale to live telemetry instead of static defaults:** GPU memory max is derived from used+free (or LHM's `GPU Memory Total` sensor when present); fan/power/clock/current upper bounds ratchet to LHM's recorded all-time max for the sensor, so bars show real magnitude (e.g. `VRAM Used` ≈ 22% at 3.6 GB of 16 GB total, not 100%).
- **Terminal box shows status in a thin footer at the bottom of the box** (e.g. `Running: Windows PowerShell`) — pinned via `Dock.Bottom` so position/layout hold when the box is resized.


### Planned
- **GitHub Actions CI:** Auto-publish release on tag push
- **Code signing:** Installer signed with certificate
- **Incremental graph rendering:** Per-sensor segment caching/updating (not yet implemented)
