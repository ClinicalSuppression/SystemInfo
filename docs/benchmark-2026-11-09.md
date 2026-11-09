# Performance benchmark — 2026-11-09

Goal: confirm that CPU time spent by the app in steady state is reduced to a minimum, and capture a before/after sample so future regressions are easy to spot.

## Environment

- Machine: this PC (single machine, quiet conditions)
- Build: Release, `-warnaserror`, branch `production-audit-baseline`
- Install: `\bin\Release\net8.0-windows\SystemInfo.exe` launched from source
- Layout: unsaved test layout with `StartAsAdministrator = false` to avoid UAC interaction during measurement

## Method

`SystemInfo.exe --perf-ms 10000` — runs the normal 9-box layout headlessly without showing the window; samples working set / private bytes / handle count / GC generations over 10 s and writes a row to `%LOCALAPPDATA%\SystemInfo\perf.log`.

`SystemInfo.exe --stress --perf-ms 10000` — same, with the `--stress` layout (extra synthetic boxes for worst-case load).

Layouts used contain: CPU 8-core+hyperthread, GPU, 2× NVMe + SanDisk + Samsung, 1 NIC, Top Processes with limit=5, Terminal (PowerShell embedded).

## Numbers

| Scenario | CPU % | Working set | Private bytes | Handles | gen0/gen1/gen2 |
|---|---|---|---|---|---|
| pre-optimization, `--perf-ms 10000` | 0.8 | 206 MB | 133 MB | 2,180 | 0 / 0 / 0 |
| pre-optimization, `--stress --perf-ms 10000` | 0.5 | 184 MB | 118 MB | 2,135 | 1 / 0 / 0 |
| post-optimization, `--perf-ms 10000` | 0.7 | 206 MB | 134 MB | 2,180 | 0 / 0 / 0 |
| post-optimization, `--stress --perf-ms 10000` | 0.7 | 185 MB | 118 MB | 2,140 | 0 / 0 / 0 |

## What changed

- **`ProcessHelper::GetTop` no longer calls `Process.Threads.Count`** every refresh for 250+ processes. Thread count is now read only when the user sorts by "Threads" (the default sort is CPU, the most common view)
- Heat-map / process heat is computed only for the final list returned to the UI (typically 5 entries), not for every process scanned
- Rolling averages are computed in a single pass over the watched process list instead of re-walking the CPU history dictionaries
- Top process ordering is selected before heat computation is applied
- A minor avoidable allocation is removed from the helper's average step

## Caveats

- All numbers are noisy at this magnitude; CPU% varies 0.5–1.4 % across runs. The changes are far from any point of application crash or visible stutter, but raw percentages here are not a guaranteed 1:1 win.
- The stress run was not a true synthetic load; it is the existing flag set the app already accepts and does not add artificial CPU burn.
- Measurements ran on a single machine in quiet conditions. The result does not generalize to a fully-loaded, busy, large-PC across all error modes.
- No working-set improvement was observed — the re-used/reduced detail list is minor allocation toward total private bytes.
- No benchmarks were run on installer-produced `.\Output\` distribution; only source-run binary.
