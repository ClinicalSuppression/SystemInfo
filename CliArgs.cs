using System.IO;

namespace SystemInfo
{
    /// <summary>Parsed command-line options. See README for the full list.</summary>
    public sealed class CliOptions
    {
        public bool Help { get; set; }
        public string? JsonPath { get; set; }
        public string? ExportPath { get; set; }
        public bool Minimized { get; set; }
        public string? ProfilePath { get; set; }   // --profile [out]: headless perf sampling
        public bool Stress { get; set; }           // --stress: enable all sensors + graphs for max load
        public bool FitTest { get; set; }          // --fit-test: headless Fit boxes verification (writes perf.log)
        public bool CompactTest { get; set; }      // --compact-test: headless Compact window verification (writes perf.log)
        public bool HasExportAction => JsonPath is not null || ExportPath is not null;
        public bool HasProfileAction => ProfilePath is not null;
        public bool HasTestAction => FitTest || CompactTest;
    }

    /// <summary>Pure, testable command-line parser (no side effects).</summary>
    public static class CliArgs
    {
        public const string HelpText =
            "SystemInfo\n" +
            "Usage: SystemInfo.exe [options]\n" +
            "\n" +
            "  (no options)                 Open the dashboard\n" +
            "  --minimized                  Start hidden / minimized\n" +
            "  --json [report.json]         Save the full report as JSON, then exit\n" +
            "  --export [report.txt]        Save the full report as text, then exit\n" +
            "  --profile [out]              Sample memory/CPU for 60s, write to [out] (default perf.log), then exit\n" +
            "  --stress                     Enable all sensors + graphs (max load); pair with --profile\n" +
            "  --fit-test                   Run Fit boxes headlessly, write result to perf.log, then exit (use with --stress for worst case)\n" +
            "  --compact-test               Run Compact window headlessly, write result to perf.log, then exit\n" +
            "  --help                       Show this help\n" +
            "\nWithout a path, the report is saved to Documents\\SystemInfo-Report.json / .txt.\n";

        public static string DefaultJsonPath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SystemInfo-Report.json");

        public static string DefaultTextPath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SystemInfo-Report.txt");

        public static string DefaultProfilePath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemInfo", "perf.log");

        public static CliOptions Parse(string[] args)
        {
            var options = new CliOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase))
                    options.Help = true;
                else if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
                    options.JsonPath = PeekPath(args, ref i) ?? DefaultJsonPath();
                else if (arg.Equals("--export", StringComparison.OrdinalIgnoreCase))
                    options.ExportPath = PeekPath(args, ref i) ?? DefaultTextPath();
                else if (arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase))
                    options.Minimized = true;
                else if (arg.Equals("--profile", StringComparison.OrdinalIgnoreCase))
                    options.ProfilePath = PeekPath(args, ref i) ?? DefaultProfilePath();
                else if (arg.Equals("--stress", StringComparison.OrdinalIgnoreCase))
                    options.Stress = true;
                else if (arg.Equals("--fit-test", StringComparison.OrdinalIgnoreCase))
                    options.FitTest = true;
                else if (arg.Equals("--compact-test", StringComparison.OrdinalIgnoreCase))
                    options.CompactTest = true;
                // Unknown args are ignored (forward-compatible).
            }
            return options;
        }

        private static string? PeekPath(string[] args, ref int i)
        {
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                i++;
                return args[i];
            }
            return null;
        }
    }
}
