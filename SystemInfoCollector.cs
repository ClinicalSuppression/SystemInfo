using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SystemInfo
{
    /// <summary>
    /// Collects a <see cref="SystemReport"/> using only built-in .NET / Win32 APIs
    /// (no System.Management package). Every section is guarded: a failure becomes
    /// an "Unavailable" row instead of crashing the dashboard.
    /// </summary>
    public static class SystemInfoCollector
    {
        public static SystemReport Collect()
        {
            var report = new SystemReport
            {
                GeneratedAt = DateTimeOffset.Now,
                ComputerName = Environment.MachineName,
                UserName = Environment.UserName,
            };

            report.Sections.Add(Safe(BuildOverview, "Overview"));
            report.Sections.Add(Safe(BuildOsCpuMemory, "OS, CPU & Memory"));
            report.Sections.Add(Safe(BuildGpuDisplays, "GPU & Displays"));
            report.Sections.Add(Safe(BuildStorage, "Storage"));
            report.Sections.Add(Safe(BuildNetwork, "Network"));
            report.Sections.Add(Safe(BuildBattery, "Battery"));
            return report;
        }

        private static ReportSection Safe(Func<ReportSection> build, string name)
        {
            try { return build(); }
            catch (Exception ex) { return new ReportSection { Name = name, Rows = [Row("Status", $"Unavailable: {ex.Message}")] }; }
        }

        private static ReportRow Row(string key, string value) => new() { Key = key, Value = value };

        // ----- Overview -----

        private static ReportSection BuildOverview()
        {
            var section = new ReportSection { Name = "Overview" };
            section.Rows.Add(Row("Computer", Environment.MachineName));
            section.Rows.Add(Row("User", Environment.UserName));
            section.Rows.Add(Row("Domain", Environment.UserDomainName));
            section.Rows.Add(Row(".NET runtime", Environment.Version.ToString()));
            section.Rows.Add(Row("64-bit OS", Environment.Is64BitOperatingSystem ? "Yes (x64)" : "No"));
            section.Rows.Add(Row("64-bit process", Environment.Is64BitProcess ? "Yes (x64)" : "No (x86)"));
            section.Rows.Add(Row("Processor count", Environment.ProcessorCount.ToString()));
            section.Rows.Add(Row("System uptime", ReportFormatter.FormatUptime(TimeSpan.FromMilliseconds(Environment.TickCount64))));
            section.Rows.Add(Row("Last boot", (DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64)).ToString("yyyy-MM-dd HH:mm:ss")));
            return section;
        }

        // ----- OS, CPU & Memory -----

        private static ReportSection BuildOsCpuMemory()
        {
            var section = new ReportSection { Name = "OS, CPU & Memory" };

            string product = RegText(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName");
            string displayVersion = RegText(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");
            string build = RegText(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber");
            string ubr = RegText(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR");
            string os = string.IsNullOrWhiteSpace(product)
                ? Environment.OSVersion.VersionString
                : $"{product} {displayVersion} (build {build}.{ubr})".Replace("  (build .)", "").Replace("  (build .)", "");
            section.Rows.Add(Row("Operating system", os));
            section.Rows.Add(Row("Windows build", string.IsNullOrWhiteSpace(build) ? "" : $"{build}.{ubr}"));

            string cpu = RegText(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString");
            section.Rows.Add(Row("CPU", string.IsNullOrWhiteSpace(cpu) ? Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "" : cpu.Trim()));
            section.Rows.Add(Row("Logical processors", Environment.ProcessorCount.ToString()));
            section.Rows.Add(Row("CPU usage", TryCpuUsage()));

            MEMORYSTATUSEX mem = new();
            if (GlobalMemoryStatusExInit(ref mem))
            {
                long total = (long)mem.UllTotalPhys;
                long available = (long)mem.UllAvailPhys;
                long used = total - available;
                double pct = total == 0 ? 0 : (double)used / total * 100;
                section.Rows.Add(Row("Total RAM", ReportFormatter.FormatBytes(total)));
                section.Rows.Add(Row("Available RAM", ReportFormatter.FormatBytes(available)));
                section.Rows.Add(Row("RAM in use", $"{ReportFormatter.FormatBytes(used)} ({ReportFormatter.FormatPercent(pct)})"));
            }
            else
            {
                section.Rows.Add(Row("Total RAM", ""));
                section.Rows.Add(Row("Available RAM", ""));
            }
            return section;
        }

        private static string TryCpuUsage()
        {
            try
            {
                if (!GetSystemTimes(out FILETIME idle1, out FILETIME kernel1, out FILETIME user1)) return "";
                System.Threading.Thread.Sleep(250);
                if (!GetSystemTimes(out FILETIME idle2, out FILETIME kernel2, out FILETIME user2)) return "";
                long idle = ToTicks(idle2) - ToTicks(idle1);
                long kernel = ToTicks(kernel2) - ToTicks(kernel1);
                long user = ToTicks(user2) - ToTicks(user1);
                long total = Math.Max(1, kernel + user);
                double pct = 100.0 * Math.Max(0, (total - idle)) / total;
                return ReportFormatter.FormatPercent(pct);
            }
            catch { return ""; }
        }

        private static long ToTicks(FILETIME ft) => ((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

        // ----- GPU & Displays -----

        private static ReportSection BuildGpuDisplays()
        {
            var section = new ReportSection { Name = "GPU & Displays" };
            List<string> gpus = [];
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (key is not null)
                    foreach (string sub in key.GetSubKeyNames())
                    {
                        if (!int.TryParse(sub, out _)) continue;
                        using RegistryKey? device = key.OpenSubKey(sub);
                        string? desc = device?.GetValue("DriverDesc") as string;
                        string? provider = device?.GetValue("ProviderName") as string;
                        if (!string.IsNullOrWhiteSpace(desc))
                            gpus.Add(string.IsNullOrWhiteSpace(provider) ? desc.Trim() : $"{desc.Trim()} ({provider.Trim()})");
                    }
            }
            catch { /* fall through to no data */ }

            if (gpus.Count == 0) section.Rows.Add(Row("GPU", ""));
            else for (int i = 0; i < gpus.Count; i++) section.Rows.Add(Row(i == 0 ? "GPU" : $"GPU {i + 1}", gpus[i]));

            var screens = GetDisplays();
            for (int i = 0; i < screens.Count; i++)
            {
                var s = screens[i];
                section.Rows.Add(Row($"Display {i + 1}",
                    $"{s.DeviceName} — {s.Width}×{s.Height}" +
                    (s.IsPrimary ? " (primary)" : "") +
                    (s.WorkWidth != s.Width || s.WorkHeight != s.Height
                        ? $", working area {s.WorkWidth}×{s.WorkHeight}" : "")));
            }
            return section;
        }

        // ----- Storage -----

        private static ReportSection BuildStorage()
        {
            var section = new ReportSection { Name = "Storage" };
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType is DriveType.Unknown or DriveType.NoRootDirectory) continue;
                string name = drive.Name;
                try
                {
                    if (!drive.IsReady) { section.Rows.Add(Row(name, $"{drive.DriveType} — not ready")); continue; }
                    long total = drive.TotalSize;
                    long free = drive.TotalFreeSpace;
                    long used = total - free;
                    double pct = total == 0 ? 0 : (double)used / total * 100;
                    section.Rows.Add(Row(name,
                        $"{drive.DriveType} {drive.VolumeLabel} — {ReportFormatter.FormatBytes(used)} of {ReportFormatter.FormatBytes(total)} used ({ReportFormatter.FormatPercent(pct)}), {ReportFormatter.FormatBytes(free)} free"));
                }
                catch (Exception ex)
                {
                    section.Rows.Add(Row(name, $"{drive.DriveType} — Unavailable: {ex.Message}"));
                }
            }
            return section;
        }

        // ----- Network -----

        private static ReportSection BuildNetwork()
        {
            var section = new ReportSection { Name = "Network" };
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            if (interfaces.Length == 0) section.Rows.Add(Row("Adapters", "(none found)"));

            foreach (NetworkInterface nic in interfaces)
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var props = nic.GetIPProperties();
                var ipv4 = props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString()).ToList();
                var ipv6 = props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    .Select(a => a.Address.ToString()).ToList();
                var dns = TryDnsServers(nic.Id);
                string speed = nic.Speed > 0 ? ReportFormatter.FormatBytes(nic.Speed / 8) + "/s" : "—";
                section.Rows.Add(Row(nic.Name,
                    $"{nic.Description} — {nic.OperationalStatus}, {nic.NetworkInterfaceType}, {speed}, MAC {nic.GetPhysicalAddress()}" +
                    (ipv4.Count > 0 ? $", IPv4 {string.Join(", ", ipv4)}" : "") +
                    (ipv6.Count > 0 ? $", IPv6 {string.Join(", ", ipv6)}" : "") +
                    (dns.Count > 0 ? $", DNS {string.Join(", ", dns)}" : "")));
            }
            return section;
        }

        // ----- Battery -----

        private static ReportSection BuildBattery()
        {
            var section = new ReportSection { Name = "Battery" };
            try
            {
                if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS pwr))
                {
                    string line = pwr.ACLineStatus switch { 0 => "Offline", 1 => "Online", _ => "Unknown" };
                    string chargeStatus = pwr.BatteryFlag switch
                    {
                        1 => "Discharging", 4 => "Charging", 8 => "High", 16 => "Low", 32 => "Critical", 9 => "Discharging + High", 12 => "Charging + High", 128 => "No system battery", _ => "Unknown",
                    };
                    section.Rows.Add(Row("Power line", line));
                    section.Rows.Add(Row("Charge status", chargeStatus));
                    section.Rows.Add(Row("Charge", pwr.BatteryLifePercent is >= 0 and < 255
                        ? ReportFormatter.FormatPercent(pwr.BatteryLifePercent) : ""));
                    section.Rows.Add(Row("Time remaining", pwr.BatteryLifeTime >= 0 && pwr.BatteryLifeTime < int.MaxValue
                        ? $"{pwr.BatteryLifeTime / 60} min" : ""));
                }
                else
                {
                    section.Rows.Add(Row("Power line", ""));
                }
            }
            catch { section.Rows.Add(Row("Power line", "")); }
            return section;
        }

        // ----- helpers -----

        private static string RegText(string subKey, string valueName)
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(subKey);
                return key?.GetValue(valueName)?.ToString()?.Trim() ?? "";
            }
            catch { return ""; }
        }

        private static List<string> TryDnsServers(string interfaceId)
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{interfaceId}");
                string? dns = (key?.GetValue("DhcpNameServer") as string)
                    ?? (key?.GetValue("NameServer") as string);
                if (string.IsNullOrWhiteSpace(dns)) return [];
                return dns.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            }
            catch { return []; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong UllTotalPhys, UllAvailPhys, UllTotalPageFile, UllAvailPageFile,
                         UllTotalVirtual, UllAvailVirtual, UllAvailExtendedVirtual;
            public void Init() => dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME { public uint dwLowDateTime; public uint dwHighDateTime; }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        private readonly struct DisplayInfo
        {
            public DisplayInfo(string deviceName, int width, int height, bool isPrimary, int workWidth, int workHeight)
            { DeviceName = deviceName; Width = width; Height = height; IsPrimary = isPrimary; WorkWidth = workWidth; WorkHeight = workHeight; }
            public string DeviceName { get; }
            public int Width { get; }
            public int Height { get; }
            public bool IsPrimary { get; }
            public int WorkWidth { get; }
            public int WorkHeight { get; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private static List<DisplayInfo> GetDisplays()
        {
            var displays = new List<DisplayInfo>();
            try
            {
                var cb = new MonitorEnumProc((IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) =>
                {
                    var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf(typeof(MONITORINFO)) };
                    if (GetMonitorInfo(hMon, ref info))
                    {
                        const uint MONITORINFOF_PRIMARY = 0x00000001;
                        int w = info.rcMonitor.Right - info.rcMonitor.Left;
                        int h = info.rcMonitor.Bottom - info.rcMonitor.Top;
                        int ww = info.rcWork.Right - info.rcWork.Left;
                        int wh = info.rcWork.Bottom - info.rcWork.Top;
                        displays.Add(new DisplayInfo(string.IsNullOrWhiteSpace(info.szDevice) ? $"\\\\.\\DISPLAY{displays.Count + 1}" : info.szDevice, w, h, (info.dwFlags & MONITORINFOF_PRIMARY) != 0, ww, wh));
                    }
                    return true;
                });
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
            }
            catch { }
            if (displays.Count == 0)
            {
                try { displays.Add(new DisplayInfo("Primary", System.Windows.SystemParameters.VirtualScreenWidth > 0 ? (int)System.Windows.SystemParameters.VirtualScreenWidth : 1920, System.Windows.SystemParameters.VirtualScreenHeight > 0 ? (int)System.Windows.SystemParameters.VirtualScreenHeight : 1080, true, (int)System.Windows.SystemParameters.WorkArea.Width, (int)System.Windows.SystemParameters.WorkArea.Height)); } catch { }
            }
            return displays;
        }

        // Memory struct needs Init() before first use; wrap the call site via a helper.
        private static bool GlobalMemoryStatusExInit(ref MEMORYSTATUSEX mem)
        {
            mem.Init();
            return GlobalMemoryStatusEx(ref mem);
        }
    }
}
