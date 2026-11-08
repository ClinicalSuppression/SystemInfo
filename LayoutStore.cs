using System.IO;
using System.Text.Json;

namespace SystemInfo;

public static class LayoutStore
{
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    public static string? LayoutFilePathOverride { get; set; }
    public static string LayoutFilePath
    {
        get
        {
            if (LayoutFilePathOverride is not null) return LayoutFilePathOverride;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemInfo", "layout.json");
        }
    }
    public static string BackupFilePath => LayoutFilePath + ".bak";

    public static WindowLayout LoadOrDefault()
    {
        string path = LayoutFilePath;
        if (File.Exists(path))
        {
            try
            {
                var w = JsonSerializer.Deserialize<WindowLayout>(File.ReadAllText(path), Opts);
                if (w is not null && w.Boxes is not null) return Normalize(w);
            }
            catch { }
            string bak = BackupFilePath;
            if (File.Exists(bak))
            {
                try
                {
                    var w = JsonSerializer.Deserialize<WindowLayout>(File.ReadAllText(bak), Opts);
                    if (w is not null && w.Boxes is not null) return Normalize(w);
                }
                catch { }
            }
        }
        return Normalize(new WindowLayout());
    }

    /// <summary>
    /// Attempts to load layout from disk. Falls back to the .bak backup if the primary file is missing or corrupt.
    /// Returns true on success, false if both fail.
    /// </summary>
    public static bool TryLoad(out WindowLayout layout)
    {
        // Primary.
        string path = LayoutFilePath;
        if (File.Exists(path))
        {
            try
            {
                var w = JsonSerializer.Deserialize<WindowLayout>(File.ReadAllText(path), Opts);
                if (w is not null && w.Boxes is not null)
                {
                    layout = Normalize(w);
                    return true;
                }
            }
            catch { }
        }
        // Backup fallback.
        layout = new WindowLayout();
        return TryLoadFromBackup(out layout);
    }

    /// <summary>
    /// Attempts to load layout from backup if primary fails. Returns true on success, false if both fail.
    /// </summary>
    public static bool TryLoadFromBackup(out WindowLayout layout)
    {
        string bak = BackupFilePath;
        if (!File.Exists(bak))
        {
            layout = new WindowLayout();
            return false;
        }

        try
        {
            var w = JsonSerializer.Deserialize<WindowLayout>(File.ReadAllText(bak), Opts);
            if (w is not null && w.Boxes is not null)
            {
                layout = Normalize(w);
                return true;
            }
            else
            {
                layout = new WindowLayout();
                return false;
            }
        }
        catch
        {
            layout = new WindowLayout();
            return false;
        }
    }

    /// <summary>
    /// Saves layout to disk with atomic write and backup rotation. Returns true on success, false if fail.
    /// </summary>
    public static bool TrySave(WindowLayout layout)
    {
        string path = LayoutFilePath;
        string dir = Path.GetDirectoryName(path) ?? ".";
        try
        {
            Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(layout, Opts));
            if (File.Exists(path))
            {
                try
                {
                    File.Copy(path, BackupFilePath, overwrite: true);
                }
                catch { }
            }
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Saves layout if possible; logs failure to App.LogError but never throws.
    /// </summary>
    public static void Save(WindowLayout layout)
    {
        if (!TrySave(layout))
        {
            try { App.LogError(new Exception($"Layout save failed at {LayoutFilePath}")); } catch { }
        }
    }

    public static WindowLayout Normalize(WindowLayout w)
    {
        w.Width = Math.Clamp(w.Width, 300, 4000);
        w.Height = Math.Clamp(w.Height, 300, 4000);
        w.Left = Math.Max(0, w.Left);
        w.Top = Math.Max(0, w.Top);
        w.FontScale = Math.Clamp(w.FontScale, 0, 4);
        w.BarScale = Math.Clamp(w.BarScale, 0, 4);
        w.GraphScale = Math.Clamp(w.GraphScale, 0, 4);
        if (w.ThemeMode is not ("System" or "Light" or "Dark")) w.ThemeMode = "System";
        if (w.ThemeScheme is not ("Midnight" or "Aurora" or "Ember" or "Paper")) w.ThemeScheme = "Midnight";
        w.SearchFilter ??= "";
        w.TempAlertThreshold = Math.Clamp(w.TempAlertThreshold, 50, 110);
        foreach (var b in w.Boxes)
        {
            b.X = Math.Max(0, b.X);
            b.Y = Math.Max(0, b.Y);
            b.Width = Math.Clamp(b.Width, 200, 2000);
            b.Height = Math.Clamp(b.Height, 90, 2000);
            b.SensorIds ??= [];
            b.GraphSensorIds ??= [];
            b.GraphSeriesColors ??= new Dictionary<string, int>(StringComparer.Ordinal);
            if (b.GraphScaleMode is not ("Auto" or "Fixed")) b.GraphScaleMode = "Auto";
            b.GraphLineThickness = Math.Clamp(b.GraphLineThickness <= 0 ? 1.4 : b.GraphLineThickness, 0.8, 3.0);
            b.SensorModes ??= new Dictionary<string, int>(StringComparer.Ordinal);
            if (b.NetworkSpeedUnit is not ("Auto" or "B/s" or "KB/s" or "MB/s" or "GB/s")) b.NetworkSpeedUnit = "Auto";
            foreach (string id in b.SensorModes.Keys.ToList())
            {
                int m = b.SensorModes[id];
                if (m == 0 || m < 0 || m > 7) b.SensorModes[id] = (int)RowMode.Text;
            }
            if (w.GraphMigrationVersion < 2)
            {
                // Remove any default "main"/header graphs: only per-sensor graphs the user selects show.
                b.GraphSensorIds.Clear();
                foreach (string id in b.SensorModes.Keys.ToList())
                {
                    int m = b.SensorModes[id] & ~((int)RowMode.Graph);
                    if (m == 0) m = (int)RowMode.Text;
                    b.SensorModes[id] = m;
                }
            }
            b.GraphZoom = Math.Clamp(b.GraphZoom <= 0 ? 48 : b.GraphZoom, 10, 300);
            if (b.CustomKind is not ("processes" or "power" or "app")) b.CustomKind = "";
            b.ProcessLimit = Math.Clamp(b.ProcessLimit, 1, 20);
            if (b.ProcessSort is not ("Cpu" or "Ram" or "Name" or "Threads")) b.ProcessSort = "Cpu";
            b.AppId ??= "cmd";
            b.AppArgs ??= "";
            b.AppBindings ??= [];
            // Terminals only. Migrate legacy non-terminal bindings (explorer, notepad, ...) to cmd.
            if (!AppHelper.IsAllowedAppId(b.AppId)) b.AppId = "cmd";
        }
        w.GraphMigrationVersion = 2;
        return w;
    }

    /// <summary>Creates one box per component instance. Very limited: only the most useful sensors per component.</summary>
    public static void EnsureComponentBoxes(WindowLayout layout, IReadOnlyList<(string key, ComponentType type, string name)> components, IReadOnlyDictionary<string, SensorDescriptor> catalog)
    {
        foreach (var (key, type, name) in components)
        {
            if (layout.Boxes.Any(b => b.ComponentKey == key)) continue;
            var sensors = catalog.Values.Where(d => d.ComponentKey == key).ToList();
            if (sensors.Count == 0) continue;
            var chosen = PickUsefulDefaults(type, sensors);
            if (chosen.Count == 0)
            {
                // Fallback: at most 3 best-scoring sensors (never per-core bursts).
                chosen = sensors
                    .OrderByDescending(d => UsefulnessScore(d))
                    .Take(3)
                    .ToList();
            }
            var graph = PickDefaultGraphs(type, chosen);
            var modes = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var d in chosen) modes[d.Id] = ScaleHelper.DefaultMode(d, graph.Contains(d.Id));
            int idx = layout.Boxes.Count;
            layout.Boxes.Add(new BoxLayout
            {
                ComponentKey = key, ComponentType = type, Title = ScaleHelper.ShortTitle(type, name, sensors),
                X = 8, Y = 8 + idx * 224, Width = 320, Height = 210, Visible = true,
                SensorIds = chosen.Select(d => d.Id).ToList(),
                GraphSensorIds = graph,
                SensorModes = modes,
                CombineGraphs = true, Locked = false,
            });
        }
    }

    /// <summary>
    /// Pick a very limited useful set. Prefers aggregate sensors (Total/Max/Package/Core) over per-core (#n) ones.
    /// CPU: All-Cores Load + Package Temp + Package Power + one Voltage. GPU: Core Load + Core Temp + Package Power
    /// + Core Clock + Fan + GPU-Memory Used/Total + one Voltage. Memory: Load + Used + Available. Network: Utilization
    /// + Download/Upload Speed. Others: Load + Temp + Power aggregates.
    /// </summary>
    private static List<SensorDescriptor> PickUsefulDefaults(ComponentType type, List<SensorDescriptor> sensors)
    {
        List<SensorDescriptor> chosen = [];
        void AddBest(string category, Func<SensorDescriptor, int> score, bool requireAggregate = false)
        {
            var cands = sensors.Where(d => d.Category.ToLowerInvariant() == category).ToList();
            if (requireAggregate)
                cands = cands.Where(d => !d.Name.Contains('#')).ToList();
            if (cands.Count == 0) return;
            var best = cands.OrderByDescending(d => score(d)).ThenBy(d => d.Name).FirstOrDefault();
            if (best is not null && !chosen.Contains(best)) chosen.Add(best);
        }
        int Score(string d, params string[] keys)
        {
            string n = d.ToLowerInvariant();
            int s = 0;
            if (n.Contains("total")) s += 100;
            if (n.Contains("max")) s += 90;
            if (n.Contains("package")) s += 80;
            if (n.Contains("tctl") || n.Contains("tdie")) s += 85;
            if (n.Contains("hot spot")) s += 70;
            if (n.Contains("gpu core")) s += 95;
            if (n.Contains("cpu total")) s += 95;
            if (n.Contains("average")) s += 60;
            if (n.Contains("effective")) s += 95;
            if (n.Contains("pdh")) s += 55;
            if (n.Contains('#')) s -= 50;
            return s;
        }

        switch (type)
        {
            case ComponentType.Cpu:
                AddBest("load", d => Score(d.Name)); // -> CPU Total
                // LHM aggregates that the picker must show: Cores (Average)/Max, Effective clock.
                foreach (var n in new[] { "Cores (Average)", "Cores (Max)", "CPU Core Max", "CPU Total" })
                {
                    var sd = sensors.FirstOrDefault(d => d.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
                    if (sd is not null && !chosen.Contains(sd)) chosen.Add(sd);
                }
                AddBest("temperature", d => Score(d.Name)); // -> Core (Tctl/Tdie)
                AddBest("power", d => Score(d.Name)); // -> Package
                AddBest("voltage", d => d.Name.Contains("core ") && d.Name.Contains("svi2") || d.Name.Contains("core") ? 80 : 0, requireAggregate: true);
                // Effective/Average clocks (may be NaN without Ring0 — still listed, shown as —).
                var clockAgg = sensors.Where(d => (d.Category.Equals("Clock", StringComparison.OrdinalIgnoreCase) || d.Category.Equals("Frequency", StringComparison.OrdinalIgnoreCase)) && !d.Name.Contains('#') && !chosen.Contains(d)).OrderByDescending(d => Score(d.Name) + (d.Name.ToLowerInvariant().Contains("effective") ? 90 : 0) + (d.Name.ToLowerInvariant().Contains("average") ? 80 : 0)).ToList();
                foreach (var d in clockAgg) if (!chosen.Contains(d)) chosen.Add(d);
                break;
            case ComponentType.Gpu:
                AddBest("load", d => Score(d.Name)); // -> GPU Core
                AddBest("temperature", d => Score(d.Name)); // -> GPU Core
                AddBest("power", d => Score(d.Name)); // -> GPU Package
                AddBest("clock", d => Score(d.Name), requireAggregate: true); // -> GPU Core Clock
                AddBest("fan", d => 100);
                AddBest("voltage", d => Score(d.Name), requireAggregate: true);
                foreach (var n in new[] { "GPU Memory Used", "GPU Memory Total" })
                {
                    var sd = sensors.FirstOrDefault(d => d.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
                    if (sd is not null && !chosen.Contains(sd)) chosen.Add(sd);
                }
                break;
            case ComponentType.Memory:
                AddBest("load", d => d.Name.Equals("Memory", StringComparison.OrdinalIgnoreCase) ? 100 : 0);
                foreach (var n in new[] { "Memory Used", "Memory Available" })
                {
                    var sd = sensors.FirstOrDefault(d => d.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
                    if (sd is not null && !chosen.Contains(sd)) chosen.Add(sd);
                }
                break;
            case ComponentType.Network:
                AddBest("load", d => 100); // Network Utilization (only load)
                foreach (var n in new[] { "Download Speed", "Upload Speed" })
                {
                    var sd = sensors.FirstOrDefault(d => d.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
                    if (sd is not null && !chosen.Contains(sd)) chosen.Add(sd);
                }
                break;
            case ComponentType.Storage:
            case ComponentType.StorageHdd:
            case ComponentType.StorageSsd:
                AddBest("load", d => Score(d.Name));
                AddBest("temperature", d => Score(d.Name));
                AddBest("power", d => Score(d.Name));
                break;
            default:
                AddBest("load", d => Score(d.Name));
                AddBest("temperature", d => Score(d.Name));
                AddBest("power", d => Score(d.Name));
                AddBest("clock", d => Score(d.Name), requireAggregate: true);
                break;
        }
        return chosen;
    }

    private static int UsefulnessScore(SensorDescriptor d)
    {
        string n = d.Name.ToLowerInvariant();
        int s = 0;
        if (n.Contains("total")) s += 100;
        if (n.Contains("max")) s += 90;
        if (n.Contains("package")) s += 80;
        if (SurfaceCategoryWeight(d.Category) == 0) s -= 20;
        if (n.Contains('#')) s -= 50;
        return s;
    }

    private static int SurfaceCategoryWeight(string category) => category.ToLowerInvariant() switch
    {
        "load" or "temperature" or "power" or "clock" or "throughput" or "data" or "smalldata" => 10,
        _ => 0,
    };

    private static List<string> PickDefaultGraphs(ComponentType type, List<SensorDescriptor> chosen)
    {
        // No default graphs: only show graphs the user explicitly enables.
        return [];
    }

    public static BoxLayout CreateProcessesBox(double y)
        => new() { ComponentKey = "custom:processes", ComponentType = ComponentType.System, Title = "Top Processes", X = 8, Y = y, Width = 320, Height = 260, CustomKind = "processes", ProcessLimit = 10, ProcessSort = "Cpu" };

    public static BoxLayout CreatePowerBox(double y)
        => new() { ComponentKey = "custom:power", ComponentType = ComponentType.System, Title = "Power", X = 8, Y = y, Width = 320, Height = 200, CustomKind = "power" };

    public static BoxLayout CreateAppBox(double y, string appId = "cmd")
        => new() { ComponentKey = "custom:terminal", ComponentType = ComponentType.System, Title = "Terminal", X = 8, Y = y, Width = 380, Height = 300, CustomKind = "app", AppId = appId };
}
