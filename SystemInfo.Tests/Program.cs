// Dependency-free test harness: each Check() records a failure; Main returns non-zero.
// Run with: dotnet run --project SystemInfo.Tests -c Release
using System.IO;
using System.Text.Json;
using SystemInfo;

int passed = 0;
int failed = 0;

void Check(string name, bool condition)
{
    if (condition)
    {
        passed++;
        Console.WriteLine($"  PASS  {name}");
    }
    else
    {
        failed++;
        Console.WriteLine($"  FAIL  {name}");
    }
}

Console.WriteLine("ReportFormatter.FormatBytes");
Check("0 bytes", ReportFormatter.FormatBytes(0) == "0 B");
Check("512 bytes", ReportFormatter.FormatBytes(512) == "512 B");
Check("1024 -> 1.0 KB", ReportFormatter.FormatBytes(1024) == "1.0 KB");
Check("1 MiB -> 1.0 MB", ReportFormatter.FormatBytes(1024 * 1024) == "1.0 MB");
Check("16 GiB -> 16.0 GB", ReportFormatter.FormatBytes(16L * 1024 * 1024 * 1024) == "16.0 GB");
Check("negative clamps to 0 B", ReportFormatter.FormatBytes(-5) == "0 B");

Console.WriteLine("ReportFormatter.FormatUptime");
Check("seconds only", ReportFormatter.FormatUptime(TimeSpan.FromSeconds(30)) == "30s");
Check("minutes + seconds", ReportFormatter.FormatUptime(TimeSpan.FromMinutes(45).Add(TimeSpan.FromSeconds(10))) == "45m 10s");
Check("hours + minutes", ReportFormatter.FormatUptime(new TimeSpan(2, 5, 0)) == "2h 5m");
Check("days + hours + minutes", ReportFormatter.FormatUptime(new TimeSpan(3, 4, 12, 0)) == "3d 4h 12m");
Check("negative clamps to 0s", ReportFormatter.FormatUptime(TimeSpan.FromSeconds(-1)) == "0s");

Console.WriteLine("ReportFormatter.ToText / ToJson");
var report = new SystemReport
{
    GeneratedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
    ComputerName = "DESKTOP-TEST",
    UserName = "tester",
    Sections =
    [
        new ReportSection { Name = "Overview", Rows = [new ReportRow { Key = "Computer", Value = "DESKTOP-TEST" }] },
        new ReportSection { Name = "Empty", Rows = [] },
        new ReportSection { Name = "Blank", Rows = [new ReportRow { Key = "GPU", Value = "" }] },
    ],
};
string text = ReportFormatter.ToText(report);
Check("text has header", text.Contains("SystemInfo Report") && text.Contains("DESKTOP-TEST"));
Check("text has sections", text.Contains("=== Overview ===") && text.Contains("Computer: DESKTOP-TEST"));
Check("empty section renders placeholder", text.Contains("(no data)"));
Check("blank value renders Unavailable", text.Contains("GPU: Unavailable"));
string json = ReportFormatter.ToJson(report);
Check("json roundtrips", JsonDocument.Parse(json).RootElement.GetProperty("ComputerName").GetString() == "DESKTOP-TEST");
Check("section text helper", ReportFormatter.SectionToText(report.Sections[0]) == "Computer: DESKTOP-TEST");
Check("empty section helper", ReportFormatter.SectionToText(report.Sections[1]) == "(no data)");

Console.WriteLine("CliArgs");
Check("no args", !CliArgs.Parse([]).HasExportAction && !CliArgs.Parse([]).Help);
Check("--help", CliArgs.Parse(["--help"]).Help);
Check("--json <path>", CliArgs.Parse(["--json", "out.json"]).JsonPath == "out.json");
Check("--export <path>", CliArgs.Parse(["--export", "out.txt"]).ExportPath == "out.txt");
Check("--json default path", CliArgs.Parse(["--json"]).JsonPath == CliArgs.DefaultJsonPath());
Check("--export default path", CliArgs.Parse(["--export"]).ExportPath == CliArgs.DefaultTextPath());
Check("unknown args ignored", !CliArgs.Parse(["--future-flag"]).HasExportAction);

Console.WriteLine("SensorRange removed - gadget uses fixed sections + LHM catalog");
Check("ReportFormatter still works", ReportFormatter.FormatBytes(1024) == "1.0 KB");

Console.WriteLine("LayoutStore");
string tmpDir = Path.Combine(Path.GetTempPath(), "SI-LayoutTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tmpDir);
LayoutStore.LayoutFilePathOverride = Path.Combine(tmpDir, "layout.json");
try
{
    var w = LayoutStore.LoadOrDefault();
    w.KeepOnTop = false;
    w.Boxes.Add(new BoxLayout { ComponentKey = "Cpu:Test CPU", Title = "Test CPU", X = 8, Y = 8, Width = 320, Height = 210, SensorIds = ["lhm:Cpu:Test CPU:Load:CPU Total:0"], GraphSensorIds = ["lhm:Cpu:Test CPU:Load:CPU Total:0"] });
    LayoutStore.Save(w);
    Check("layout file written", File.Exists(LayoutStore.LayoutFilePath));
    var loaded = LayoutStore.LoadOrDefault();
    Check("layout roundtrip", !loaded.KeepOnTop && loaded.Boxes.Count == 1 && loaded.Boxes[0].SensorIds.Count == 1);
    Check("normalize clamps width", LayoutStore.Normalize(new WindowLayout { Width = 1 }).Width >= 300);
    File.WriteAllText(LayoutStore.LayoutFilePath, "{ broken");
    try { File.Delete(LayoutStore.BackupFilePath); } catch { }
    var recovered = LayoutStore.LoadOrDefault();
    Check("corrupt falls back to backup/default", recovered is not null);
}
finally
{
    LayoutStore.LayoutFilePathOverride = null;
    try { Directory.Delete(tmpDir, recursive: true); } catch { }
}

Console.WriteLine();
Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
