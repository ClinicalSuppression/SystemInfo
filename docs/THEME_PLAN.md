# SystemInfo — UI / Theme overhaul plan

> Status: **Plan only** (no code yet). Current build: dark `#0E1A28` window, `#122537` boxes (hard-coded in `App.xaml` + `ComponentBox.cs`), `SystemInfo` title, `LayoutSettingsWindow`, `SensorPickerWindow`.

## 1. Why current look feels flat

| Symptom | Cause (today) |
|---|---|
| Boxes melt into window | `Window #0E1A28` vs `Box #122537` = only Δ=~5% lightness. Flat `Border #26425F`, no shadow, `CornerRadius 8` + `Padding 8` eats most of the gradient. |
| Hard-coded colours | `App.xaml` ScrollBar/Thumb (`#2E4A6B/#3E6A94`), `ComponentBox` (`#EAF6FF/#6DD5FA/#8FB0CC`, graph palette `[0x6D,0xD5,0xFA...]`, bar brushes) — impossible to switch theme. |
| Dense rows | Name 150px + value 76px + bar/graph 70px = cramped; sensor picker = 280px `CheckBox` + 3 toggles. No scanline / zebra. |
| No hierarchy | Title 12pt SemiBold small difference vs rows; header graph + mini-graphs compete; scrollbars are pure decoration. |
| No light mode | All brushes are dark-on-dark; picker + settings windows are white (`Background="White"`) but not themed. |

## 2. Design goals

1. **Separate boxes clearly** — window ≠ box fill, box has elevation + 2-tone border.
2. **Light + Dark** with *one* token set per scheme; mode auto-follows Windows (`AppsUseLightTheme` registry) but is overridable.
3. **Several schemes** that work in *both* modes (shared accent of the family, different surfaces).
4. **Keep personality**: rounded, compact, dashboard-like. 4px spacing grid, Segoe UI.

## 3. Colour tokens (the heart)

Replace every hard-coded brush with `DynamicResource`:

```
WindowBackgroundBrush          // vertical gradient: WindowBackgroundBrushTop → Bottom
WindowBorderBrush
BoxBackgroundBrush             // card fill
BoxHeaderBackgroundBrush
BoxBorderBrush                 // outer 1px
BoxHighlightBrush              // top inner 1px (white 10 %)
TextPrimaryBrush  (#EAF6FF)
TextMutedBrush    (#8FB0CC)
AccentBrush       (#6DD5FA)    // values, graph 1, links, primary buttons
AccentDimBrush    (#3E6A94)
TrackBrush        (#0A1624)    // bar & slider track
BarOkayBrush / BarWarnBrush / BarHotBrush  (green / amber / red)
ScrollbarThumbBrush / Hover
ResizeGripBrush
GraphPalette[0..11]            // 12-colour cycle, stored per theme
```

Storage: `WindowLayout.ThemeMode = System|Light|Dark`, `WindowLayout.ThemeScheme = Midnight|Aurora|Ember|Frost`. Loaded in `App.OnStartup` by merging `Themes/{Scheme}{Mode}.xaml`. Everywhere you now do `new SolidColorBrush(Color.FromRgb(0x12,0x25,0x37))` you’ll do `(Brush)Application.Current.FindResource("BoxBackgroundBrush")` or better `DynamicResource` in XAML / `SetResourceReference` in code.

No new dependency: pure `ResourceDictionary` XAML files.

## 4. Background separation (window vs box)

**Window:** `<LinearGradientBrush StartPoint="0,0" EndPoint="0,1">` Top = surface +8 % lightness, Bottom = surface. Add a subtle tiled dot pattern overlay:

```xml
<TileBrush Viewport="0,0,14,14" TileSource="{... dots 1px/12 %}">
   <Canvas><Ellipse Width="1.2" Height="1.2" Fill="{DynamicResource PatternDotBrush}"/></Canvas>
</TileBrush>
```

Stack: `Border(Window) > Grid: PatternRect + ScrollViewer`.

**Box elevation:**

```xml
<Border Background="{DynamicResource BoxBackgroundBrush}"
        BorderBrush="{DynamicResource BoxBorderBrush}" BorderThickness="1"
        CornerRadius="10" SnapsToDevicePixels="True">
  <Border.Effect>
    <DropShadowEffect BlurRadius="12" ShadowDepth="2" Opacity="0.35" Color="Black"/>
  </Border.Effect>
  <Border><Border BorderBrush="{DynamicResource BoxHighlightBrush}" BorderThickness="0,1,0,0" CornerRadius="10,10,0,0"/></Border>
</Border>
```

Header gets `BoxHeaderBackgroundBrush` + `CornerRadius 10,10,0,0`, title `TextPrimary` 12pt SemiBold, muted tooltip. This gives cards a classic “lit-from-above” read in both modes.

## 5. Schemes (all with Light + Dark variants, 8 total tiles)

Share one accent per family; only surfaces swap.

### Dark (defaults)

| Scheme | Window Top→Bottom | Box | Header | Border | Accent | Text | Muted |
|---|---|---|---|---|---|---|---|
| **Midnight** (current, polished) | `#142A3E → #0E1A28` | `#122537` | `#0D1C2E` | `#3E6A94` | `#6DD5FA` | `#EAF6FF` | `#8FB0CC` |
| **Aurora** (teal-green night) | `#122830 → #0B1A1E` | `#10232A` | `#0A181C` | `#2F5D5A` | `#4AE3B2` | `#E8FFF6` | `#8AC7B5` |
| **Ember** (warm dark) | `#2A1E22 → #1A121A` | `#241A20` | `#1D1318` | `#5A3A3A` | `#FFB86B` | `#FFF3E6` | `#C9AFA0` |
| **Mono Dark / HC** | `#1A1A1A → #000000` | `#111111` | `#000000` | `#5A5A5A` | `#FFFFFF` | `#FFFFFF` | `#A0A0A0` |

Graph palettes: keep 12 hues, just rotate per scheme (Cyan → Teal → Amber → Rose …) so no two series collide on a given background.

### Light

| Scheme | Window | Box | Header | Border | Accent | Text | Muted |
|---|---|---|---|---|---|---|---|
| **Frost** (Windows 11 light) | `#F2F4F8 → #E8EDF3` | `#FFFFFF` | `#E6EAF0` | `#D0D8E4` | `#0078D4` | `#1C2533` | `#5A6B80` |
| **Paper** (warm) | `#FAF7F2 → #F0EAE0` | `#FFFFFF` | `#EFE9DD` | `#E0D6C2` | `#B5533C` | `#342A1E` | `#8A7A64` |
| **Midnight Light** | `#EAF2FA → #D9E6F2` | `#FFFFFF` | `#D6E4F0` | `#B9CDE3` | `#3E6A94` | `#10233A` | `#4A6B8A` |
| **Aurora Light** | `#E8F7F2 → #D6EFE6` | `#FFFFFF` | `#D6EFE6` | `#B9D9CC` | `#0F7A5A` | `#0E2E28` | `#4A7A68` |

Bar green/amber/red also get light variants (`#0F8A4A/#B7791F/#C81E1E` darkened for contrast on white).

Total implementation: 8 small XAML files in `Themes/`, each ~40 lines. Picker/settings window backgrounds become `WindowBackgroundBrush` so they follow mode too (today they’re hard `White`).

## 6. UX overhaul (beyond colours)

- **Sensor picker v2** (at `SensorPickerWindow`, soon `LayoutSettingsWindow` too):
  - `DataGrid` (or `Grid` rows) with columns: `Show ✓ | Text ✓ | Bar ✓ | Graph ✓` aligned, plus category filter chips (`Load | Temp | Power | All`) instead of only search text. Bulk row: “Select all Text/Bar/Graph for filtered”.
  - Sticky header (`Name / Category / Unit`). Scrolls with thin themed scrollbar, stays visible while scrolling.
  - Keep the combined-header-graph toggle, but label it `Combine header graph (same category)`.

- **Box header:**
  - Title ellipsized (already), `＋ Sensors` becomes ghost button (transparent → `AccentDim` hover). `✕` gets red hover.
  - Drag handle shows on hover (`SizeAll` cursor already). Add 4px top inset so draggable area isn’t the grip thumb.

- **Rows:**
  - Zebra? No — rely on spacing + muted category removed already. Use `RowSpacing 4px`, `Name 110px / value 70px / bar 60px / spark 60px` so you can see `Text+Bar+Graph` without clipping.
  - Values right-aligned tabular (`FontFeatureSettings` tnum if you want).
  - Sparklines get 1px baseline (faint `TrackBrush`) for readability.

- **Header graph:**
  - Add axes labels (`0 – 100 %`, `0 – 100 °C`) on hover tooltip + tooltip with current value & time. Keep it non-intrusive.
  - Legend? With ≤2 series: tooltip suffices; with 4+ series: tiny colour keys in graph corner.

- **Empty/NaN states:** `—` already; add italic muted for “No sensors” and for custom boxes a dashed placeholder.

- **Settings window:**
  - Add **Theme** section: Mode `System|Light|Dark` + Scheme dropdown + live preview swatch.
  - Keep scaling (Font/Bar/Graph) but group as a segmented control (XS–XL segmented, not dropdown).
  - Keep single `Lock window`, `Lock boxes`, `Keep on top` (the duplicate “Prevent move” was removed in the last build).

- **Motion:**
  - 120 ms ease on box drag snap, 90 ms fade for picker open, bar fill animates (250 ms). Respect “Reduce motion” (`SystemParameters.IsRemoteSession` / `ClientAreaAnimation`).

- **Minimise to tray:** `—` currently minimises; add tray icon (gadget style) so `✕` can “hide to tray” as an option.

## 7. Phases & effort

| Phase | Work | Verif |
|---|---|---|
| **P0 – Tokens** | Extract all hard-coded brushes in `ComponentBox` + `App.xaml` to 14 `DynamicResource`s. No look change. | `dotnet build -warnaserror`, `30/30` tests. |
| **P1 – Separation** | Gradient window + dot pattern + box shadow/highlight. | Screenshot 340×640, boxes pop. |
| **P2 – Themes** | `Themes/*.xaml` × 8, `ThemeMode/Scheme` in `WindowLayout`, auto `AppsUseLightTheme` watcher. | Toggle in Settings repaints instantly, persists to `layout.json`. |
| **P3 – UX** | Picker DataGrid + chips, segmented scaling, graph tooltips, motion, tray. | Picker screenshot, 1s refresh still 60 fps. |
| **P4 – Polish** | Accessibility (HC scheme, focus rings), layout export/import, profiles. | HC mode AA on white + black. |

Each phase is `one-file` small; total ~600 lines XAML/CS.

## 8. Further recommendations (beyond theming)

1. **Alerts:** threshold per sensor (temp > 85 °C, fan < 1000 RPM) → badge + tray bubble, log to `app.log`. Trivial on top of `Values`.
2. **Profiles:** save/import `layout.json` as named profiles (`Gaming`, `Idle`, `Stream`) + “Reset to defaults” (today you must delete the file).
3. **Hide/show boxes:** eye toggle in header (persisted `Visible` already exists but no UI).
4. **Search in main window:** filter boxes by component.
5. **Copy/Layout export:** already have `--json/--export`; add “Copy box as image” (render `ComponentBox` to PNG via `RenderTargetBitmap`).
6. **History zoom:** hold `Ctrl` + wheel on header graph to zoom 10 s → 5 min (ring buffer already 48; raise to 300).
7. **Process box spark:** mini bar for CPU% already; add % of total tooltips.
8. **Power box:** 30 s abort already; add hibernate + “Wake on LAN” disabled flag.
9. **App pinning:** multiple apps per box (today one `AppId` per box) + app icon (from exe) + args.
10. **Sys-tray + startup:** `--minimized` / registry `Run` entry.
11. **Colour-blind safe palette:** include Okabe-Ito variant of graph palette (toggle).
12. **Keyboard:** `Esc` closes dialogs, `Ctrl+F` focuses picker search.
13. **Performance:** LHM `Update()` every 1 s is fine; skip off-screen boxes’ graphs (only visible viewport).
14. **i18n:** UI strings externalised; date/percent already `CurrentCulture`.

## 9. Recommendation for your first pick

Start with **Midnight Dark (default) + Frost Light** — one family, both modes, minimal new tokens — then add **Aurora** (green) as the second family if you like it. Ember/Mono can follow. Implementation order above gives you a visible win (gradient + elevation) in <1 day, then themes are just swapping XAML files.

**Next step?** Tell me which schemes you want in P2 (e.g. “Midnight Dark/Light + Aurora Dark/Light”) and whether you want tray/alerts in this pass, and I’ll implement P0→P2.
