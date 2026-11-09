namespace SystemInfo
{
    /// <summary>One key/value row inside a report section.</summary>
    public sealed class ReportRow
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }

    /// <summary>A named group of rows (e.g. "Network").</summary>
    public sealed class ReportSection
    {
        public string Name { get; set; } = "";
        public List<ReportRow> Rows { get; set; } = [];
    }

    /// <summary>The complete collected report: identity + sections.</summary>
    public sealed class SystemReport
    {
        public DateTimeOffset GeneratedAt { get; set; }
        public string ComputerName { get; set; } = "";
        public string UserName { get; set; } = "";
        public List<ReportSection> Sections { get; set; } = [];
    }

    /// <summary>Pure helpers for rendering a <see cref="SystemReport"/> (no I/O, fully testable).</summary>
    public static class ReportFormatter
    {
        /// <summary>"1024" -&gt; "1.0 KB", "16 GiB" in GB with one decimal. Negative clamps to 0.</summary>
        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) bytes = 0;
            if (bytes < 1024) return $"{bytes} B";
            string[] units = ["KB", "MB", "GB", "TB", "PB"];
            double value = bytes;
            int i = -1;
            while (value >= 1024 && i < units.Length - 1)
            {
                value /= 1024;
                i++;
            }
            return $"{value:0.0} {units[i]}";
        }

        /// <summary>"3d 4h 12m", "2h 5m", "45m 10s", or "30s" for short uptimes.</summary>
        public static string FormatUptime(TimeSpan uptime)
        {
            if (uptime < TimeSpan.Zero) uptime = TimeSpan.Zero;
            if (uptime.Days > 0) return $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m";
            if (uptime.Hours > 0) return $"{uptime.Hours}h {uptime.Minutes}m";
            if (uptime.Minutes > 0) return $"{uptime.Minutes}m {uptime.Seconds}s";
            return $"{uptime.Seconds}s";
        }

        public static string FormatPercent(double percent)
            => $"{Math.Clamp(percent, 0, 100):0.0}%";

        /// <summary>Plain-text report suitable for clipboard / .txt export.</summary>
        public static string ToText(SystemReport report)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("SystemInfo Report");
            sb.AppendLine($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss zzz}");
            sb.AppendLine($"Computer: {report.ComputerName}");
            sb.AppendLine($"User: {report.UserName}");
            foreach (ReportSection section in report.Sections)
            {
                sb.AppendLine();
                sb.AppendLine($"=== {section.Name} ===");
                if (section.Rows.Count == 0)
                {
                    sb.AppendLine("(no data)");
                    continue;
                }
                foreach (ReportRow row in section.Rows)
                {
                    sb.AppendLine($"{row.Key}: {(string.IsNullOrWhiteSpace(row.Value) ? "Unavailable" : row.Value)}");
                }
            }
            return sb.ToString();
        }

        /// <summary>Compact indented JSON for .json export.</summary>
        public static string ToJson(SystemReport report)
            => System.Text.Json.JsonSerializer.Serialize(report,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}
