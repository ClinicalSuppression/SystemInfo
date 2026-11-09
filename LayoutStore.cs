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

    /// <summary>True when a saved layout (or its backup) exists. Used to decide whether this is a first run.</summary>
    public static bool ExistsOnDisk => File.Exists(LayoutFilePath) || File.Exists(BackupFilePath);

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
            b.Width = Math.Clamp(b.Width, 60, 2000);
            b.Height = Math.Clamp(b.Height, 60, 2000);
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
        // Normalize: one-time title migration. Runs ONLY when TitlesMigrationVersion < 1.
        if (w.TitlesMigrationVersion < 1)
        {
            foreach (var b in w.Boxes)
            {
                if (!string.IsNullOrWhiteSpace(b.Title))
                    b.Title = ScaleHelper.ShortTitle(b.ComponentType, b.Title);
            }
            w.TitlesMigrationVersion = 1;
        }
        // The title-width clamp lives only in box creation (stock layout), never on load:
        // a user may manually size boxes smaller than the full title.
        //
        // Migration for layouts saved before SourceTitle existed: the last title the box was
        // shown is the best available creation-time title, and the value that previously had no
        // reliable "reset" semantics at all. Never overwrite a recorded SourceTitle.
        foreach (var b in w.Boxes)
            if (string.IsNullOrWhiteSpace(b.SourceTitle)) b.SourceTitle = b.Title;
        return w;
    }

    /// <summary>Minimum width that shows a box title in full (text + header padding and buttons).</summary>
    private static double TitleWidthFor(string? title)
        => Math.Min(500, Math.Ceiling(ScaleHelper.MeasureTextWidth(
            string.IsNullOrWhiteSpace(title) ? "x" : title, 12, semibold: true) + 8 /*pad*/ + 58 /*gear*/ + 30 /*eye*/ + 30 /*close*/));

    /// <summary>
    /// Auto-generated title to use when the user clears a box title or presses "Reset to automatic title".
    /// Straight engine derivation from the box's hardware identity only — recorded titles are ignored.
    /// </summary>
    public static string DeriveAutoTitle(BoxLayout b)
    {
        // Custom boxes: their creation rules, unchanged.
        switch (b.CustomKind)
        {
            case "processes": return "Top Processes";
            case "power": return "Power";
            case "app":
                string app = AppHelper.DisplayName(b.AppId);
                return string.IsNullOrWhiteSpace(app) ? "Terminal" : app;
        }
        if (b.ComponentKey?.StartsWith("type:", StringComparison.OrdinalIgnoreCase) == true)
        {
            return (b.ComponentKey[5..].ToLowerInvariant()) switch
            {
                "cpu" => "CPU", "gpu" => "GPU", "memory" => "Memory", "storagessd" => "SSD",
                "storagehdd" => "HDD", "storage" => "Storage", "motherboard" => "Motherboard",
                "controller" => "Controller", "psu" => "PSU", "network" => "Network", "battery" => "Battery",
                _ => "Sensors",
            };
        }
        if (string.Equals(b.ComponentKey, "custom", StringComparison.OrdinalIgnoreCase)) return "Sensors";
        // Hardware components were saved as "Type:Name" keys: shorten the embedded name.
        if (b.ComponentKey is not null && b.ComponentKey.Contains(':'))
            return ScaleHelper.ShortTitle(b.ComponentType, b.ComponentKey[(b.ComponentKey.IndexOf(':') + 1)..]);
        return ScaleHelper.ShortTitle(b.ComponentType, b.ComponentKey ?? "Sensors");
    }

    /// <summary>
    /// Title to restore when the user clears a box title.
    /// Honours the recorded creation title; falls back to engine derivation for boxes without one.
    /// </summary>
    public static string AutoTitleFor(BoxLayout b)
    {
        if (!string.IsNullOrWhiteSpace(b.SourceTitle)) return b.SourceTitle.Trim();
        return DeriveAutoTitle(b);
    }

    /// <summary>
    /// The stock layout used only on first run (no layout on disk yet) or after "Reset to defaults".
    /// It mirrors the reference 8-box arrangement, but every title/sensor is derived from the machine it runs on.
    /// New boxes are never added automatically after that — only the user adds boxes.
    /// </summary>
    public static WindowLayout BuildStockDefault(
        IReadOnlyList<(string key, ComponentType type, string name)> components,
        IReadOnlyDictionary<string, SensorDescriptor> catalog,
        double left, double top)
    {
        var w = new WindowLayout
        {
            Left = left, Top = top, Width = 1079, Height = 586,
            ThemeMode = "Dark", ThemeScheme = "Midnight",
            FontScale = 2, BarScale = 2, GraphScale = 2,
            HideBackground = true, CheckForUpdatesOnStartup = false,
            GraphMigrationVersion = 2, TitlesMigrationVersion = 1,
        };

        BoxLayout? MakeComponent((string key, ComponentType type, string name) comp, double y, double width, double height)
        {
            var sensors = catalog.Values.Where(d => d.ComponentKey == comp.key).ToList();
            if (sensors.Count == 0) return null;
            var chosen = PickStockSensors(comp.type, sensors);
            if (chosen.Count == 0) return null;
            var modes = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var d in chosen) modes[d.Id] = StockModeFor(d);
            return new BoxLayout
            {
                ComponentKey = comp.key,
                ComponentType = comp.type,
                Title = ScaleHelper.ShortTitle(comp.type, comp.name, sensors),
                Y = y, Width = width, Height = height,
                SensorIds = chosen.Select(d => d.Id).ToList(),
                SensorModes = modes,
                GraphSensorIds = chosen.Where(d => ScaleHelper.ModeHasGraph(modes[d.Id])).Select(d => d.Id).ToList(),
                CombineGraphs = true,
            };
        }

        var col1 = new List<BoxLayout>();
        var col2 = new List<BoxLayout>();
        var col3 = new List<BoxLayout>();
        var col4 = new List<BoxLayout>();
        void AddIf(List<BoxLayout> col, BoxLayout? b) { if (b is not null) col.Add(b); }

        // Column 1: CPU on top, one network adapter underneath.
        var cpu = components.FirstOrDefault(c => c.type == ComponentType.Cpu);
        if (cpu.key is not null) AddIf(col1, MakeComponent(cpu, 8, 201, 358));

        int NetScore((string key, ComponentType type, string name) c)
        {
            string n = c.name.ToLowerInvariant();
            if (n.Contains("ethernet")) return 3;
            if (n.Contains("wi-fi") || n.Contains("wifi") || n.Contains("wireless") || n.Contains("wlan")) return 2;
            if (n.Contains("virtual") || n.Contains("vpn") || n.Contains("tun") || n.Contains("loopback")) return 0;
            return 1;
        }
        var net = components.Where(c => c.type == ComponentType.Network)
                            .OrderByDescending(NetScore).ThenBy(c => c.name)
                            .FirstOrDefault();
        if (net.key is not null) AddIf(col1, MakeComponent(net, 370, 201, 140));

        // Column 2: GPU.
        var gpu = components.FirstOrDefault(c => c.type == ComponentType.Gpu);
        if (gpu.key is not null) AddIf(col2, MakeComponent(gpu, 8, 223, 506));

        // Column 3: Top Processes, then up to three storage devices.
        var proc = CreateProcessesBox(8);
        proc.Width = 230; proc.Height = 146; proc.ProcessLimit = 5;
        col3.Add(proc);
        var storageWidths = new double[] { 230, 276, 266 };
        var storage = components
            .Where(c => c.type is ComponentType.StorageSsd or ComponentType.StorageHdd or ComponentType.Storage)
            .Where(c => catalog.Values.Any(d => d.ComponentKey == c.key))
            .Take(3)
            .ToList();
        for (int i = 0; i < storage.Count; i++)
            AddIf(col3, MakeComponent(storage[i], 158 + i * 123, storageWidths[i], 119));

        // Column 4: embedded terminal.
        var term = CreateAppBox(8, "powershell");
        term.Title = AppHelper.DisplayName("powershell");
        term.Width = 389; term.Height = 269;
        col4.Add(term);

        // Position columns left-to-right, widening a column when a title needs more room.
        var columns = new[] { col1, col2, col3, col4 };
        double x = 8, bottom = 0;
        foreach (var col in columns)
        {
            if (col.Count == 0) continue;
            double colW = col.Max(b => Math.Max(b.Width, TitleWidthFor(b.Title)));
            foreach (var b in col)
            {
                b.X = x;
                b.Width = Math.Max(b.Width, TitleWidthFor(b.Title));
                w.Boxes.Add(b);
                bottom = Math.Max(bottom, b.Y + b.Height);
            }
            x += colW + 4;
        }
        w.Width = Math.Max(1079, Math.Ceiling(x + 4));
        w.Height = Math.Max(586, Math.Ceiling(bottom + 8));
        var built = Normalize(w);
        foreach (var b in built.Boxes) b.SourceTitle = b.Title;
        return built;
    }

    /// <summary>Sensor set that mirrors the stock boxes (CPU total + alternated cores; GPU load/temp/mem; etc.).</summary>
    private static List<SensorDescriptor> PickStockSensors(ComponentType type, List<SensorDescriptor> sensors)
    {
        List<SensorDescriptor> chosen = [];
        void Add(SensorDescriptor? d) { if (d is not null && !chosen.Contains(d)) chosen.Add(d); }
        SensorDescriptor? Exact(string category, params string[] names)
        {
            foreach (string n in names)
            {
                var hit = sensors.FirstOrDefault(d => d.Category.Equals(category, StringComparison.OrdinalIgnoreCase)
                                                   && d.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) return hit;
            }
            return null;
        }
        SensorDescriptor? FirstAggregate(string category)
            => sensors.FirstOrDefault(d => d.Category.Equals(category, StringComparison.OrdinalIgnoreCase) && !d.Name.Contains('#'));
        bool HasCategory(string category) => chosen.Any(d => d.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        switch (type)
        {
            case ComponentType.Cpu:
                Add(Exact("Load", "CPU Total") ?? FirstAggregate("Load"));
                // Show one bar per physical core: LHM lists logical threads, so take every second core.
                var cores = sensors
                    .Where(d => d.Category.Equals("Load", StringComparison.OrdinalIgnoreCase)
                             && d.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase))
                    .Select(d => (d, n: CoreNumber(d.Name)))
                    .Where(x => x.n > 0)
                    .OrderBy(x => x.n)
                    .Select(x => x.d)
                    .ToList();
                int shown = 0;
                foreach (var core in cores)
                {
                    if (shown >= 8) break;
                    if (CoreNumber(core.Name) % 2 == 0) continue;
                    Add(core);
                    shown++;
                }
                Add(Exact("Temperature", "Core (Tctl/Tdie)", "Core (Tctl)", "CPU Package", "Core Average", "Core Max")
                    ?? FirstAggregate("Temperature"));
                Add(Exact("Power", "Package", "CPU Package", "Package Power") ?? FirstAggregate("Power"));
                foreach (var v in sensors
                    .Where(d => d.Category.Equals("Voltage", StringComparison.OrdinalIgnoreCase) && !d.Name.Contains('#'))
                    .OrderByDescending(d => d.Name.Contains("SVI2 TFN", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(d => d.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                    .Take(2))
                    Add(v);
                break;

            case ComponentType.Gpu:
                Add(Exact("Load", "D3D 3D"));
                Add(Exact("Load", "D3D Compute 0"));
                Add(Exact("Load", "GPU Core"));
                if (!HasCategory("Load")) Add(FirstAggregate("Load"));
                Add(Exact("Temperature", "GPU Core"));
                Add(Exact("Temperature", "GPU Hot Spot"));
                Add(Exact("Temperature", "GPU Memory"));
                if (!HasCategory("Temperature")) Add(FirstAggregate("Temperature"));
                Add(Exact("Power", "GPU Package") ?? FirstAggregate("Power"));
                Add(Exact("Clock", "GPU Core") ?? FirstAggregate("Clock"));
                Add(Exact("Clock", "GPU Memory"));
                Add(Exact("Fan", "GPU Fan") ?? FirstAggregate("Fan"));
                Add(Exact("SmallData", "GPU Memory Used", "GPU Memory Total"));
                Add(Exact("SmallData", "GPU Memory Free"));
                break;

            case ComponentType.Memory:
                Add(Exact("Load", "Memory") ?? FirstAggregate("Load"));
                Add(Exact("Data", "Memory Used", "Used Memory"));
                Add(Exact("Data", "Memory Available", "Available Memory"));
                break;

            case ComponentType.Network:
                Add(Exact("Throughput", "Download Speed"));
                Add(Exact("Throughput", "Upload Speed"));
                if (chosen.Count == 0) Add(FirstAggregate("Load"));
                if (chosen.Count == 0) Add(FirstAggregate("Throughput"));
                break;

            case ComponentType.Storage:
            case ComponentType.StorageHdd:
            case ComponentType.StorageSsd:
                Add(Exact("Load", "Total Activity", "Activity") ?? FirstAggregate("Load"));
                Add(Exact("Temperature", "Composite Temperature", "Temperature") ?? FirstAggregate("Temperature"));
                Add(Exact("Load", "Used Space"));
                Add(Exact("Level", "Life", "Remaining Life") ?? FirstAggregate("Level"));
                break;

            default:
                Add(FirstAggregate("Load"));
                Add(FirstAggregate("Temperature"));
                Add(FirstAggregate("Power"));
                Add(FirstAggregate("Clock"));
                break;
        }
        return chosen;
    }

    /// <summary>Per-row display mode that matches the stock look (bars, graphs, or plain text).</summary>
    private static int StockModeFor(SensorDescriptor d)
    {
        int text = (int)RowMode.Text;
        string name = d.Name.ToLowerInvariant();
        string cat = d.Category.ToLowerInvariant();
        switch (d.ComponentType)
        {
            case ComponentType.Cpu:
                if (cat == "load" && name.Contains("core")) return text | (int)RowMode.Bar;
                if (cat is "temperature" or "power") return text | (int)RowMode.Bar | (int)RowMode.Graph;
                if (cat == "load") return text | (int)RowMode.Graph;
                return text | (int)RowMode.Bar;
            case ComponentType.Gpu:
                if (cat is "load" or "temperature" or "fan") return text | (int)RowMode.Graph;
                if (cat == "smalldata") return name.Contains("free") ? text | (int)RowMode.Bar : text | (int)RowMode.Graph;
                if (cat is "clock" or "power") return text;
                return ScaleHelper.DefaultMode(d, false);
            case ComponentType.Network:
                return name.Contains("download") ? text | (int)RowMode.Graph : text | (int)RowMode.Bar | (int)RowMode.Graph;
            default:
                return text | (int)RowMode.Bar;
        }
    }

    /// <summary>Extracts the trailing number from names like "CPU Core #7".</summary>
    private static int CoreNumber(string name)
    {
        int hash = name.LastIndexOf('#');
        return hash >= 0 && int.TryParse(name[(hash + 1)..], out int n) ? n : 0;
    }

    public static BoxLayout CreateProcessesBox(double y)
        => new() { ComponentKey = "custom:processes", ComponentType = ComponentType.System, Title = "Top Processes", SourceTitle = "Top Processes", X = 8, Y = y, Width = 320, Height = 260, CustomKind = "processes", ProcessLimit = 10, ProcessSort = "Cpu" };

    public static BoxLayout CreatePowerBox(double y)
        => new() { ComponentKey = "custom:power", ComponentType = ComponentType.System, Title = "Power", SourceTitle = "Power", X = 8, Y = y, Width = 320, Height = 200, CustomKind = "power" };

    public static BoxLayout CreateAppBox(double y, string appId = "cmd")
        => new() { ComponentKey = "custom:terminal", ComponentType = ComponentType.System, Title = "Terminal", SourceTitle = "Terminal", X = 8, Y = y, Width = 380, Height = 300, CustomKind = "app", AppId = appId };
}
