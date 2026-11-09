using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace SystemInfo
{
    /// <summary>CPU / GPU model shortening database (built-in subset).</summary>
    internal static class CpuGpuModels
    {
        private static readonly Dictionary<string, string> _map;

        static CpuGpuModels()
        {
            _map = Load();
        }

        public static bool TryGetShort(string fullName, out string shortName)
        {
            shortName = fullName;
            if (string.IsNullOrWhiteSpace(fullName)) return false;
            if (_map.TryGetValue(fullName.Trim(), out var mapped) && !string.IsNullOrWhiteSpace(mapped))
            {
                shortName = mapped;
                return true;
            }
            return false;
        }

        private static Dictionary<string, string> Load()
        {
            var asm = Assembly.GetExecutingAssembly();
            string resourceName = "SystemInfo.cpu_gpu_models.json";
            try
            {
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream is null) return new(StringComparer.OrdinalIgnoreCase);
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("cpus", out var cpus))
                    foreach (var p in cpus.EnumerateObject()) { var v = p.Value.GetString(); if (v is not null) map[p.Name] = v; }
                if (root.TryGetProperty("gpus", out var gpus))
                    foreach (var p in gpus.EnumerateObject()) { var v = p.Value.GetString(); if (v is not null) map[p.Name] = v; }
                return map;
            }
            catch { return new(StringComparer.OrdinalIgnoreCase); }
        }
    }
}
