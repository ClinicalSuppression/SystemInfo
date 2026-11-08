// Driver-free (no Ring0) CPU frequency fallback via PDH Performance Counters + WMI.
// Used when LibreHardwareMonitor needs Ring0 for effective clocks / temps.
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SystemInfo;

public sealed class NativeCpuFallback : IDisposable
{
    private IntPtr _query = IntPtr.Zero;
    private readonly Dictionary<string, IntPtr> _counters = new(StringComparer.Ordinal);
    private double _maxMHz = 4600;
    private bool _ready;
    private readonly List<(string id, SensorDescriptor d)> _extra = new();

    public IReadOnlyList<(string id, SensorDescriptor d)> Extra => _extra;

    public bool TryOpen(string componentKey, string componentName)
    {
        try
        {
            // Max clock via WMI (no admin) for Effective = Performance% * MaxMHz / 100
            try
            {
                var scope = new System.Management.ManagementScope(@"\\.\root\CIMV2");
                scope.Connect();
                var q = new System.Management.ObjectQuery("SELECT MaxClockSpeed FROM Win32_Processor");
                var searcher = new System.Management.ManagementObjectSearcher(scope, q);
                var col = searcher.Get();
                foreach (System.Management.ManagementObject mo in col)
                {
                    var v = mo["MaxClockSpeed"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(v) && double.TryParse(v, out double mhz) && mhz > 0) { _maxMHz = mhz; break; }
                }
            }
            catch { }
            if (PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out _query) != 0) return false;
            // Counter paths are per-processor instance; _Total aggregate + up to 32 cores.
            AddCounter("\\Processor Information(_Total)\\% Processor Performance", $"lhm:{componentKey}:Clock:PDH Effective (Total):0");
            AddCounter("\\Processor Information(_Total)\\% Processor Utility", $"lhm:{componentKey}:Load:PDH Effective (Total):0");
            for (int i = 0; i < 32; i++)
            {
                AddCounter($"\\Processor Information({i})\\% Processor Performance", $"lhm:{componentKey}:Clock:PDH Effective #{i + 1}:0");
            }
            uint st = PdhCollectQueryData(_query);
            if (st != 0 && st != 0x800007D5u) return false;
            _ready = true;
            // Register synthetic descriptors (names shown come from this native source, LHM names kept for LHM sensors).
            if (componentKey is not null)
            {
                _extra.Add(( $"lhm:{componentKey}:Clock:PDH Effective (Total):0", new SensorDescriptor { Id = $"lhm:{componentKey}:Clock:PDH Effective (Total):0", ComponentKey = componentKey, ComponentType = ComponentType.Cpu, ComponentName = componentName, HardwareType = "Cpu", Name = "Effective (Total)", Category = "Clock", Unit = "MHz", Min = 0, Max = _maxMHz, Decimals = 0, GraphBeneficial = true, GraphGroup = ScaleHelper.GraphGroup(componentKey, "Clock", "MHz") }));
            }
            return true;
        }
        catch { return false; }
    }

    private void AddCounter(string path, string id)
    {
        if (PdhAddEnglishCounter(_query, path, IntPtr.Zero, out IntPtr c) == 0) _counters[id] = c;
    }

    public Dictionary<string, double?> Poll()
    {
        var vals = new Dictionary<string, double?>(StringComparer.Ordinal);
        if (!_ready) return vals;
        try
        {
            PdhCollectQueryData(_query);
            foreach (var (id, counter) in _counters)
            {
                if (PdhGetFormattedCounterValue(counter, out PDH_FMT_COUNTERVALUE fmt) == 0 && fmt.CStatus == 0)
                {
                    double v = fmt.doubleValue;
                    if (id.Contains("Clock"))
                    {
                        // % performance (100 = max) -> Effective MHz
                        vals[id] = Math.Clamp(v * _maxMHz / 100.0, 0, _maxMHz);
                    }
                    else
                    {
                        vals[id] = Math.Clamp(v, 0, 100);
                    }
                }
            }
        }
        catch { }
        return vals;
    }

    public void Dispose()
    {
        try { if (_query != IntPtr.Zero) PdhCloseQuery(_query); } catch { }
        _query = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE { public int CStatus; public int dwType; public long FileTime; public long FirstValue; public long SecondValue; public double doubleValue; }

    [DllImport("pdh.dll")]
    private static extern uint PdhOpenQuery(IntPtr reserved, IntPtr refresh, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, out PDH_FMT_COUNTERVALUE value);
    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
