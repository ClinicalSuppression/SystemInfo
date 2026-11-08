namespace SystemInfo
{
    /// <summary>Component type grouping sensors into boxes (one box per hardware instance).</summary>
    public enum ComponentType { Cpu, Gpu, Memory, Motherboard, Storage, StorageHdd, StorageSsd, Network, Battery, Controller, Psu, System }
    /// <summary>How a sensor row draws its value. Flags combine: Text | Bar | Graph.</summary>
    [Flags]
    public enum RowMode
    {
        None = 0,
        Text = 1,
        Bar = 2,
        Graph = 4,
    }

    /// <summary>Descriptor for one LibreHardwareMonitor sensor.</summary>
    public sealed class SensorDescriptor
    {
        public string Id { get; set; } = "";
        public string ComponentKey { get; set; } = "";   // stable instance key, e.g. "Cpu|AMD Ryzen..."
        public ComponentType ComponentType { get; set; }
        public string ComponentName { get; set; } = "";  // hardware name (default box title)
        public string HardwareType { get; set; } = "";
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";       // LHM SensorType, e.g. Load, Clock, Temperature
        public string Unit { get; set; } = "";
        public double Min { get; set; } = 0;
        public double Max { get; set; } = 100;
        public int Decimals { get; set; } = 0;
        public bool GraphBeneficial { get; set; } = false;
        public string GraphGroup { get; set; } = "";     // key for combined graphs, e.g. "Cpu|Load"
    }

    /// <summary>One box = one hardware component instance (or a custom Processes/Power/App box).</summary>
    public sealed class BoxLayout
    {
        public string ComponentKey { get; set; } = "";
        public ComponentType ComponentType { get; set; }
        public string Title { get; set; } = "";          // editable, defaults to component name
        public double X { get; set; } = 8;
        public double Y { get; set; } = 8;
        public double Width { get; set; } = 300;
        public double Height { get; set; } = 200;
        public bool Visible { get; set; } = true;
        public List<string> SensorIds { get; set; } = [];
        public List<string> GraphSensorIds { get; set; } = [];
        public string GraphScaleMode { get; set; } = "Auto"; // Auto (history+descriptor) | Fixed (descriptor Min/Max only)
        public bool GraphShowLegend { get; set; } = true;
        public double GraphLineThickness { get; set; } = 1.4;
        public Dictionary<string, int> GraphSeriesColors { get; set; } = new(StringComparer.Ordinal); // sensorId -> palette index 0..5
        public Dictionary<string, int> SensorModes { get; set; } = new(StringComparer.Ordinal);
        public bool CombineGraphs { get; set; } = true;
        public bool Locked { get; set; } = false;       // per-box lock (in addition to global BoxesLocked)
        public int GraphZoom { get; set; } = 48;        // history samples shown (48–300, Ctrl+wheel zooms)
        public string NetworkSpeedUnit { get; set; } = "Auto"; // Auto | B/s | KB/s | MB/s | GB/s (throughput sensors)
        // Custom boxes (not enabled by default; created only via "+ Processes/Power/App Box").
        public string CustomKind { get; set; } = "";    // "", "processes", "power", "app"
        public int ProcessLimit { get; set; } = 10;
        public string ProcessSort { get; set; } = "Cpu"; // Cpu | Ram | Name | Threads
        public string AppId { get; set; } = "cmd";      // legacy single app
        public string AppArgs { get; set; } = "";
        public List<string> AppBindings { get; set; } = []; // "appId|args" per entry; if empty falls back to AppId/AppArgs
    }

    /// <summary>Window layout + global locks + independent scaling.</summary>
    public sealed class WindowLayout
    {
        public double Left { get; set; } = 100;
        public double Top { get; set; } = 100;
        public double Width { get; set; } = 340;
        public double Height { get; set; } = 640;
        public bool WindowLocked { get; set; } = false;  // prevents window move + resize
        public bool BoxesLocked { get; set; } = false;   // prevents box move + resize
        public bool KeepOnTop { get; set; } = true;
        public int FontScale { get; set; } = 2;          // 0..4
        public int BarScale { get; set; } = 2;           // 0..4
        public int GraphScale { get; set; } = 2;         // 0..4
        public string ThemeMode { get; set; } = "System"; // System | Light | Dark
        public string ThemeScheme { get; set; } = "Midnight"; // Midnight | Aurora | Ember | Paper
        public string SearchFilter { get; set; } = "";
        public double TempAlertThreshold { get; set; } = 85;
        public bool ColorBlindSafe { get; set; } = false;
        public bool MinimizeToTray { get; set; } = false;
        public bool StartWithWindows { get; set; } = false;
        public bool StartAsAdministrator { get; set; } = false;
        public int GraphMigrationVersion { get; set; } = 0; // 2 = main/header graphs removed, only per-sensor user graphs
        public List<BoxLayout> Boxes { get; set; } = [];
    }

    /// <summary>Pure helpers: scaling, graph heuristic, unit/range defaults.</summary>
    public static class ScaleHelper
    {
        public static double[] FontSizes { get; } = [9, 10, 11, 13, 15];
        public static double[] BarHeights { get; } = [4, 6, 8, 10, 12];
        public static double[] GraphHeights { get; } = [28, 40, 56, 72, 96];
        public static string[] ScaleNames { get; } = ["XS", "S", "M", "L", "XL"];

        public static double FontSize(int idx) => FontSizes[Math.Clamp(idx, 0, 4)];
        public static double BarHeight(int idx) => BarHeights[Math.Clamp(idx, 0, 4)];
        public static double GraphHeight(int idx) => GraphHeights[Math.Clamp(idx, 0, 4)];

        /// <summary>Smart graph decision: is history useful for this category?</summary>
        public static bool GraphBeneficial(string category) => category.ToLowerInvariant() switch
        {
            "load" => true,
            "clock" => true,
            "temperature" => true,
            "power" => true,
            "frequency" => true,
            "throughput" => true,
            "fan" => true,
            "voltage" => true,
            "current" => true,
            "flow" => true,
            "control" => true,
            _ => false, // Data, Factor, SmallData, Level, TimeSpan, Energy, Noise, Conductivity, Humidity
        };

        /// <summary>Bar is only meaningful for bounded categories; Data/Throughput/Factor are unbounded.</summary>
        public static bool ShouldShowBar(string category) => category.ToLowerInvariant() switch
        {
            "load" or "temperature" or "power" or "clock" or "voltage" or "current" or "fan" or "flow" or "control" or "level" => true,
            _ => false,
        };

        public static int DefaultMode(SensorDescriptor d, bool isGraphed)
        {
            int m = (int)RowMode.Text;
            if (isGraphed) m |= (int)RowMode.Graph;
            else if (ShouldShowBar(d.Category)) m |= (int)RowMode.Bar;
            return m;
        }

        public static bool ModeHasText(int mode) => (mode & (int)RowMode.Text) != 0;
        public static bool ModeHasBar(int mode) => (mode & (int)RowMode.Bar) != 0;
        public static bool ModeHasGraph(int mode) => (mode & (int)RowMode.Graph) != 0;

        /// <summary>Logical combined-graph groups: only same box + same category + same unit combine.</summary>
        public static string GraphGroup(string componentKey, string category, string unit) => $"{componentKey}|{category}|{unit}";

        public static (string unit, double min, double max, int dec) DefaultsFor(string category)
        {
            return category.ToLowerInvariant() switch
            {
                "load" => ("%", 0, 100, 0),
                "temperature" => ("°C", 0, 100, 0),
                "power" => ("W", 0, 200, 0),
                "clock" => ("MHz", 0, 6000, 0),
                "voltage" => ("V", 0, 5, 2),
                "current" => ("A", 0, 50, 1),
                "fan" => ("RPM", 0, 3000, 0),
                "flow" => ("L/h", 0, 10, 1),
                "control" => ("%", 0, 100, 0),
                "level" => ("%", 0, 100, 0),
                "frequency" => ("Hz", 0, 100, 0),
                "throughput" => ("B/s", 0, 100_000_000, 0),
                "data" => ("", 0, 100, 0),
                "smalldata" => ("", 0, 100, 0),
                "factor" => ("×", 0, 10, 2),
                _ => ("", 0, 100, 0),
            };
        }

        public static double Normalize(double? value, double min, double max)
        {
            if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) return 0;
            if (max <= min) return 0;
            return (Math.Clamp(value.Value, min, max) - min) / (max - min);
        }

        public static string Format(double? value, SensorDescriptor d, string networkSpeedUnit = "Auto")
        {
            if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) return "—";
            // Throughput (network) unit override: Auto picks B/KB/MB/GB by magnitude.
            if (d.Category.Equals("Throughput", StringComparison.OrdinalIgnoreCase))
            {
                double b = value.Value;
                string pref = (networkSpeedUnit ?? "Auto").Trim();
                if (pref is "B/s" or "KB/s" or "MB/s" or "GB/s")
                {
                    double div = pref switch { "KB/s" => 1000, "MB/s" => 1000000, "GB/s" => 1000000000, _ => 1 };
                    double v = b / div;
                    return $"{v:0.0} {pref}";
                }
                if (b >= 1_000_000_000) return $"{b / 1e9:0.00} GB/s";
                if (b >= 1_000_000) return $"{b / 1e6:0.00} MB/s";
                if (b >= 1_000) return $"{b / 1e3:0.0} KB/s";
                return $"{b:0} B/s";
            }
            string fmt = d.Decimals > 0 ? $"F{d.Decimals}" : "F0";
            return $"{value.Value.ToString(fmt)} {d.Unit}".Trim();
        }

        /// <summary>Shorten a hardware name intelligently for box titles (generic for any PC).</summary>
        public static string ShortTitle(ComponentType type, string fullName, IReadOnlyList<SensorDescriptor>? sensorsForComponent = null)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return fullName;
            string s = fullName.Trim();
            // Memory: RAM + DDR speed (from LHM clock/data sensor if present, else WMI).
            if (type == ComponentType.Memory)
            {
                string ddr = TryFindDdrSpeed(sensorsForComponent);
                return string.IsNullOrWhiteSpace(ddr) ? "RAM" : $"RAM {ddr}";
            }
            foreach (string v in new[] { "Advanced Micro Devices", "AMD", "Intel", "NVIDIA", "Qualcomm" })
            {
                if (s.StartsWith(v + " ", StringComparison.OrdinalIgnoreCase)) s = s[(v.Length + 1)..].TrimStart();
            }
            int withIdx = s.IndexOf(" with ", StringComparison.OrdinalIgnoreCase);
            if (withIdx >= 0) s = s[..withIdx].Trim();
            string[] drop = { "Radeon", "GeForce", "Graphics", "Processor" };
            var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !drop.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
            string s2 = string.Join(" ", parts);
            if (string.IsNullOrWhiteSpace(s2)) s2 = fullName;
            if (type == ComponentType.StorageHdd || type == ComponentType.StorageSsd || type == ComponentType.Storage)
            {
                // Storage: model name as-is (LHM name is already the model).
                return s2.Trim();
            }
            return s2.Trim();
        }

        private static string TryFindDdrSpeed(IReadOnlyList<SensorDescriptor>? sensorsForComponent)
        {
            if (sensorsForComponent is not null)
            {
                foreach (var d in sensorsForComponent)
                {
                    string n = (d.Name ?? "").ToLowerInvariant() + " " + d.Category.ToLowerInvariant();
                    if (n.Contains("ddr") || n.Contains("memory clock") || n.Contains("ram clock"))
                    {
                        // LHM rarely exposes this; fall through to WMI below.
                    }
                }
            }
            try
            {
                var scope = new System.Management.ManagementScope(@"\\.\root\CIMV2");
                scope.Connect();
                var q = new System.Management.ObjectQuery("SELECT Speed FROM Win32_PhysicalMemory");
                var searcher = new System.Management.ManagementObjectSearcher(scope, q);
                var col = searcher.Get();
                foreach (System.Management.ManagementObject mo in col)
                {
                    try
                    {
                        var spd = mo["Speed"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(spd)) return $"DDR {spd}";
                    }
                    catch { }
                }
            }
            catch { }
            return "";
        }
    }

    /// <summary>Ring buffer for one sensor's history (zoomable 48–300).</summary>
    public sealed class SensorHistory
    {
        private readonly Queue<double> _v = new();
        public int Capacity { get; set; } = 48;
        public IReadOnlyCollection<double> Values => _v;
        public void Push(double? v)
        {
            if (v is null || double.IsNaN(v.Value) || double.IsInfinity(v.Value)) return;
            _v.Enqueue(v.Value);
            while (_v.Count > Capacity) _v.Dequeue();
        }
    }
}
