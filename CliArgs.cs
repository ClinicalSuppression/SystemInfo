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
        public bool HasExportAction => JsonPath is not null || ExportPath is not null;
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
            "  --help                       Show this help\n" +
            "\nWithout a path, the report is saved to Documents\\SystemInfo-Report.json / .txt.\n";

        public static string DefaultJsonPath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SystemInfo-Report.json");

        public static string DefaultTextPath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SystemInfo-Report.txt");

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
