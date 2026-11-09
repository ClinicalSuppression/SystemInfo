using LibreHardwareMonitor.Hardware;

using LhmComputer = LibreHardwareMonitor.Hardware.Computer;
using LhmIHardware = LibreHardwareMonitor.Hardware.IHardware;
using LhmISensor = LibreHardwareMonitor.Hardware.ISensor;
using LhmIVisitor = LibreHardwareMonitor.Hardware.IVisitor;

namespace SystemInfo;

public sealed class GadgetData
{
    public DateTime Time { get; set; }
    public TimeSpan Uptime { get; set; }
    public Dictionary<string, double?> Values { get; } = new(StringComparer.Ordinal);
}

public sealed class UnifiedSensorCollector : IDisposable
{
    private LhmComputer? _computer;
    private bool _tried;
    private readonly Dictionary<string, SensorDescriptor> _catalog = new(StringComparer.Ordinal);
    private readonly List<LhmISensor> _sensors = new();
    private readonly Dictionary<string, LhmISensor> _sensorsById = new(StringComparer.Ordinal);
    private readonly List<(LhmIHardware hw, ComponentType type, string componentKey)> _hardwares = new();
    private static readonly object Lock = new();

    public IReadOnlyDictionary<string, SensorDescriptor> Descriptors => _catalog;
    public bool LhmAvailable { get; private set; }
    public string? LhmError { get; private set; }
    public IReadOnlyList<(string key, ComponentType type, string name)> Components => _hardwares.Select(h => (h.componentKey, h.type, h.hw.Name)).ToList();
    public string GetStatusSummary(IReadOnlyDictionary<string, double?>? values = null)
    {
        int hw = _hardwares.Count;
        int sensors = _catalog.Count;
        int live = 0, unavailable = 0;
        if (values is not null)
        {
            foreach (var d in _catalog.Values) { if (values.TryGetValue(d.Id, out var v) && v.HasValue) live++; else unavailable++; }
        }
        else { live = sensors; unavailable = 0; }
        string lib = "LibreHardwareMonitorLib 0.9.6";
        if (!LhmAvailable) return $"Source: {lib} — NOT available{(string.IsNullOrWhiteSpace(LhmError) ? "" : $": {LhmError}")}";
        string tail = "Ring0 (MSR temp/power/fan) needs PawnIO + administrator; user-mode sensors work without elevation.";
        return values is null
            ? $"Source: {lib} — {hw} hardware, {sensors} sensors. {tail}"
            : $"Source: {lib} — {hw} hardware, {sensors} sensors (live now: {live}, unavailable/NaN: {unavailable}). {tail}";
    }
    public string GetHardwareBreakdown()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var g in _catalog.Values.GroupBy(d => d.ComponentKey).OrderBy(g => g.Key))
        {
            var first = g.First();
            sb.AppendLine($"{first.ComponentName} ({first.ComponentType}) — {g.Count()} sensors");
        }
        if (_catalog.Count == 0) sb.AppendLine("No hardware found. LHM returned 0 items" + (string.IsNullOrWhiteSpace(LhmError) ? "." : $": {LhmError}"));
        return sb.ToString();
    }

    public void Open(bool enableLhm = true)
    {
        lock (Lock)
        {
            if (_tried) return;
            _tried = true;
            if (!enableLhm) return;
            try
            {
                _computer = new LhmComputer
                {
                    IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true,
                    IsMotherboardEnabled = true, IsControllerEnabled = true,
                    IsNetworkEnabled = true, IsStorageEnabled = true, IsPsuEnabled = true, IsBatteryEnabled = true,
                    IsPowerMonitorEnabled = true,
                };
                _computer.Open();
                _computer.Accept(new UpdateVisitor());
                foreach (var hw in _computer.Hardware) CollectHardware(hw);
                LhmAvailable = _computer.Hardware.Count > 0;
                // Driver-free fallback disabled: native PDH struct caused coreclr fast-fail; keep LHM-only until proven safe.
            }
            catch (Exception ex)
            {
                LhmAvailable = false; LhmError = ex.Message;
                try { _computer?.Close(); } catch { }
                _computer = null;
            }
        }
    }

    private void CollectHardware(LhmIHardware hw)
    {
        ComponentType type = MapKind(hw);
        string componentKey = $"{hw.HardwareType}:{hw.Name}";
        _hardwares.Add((hw, type, componentKey));
        foreach (var s in hw.Sensors) Register(hw, s, type, componentKey);
        foreach (var sub in hw.SubHardware) CollectHardware(sub);
    }

    private void Register(LhmIHardware hw, LhmISensor s, ComponentType type, string componentKey)
    {
        string cat = s.SensorType.ToString();
        var (unit, min, max, dec) = ScaleHelper.DefaultsFor(cat);
        string id = $"lhm:{componentKey}:{cat}:{s.Name}:{s.Index}";
        _catalog[id] = new SensorDescriptor
        {
            Id = id, ComponentKey = componentKey, ComponentType = type, ComponentName = hw.Name,
            HardwareType = hw.HardwareType.ToString(), Name = s.Name, Category = cat,
            Unit = unit, Min = min, Max = max, Decimals = dec,
            GraphBeneficial = ScaleHelper.GraphBeneficial(cat),
            GraphGroup = ScaleHelper.GraphGroup(componentKey, cat, unit),
        };
        _sensors.Add(s);
        _sensorsById[id] = s;
    }

    public Dictionary<string, double?> Collect()
    {
        var values = new Dictionary<string, double?>(StringComparer.Ordinal);
        lock (Lock)
        {
            if (_computer is not null)
            {
                try { _computer.Accept(new UpdateVisitor()); } catch { }
                foreach (var s in _sensors)
                {
                    string id = $"lhm:{s.Hardware.HardwareType}:{s.Hardware.Name}:{s.SensorType}:{s.Name}:{s.Index}";
                    double? v = null;
                    try
                    {
                        if (s.Value.HasValue)
                        {
                            double d = (double)s.Value.Value;
                            if (!double.IsNaN(d) && !double.IsInfinity(d)) v = d;
                        }
                    }
                    catch { }
                    values[id] = v;
                }
            }
            PatchLiveBounds(values);
        }
        return values;
    }

    /// <summary>
    /// Refresh derived bar/graph bounds from live telemetry so bars show real proportions
    /// instead of saturating against static default maxima.
    /// GPU memory: total capacity is derived from the complementary Used/Free pair (or, when present, the Total sensor).
    /// Power / clock / fan / current: max bound is raised to the greatest value LHM has ever recorded for that sensor.
    /// </summary>
    private void PatchLiveBounds(Dictionary<string, double?> live)
    {
        foreach (var group in _catalog.Values
            .Where(d => d.Name.StartsWith("GPU Memory", StringComparison.OrdinalIgnoreCase)
                     && d.Category.Equals("SmallData", StringComparison.OrdinalIgnoreCase))
            .GroupBy(d => d.ComponentKey))
        {
            SensorDescriptor? used = null, free = null, total = null;
            foreach (var d in group)
            {
                if (d.Name.Equals("GPU Memory Used", StringComparison.OrdinalIgnoreCase)) used = d;
                else if (d.Name.Equals("GPU Memory Free", StringComparison.OrdinalIgnoreCase)) free = d;
                else if (d.Name.Equals("GPU Memory Total", StringComparison.OrdinalIgnoreCase)) total = d;
            }
            double? cap = null;
            if (total is not null && live.TryGetValue(total.Id, out double? tv) && tv.HasValue)
            {
                cap = tv;
            }
            else if (used is not null && free is not null)
            {
                bool okU = live.TryGetValue(used.Id, out double? uv) && uv.HasValue;
                bool okF = live.TryGetValue(free.Id, out double? fv) && fv.HasValue;
                if (okU && okF && uv!.Value + fv!.Value > 0) cap = uv!.Value + fv!.Value;
            }
            if (cap.HasValue)
            {
                if (used is not null) { used.Min = 0; used.Max = Math.Max(used.Max, cap.Value); }
                if (free is not null) { free.Min = 0; free.Max = Math.Max(free.Max, cap.Value); }
                if (total is not null) { total.Min = 0; total.Max = Math.Max(total.Max, cap.Value); }
            }
        }
        foreach (var d in _catalog.Values)
        {
            if (!d.Category.Equals("Power", StringComparison.OrdinalIgnoreCase)
             && !d.Category.Equals("Clock", StringComparison.OrdinalIgnoreCase)
             && !d.Category.Equals("Fan", StringComparison.OrdinalIgnoreCase)
             && !d.Category.Equals("Current", StringComparison.OrdinalIgnoreCase)) continue;
            if (!_sensorsById.TryGetValue(d.Id, out var s)) continue;
            float? rec;
            try { rec = s.Max; } catch { continue; }
            if (rec.HasValue && rec.Value > d.Max) d.Max = rec.Value;
        }
    }

    private static ComponentType MapKind(LhmIHardware hw) => hw.HardwareType.ToString() switch
    {
        "Cpu" => ComponentType.Cpu,
        "GpuNvidia" or "GpuAmd" or "GpuIntel" => ComponentType.Gpu,
        "Memory" => ComponentType.Memory,
        "Storage" => MapStorageKind(hw),
        "Network" => ComponentType.Network,
        "Battery" => ComponentType.Battery,
        "Motherboard" or "SuperIO" or "EmbeddedController" => ComponentType.Motherboard,
        "Psu" => ComponentType.Psu,
        _ => ComponentType.Controller,
    };

    private static ComponentType MapStorageKind(LhmIHardware hw)
    {
        string n = hw.Name ?? "";
        string lower = n.ToLowerInvariant();
        // SSD strong signals
        if (lower.Contains("nvme") || lower.Contains("ssd") || lower.Contains("sn9") || lower.Contains("sn8") || lower.Contains("sn7") ||
            lower.Contains("970") || lower.Contains("990") || lower.Contains("980") || lower.Contains("960") || lower.Contains("kingston") ||
            lower.Contains("samsung 9") || lower.Contains("crucial mx") || lower.Contains("crucial p") || lower.Contains("sk hynix") ||
            lower.Contains("sandisk") || lower.Contains("firecuda") || lower.Contains("sn550") || lower.Contains("sn850") || lower.Contains("sn770"))
            return ComponentType.StorageSsd;
        // HDD signals: Seagate STxxxx, WDxxxx serials, WD10/WD20/WD40, Barracuda/IronWolf, Toshiba
        if (lower.StartsWith("st") && System.Text.RegularExpressions.Regex.IsMatch(lower, @"st\d{3,}")) return ComponentType.StorageHdd;
        if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"wd\d{2}[a-z]{3,}")) return ComponentType.StorageHdd;
        if (lower.Contains("barracuda") || lower.Contains("ironwolf") || lower.Contains("hdd")) return ComponentType.StorageHdd;
        if (lower.Contains("wd10") || lower.Contains("wd20") || lower.Contains("wd40") || lower.Contains("wd30")) return ComponentType.StorageHdd;
        // Sensor-name fallback: SSD health controllers often expose "SSD Health"/"Percentage Used" style names.
        foreach (var s in hw.Sensors)
        {
            string sn = (s.Name ?? "").ToLowerInvariant();
            if (sn.Contains("ssd") || sn.Contains("percentage used") || sn.Contains("wear") || sn.Contains("nvme")) return ComponentType.StorageSsd;
        }
        return ComponentType.Storage;
    }

    public void Dispose()
    {
        lock (Lock)
        {
            try { _computer?.Close(); } catch { }
            _computer = null;
        }
    }

    private sealed class UpdateVisitor : LhmIVisitor
    {
        public void VisitComputer(LibreHardwareMonitor.Hardware.IComputer c) => c.Traverse(this);
        public void VisitHardware(LhmIHardware h) { h.Update(); foreach (var s in h.SubHardware) s.Accept(this); }
        public void VisitSensor(LhmISensor s) { }
        public void VisitParameter(LibreHardwareMonitor.Hardware.IParameter p) { }
    }
}
