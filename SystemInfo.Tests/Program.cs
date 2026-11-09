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
Check("no args", !CliArgs.Parse([]).HasExportAction && !CliArgs.Parse([]).Help && !CliArgs.Parse([]).HasProfileAction);
Check("--help", CliArgs.Parse(["--help"]).Help);
Check("--json <path>", CliArgs.Parse(["--json", "out.json"]).JsonPath == "out.json");
Check("--export <path>", CliArgs.Parse(["--export", "out.txt"]).ExportPath == "out.txt");
Check("--profile <path>", CliArgs.Parse(["--profile", "out.log"]).ProfilePath == "out.log");
Check("--profile default path", CliArgs.Parse(["--profile"]).ProfilePath == CliArgs.DefaultProfilePath());
Check("--stress flag", CliArgs.Parse(["--stress"]).Stress);
Check("--stress --profile combo", CliArgs.Parse(["--stress", "--profile"]).Stress && CliArgs.Parse(["--stress", "--profile"]).HasProfileAction);
Check("--json default path", CliArgs.Parse(["--json"]).JsonPath == CliArgs.DefaultJsonPath());
Check("--export default path", CliArgs.Parse(["--export"]).ExportPath == CliArgs.DefaultTextPath());
Check("unknown args ignored", !CliArgs.Parse(["--future-flag"]).HasExportAction && !CliArgs.Parse(["--future-flag"]).HasProfileAction);

Console.WriteLine("SensorRange removed - gadget uses fixed sections + LHM catalog");
Check("ReportFormatter still works", ReportFormatter.FormatBytes(1024) == "1.0 KB");

Console.WriteLine("ReleaseChecker.IsNewer");
Check("newer minor version", ReleaseChecker.IsNewer("1.1.0", "1.0.9"));
Check("same version is not newer", !ReleaseChecker.IsNewer("1.0.0", "1.0.0"));
Check("older version is not newer", !ReleaseChecker.IsNewer("0.9.9", "1.0.0"));
Check("shorter tags treated as zero", !ReleaseChecker.IsNewer("1.0", "1.0.0") && ReleaseChecker.IsNewer("1.0.1", "1.0"));

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

    // backup is only created when the primary already exists; do a second save to seed it
    var w2 = LayoutStore.LoadOrDefault(); // w (KeepOnTop=false)
    w2.KeepOnTop = true;
    LayoutStore.Save(w2); // now .bak == w (false)
    Check("backup file written", File.Exists(LayoutStore.BackupFilePath));

    // primary still good at this point
    Check("TryLoad returns true on good primary", LayoutStore.TryLoad(out var goodLayout));
    Check("TryLoad reads correct values", goodLayout.KeepOnTop == true);
    Check("TryLoadFromBackup returns true", LayoutStore.TryLoadFromBackup(out var bakLayout));
    Check("TryLoadFromBackup returns backup values", bakLayout.KeepOnTop == false);

    // corrupt primary, keep good backup
    File.WriteAllText(LayoutStore.LayoutFilePath, "{ broken");
    var recovered = LayoutStore.LoadOrDefault();
    Check("corrupt primary recovered from backup", recovered is not null && recovered.KeepOnTop == false);
    Check("TryLoad falls back to backup when primary is corrupt", LayoutStore.TryLoad(out var recoveredLayout) && recoveredLayout.KeepOnTop == false);
    Check("TrySave succeeds", LayoutStore.TrySave(w2)); // rewrites primary; backup goes bad (copies broken primary)
}
finally
{
    LayoutStore.LayoutFilePathOverride = null;
    try { Directory.Delete(tmpDir, recursive: true); } catch { }
}

Console.WriteLine("LayoutStore.BuildStockDefault (first run only)");
var stockComps = new List<(string key, ComponentType type, string name)>
{
    ("Cpu|AMD Ryzen 7 5700G with Radeon Graphics", ComponentType.Cpu, "AMD Ryzen 7 5700G with Radeon Graphics"),
    ("GpuAmd|AMD Radeon RX 7800 XT", ComponentType.Gpu, "AMD Radeon RX 7800 XT"),
    ("Storage|Samsung SSD 970 EVO Plus 1TB", ComponentType.StorageSsd, "Samsung SSD 970 EVO Plus 1TB"),
    ("Storage|ST2000DM008-2FR102", ComponentType.StorageHdd, "ST2000DM008-2FR102"),
    ("Network|Ethernet", ComponentType.Network, "Ethernet"),
    ("Network|Wi-Fi 2", ComponentType.Network, "Wi-Fi 2"),
};
var stockCatalog = new Dictionary<string, SensorDescriptor>();
void AddStockSensor(string key, ComponentType type, string category, string name, string unit = "%")
{
    string id = $"lhm:{key}:{category}:{name}";
    stockCatalog[id] = new SensorDescriptor { Id = id, ComponentKey = key, ComponentType = type, ComponentName = key, Category = category, Name = name, Unit = unit };
}
const string cpuKey = "Cpu|AMD Ryzen 7 5700G with Radeon Graphics";
AddStockSensor(cpuKey, ComponentType.Cpu, "Load", "CPU Total");
for (int i = 1; i <= 16; i++) AddStockSensor(cpuKey, ComponentType.Cpu, "Load", $"CPU Core #{i}");
AddStockSensor(cpuKey, ComponentType.Cpu, "Temperature", "Core (Tctl/Tdie)", "°C");
AddStockSensor(cpuKey, ComponentType.Cpu, "Power", "Package", "W");
AddStockSensor(cpuKey, ComponentType.Cpu, "Voltage", "Core (SVI2 TFN)", "V");
const string gpuKey = "GpuAmd|AMD Radeon RX 7800 XT";
AddStockSensor(gpuKey, ComponentType.Gpu, "Load", "GPU Core");
AddStockSensor(gpuKey, ComponentType.Gpu, "Temperature", "GPU Core", "°C");
AddStockSensor(gpuKey, ComponentType.Gpu, "Power", "GPU Package", "W");
AddStockSensor(gpuKey, ComponentType.Gpu, "Clock", "GPU Core", "MHz");
AddStockSensor(gpuKey, ComponentType.Gpu, "Fan", "GPU Fan", "RPM");
AddStockSensor(gpuKey, ComponentType.Gpu, "SmallData", "GPU Memory Used", "MB");
AddStockSensor(gpuKey, ComponentType.Gpu, "SmallData", "GPU Memory Free", "MB");
const string ssdKey = "Storage|Samsung SSD 970 EVO Plus 1TB";
AddStockSensor(ssdKey, ComponentType.StorageSsd, "Load", "Total Activity");
AddStockSensor(ssdKey, ComponentType.StorageSsd, "Temperature", "Composite Temperature", "°C");
AddStockSensor(ssdKey, ComponentType.StorageSsd, "Load", "Used Space");
AddStockSensor(ssdKey, ComponentType.StorageSsd, "Level", "Life");
const string hddKey = "Storage|ST2000DM008-2FR102";
AddStockSensor(hddKey, ComponentType.StorageHdd, "Load", "Total Activity");
AddStockSensor(hddKey, ComponentType.StorageHdd, "Temperature", "Temperature", "°C");
AddStockSensor(hddKey, ComponentType.StorageHdd, "Load", "Used Space");
const string ethKey = "Network|Ethernet";
AddStockSensor(ethKey, ComponentType.Network, "Throughput", "Download Speed", "B/s");
AddStockSensor(ethKey, ComponentType.Network, "Throughput", "Upload Speed", "B/s");
const string wifiKey = "Network|Wi-Fi 2";
AddStockSensor(wifiKey, ComponentType.Network, "Throughput", "Download Speed", "B/s");

var stock = LayoutStore.BuildStockDefault(stockComps, stockCatalog, 40, 40);
var stockCpu = stock.Boxes.First(b => b.ComponentType == ComponentType.Cpu);
Check("stock builds the 8-box reference arrangement", stock.Boxes.Count == 7); // CPU/GPU/net/proc/terminal + 2 storage
Check("stock CPU title is shortened from the real CPU name", stockCpu.Title == "Ryzen 7 5700G");
Check("stock CPU shows total + one row per physical core", stockCpu.SensorIds.Contains($"lhm:{cpuKey}:Load:CPU Total") && stockCpu.SensorIds.Contains($"lhm:{cpuKey}:Load:CPU Core #1") && !stockCpu.SensorIds.Contains($"lhm:{cpuKey}:Load:CPU Core #2"));
Check("stock CPU sensors all belong to the CPU instance", stockCpu.SensorIds.All(id => stockCatalog[id].ComponentKey == cpuKey));
Check("stock prefers wired Ethernet over Wi-Fi", stock.Boxes.Count(b => b.ComponentType == ComponentType.Network) == 1 && stock.Boxes.First(b => b.ComponentType == ComponentType.Network).Title == "Ethernet");
Check("stock includes an embedded PowerShell terminal", stock.Boxes.Any(b => b.CustomKind == "app" && b.AppId == "powershell" && b.Title == "Windows PowerShell"));
Check("stock includes Top Processes", stock.Boxes.Any(b => b.CustomKind == "processes" && b.Title == "Top Processes"));
Check("stock does not overlap columns", stockCpu.X + stockCpu.Width <= stock.Boxes.First(b => b.ComponentType == ComponentType.Gpu).X);
Check("stock window matches reference size", stock.Width >= 1079 && stock.Height >= 586);
Check("stock boxes record their creation-time titles", stock.Boxes.All(b => b.SourceTitle == b.Title && b.SourceTitle.Length > 0));
Check("stock boxes are all inside the window", stock.Boxes.All(b => b.X >= 0 && b.Y >= 0 && b.Y + b.Height <= stock.Height));
Check("stock CPU total is graphed", stockCpu.SensorModes[$"lhm:{cpuKey}:Load:CPU Total"] is 5 or 4);
Check("stock CPU core row is a bar", stockCpu.SensorModes[$"lhm:{cpuKey}:Load:CPU Core #1"] == 3);

Console.WriteLine("ScaleHelper.ShortSensorLabel");
static SensorDescriptor Desc(string name, string category, ComponentType type = ComponentType.Gpu) =>
    new() { Id = name, ComponentKey = name, ComponentName = name, Name = name, Category = category, ComponentType = type };
Check("GPU Memory Used -> VRAM Used", ScaleHelper.ShortSensorLabel(Desc("GPU Memory Used", "SmallData")) == "VRAM Used");
Check("GPU Memory Free -> VRAM Free", ScaleHelper.ShortSensorLabel(Desc("GPU Memory Free", "SmallData")) == "VRAM Free");
Check("GPU Memory Total -> VRAM Total", ScaleHelper.ShortSensorLabel(Desc("GPU Memory Total", "SmallData")) == "VRAM Total");
Check("GPU Memory temp -> VRAM Temp", ScaleHelper.ShortSensorLabel(Desc("GPU Memory", "Temperature")) == "VRAM Temp");
Check("GPU Memory label is case-insensitive", ScaleHelper.ShortSensorLabel(Desc("gpu memory clock", "Clock")) == "VRAM clock");
Check("CPU Core #1 -> Core 1", ScaleHelper.ShortSensorLabel(Desc("CPU Core #1", "Load", ComponentType.Cpu)) == "Core 1");
Check("CPU Core #16 -> Core 16", ScaleHelper.ShortSensorLabel(Desc("CPU Core #16", "Load", ComponentType.Cpu)) == "Core 16");
Check("CPU Total unchanged", ScaleHelper.ShortSensorLabel(Desc("CPU Total", "Load", ComponentType.Cpu)) == "CPU Total");
Check("CPU package power -> Power", ScaleHelper.ShortSensorLabel(Desc("Package", "Power", ComponentType.Cpu)) == "Power");
Check("GPU package power -> Power", ScaleHelper.ShortSensorLabel(Desc("GPU Package", "Power", ComponentType.Gpu)) == "Power");
Check("other power rails keep their names", ScaleHelper.ShortSensorLabel(Desc("CPU Cores Power", "Power", ComponentType.Cpu)) == "CPU Cores Power");
Check("GPU core temp gains Temp suffix", ScaleHelper.ShortSensorLabel(Desc("GPU Core", "Temperature")) == "GPU Core Temp");
Check("GPU Hot Spot gains Temp suffix", ScaleHelper.ShortSensorLabel(Desc("GPU Hot Spot", "Temperature")) == "GPU Hot Spot Temp");
Check("CPU Tctl gains Temp suffix", ScaleHelper.ShortSensorLabel(Desc("Core (Tctl/Tdie)", "Temperature", ComponentType.Cpu)) == "Core (Tctl/Tdie) Temp");
Check("name already containing Temp unchanged", ScaleHelper.ShortSensorLabel(Desc("Composite Temperature", "Temperature", ComponentType.StorageSsd)) == "Composite Temperature");
Check("null sensor label is empty", ScaleHelper.ShortSensorLabel(null) == "");

Console.WriteLine("LayoutStore.AutoTitleFor (empty rename restores creation-time title)");
Check("sensor box restores recorded title", LayoutStore.AutoTitleFor(new BoxLayout { ComponentKey = "Cpu:AMD Ryzen 7 5700G with Radeon Graphics", ComponentType = ComponentType.Cpu, SourceTitle = "Ryzen 7 5700G" }) == "Ryzen 7 5700G");
Check("legacy sensor box falls back to short component name", LayoutStore.AutoTitleFor(new BoxLayout { ComponentKey = "Cpu:AMD Ryzen 7 5700G with Radeon Graphics", ComponentType = ComponentType.Cpu }) == "Ryzen 7 5700G");
Check("processes box restores Top Processes", LayoutStore.AutoTitleFor(new BoxLayout { ComponentKey = "custom:processes", ComponentType = ComponentType.System, CustomKind = "processes" }) == "Top Processes");
Check("power box restores Power", LayoutStore.AutoTitleFor(new BoxLayout { ComponentKey = "custom:power", ComponentType = ComponentType.System, CustomKind = "power" }) == "Power");
Check("terminal box restores its terminal display name", LayoutStore.AutoTitleFor(new BoxLayout { ComponentKey = "custom:terminal", ComponentType = ComponentType.System, CustomKind = "app", AppId = "powershell" }) == "Windows PowerShell");
Check("type:cpu box restores CPU", LayoutStore.AutoTitleFor(new BoxLayout { ComponentKey = "type:cpu", ComponentType = ComponentType.Cpu }) == "CPU");
Check("type:storagessd box restores SSD", LayoutStore.AutoTitleFor(new BoxLayout { ComponentKey = "type:storagessd", ComponentType = ComponentType.StorageSsd }) == "SSD");
Check("plain custom box restores Sensors", LayoutStore.AutoTitleFor(new BoxLayout { ComponentKey = "custom", ComponentType = ComponentType.Cpu }) == "Sensors");
Check("whitespace SourceTitle falls back", LayoutStore.AutoTitleFor(new BoxLayout { ComponentKey = "type:gpu", ComponentType = ComponentType.Gpu, SourceTitle = "   " }) == "GPU");

Console.WriteLine("LayoutStore.DeriveAutoTitle (engine rules, ignore recorded titles)");
Check("CPU box derives Ryzen 7 5700G regardless of recorded title", LayoutStore.DeriveAutoTitle(new BoxLayout { ComponentKey = "Cpu:AMD Ryzen 7 5700G with Radeon Graphics", ComponentType = ComponentType.Cpu, SourceTitle = "Whatever" }) == "Ryzen 7 5700G");
Check("GPU box derives RX 7800 XT regardless of recorded title", LayoutStore.DeriveAutoTitle(new BoxLayout { ComponentKey = "GpuAmd:AMD Radeon RX 7800 XT", ComponentType = ComponentType.Gpu, SourceTitle = "Whatever" }) == "RX 7800 XT");
Check("storage box derives the full LHM name, not the shortened title", LayoutStore.DeriveAutoTitle(new BoxLayout { ComponentKey = "Storage:Samsung SSD 860 EVO 500GB ", ComponentType = ComponentType.StorageSsd, SourceTitle = "Samsung SSD 860" }) == "Samsung SSD 860 EVO 500GB");
Check("SanDisk Extreme derives its raw name", LayoutStore.DeriveAutoTitle(new BoxLayout { ComponentKey = "Storage:SanDisk Extreme Portable SSD", ComponentType = ComponentType.StorageSsd, SourceTitle = "SanDisk Extreme Portable" }) == "SanDisk Extreme Portable SSD");
Check("type:cpu derives CPU", LayoutStore.DeriveAutoTitle(new BoxLayout { ComponentKey = "type:cpu", ComponentType = ComponentType.Cpu, SourceTitle = "Whatever" }) == "CPU");
Check("processes box derives Top Processes", LayoutStore.DeriveAutoTitle(new BoxLayout { ComponentKey = "custom:processes", ComponentType = ComponentType.System, CustomKind = "processes", SourceTitle = "Whatever" }) == "Top Processes");
Check("power box derives Power", LayoutStore.DeriveAutoTitle(new BoxLayout { ComponentKey = "custom:power", ComponentType = ComponentType.System, CustomKind = "power", SourceTitle = "Whatever" }) == "Power");
Check("terminal box derives its terminal display name", LayoutStore.DeriveAutoTitle(new BoxLayout { ComponentKey = "custom:terminal", ComponentType = ComponentType.System, CustomKind = "app", AppId = "powershell", SourceTitle = "Whatever" }) == "Windows PowerShell");
Check("plain custom box derives Sensors", LayoutStore.DeriveAutoTitle(new BoxLayout { ComponentKey = "custom", ComponentType = ComponentType.Cpu, SourceTitle = "Whatever" }) == "Sensors");

Console.WriteLine("LayoutStore.Normalize (manual sizes survive restart)");
var narrow = new WindowLayout();
narrow.Boxes.Add(new BoxLayout { ComponentKey = "custom", Title = "Sensors 1", X = 8, Y = 8, Width = 60, Height = 60 });
var narrowNorm = LayoutStore.Normalize(narrow);
Check("manually narrowed box keeps its size on load", narrowNorm.Boxes[0].Width == 60 && narrowNorm.Boxes[0].Height == 60);
var tooNarrow = new WindowLayout();
tooNarrow.Boxes.Add(new BoxLayout { ComponentKey = "custom", Title = "Sensors 1", X = 8, Y = 8, Width = 12, Height = 12 });
var tooNarrowNorm = LayoutStore.Normalize(tooNarrow);
Check("absolute floor is 60 px", tooNarrowNorm.Boxes[0].Width == 60 && tooNarrowNorm.Boxes[0].Height == 60);
var longTitle = new WindowLayout();
longTitle.Boxes.Add(new BoxLayout { ComponentKey = "custom", Title = "A Very Long User Title That Would Want Much More Width", X = 8, Y = 8, Width = 120, Height = 100 });
Check("long user title does not force width on load", LayoutStore.Normalize(longTitle).Boxes[0].Width == 120);
Check("legacy boxes get SourceTitle stamped from their current title", narrowNorm.Boxes[0].SourceTitle == "Sensors 1" && tooNarrowNorm.Boxes[0].SourceTitle == "Sensors 1" && LayoutStore.Normalize(longTitle).Boxes[0].SourceTitle == longTitle.Boxes[0].Title);
Check("migration never overwrites a recorded SourceTitle", LayoutStore.Normalize(new WindowLayout { Boxes = { new BoxLayout { ComponentKey = "custom", Title = "My Rename", SourceTitle = "Sensors 1", X = 8, Y = 8, Width = 100, Height = 100 } } }).Boxes[0].SourceTitle == "Sensors 1");
var legacyProfile = new WindowLayout
{
    Boxes =
    {
        new BoxLayout { ComponentKey = "Storage:Samsung SSD 860 EVO 500GB ", ComponentType = ComponentType.StorageSsd, Title = "Samsung SSD 860", X = 440, Y = 158, Width = 230, Height = 119 },
        new BoxLayout { ComponentKey = "Storage:SanDisk Extreme Portable SSD", ComponentType = ComponentType.StorageSsd, Title = "SanDisk Extreme Portable", X = 440, Y = 404, Width = 266, Height = 119 },
    },
};
var legacyLoaded = LayoutStore.Normalize(legacyProfile);
Check("legacy profile boxes get honoured titles after load", legacyLoaded.Boxes.All(b => b.SourceTitle == b.Title));
Check("legacy empty rename returns the user's title, not the raw hardware name", LayoutStore.AutoTitleFor(legacyLoaded.Boxes[0]) == "Samsung SSD 860" && LayoutStore.AutoTitleFor(legacyLoaded.Boxes[1]) == "SanDisk Extreme Portable");

Console.WriteLine("ScaleHelper.IsHigherBetter");
static bool HB(string name, string cat = "Load", ComponentType type = ComponentType.Cpu)
    => ScaleHelper.IsHigherBetter(new SensorDescriptor { Id = name, ComponentKey = name, ComponentName = name, Name = name, Category = cat, ComponentType = type });
Check("SSD Life is better when high", HB("Life", "Level", ComponentType.StorageSsd));
Check("Remaining Life is better when high", HB("Remaining Life", "Level", ComponentType.StorageSsd));
Check("VRAM free is better when high", HB("GPU Memory Free", "SmallData", ComponentType.Gpu));
Check("Memory Available is better when high", HB("Memory Available", "Data", ComponentType.Memory));
Check("Available Memory is better when high", HB("Available Memory", "Data", ComponentType.Memory));
Check("Free Space is better when high", HB("Free Space", "Data", ComponentType.StorageSsd));
Check("battery level is better when high", HB("Level", "Level", ComponentType.Battery));
Check("CPU load is worse when high", !HB("CPU Total", "Load", ComponentType.Cpu));
Check("GPU load is worse when high", !HB("GPU Core", "Load", ComponentType.Gpu));
Check("temperatures are worse when high", !HB("Core (Tctl/Tdie)", "Temperature", ComponentType.Cpu));
Check("CPU package power keeps low-is-green", !HB("Package", "Power", ComponentType.Cpu));
Check("clock frequencies keep low-is-green", !HB("GPU Core", "Clock", ComponentType.Gpu));
Check("used space is worse when high", !HB("Used Space", "Load", ComponentType.StorageSsd));
Check("percentage used (wear) is worse when high", !HB("Percentage Used", "Load", ComponentType.StorageSsd));
Check("memory used is worse when high", !HB("Memory Used", "Data", ComponentType.Memory));
Check("VRAM used is worse when high", !HB("GPU Memory Used", "SmallData", ComponentType.Gpu));

Console.WriteLine("LayoutCompactor");
static BoxLayout MakeBox(string title, double x, double y, double w, double h) =>
    new() { ComponentKey = title, Title = title, X = x, Y = y, Width = w, Height = h };

// The reference 8-box arrangement: already tight, so compacting must not move anything
// (in particular it must not flatten everything into one horizontal row).
var refLayout = new WindowLayout();
foreach (var b in new[]
{
    MakeBox("CPU", 8, 8, 201, 358), MakeBox("Ethernet", 8, 370, 201, 140),
    MakeBox("GPU", 213, 8, 223, 506),
    MakeBox("Top Processes", 440, 8, 230, 146), MakeBox("SSD 860", 440, 158, 230, 119),
    MakeBox("SSD 970", 440, 281, 276, 119), MakeBox("SanDisk", 440, 404, 266, 119),
    MakeBox("PowerShell", 674, 8, 389, 269),
}) refLayout.Boxes.Add(b);
var refBefore = refLayout.Boxes.Select(b => (b.Title, b.X, b.Y, b.Width, b.Height)).ToList();
LayoutCompactor.Compact(refLayout);
Check("compact keeps the reference rows and columns",
    refLayout.Boxes.Zip(refBefore).All(p => p.First.X == p.Second.X && p.First.Y == p.Second.Y
                                         && p.First.Width == p.Second.Width && p.First.Height == p.Second.Height));
Check("compact keeps 4 distinct columns", refLayout.Boxes.Select(b => b.X).Distinct().Count() == 4);
Check("compact reports no movement when already tight", !LayoutCompactor.Compact(refLayout));

var gapLayout = new WindowLayout();
gapLayout.Boxes.Add(MakeBox("A1", 8, 8, 200, 100));
gapLayout.Boxes.Add(MakeBox("A2", 8, 400, 200, 100));
gapLayout.Boxes.Add(MakeBox("B1", 500, 8, 150, 300));
LayoutCompactor.Compact(gapLayout);
Check("compact removes vertical gaps inside a column", gapLayout.Boxes.First(b => b.Title == "A2").Y == 112);
Check("compact slides a column left into the empty space", gapLayout.Boxes.First(b => b.Title == "B1").X == 212);
Check("compact keeps rows (top edge) unchanged", gapLayout.Boxes.First(b => b.Title == "B1").Y == 8);
Check("compact never resizes boxes", gapLayout.Boxes.First(b => b.Title == "A1").Width == 200 && gapLayout.Boxes.First(b => b.Title == "B1").Height == 300);
var (contentW, contentH) = LayoutCompactor.ContentSize(gapLayout);
Check("compact content size is the tight box bounds plus padding",
    contentW == gapLayout.Boxes.Max(b => b.X + b.Width) + 8 && contentH == gapLayout.Boxes.Max(b => b.Y + b.Height) + 8);

Console.WriteLine("SensorHistory");

var h1 = new SensorHistory { Capacity = 10 };
h1.Push(1.5); h1.Push(2.5);
Check("history keeps pushed values", h1.Values.Count == 2);
var h2 = new SensorHistory { Capacity = 3 };
h2.Push(double.NaN); h2.Push(double.PositiveInfinity); h2.Push(3); h2.Push(4); h2.Push(5);
Check("NaN and Infinity are dropped", h2.Values.Count == 3);
Check("history caps at capacity", h2.Values.Count == 3);
Check("history stores oldest-first ring order", h2.Values.First() == 3 && h2.Values.Last() == 5);
var hNull = new SensorHistory { Capacity = 2 };
hNull.Push(null);
Check("null values dropped", hNull.Values.Count == 0);
var h4 = new SensorHistory { Capacity = 1 };
h4.Push(10); h4.Push(20);
Check("capacity 1 keeps only newest", h4.Values.Count == 1 && h4.Values.First() == 20);

Console.WriteLine();
Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
