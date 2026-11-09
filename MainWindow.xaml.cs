using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WpfClipboard = System.Windows.Clipboard;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfTextBox = System.Windows.Controls.TextBox;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace SystemInfo;

public partial class MainWindow : Window
{
    private WindowLayout _layout;
    private UnifiedSensorCollector? _collector;
    private DispatcherTimer? _timer;
    private readonly Dictionary<string, SensorHistory> _histories = new(StringComparer.Ordinal);
    private readonly List<ComponentBox> _boxControls = [];
    private TrayService? _tray;
    private HwndSource? _hwndSource;
    private DispatcherTimer? _hoverTimer;
    private bool _chromeHidden;
    private bool _firstRun;

    public MainWindow()
    {
        InitializeComponent();
        _layout = App.ProfileLayoutOverride ?? LayoutStore.LoadOrDefault();
        _firstRun = App.ProfileLayoutOverride is null && !LayoutStore.ExistsOnDisk;
        try { ThemeService.Apply(_layout.ThemeScheme, ThemeService.ResolveEffectiveMode(_layout.ThemeMode)); } catch { ThemeService.Apply("Midnight", "Dark"); }
        ApplyWindowBounds();
        Topmost = _layout.KeepOnTop;
        BuildContextMenu();
        RefreshThumb();

        // Headless test harness: if --fit-test/--compact-test was specified, run the action, write results, exit.
        if (App.RunTestAction is not null)
        {
            InitNativeResize();
            _collector = new UnifiedSensorCollector();
            _collector.Open(true);
            RebuildBoxes();
            RefreshAll();
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemInfo", "perf.log");
            var lines = new List<string> { "SystemInfo compact-test", $"action={App.RunTestAction}" };
            if (App.RunTestAction == "fit")
            {
                CompactBoxes();
                lines.Add($"applied=compact boxes; visible={_boxControls.Count}");
                lines.AddRange(_boxControls.Select(c => $"box={c.Layout.Title} x={c.Layout.X:0} y={c.Layout.Y:0} w={c.Layout.Width:0} h={c.Layout.Height:0}"));
            }
            else if (App.RunTestAction == "compact")
            {
                CompactWindow();
                lines.Add($"applied=compact; window={Width:0}x{Height:0}");
                lines.AddRange(_boxControls.Select(c => $"box={c.Layout.Title} x={c.Layout.X:0} y={c.Layout.Y:0} w={c.Layout.Width:0} h={c.Layout.Height:0}"));
            }
            lines.Add($"window={Width:0}x{Height:0}");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.WriteAllLines(logPath, lines);
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }

        Loaded += (_, _) =>
        {
            InitNativeResize();
            _collector = new UnifiedSensorCollector();
            _collector.Open(true);
            // First run (no layout on disk): create the stock 8-box layout from this machine's hardware.
            if (_firstRun)
            {
                _firstRun = false;
                var wa = SystemParameters.WorkArea;
                _layout = LayoutStore.BuildStockDefault(_collector.Components, _collector.Descriptors,
                    Math.Max(wa.Left, wa.Left + (wa.Width - 1079) / 2),
                    Math.Max(wa.Top, wa.Top + (wa.Height - 586) / 2));
                ApplyWindowBounds();
                SaveLayout();
            }
            RebuildBoxes();
            RefreshAll();
            // Optional release check (opt-out by default)
            try { if (_layout.CheckForUpdatesOnStartup) ReleaseChecker.CheckForUpdatesOnStartup(); } catch { }
            // Headless test harness: run fit/compact, write results, exit.
            if (App.RunTestAction is not null)
            {
                var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemInfo", "perf.log");
                var lines = new List<string> { $"SystemInfo compact-test - {DateTime.Now:yyyy-MM-dd HH:mm:ss}", $"action={App.RunTestAction}" };
                if (App.RunTestAction == "fit")
                {
                    CompactBoxes();
                    lines.Add($"applied=compact boxes; visible={_boxControls.Count}");
                    foreach (var c in _boxControls) lines.Add($"box={c.Layout.Title} x={c.Layout.X:0.0} y={c.Layout.Y:0.0} w={c.Layout.Width:0.0} h={c.Layout.Height:0.0}");
                }
                else if (App.RunTestAction == "compact")
                {
                    CompactWindow();
                    lines.Add($"applied=compact; window={Width:0.0}x{Height:0.0}");
                    foreach (var c in _boxControls) lines.Add($"box={c.Layout.Title} x={c.Layout.X:0.0} y={c.Layout.Y:0.0} w={c.Layout.Width:0.0} h={c.Layout.Height:0.0}");
                }
                lines.Add($"window={Width:0.0}x{Height:0.0}");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.WriteAllLines(logPath, lines);
                // Terminate immediately to avoid leaking the hidden window/WNDProc into the user's session
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                return;
            }
            SizeChanged += (_, _) => GrowCanvasToFit();
            LocationChanged += (_, _) => SaveLayout();
            _tray = new TrayService(this);
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => RefreshAll();
            _timer.Start();
            _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _hoverTimer.Tick += (_, _) => UpdateHoverChrome();
            _hoverTimer.Start();
            ApplyHoverChrome(false);
        };
        Closed += (_, _) =>
        {
            _timer?.Stop();
            _hoverTimer?.Stop();
            try { TerminalHost.KillAll(); } catch { }
            try { SaveLayout(); } catch { }
            try { _tray?.Dispose(); } catch { }
            try { _collector?.Dispose(); } catch { }
        };
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenu();
        var miLockWin = new MenuItem { Header = "Lock window", IsCheckable = true, IsChecked = _layout.WindowLocked };
        miLockWin.Checked += (_, _) => { _layout.WindowLocked = true; SaveLayout(); };
        miLockWin.Unchecked += (_, _) => { _layout.WindowLocked = false; SaveLayout(); };
        var miKeepTop = new MenuItem { Header = "Keep on top", IsCheckable = true, IsChecked = _layout.KeepOnTop };
        miKeepTop.Click += (_, _) => { _layout.KeepOnTop = !_layout.KeepOnTop; Topmost = _layout.KeepOnTop; SaveLayout(); miKeepTop.IsChecked = _layout.KeepOnTop; };
        var miHideBackground = new MenuItem { Header = "Hide Background", IsCheckable = true, IsChecked = _layout.HideBackground };
        miHideBackground.Click += (_, _) => { _layout.HideBackground = !_layout.HideBackground; SaveLayout(); ApplyHoverChrome(true); miHideBackground.IsChecked = _layout.HideBackground; };
        var miLockBoxes = new MenuItem { Header = "Lock boxes", IsCheckable = true, IsChecked = _layout.BoxesLocked };
        miLockBoxes.Checked += (_, _) => { _layout.BoxesLocked = true; ApplyBoxLocks(); SaveLayout(); };
        miLockBoxes.Unchecked += (_, _) => { _layout.BoxesLocked = false; ApplyBoxLocks(); SaveLayout(); };
        var miFont = new MenuItem { Header = "Font size" };
        var miBar = new MenuItem { Header = "Bar size" };
        var miGraph = new MenuItem { Header = "Graph size" };
        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var f = new MenuItem { Header = ScaleHelper.ScaleNames[i], IsCheckable = true, IsChecked = _layout.FontScale == i };
            f.Click += (_, _) =>
        {
            _layout.FontScale = idx; SaveLayout(); RefreshAll();
            foreach (var item in miFont.Items.OfType<MenuItem>()) item.IsChecked = item.Header?.ToString() == ScaleHelper.ScaleNames[idx];
        };
            miFont.Items.Add(f);
            var b = new MenuItem { Header = ScaleHelper.ScaleNames[i], IsCheckable = true, IsChecked = _layout.BarScale == i };
            b.Click += (_, _) =>
        {
            _layout.BarScale = idx; SaveLayout(); RefreshAll();
            foreach (var item in miBar.Items.OfType<MenuItem>()) item.IsChecked = item.Header?.ToString() == ScaleHelper.ScaleNames[idx];
        };
            miBar.Items.Add(b);
            var g = new MenuItem { Header = ScaleHelper.ScaleNames[i], IsCheckable = true, IsChecked = _layout.GraphScale == i };
            g.Click += (_, _) =>
        {
            _layout.GraphScale = idx; SaveLayout(); RefreshAll();
            foreach (var item in miGraph.Items.OfType<MenuItem>()) item.IsChecked = item.Header?.ToString() == ScaleHelper.ScaleNames[idx];
        };
            miGraph.Items.Add(g);
        }
        var miAdd = new MenuItem { Header = "+ Add" };
        var miAddSensor = new MenuItem { Header = "Sensors" };
        miAddSensor.Click += (_, _) =>
        {
            var slot = FindVisibleSlot(320, 210);
            var sensorTitle = $"Sensors {_layout.Boxes.Count + 1}";
            var nb = new BoxLayout { ComponentKey = "custom", Title = sensorTitle, SourceTitle = sensorTitle, X = slot.x, Y = slot.y, Width = 320, Height = 210 };
            _layout.Boxes.Add(nb);
            SaveLayout(); RebuildBoxes();
            BoardScroll.ScrollToVerticalOffset(Math.Max(0, slot.y - 20));
        };
        var miAddProc = new MenuItem { Header = "Processes (Top N)" };
        miAddProc.Click += (_, _) =>
        {
            var slot = FindVisibleSlot(320, 260);
            var nb = LayoutStore.CreateProcessesBox(slot.y); nb.X = slot.x;
            _layout.Boxes.Add(nb);
            SaveLayout(); RebuildBoxes();
            BoardScroll.ScrollToVerticalOffset(Math.Max(0, slot.y - 20));
        };
        var miAddPower = new MenuItem { Header = "Power (Shutdown/Restart…)" };
        miAddPower.Click += (_, _) =>
        {
            var slot = FindVisibleSlot(320, 200);
            var nb = LayoutStore.CreatePowerBox(slot.y); nb.X = slot.x;
            _layout.Boxes.Add(nb);
            SaveLayout(); RebuildBoxes();
            BoardScroll.ScrollToVerticalOffset(Math.Max(0, slot.y - 20));
        };
        var miAddApp = new MenuItem { Header = "Terminal (cmd / PowerShell / PuTTY)" };
        miAddApp.Click += (_, _) =>
        {
            var slot = FindVisibleSlot(380, 300);
            var nb = LayoutStore.CreateAppBox(slot.y); nb.X = slot.x;
            _layout.Boxes.Add(nb);
            SaveLayout(); RebuildBoxes();
            BoardScroll.ScrollToVerticalOffset(Math.Max(0, slot.y - 20));
        };
        miAdd.Items.Add(miAddSensor);
        foreach (var (type, label) in new (ComponentType type, string label)[]
        {
            (ComponentType.Cpu, "CPU"),
            (ComponentType.Gpu, "GPU"),
            (ComponentType.Memory, "Memory"),
            (ComponentType.StorageSsd, "SSD"),
            (ComponentType.StorageHdd, "HDD"),
            (ComponentType.Storage, "Storage"),
            (ComponentType.Motherboard, "Motherboard"),
            (ComponentType.Controller, "Controller"),
            (ComponentType.Psu, "PSU"),
            (ComponentType.Network, "Network"),
            (ComponentType.Battery, "Battery"),
        })
        {
            var t = type; string typeName = t.ToString().ToLowerInvariant();
            var miType = new MenuItem { Header = label };
            miType.Click += (_, _) =>
            {
                var slot = FindVisibleSlot(320, 210);
                var nb = new BoxLayout { ComponentKey = $"type:{typeName}", ComponentType = t, Title = label, SourceTitle = label, X = slot.x, Y = slot.y, Width = 320, Height = 210 };
                _layout.Boxes.Add(nb);
                SaveLayout(); RebuildBoxes();
                BoardScroll.ScrollToVerticalOffset(Math.Max(0, slot.y - 20));
            };
            miAdd.Items.Add(miType);
        }
        miAdd.Items.Add(miAddProc);
        miAdd.Items.Add(miAddPower);
        miAdd.Items.Add(miAddApp);
        var miTheme = new MenuItem { Header = "Theme" };
        foreach (string scheme in new[] { "Midnight", "Aurora", "Ember", "Paper" })
        {
            string s = scheme;
            foreach (string mode in new[] { "System", "Dark", "Light" })
            {
                string m = mode;
                var mi = new MenuItem { Header = $"{s} {m}", IsCheckable = true, IsChecked = _layout.ThemeScheme == s && _layout.ThemeMode == m };
                        mi.Click += (_, _) =>
        {
            _layout.ThemeScheme = s; _layout.ThemeMode = m;
            foreach (var item in miTheme.Items.OfType<MenuItem>())
                item.IsChecked = item.Header?.ToString() == $"{s} {m}";
            SaveLayout(); ApplyTheme();
            _tray?.RebuildMenu();
        };
                miTheme.Items.Add(mi);
            }
        }
        var miProfiles = new MenuItem { Header = "Profiles" };
        var miLayout = new MenuItem { Header = "Layout" };
        var miFitBoxes = new MenuItem { Header = "Compact boxes" };
        miFitBoxes.Click += (_, _) => CompactBoxes();
        var miCompact = new MenuItem { Header = "Compact window" };
        miCompact.Click += (_, _) => CompactWindow();
        miLayout.Items.Add(miFitBoxes);
        miLayout.Items.Add(miCompact);
        var miSaveProf = new MenuItem { Header = "Save profile…" };
        miSaveProf.Click += (_, _) => ProfileStore.SaveProfile(this, _layout);
        var miLoadProf = new MenuItem { Header = "Load profile…" };
        miLoadProf.Click += (_, _) =>
        {
            var loaded = ProfileStore.LoadProfile(this);
            if (loaded is not null) { _layout = loaded; SaveLayout(); ApplyTheme(); ApplyBoxLocks(); RefreshAll(); BuildContextMenu(); }
        };
        var miReset = new MenuItem { Header = "Reset to defaults" };
        miReset.Click += (_, _) =>
        {
            if (MessageBox.Show("Replace the current layout with the stock layout (rebuilt for this machine)?", "Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                foreach (var b in _layout.Boxes.Where(b => b.CustomKind == "app").ToList())
                {
                    try { TerminalHost.Kill(b); } catch { }
                }
                var wa = SystemParameters.WorkArea;
                _layout = LayoutStore.BuildStockDefault(_collector?.Components ?? [], _collector?.Descriptors ?? new Dictionary<string, SensorDescriptor>(),
                    Math.Max(wa.Left, wa.Left + (wa.Width - 1079) / 2),
                    Math.Max(wa.Top, wa.Top + (wa.Height - 586) / 2));
                ApplyWindowBounds();
                SaveLayout(); RebuildBoxes(); RefreshAll(); BuildContextMenu();
            }
        };
        miProfiles.Items.Add(miSaveProf);
        miProfiles.Items.Add(miLoadProf);
        miProfiles.Items.Add(miReset);
        var miSettings = new MenuItem { Header = "Settings…" };
        miSettings.Click += (_, _) => SettingsButton_Click(this, new RoutedEventArgs());
        var miLhm = new MenuItem { Header = "LHM status (LibreHardwareMonitor)…" };
        miLhm.Click += (_, _) => ShowLhmStatus();
        var miSaveState = new MenuItem { Header = "Save current state" };
        miSaveState.Click += (_, _) => { SaveLayout(); ProfileStore.SaveProfile(this, _layout); };
        menu.Items.Add(miLockWin);
        menu.Items.Add(miKeepTop);
        menu.Items.Add(miHideBackground);
        menu.Items.Add(miLockBoxes);
        menu.Items.Add(new Separator());
        menu.Items.Add(miTheme);
        menu.Items.Add(miFont);
        menu.Items.Add(miBar);
        menu.Items.Add(miGraph);
        menu.Items.Add(new Separator());
        menu.Items.Add(miAdd);
        menu.Items.Add(miLayout);
        menu.Items.Add(miProfiles);
        menu.Items.Add(miSaveState);
        menu.Items.Add(miLhm);
        menu.Items.Add(miSettings);
        ContextMenu = menu;
    }

    private void RefreshThumb()
    {
        // Native Windows resize via WM_NCHITTEST — no visible squares.
    }

    private void InitNativeResize()
    {
        try
        {
            _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            _hwndSource?.AddHook(WndProc);
        }
        catch { }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084;
        if (msg != WM_NCHITTEST || _layout.WindowLocked || WindowState != WindowState.Normal) return IntPtr.Zero;
        if (HitTest(lParam, out int ht)) { handled = true; return new IntPtr(ht); }
        return IntPtr.Zero;
    }

    private bool HitTest(IntPtr lParam, out int result)
    {
        result = 1; // HTCLIENT
        if (_hwndSource is null) return false;
        try
        {
            int x = (short)(lParam.ToInt32() & 0xFFFF);
            int y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
            if (!Win32GetWindowRect(_hwndSource.Handle, out RECT rc)) return false;
            const int B = 8; // physical px resize border, like a normal window
            bool left = x <= rc.Left + B, right = x >= rc.Right - B;
            bool top = y <= rc.Top + B, bottom = y >= rc.Bottom - B;
            if (left && top) { result = 13; return true; }       // HTTOPLEFT
            if (right && top) { result = 14; return true; }      // HTTOPRIGHT
            if (left && bottom) { result = 16; return true; }    // HTBOTTOMLEFT
            if (right && bottom) { result = 17; return true; }   // HTBOTTOMRIGHT
            if (left) { result = 10; return true; }              // HTLEFT
            if (right) { result = 11; return true; }             // HTRIGHT
            if (top) { result = 12; return true; }               // HTTOP
            if (bottom) { result = 15; return true; }            // HTBOTTOM
        }
        catch { }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private static bool Win32GetWindowRect(IntPtr hWnd, out RECT rc) => GetWindowRect(hWnd, out rc);

    private void UpdateHoverChrome()
    {
        // "Hide Background" unchecked -> chrome always shown, hover timer does nothing.
        if (!_layout.HideBackground) { if (_chromeHidden) ApplyHoverChrome(true); return; }
        bool inside = false;
        try
        {
            if (_hwndSource is not null && GetCursorPos(out POINT pt) && Win32GetWindowRect(_hwndSource.Handle, out RECT rc))
                inside = pt.X >= rc.Left && pt.X <= rc.Right && pt.Y >= rc.Top && pt.Y <= rc.Bottom;
            else if (IsMouseOver) inside = true;
        }
        catch { inside = IsMouseOver; }
        if (inside != !_chromeHidden) ApplyHoverChrome(inside);
    }

    private void ApplyHoverChrome(bool show)
    {
        _chromeHidden = !show;
        try
        {
            if (ChromeBorder is null) return;
            if (show)
            {
                ChromeBorder.Background = (System.Windows.Media.Brush)TryFindResource("WindowBackgroundBrush") ?? ThemeService.Find("WindowBackgroundBrush");
                ChromeBorder.BorderBrush = (System.Windows.Media.Brush)TryFindResource("WindowBorderBrush") ?? ThemeService.Find("WindowBorderBrush");
                ChromeBorder.BorderThickness = new Thickness(1);
                PatternRect.Visibility = Visibility.Visible;
                TitleBarBorder.Background = (System.Windows.Media.Brush)TryFindResource("BoxHeaderBackgroundBrush") ?? ThemeService.Find("BoxHeaderBackgroundBrush");
                TitleBarContent.Visibility = Visibility.Visible;
            }
            else
            {
                ChromeBorder.Background = System.Windows.Media.Brushes.Transparent;
                ChromeBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
                ChromeBorder.BorderThickness = new Thickness(1);
                PatternRect.Visibility = Visibility.Collapsed;
                TitleBarBorder.Background = System.Windows.Media.Brushes.Transparent;
                TitleBarContent.Visibility = Visibility.Collapsed;
            }
        }
        catch { }
    }

    public WindowLayout GetLayoutForTray() => _layout;

    public void ApplyThemeFromTray(string scheme, string mode)
    {
        _layout.ThemeScheme = scheme; _layout.ThemeMode = mode;
        SaveLayout(); ApplyTheme();
        _tray?.RebuildMenu();
    }

    public void SaveCurrentStateFromTray()
    {
        SaveLayout();
        ProfileStore.SaveProfile(this, _layout);
        _tray?.ShowBalloon("Current state saved (layout.json + profile).");
    }

    public void OpenProfilesFromTray()
    {
        var loaded = ProfileStore.LoadProfile(this);
        if (loaded is not null) { _layout = loaded; SaveLayout(); ApplyTheme(); ApplyBoxLocks(); RefreshAll(); BuildContextMenu(); }
    }

    public void OpenSettingsFromTray()
    {
        ShowFromTray();
        SettingsButton_Click(this, new RoutedEventArgs());
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void HideToTray()
    {
        Hide();
    }

    public void ApplyTheme()
    {
        try { ThemeService.Apply(_layout.ThemeScheme, ThemeService.ResolveEffectiveMode(_layout.ThemeMode)); } catch { ThemeService.Apply("Midnight", "Dark"); }
        Title = "SystemInfo";
        RebuildBoxes();
        RefreshAll();
        GrowCanvasToFit();
    }

    private void ApplySearchFilter()
    {
        GrowCanvasToFit();
    }

    public IReadOnlyList<ComponentBox> BoxControls => _boxControls;

    private void ApplyBoxLocks()
    {
        foreach (var box in _boxControls) box.SetLocked(_layout.BoxesLocked || box.Layout.Locked);
        RefreshThumb();
    }

    private void SaveLayout()
    {
        _layout.Left = Left; _layout.Top = Top; _layout.Width = Width; _layout.Height = Height;
        try { LayoutStore.Save(_layout); } catch (Exception ex) { App.LogError(ex); }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_layout.WindowLocked) return;
        if (e.OriginalSource is System.Windows.Controls.Button || e.OriginalSource is System.Windows.Controls.CheckBox) return;
        try { DragMove(); } catch { }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_layout.MinimizeToTray) HideToTray();
        else WindowState = WindowState.Minimized;
    }
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new LayoutSettingsWindow(_layout) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            SaveLayout();
            ApplyBoxLocks();
            ApplyTheme();
            RefreshAll();
            BuildContextMenu();
        }
    }

    /// <summary>Applies the persisted window size/position to the window (used after stock/default layout creation).</summary>
    private void ApplyWindowBounds()
    {
        Width = _layout.Width;
        Height = _layout.Height;
        Left = _layout.Left;
        Top = _layout.Top;
    }

    private void RebuildBoxes()
    {
        _boxControls.Clear();
        BoardCanvas.Children.Clear();
        double fs = ScaleHelper.FontSize(_layout.FontScale);
        double bh = ScaleHelper.BarHeight(_layout.BarScale);
        double gh = ScaleHelper.GraphHeight(_layout.GraphScale);
        foreach (var bl in _layout.Boxes.Where(b => b.Visible).ToList())
        {
            var ctrl = new ComponentBox(bl, BoardCanvas, fs, bh, gh);
            ctrl.SetLocked(_layout.BoxesLocked || bl.Locked);
            Canvas.SetLeft(ctrl, bl.X);
            Canvas.SetTop(ctrl, bl.Y);
            ctrl.Moved += _ => { SaveLayout(); GrowCanvasToFit(); };
            ctrl.Resized += _ => { SaveLayout(); GrowCanvasToFit(); RefreshAll(); };
            ctrl.LayoutChanged += _ => { SaveLayout(); GrowCanvasToFit(); };
            ctrl.PickSensors += c => OpenPicker(c);
            ctrl.RemoveClicked += c => RemoveBox(c);
            BoardCanvas.Children.Add(ctrl);
            _boxControls.Add(ctrl);
        }
        GrowCanvasToFit();
        ApplySearchFilter();
    }

    private void GrowCanvasToFit()
    {
        // Canvas width tracks window viewport (free left/right drag); height grows to content for vertical scroll.
        double viewW = Math.Max(200, BoardScroll.ActualWidth - 2);
        double viewH = Math.Max(200, BoardScroll.ActualHeight - 2);
        double maxY = 0;
        foreach (var bl in _layout.Boxes.Where(b => b.Visible))
            maxY = Math.Max(maxY, bl.Y + bl.Height);
        BoardCanvas.Width = viewW;
        BoardCanvas.Height = Math.Max(viewH, maxY + 8);
    }

    private (double x, double y) FindVisibleSlot(double boxW, double boxH)
    {
        double viewW = Math.Max(300, BoardScroll.ActualWidth - 2);
        double viewH = Math.Max(200, BoardScroll.ActualHeight - 2);
        // Try grid slots inside viewport first.
        for (double y = 8; y + boxH <= viewH; y += 224)
        {
            for (double x = 8; x + boxW <= viewW; x += 324)
            {
                bool overlaps = _layout.Boxes.Any(o => o.Visible && x < o.X + o.Width + 4 && x + boxW + 4 > o.X && y < o.Y + o.Height + 4 && y + boxH + 4 > o.Y);
                if (!overlaps) return (x, y);
            }
        }
        // Fall back to below content + auto-grow window if needed.
        double maxBottom = viewH;
        foreach (var b in _layout.Boxes.Where(b => b.Visible))
            maxBottom = Math.Max(maxBottom, b.Y + b.Height + 8);
        double nextY = maxBottom;
        double needH = nextY + (int)boxH + 46;
        if (needH > Height && Height < MaxHeight)
            Height = Math.Min(MaxHeight, needH);
        return (8, nextY);
    }

    /// <summary>Shrinks every box to the size its current content needs (never clips). Does not move boxes.</summary>
    private void CompactBoxes()
    {
        if (_collector is null) return;
        Dictionary<string, double?> values;
        try { values = _collector.Collect(); } catch (Exception ex) { App.LogError(ex); return; }
        var fs = ScaleHelper.FontSize(_layout.FontScale);
        var bh = ScaleHelper.BarHeight(_layout.BarScale);
        var gh = ScaleHelper.GraphHeight(_layout.GraphScale);
        foreach (var bl in _layout.Boxes.Where(b => b.Visible).ToList())
        {
            // Build the box first so we can measure it (important for terminal/processes).
            // Find the ComponentBox instance (may be null if not built yet — always exists after RebuildBoxes).
            var ctrl = _boxControls.FirstOrDefault(c => c.Layout == bl);
            if (ctrl is not null)
            {
                double naturalW = ctrl.MeasureNaturalWidth(_collector.Descriptors, values, fs);
                double naturalH = ctrl.MeasureNaturalHeight(_collector.Descriptors, fs, bh, gh);
                SetBoxSize(ctrl, naturalW, naturalH);
            }
            else
            {
                bl.Width = 320; bl.Height = 210;
            }
        }
        SaveLayout();
        RebuildBoxes();
        RefreshAll();
        GrowCanvasToFit();
    }

    private void CompactWindow()
    {
        // Remove empty space only: keep every box's size and its row/column arrangement.
        LayoutCompactor.Compact(_layout);

        var visible = _layout.Boxes.Where(b => b.Visible).ToList();
        if (visible.Count == 0) return;
        var (contentW, contentH) = LayoutCompactor.ContentSize(_layout);
        // Layout coordinates live inside the ScrollViewer viewport, which is inset by the window
        // chrome: 6px outer margin + 1px border on each side (top also carries the 34px title bar + 6px gap).
        const double ChromeWidth = 14;   // 7px left (1 border + 6 margin) + 7px right
        const double ChromeHeight = 54;  // 46px top (border + 6 margin + 34 title + 6 gap) + 7px bottom + 1px border
        Width = Math.Clamp(contentW + ChromeWidth, MinWidth, MaxWidth);
        Height = Math.Clamp(contentH + ChromeHeight, MinHeight, MaxHeight);

        SaveLayout();
        RebuildBoxes();
        RefreshAll();
        GrowCanvasToFit();
    }

    private void SetBoxSize(ComponentBox ctrl, double width, double height)
    {
        // Apply measured size with a small safety margin to guarantee no clipping.
        double w = Math.Clamp(width, 200, 2000);
        double h = Math.Clamp(height + 4, 90, 2000); // +4 safety
        ctrl.Layout.Width = w;
        ctrl.Layout.Height = h;
        ctrl.Width = w;
        ctrl.Height = h;
    }

    private void RemoveBox(ComponentBox box)
    {
        // Lock only prevents move/resize — closing is always allowed.
        if (box.Layout.CustomKind == "app")
        {
            try { TerminalHost.Kill(box.Layout); } catch { }
        }
        _layout.Boxes.Remove(box.Layout);
        SaveLayout();
        RefreshAll();
        RebuildBoxes();
        ApplySearchFilter();
    }

    private void OpenPicker(ComponentBox box)
    {
        if (_collector is null) return;
        Dictionary<string, double?> liveValues;
        try { liveValues = _collector.Collect(); } catch (Exception ex) { App.LogError(ex); return; }
        // Feed the picker history map so live values render in the list.
        foreach (var kv in liveValues)
        {
            if (!_histories.TryGetValue(kv.Key, out var h)) { h = new SensorHistory { Capacity = 300 }; _histories[kv.Key] = h; }
            h.Push(kv.Value);
        }
        var dlg = new SensorPickerWindow(box.Layout.Title, box.Layout.ComponentKey, _collector.Descriptors, box.Layout.SensorIds, box.Layout.GraphSensorIds, box.Layout.SensorModes, box.Layout.CombineGraphs, liveValues, box.Layout.GraphShowLegend, box.Layout.GraphScaleMode, box.Layout.GraphLineThickness, box.Layout.GraphSeriesColors, _collector.GetStatusSummary(liveValues), box.Layout.NetworkSpeedUnit) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            box.Layout.SensorIds = dlg.SensorIds.ToList();
            box.Layout.GraphSensorIds = dlg.GraphSensorIds.ToList();
            box.Layout.SensorModes = dlg.SensorModes;
            box.Layout.CombineGraphs = dlg.CombineGraphs;
            box.Layout.GraphShowLegend = dlg.GraphShowLegend;
            box.Layout.GraphScaleMode = dlg.GraphScaleMode;
            box.Layout.GraphLineThickness = dlg.GraphLineThickness;
            box.Layout.GraphSeriesColors = dlg.GraphSeriesColors;
            box.Layout.NetworkSpeedUnit = dlg.NetworkSpeedUnit;
            SaveLayout();
            RefreshAll();
        }
    }

    private void ShowLhmStatus()
    {
        if (_collector is null) return;
        Dictionary<string, double?> liveValues;
        try { liveValues = _collector.Collect(); } catch (Exception ex) { MessageBox.Show(ex.Message, "LHM status", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        string text = _collector.GetStatusSummary(liveValues) + "\n\n" + _collector.GetHardwareBreakdown()
            + "\nEvery row/graph in this app is a live LHM sensor (LibreHardwareMonitorLib 0.9.4). Boxes show limited defaults, but the sensor picker lists every LHM sensor for a box's hardware instance — tick Header to chart All-Cores vs per-core lines. NaN/— means LHM returned no value (often needs Administrator for temps/power/clocks/fan).";
        MessageBox.Show(text, "LHM status (LibreHardwareMonitor)", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RefreshAll()
    {
        if (_collector is null) return;
        Dictionary<string, double?> values;
        try { values = _collector.Collect(); } catch (Exception ex) { App.LogError(ex); return; }
        foreach (var kv in values)
        {
            if (!_histories.TryGetValue(kv.Key, out var h)) { h = new SensorHistory { Capacity = 300 }; _histories[kv.Key] = h; }
            h.Push(kv.Value);
        }
        double fs = ScaleHelper.FontSize(_layout.FontScale);
        double bh = ScaleHelper.BarHeight(_layout.BarScale);
        double gh = ScaleHelper.GraphHeight(_layout.GraphScale);
        foreach (var box in _boxControls) box.UpdateRows(_collector.Descriptors, values, _histories, fs, bh, gh);
    }
}
