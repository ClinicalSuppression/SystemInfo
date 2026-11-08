using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace SystemInfo
{
    public partial class App : System.Windows.Application
    {
        private Mutex? singleInstanceMutex;
        private bool ownsMutex;

        /// <summary>In-memory stress layout used only for --profile --stress runs (never written to disk).</summary>
        internal static WindowLayout? ProfileLayoutOverride { get; set; }

        /// <summary>One box per hardware component with ALL its LHM sensors, all Graph-enabled — worst-case UI/tree/graph load.</summary>
        private static WindowLayout BuildStressLayout(UnifiedSensorCollector collector)
        {
            var stress = new WindowLayout();
            int y = 8;
            foreach (var (key, type, name) in collector.Components)
            {
                var sensors = collector.Descriptors.Values.Where(d => d.ComponentKey == key).ToList();
                if (sensors.Count == 0) continue;
                var modes = new Dictionary<string, int>(StringComparer.Ordinal);
                var sensorIds = new List<string>();
                foreach (var d in sensors)
                {
                    sensorIds.Add(d.Id);
                    modes[d.Id] = (int)RowMode.Text | (int)RowMode.Bar | (int)RowMode.Graph;
                }
                stress.Boxes.Add(new BoxLayout
                {
                    ComponentKey = key, ComponentType = type, Title = name,
                    X = 8, Y = y, Width = 640, Height = 400,
                    SensorIds = sensorIds, GraphSensorIds = sensorIds.ToList(), SensorModes = modes, CombineGraphs = true,
                });
                y += 420;
            }
            return stress;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (sender, args) =>
            {
                args.Handled = true;
                LogError(args.Exception);
                MessageBox.Show(
                    $"Something went wrong, but the app kept running.\n\n{args.Exception.Message}\n\nDetails were written to the log in %LOCALAPPDATA%\\SystemInfo\\app.log.",
                    "SystemInfo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                    LogError(ex);
            };

            CliOptions options = CliArgs.Parse(e.Args);

            // Optional self-elevation: relaunch via UAC if requested and not already elevated.
            try
            {
                var preLayout = LayoutStore.LoadOrDefault();
                if (preLayout.StartAsAdministrator && StartupHelper.TryRelaunchElevated(e.Args))
                {
                    Shutdown();
                    return;
                }
            }
            catch { }

            if (options.Help)
            {
                MessageBox.Show(
                    CliArgs.HelpText,
                    "SystemInfo — help",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            if (options.HasProfileAction)
            {
                // Hidden full-lifetime harness: run a 60s perf sample invisibly, write perf log, exit.
                var collector = new UnifiedSensorCollector();
                collector.Open();
                var layout = options.Stress ? BuildStressLayout(collector) : LayoutStore.LoadOrDefault();
                App.ProfileLayoutOverride = layout;
                var profileWindow = new MainWindow();
                profileWindow.Opacity = 0;          // still renders (WPF draws opacity=0 windows)
                profileWindow.Topmost = false;
                profileWindow.Left = -5000; profileWindow.Top = 0;
                profileWindow.Show();
                // 1 Hz internal RefreshAll() already drives collection/rendering; we sample process metrics every 5 s.
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var lines = new List<string>
                {
                    $"SystemInfo profile - {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    $"stress={options.Stress}, lhs=ALL enabled",
                    "elapsed_s,working_set_bytes,private_bytes,handles,cpu_percent"
                };
                TimeSpan? lastCpu = null;
                DateTime lastSample = DateTime.UtcNow;
                var sampler = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                sampler.Tick += (_, _) =>
                {
                    var p = Process.GetCurrentProcess();
                    p.Refresh();
                    double cpuPct = 0;
                    var cur = p.TotalProcessorTime;
                    if (lastCpu.HasValue)
                    {
                        double elapsed = (DateTime.UtcNow - lastSample).TotalSeconds;
                        cpuPct = elapsed > 0 ? (cur - lastCpu.Value).TotalSeconds / elapsed / Math.Max(1, Environment.ProcessorCount) * 100.0 : 0;
                    }
                    lines.Add($"{clock.Elapsed.TotalSeconds:0},{p.WorkingSet64},{p.PrivateMemorySize64},{p.HandleCount},{cpuPct:0.0}");
                    lastCpu = cur; lastSample = DateTime.UtcNow;
                    if (clock.Elapsed.TotalSeconds >= 60)
                    {
                        sampler.Stop();
                        try { File.WriteAllLines(options.ProfilePath!, lines); } catch { }
                        Shutdown();
                    }
                };
                sampler.Start();
                return;
            }

            if (options.HasExportAction)
            {
                try
                {
                    SystemReport report = SystemInfoCollector.Collect();
                    if (options.JsonPath is not null)
                    {
                        File.WriteAllText(options.JsonPath, ReportFormatter.ToJson(report));
                        MessageBox.Show($"JSON report saved to:\n{options.JsonPath}", "SystemInfo",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else if (options.ExportPath is not null)
                    {
                        File.WriteAllText(options.ExportPath, ReportFormatter.ToText(report));
                        MessageBox.Show($"Text report saved to:\n{options.ExportPath}", "SystemInfo",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    LogError(ex);
                    MessageBox.Show($"Could not save the report.\n\n{ex.Message}", "SystemInfo",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                Shutdown();
                return;
            }

            // Single instance. A named mutex is released automatically if the process dies,
            // so a crash never leaves a stale lock behind.
            singleInstanceMutex = new Mutex(initiallyOwned: false, @"Local\SystemInfo.SingleInstance");
            try
            {
                ownsMutex = singleInstanceMutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex)
            {
                MessageBox.Show(
                    "SystemInfo is already running.",
                    "SystemInfo",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            try
            {
                var layout = LayoutStore.LoadOrDefault();
                ThemeService.Apply(layout.ThemeScheme, ThemeService.ResolveEffectiveMode(layout.ThemeMode));
            }
            catch { ThemeService.Apply("Midnight", "Dark"); }

            var window = new MainWindow();
            MainWindow = window;
            if (options.Minimized || LayoutStore.LoadOrDefault().MinimizeToTray)
                window.WindowState = WindowState.Minimized;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                if (ownsMutex)
                    singleInstanceMutex?.ReleaseMutex();
            }
            catch
            {
                // Releasing an unowned mutex is harmless to ignore on shutdown.
            }

            singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }

        internal static void LogError(Exception exception)
        {
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SystemInfo");
                Directory.CreateDirectory(directory);
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}";
                File.AppendAllText(Path.Combine(directory, "app.log"), line);
            }
            catch
            {
                // Logging must never crash the app.
            }
        }
    }
}
