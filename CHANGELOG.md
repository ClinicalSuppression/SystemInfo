# SystemInfo Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.2] - 2026-11-09

### Changed
- **Application icon remastered** to match the shared icon family (Quick Scaler / Crispy Bills / Dupe Check): same deep-navy squircle, periwinkle inset outline and off-white monogram layout, with the teal->green equalizer strip as the SI purpose glyph. `Assets/SystemInfo.ico` and `docs/icon.png` regenerated; `scripts/icon/generate_icon.py` updated to the new design. No functional changes.

### Fixed
- **Memory/event leak (primary defect):** rows of graph-enabled sensors were never unsubscribed from the process-wide `CompositionTarget.Rendering` event on row replacement, `Detach()` (new) is now called on row replacement, box rebuild, box removal and before all box destruction; `MainWindow.RebuildBoxes` detaches every box before clearing; `Layout.SensorIds` set changes reset sticky widths so purged sensors shrink back.
- **Corrupt `layout.json` now surfaces:** `LoadOrNull()` distinguishes missing (first run) from corrupt; corrupt primary *and* backup are copied aside as `layout.json.corrupt-<timestamp>` and logged; the app then rebuilds the stock layout instead of showing an empty window.
- **Layout save path hardened:** unique tmp, fsync, and `File.Replace` for atomic swap (copy+move fallback); backup failures are logged, orphan tmp cleaned.
- **Terminal embedding can no longer hijack unrelated windows:** only the spawned process (direct) or a process that is an ancestor of it (the conhost child process that owns the classic console window) can be embedded/hidden; arbitrary `ConsoleWindowClass`/`Cascadia` windows created by the user during the poll window are no longer captured or hidden.
- **Windows Terminal dropped from terminals:** `wt.exe` cannot be embedded and is no longer offered as a preset (the saved app box requires a classic conhost-compatible shell). Existing `AppId = "wt"` boxes migrate to `pwsh` on next load; the stock first-run app box is now PowerShell 7 instead of Windows PowerShell.
- **`SensorPickerWindow` OK no longer wipes** `CombineGraphs`/`GraphShowLegend` and removes dead plumb-through for those properties (`BoxLayout`, `WindowLayout` untouched). Settings Refresh preset resolution now uses `ScaleHelper.UpdateCadence` (nearest-preset snap; never emits 0).
- **Theme rollback:** if `ThemeService.Apply` throws mid-publish, caches roll back to the previous palette instead of leaving mixed themes.
- **Placeholder bugs:** self-elevation forwards original CLI args (`--minimized`, `--json`, …); layout save on close persists restored (not minimized) bounds; `DispatcherUnhandledException` no longer spams MessageBox — deduped to ≤1 balloon notice per minute; tray icon updated (tooltip no longer mentions the gadget) and old `ContextMenuStrip` is disposed; background image cache invalidates on mtime change; two-process-box shared tick cache.

### Changed
- **Dead code removed:** header graph canvas, legend panel, zoom-wheel wiring, `DrawGraph`, `GraphBrush`, `GadgetData`, `StickyMin`, `SectionToText`, `BuildPreviewBrush`, `IsStartWithWindows`, `RefreshThumb`, `AppBindings`, `SearchFilter`/`ApplySearchFilter`, `ColorBlindSafe` (unused), no-op Rx boxes, broken temp files, `|| true` disjunct, double-redundant tag comparisons, unused `ox/oy`, `_suppressTermCombo` no-op.
- **Row reuse keys:** string interpolation keys replaced by zero-alloc `RowLayoutKey` value type.
- **Graphs:** LINQ-free inner loop (clamps to `WindowSamples`, `ClipToBounds`), history capacity is shared (`SensorHistory.DefaultCapacity`), graph canvas recreated in-place only when its size actually changes (position preserved).
- **GPU memory bound sweep** is throttled to one pass per 10 ticks with a 20% growth threshold and a prebuilt cache (was full-catalog LINQ GroupBy per tick).
- **Process box snapshot sharing:** process list cached for ~1.5 s so two process boxes do not double the shell cost; Process snapshot is rebuilt at most once per refresh tick.
- **Snap computation** allocates one transient list per drag frame instead of `Append().ToArray()` chains; `GraphScale` header legend rendering is gone entirely.

### Known-good verification
- Build `-warnaserror` clean; 177/177 unit tests pass.

## [1.0.1] — 2026-11-09

### Added
- **Application icon:** new `Assets/SystemInfo.ico` (SI monogram over an teal→green equalizer strip on a rounded dark badge), multi-resolution 16/24/32/48/64/128/256. Generator is committed at `scripts/icon/generate_icon.py` (Pillow + numpy). Also added `docs/icon.png` master.

### Changed
- **Release asset names now include the version:** the portable build ships as `SystemInfo_1.0.1_portable.exe`; the installer as `SystemInfo_Setup_1.0.1.exe` (was `SystemInfo_Setup_1.0.exe`). `installer.iss` `OutputBaseFilename` updated to match. Note: the `v1.0.1` tag was moved onto this commit so the release artifacts are built from the tagged source.

### Fixed
- **Tray icon shipped incorrectly:** installed builds showed the generic Windows gear in the tray because `SystemInfo.ico` was referenced only at compile time. The `.ico` is now copied next to the app (`Content` + `CopyToOutputDirectory`) so `TrayService` finds it and loads it at runtime.
- **Release assets were built from a pre-tag tree** in v1.0.1's first upload: metadata embedded the previous commit hash (`1.0.1+7966a36…`). Rebuilt from the tagged source and re-uploaded; both assets now carry `ProductVersion = 1.0.1+<tag commit>`.

## [1.0.0] - 2026-11-09

### Added

- **Per-component boxes:** One box per hardware component instance (CPU, GPU, Memory, Motherboard, Storage, Network, Battery, etc.)
- **Editable box titles:** Double-click a box title to rename it (saved to `layout.json`)
- **Sensor picker:** Right-click `ï¼‹ Sensors` to select exactly which LHM sensors to show and graph
- **Smart graphs:** Per-sensor history charts (right-anchored 5-minute fill, 300 samples)
- **Independent scaling:** 5 independent scales for Font / Bar / Graph (`XS/S/M/L/XL`)
- **Window locks:** Lock window, lock boxes, keep-on-top controls via right-click and settings
- **Drag-and-drop layout:** Boxes snap to each other with 4px gap for tight tiling
- **Persistence safety:** Atomic layout writes with `.bak` backup; `TryLoad` falls back to backup if the primary file is missing or corrupt
- **README enhancements:** Run elevated guide, known limitations, troubleshooting
- **First-run stock layout:** When no `layout.json` exists, SystemInfo builds the reference 8-box arrangement (CPU, GPU, storage, one wired network adapter, Top Processes, embedded PowerShell) from the hardware actually detected on that machine â€” titles, sensor sets and modes adapt to the hardware, nothing is hard-coded to the developer's PC.
- **`Reset to defaults`** now rebuilds that same stock layout for the current machine instead of stacking every detected component.

### Changed

- **LayoutStore:** `TryLoad()` / `TryLoadFromBackup()` / `TrySave()` â€” explicit success/failure return; `Save()` wraps `TrySave()` and logs failures instead of throwing
- **Docs:** Clarified that graphs are rebuilt per refresh (no hidden segment cache)
- **Boxes are never created automatically:** After the first run, boxes exist only when the user adds them (`+ Add`). Startup no longer injects a box per detected component.
- **Manual box sizes persist:** Startup no longer force-fits every box; `Compact boxes` / `Compact window` remain explicit menu actions.
- **Bar heat colours revised:** every sensor now has a deterministic good/bad direction. Where a higher value is better â€” SSD `Life`/`Remaining`, `Memory Available`/`Available Memory`, `GPU Memory Free`, `Free Space`, battery charge â€” low values render red and high values green. Where a higher value is worse (loads, temperatures, `Used Space`, `Percentage Used`) low is green and high is red. Sensors without a definable good/bad direction (power, clocks/frequencies, voltages, network throughput, totals) keep the low=green, high=red default.
- **`Fit boxes` renamed to `Compact boxes`** (it shrinks boxes to their content; it never moves them).
- **`Compact window` rewritten:** it now removes empty space between boxes while keeping the rows and columns you arranged â€” it no longer flattens every box into one horizontal line, and it never resizes boxes.
- **Update check:** Optional GitHub release check on startup (off by default, Settings only).

### Fixed

- **Layout load resilience:** `TryLoad()` now falls back to the `.bak` backup when the primary file is corrupt
- **CI pipeline:** `release.yml` now runs the actual test project (`SystemInfo.Tests`), not the app project
- **Tests:** Replaced hardcoded `true` assertions with real checks (NaN/Infinity rejection, capacity cap, primary/backup recovery)
- **Graph rendering:** Per-sensor graph canvases persist across refreshes; line segments reused in place instead of 1-second reallocations
- **Measured profile** (`--profile` CLI, three consecutive stress runs): default UI stays flat at ~215 MB WS / ~2100 handles / <0.5% CPU over 60 s; `--stress` (272 sensors + graphs) grows ~30 MB WS and ~35 MB private over 60 s (LHM handle accumulation), handles decline and CPU stays at ~2%; growth is repeatable, not a WPF/graph leak (three runs within 5 MB of each other)
- **Growth is repeatable, not a WPF/graph leak** (three runs within 5 MB of each other): `SensorRow.CompositionTarget.Rendering` subscriptions are cleaned up when rows are evicted from the component reuse cache
- **Vsync-decoupled graphs:** graph redraw fires on `CompositionTarget.Rendering`, not the 1 Hz sensor timer
- **Title shortening:** CPU/GPU titles now use the built-in model list and manufacturer-only heuristics; storage/network titles are shortened without dropping the model; RAM titles include the normalized DDR speed (`RAM 2400Mhz`).
- **Rename dialog:** empty title is now a legitimate value â€” erases the box's title text entirely (header shows no text). Hovering the header then shows the original creation title as a tooltip (`(empty title) â€” created as "â€¦" â€” double-click to rename`). To re-derive a title, use the "Reset to automatic title" button.
- **Box hiding removed:** The eye button, `Show hidden boxes` menu and `Visible`-based hide paths are gone; use `Hide Background` for chrome-only hover hiding.
- **Settings "Performance â†’ Refresh":** the refresh cadence is now user-selectable. Four presets: Normal (1000 ms, default), Eco (2000 ms), Fast (500 ms), Fastest (250 ms). Clamped to `[250 ms, 10000 ms]` by `Normalize`; persisted to `layout.json` as `UpdateIntervalMs`.
- **Startup/runtime performance pass:** per-box shadow removed (no more `DropShadowEffect`), `HeatBrush` now cached/coloured per 1% step, `Typeface` and `FontFamily` reused, `SensorHistory` now a ring-style ordered list (no IT-era `Queue<double>` churn), `GetBarPercent`/graph Min/Max computed inline, Top Processes PID loop collapsed (O(1) rolling count, O(1) rolling per-CPU-sum).
- **Performance harness:** `--perf-ms <milliseconds>` measures working set (delta), private bytes, handle count, and generation 0/1/2 collections for the specified window, samples every 500 ms, and writes results to `perf.log`. Excludes rendering itself; useful for catching allocations in steady state.Measured on this machine (idle layout, 9 boxes): ~1.2â€“1.4% CPU, ~210 MB working set, gen0/1/2 = 0 over 5 s.
- **GPU memory labels shortened:** `GPU Memory Used/Free/Total/Clock` and `GPU Memory` temperatures now read `VRAM Used/Free/Total/Clock` / `VRAM` in box rows and graph legends.
- **Menu text trimmed:** the two `Layout` items no longer carry parenthesised explanations â€” `Compact boxes` / `Compact window`.
- **"Jitter" gone for good from sensor rows:** width is now derived from the box's logical width alone (`Floor(Layout.Width)` minus fixed chrome/scrollbar insets with an integer grid width), never from a live `ScrollViewer.ViewportWidth` reading. The Auto scrollbar appearing/disappearing no longer re-flows the row grid, and all row dimensions are whole pixels, so text, bars and graphs stay anchored sideways across every refresh tick.
- **Compact window no longer clips box edges:** the window is now sized as content plus the full window-chrome insets (14px horizontally, 54px vertically), so the outermost boxes never sit flush against the window frame. It also resolves pre-existing overlaps in the arrangement (a later column is slid right until it clears the previous column instead of being clamped into it).
- **`SourceTitle` migration:** pre-`SourceTitle` layouts and profiles are migrated in place at first load so a future feature can distinguish creation-time titles from user-set ones. Live `layout.json` and `Chris-PC.json` were migrated in place (backups kept as `*.premigration-bak`).
- **Profiles save and restore `SourceTitle` too** (same migration applies at profile load).
- **Storage title heuristic was dead code:** the intended â‰¤5-word rule for storage names was shadowed by a 3-word truncation applied first. Storage titles now keep capacity/model tokens as intended.
- **Rename dialog derives automatic titles from hardware identity** for both empty field and the `Reset to automatic title` button: type-labelled boxes resolve to the actual component names (`type:cpu` â†’ `Ryzen 7 5700G`, `type:gpu` â†’ `RX 7800 XT`, `type:memory` â†’ resolved RAM title, `type:storagessd` â†’ first SSD model), with label fallback when that type isn't present.
- **Empty rename no longer auto-resets to the automatic title** â€” that behaviour was redundant with the `Reset to automatic title` button and masked intentional empty titles. The empty field now simply stores the empty string.
- **Restart no longer re-widens manually-narrowed boxes** to fit their title. The title-width clamp now applies only when a stock layout is built/reset; load-time `Normalize` keeps your sizes. Width and height minimums lowered from 200Ã—90 to **60Ã—60**, so boxes can be sized as small as you like.
- **Box drag delayed to a 4px threshold:** a box starts moving only after the pointer travels ~4 pixels. Before the threshold the gesture is a click â€” a double-click on the title opens Rename with the box in place; no more nudges from "accidental" drags caused by double-click attempts.
- **Sensor rows renamed for clarity:** `CPU Core #N` â†’ `Core N`; temperature sensors whose names don't say so gain a `Temp` suffix (`GPU Core` â†’ `GPU Core Temp`, `VRAM Temp`, `GPU Hot Spot Temp`); power rails named `Package` are now plain `Power` (CPU Package â†’ `Power`, GPU Package â†’ `Power`).
- **Optional window background image** (off by default): Settings section "Background image" lets the user pick a file, set opacity/saturation/brightness, see a live preview, and have it written to `layout.json`. Boxes remain opaque above the image â€” the image appears only between/around boxes. Hidden when "Hide background" is active. The Settings dialog was also enlarged to 420 Ã— 700 and the section placed just under "Window" so it doesn't disappear below the fold.
- **Rename box from the right-click menu:** each box's context menu now has `Rename boxâ€¦` (alongside `Copy box as image`). Invoking it opens the same rename dialog you get from double-clicking the title.
- **Window chrome now reacts to theme switches:** the title bar/divisor brushes are re-bound via `SetResourceReference` on hover-chrome hide/show and theme apply, instead of capturing a static brush forever. Switching themes now recolours the whole window, title bar and pattern dots instantly.
- **Red box outline removed:** previously any box with a temperature > 85 Â°C or any `Load`-category sensor > 98 triggered a red border â€” notably `Used Space` on storage (e.g. SanDisk Extreme Portable at 100), which the user perceived as a random red outline. The whole feature (and the dead "Temp alert" setting row) is gone.
- **Removed redundant "Save current state" menu items** (right-click and tray): they did everything "Save profileâ€¦" already does â€” the window bounds are written to `layout.json` on close anyway. "Profiles â†’ Save profileâ€¦" is kept as the one canonical save path.
- **Bars/graphs now scale to live telemetry instead of static defaults:** GPU memory max is derived from used+free (or LHM's `GPU Memory Total` sensor when present); fan/power/clock/current upper bounds ratchet to LHM's recorded all-time max for the sensor, so bars show real magnitude (e.g. `VRAM Used` â‰ˆ 22% at 3.6 GB of 16 GB total, not 100%).
- **Terminal box shows status in a thin footer at the bottom of the box** (e.g. `Running: Windows PowerShell`) â€” pinned via `Dock.Bottom` so position/layout hold when the box is resized.

### Technical Details

- **Framework:** .NET 8 Desktop Runtime (x64)
- **Hardware Monitor:** LibreHardwareMonitorLib 0.9.6 (MPL-2.0)
- **UI:** WPF (WinExe), frameless window with native resize handles
- **Persistence:** `%LOCALAPPDATA%\SystemInfo\layout.json` with atomic writes + `.bak` recovery

### Planned

- **GitHub Actions CI:** Auto-publish release on tag push
- **Code signing:** Installer signed with certificate
- **Incremental graph rendering:** Per-sensor segment caching/updating (not yet implemented)

