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

### Planned
- **GitHub Actions CI:** Auto-publish release on tag push
- **Code signing:** Installer signed with certificate
- **Incremental graph rendering:** Per-sensor segment caching/updating (not yet implemented)
