using System.IO;
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
