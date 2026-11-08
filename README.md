# SystemInfo — System Monitor II emulation

SystemInfo is a native WinExe that **emulates the function and look/feel of the Windows 7 sidebar gadget "System Monitor II"** — a compact dark widget on the desktop. Desktop Gadgets are no longer supported, so this is a stand-alone window that sits on the desktop like the original: dark theme, frameless title bar with `SystemInfo` text, draggable, always-on-top by default, `— / ✕` in the top-right, and a settings gear `⚙`.

Data comes **only from LibreHardwareMonitor** (`LibreHardwareMonitorLib` — the maintained successor to OpenHardwareMonitor, which is deprecated and no longer used). No built-in / non-Libre sensor sections are drawn; every row is a live LHM sensor. If you disable/untick sensors for a box, that box shows "No sensors" — there is no phantom CPU box from built-ins.

## Current version: 1.0 (per-component boxes)

## Features

- **One box per hardware component instance.** CPU, each GPU (two GPUs → two boxes), Memory, Motherboard, Storage (SSD/HDD), Network adapter, etc. — each LHM hardware instance gets its own box, titled by the hardware name (e.g. `AMD Ryzen 7 5700G with Radeon Graphics`, `AMD Radeon RX 7800 XT`, `Generic Memory`, `Ethernet`). Double-click a box title to rename it (stored in `layout.json`).
- **Inside the window, boxes are movable and resizable.** Drag by the header; bottom-right thumb resizes. Dropped positions **snap to other boxes and the canvas edge with a 4px gap** (minimal space) so you can tile boxes tightly.
- **Sensor picker per box.** Header `＋ Sensors` opens a picker filtered to that component (custom `+ Box` shows all sensors). Tick **Show** to list a sensor, tick **Graph** to graph it. Search filters by sensor name or category.
- **Smart graphs.** A sensor is **graphable** only for time-varying categories: `Load, Clock, Temperature, Power, Frequency, Throughput, Fan, Voltage, Current, Flow, Control`. Graph header shows a **combined multi-line history chart** (48 samples, one colour per sensor) grouped by same component + same category + same unit (so all CPU cores share one Load chart). Per-row mini-sparklines show for graphed sensors. Bounded categories (`Load, Temperature, Power, Clock, …`) show a coloured bar (green → yellow → red); unbounded values (`Data, Throughput, Factor, SmallData`) show **value only** — no bar — to avoid misleading full-red bars for large counters.
- **`NaN` handling.** LHM may return `NaN`/`Infinity` (notably CPU Clock/Temp without admin) or `null`. Those render as `—` (not `NaN`), and bars normalize `NaN` to 0 — same limitation as LibreHardwareMonitor itself.
- **Independent scaling (5 sizes each).** Right-click menu and `⚙` settings provide separate **Font / Bar / Graph** scales (`XS/S/M/L/XL`), independent of each other (e.g. XL fonts need not enlarge bars or graphs).
- **Window controls (right-click).** `Lock window (move + resize)`, `Prevent window move`, `Keep on top`, `Lock boxes (move + resize)`, Font/Bar/Graph size submenus, `+ Box`, and `Settings…`. Dragging the title bar moves the window; the corner thumb resizes it. Window and box positions/sizes, locks, scaling, and box titles/sensors persist atomically to `%LOCALAPPDATA%\SystemInfo\layout.json` (`.bak` recovery on corruption).
- **Combo graph colours** cycle per series; up to 12 colours.
- **Reports.** Title-bar features aside, `—` minimises and `✕` closes. CLI export (`--json/--export/--help`) saves the full text/JSON report from `SystemInfoCollector` (Win32 + `EnumDisplayMonitors`/`GetSystemPowerStatus` — no WinForms).
- **Robustness.** Single-instance guard, per-monitor DPI (`PerMonitorV2`), `App.log` in `%LOCALAPPDATA%\SystemInfo\`. LHM `Computer` is opened with `IsCpu/Gpu/Memory/Motherboard/Controller/Network/Storage/Psu/Battery = true` and `Update()` visited each tick (1 s).

## Requirements

- Windows 10 or later.
- Framework-dependent build: the **.NET 8 Desktop Runtime** (x64). The self-contained publish profile needs no runtime.
- Inno Setup 6 to compile the installer.
- No admin required (run elevated only for SuperIO/motherboard or CPU-package sensors if you need them).

## Use

1. Launch `SystemInfo.exe` (or install via `Output\SystemInfo_Setup_1.0.exe`). One box per component appears stacked; scroll the window to reach lower boxes (e.g. network adapters).
2. **Right-click** the window for window locks, Keep-on-top, and scaling. **Right-click is also how you keep the window on top or prevent it being moved.** `⚙` opens Display & scaling / locks.
3. Inside a box, drag the header to move it (it snaps to neighbours with a 4px gap); drag the box's bottom-right thumb to resize.
4. Click **`＋ Sensors`** on a box to pick exactly which of its LHM sensors to show and which to graph (with combined-graphs toggle per box).
5. Double-click a box title to rename it.

### Command line

```text
SystemInfo.exe                  open the gadget
SystemInfo.exe --json           save JSON report to Documents\SystemInfo-Report.json, then exit
SystemInfo.exe --json out.json  save JSON report to out.json, then exit
SystemInfo.exe --export         save text report to Documents\SystemInfo-Report.txt, then exit
SystemInfo.exe --export out.txt save text report to out.txt, then exit
SystemInfo.exe --help           show help
```

## Build from source

```powershell
dotnet restore .\SystemInfo.slnx
dotnet build .\SystemInfo.slnx -c Release -warnaserror
dotnet run --project .\SystemInfo.Tests\SystemInfo.Tests.csproj -c Release
dotnet publish .\SystemInfo.csproj -c Release -r win-x64 --self-contained false -warnaserror
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" .\installer.iss
```

Writes `Output\SystemInfo_Setup_1.0.exe` (upgrades replace in place).

## Project layout

| File | Purpose |
| --- | --- |
| `App.xaml`, `App.xaml.cs` | WPF entry, single-instance guard, error handling, headless export. |
| `MainWindow.xaml`, `MainWindow.xaml.cs` | Frameless `SystemInfo` window; box board, drag/resize, snapping, right-click locks/scaling, per-box picker launch. |
| `ComponentBox.cs` | Per-component box control (header drag/snap, resize thumb, editable title, rows + combined graph). |
| `ComponentLayout.cs` | `BoxLayout`/`WindowLayout`, `ScaleHelper` (5 independent scales, graph/bar heuristics), `SensorHistory`, sensor descriptors. |
| `SensorCollector.cs` | LibreHardwareMonitor `Computer` collector (catalog per hardware instance, live `Collect()`). |
| `SensorPickerWindow.xaml`, `.xaml.cs` | Per-box sensor picker (Show + Graph ticks, combined-graphs toggle, search). |
| `LayoutStore.cs` | `layout.json` persistence (normalize, `.bak`, `EnsureComponentBoxes`). |
| `LayoutSettingsWindow.xaml`, `.xaml.cs` | Display & scaling + global locks dialog. |
| `SystemInfoCollector.cs`, `SystemReport.cs`, `CliArgs.cs` | Full text/JSON report + CLI parser. |
| `SystemInfo.Tests\` | 30 dependency-free checks (formatting, CLI, layout roundtrip). |
| `.github\workflows\build.yml` | CI: restore, build `-warnaserror`, tests, publish. |
| `Assets\SystemInfo.ico` | Placeholder icon. |
| `installer.iss` | Inno Setup installer. |

## Data and troubleshooting

- `NaN`/`Infinity` sensors show `—`.
- CPU temp/power/fans may need admin (Ring0) — same as LHM.
- Corrupt `layout.json` falls back to `.bak`, then defaults.
- Log: `%LOCALAPPDATA%\SystemInfo\app.log` (safe to delete).

## License

SystemInfo is distributed under the [PolyForm Noncommercial License 1.0.0](./LICENSE), copyright (c) 2026 ClinicalSuppression.

The license permits use, modification, and distribution for permitted noncommercial purposes. Commercial use is not permitted under these terms.

> SystemInfo links **LibreHardwareMonitorLib 0.9.4**, MPL-2.0 © LibreHardwareMonitor and Contributors. "System Monitor II" is a Windows sidebar gadget name used here descriptively for an emulation inspired by its look/feel; no original gadget assets or code are included.
