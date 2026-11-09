using System;
using System.Collections.Generic;

namespace SystemInfo
{
    /// <summary>
    /// DDR speed detection: Registry-based, snap to nearest standard frequency from a fixed set.
    /// </summary>
    internal static class DdrSpeedHelper
    {
        private static readonly int[] StandardSpeeds = [1866, 2133, 2400, 2666, 3000, 3200, 3600, 4000, 4266, 4400, 4800, 5200, 5600, 6000, 6400, 7200];

        public static string? TryGetDdrTitlePart()
        {
            int? mhz = TryGetDdrSpeedMhz();
            return mhz is null ? null : $" {SnapToNearestStandard(mhz.Value)}Mhz";
        }

        public static int? TryGetDdrSpeedMhz()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0\Set", writable: false)
                              ?? Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", writable: false);
                // SystemCentral is stored per-CPU; Speed.MHz is not directly present. Fall back to SystemInformation.
                // WMI is the canonical source: query via Win32 unmanaged API through System.Management.
                // If System.Management is unavailable (test harness), fall through to null.
                return QueryDdrSpeedMhz();
            }
            catch { return null; }
        }

        public static int SnapToNearestStandard(int mhz)
        {
            if (StandardSpeeds.Length == 0) return mhz;
            int best = StandardSpeeds[0];
            int bestDelta = Math.Abs(mhz - best);
            for (int i = 1; i < StandardSpeeds.Length; i++)
            {
                int d = Math.Abs(mhz - StandardSpeeds[i]);
                if (d < bestDelta) { bestDelta = d; best = StandardSpeeds[i]; }
            }
            return best;
        }

        private static int? QueryDdrSpeedMhz()
        {
            try
            {
                var scope = new System.Management.ManagementScope(@"\\.\root\cimv2");
                scope.Connect();
                var q = new System.Management.ObjectQuery("SELECT Speed FROM Win32_PhysicalMemory");
                var searcher = new System.Management.ManagementObjectSearcher(scope, q);
                int? result = null;
                foreach (System.Management.ManagementObject mo in searcher.Get())
                {
                    try
                    {
                        var spdObj = mo["Speed"];
                        if (spdObj is not null && uint.TryParse(spdObj.ToString(), out var spd))
                        {
                            result = SnapToNearestStandard((int)spd);
                            break;
                        }
                    }
                    catch { }
                }
                return result;
            }
            catch { return null; }
        }
    }
}
