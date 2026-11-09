using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Wpfbrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfFontWeights = System.Windows.FontWeights;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfPoint = System.Windows.Point;
using WpfPointCollection = System.Windows.Media.PointCollection;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfDragDeltaEventArgs = System.Windows.Controls.Primitives.DragDeltaEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfButton = System.Windows.Controls.Button;
using WpfCursors = System.Windows.Input.Cursors;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;
using WpfApplication = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace SystemInfo;

public sealed class ComponentBox : Border
{
    public BoxLayout Layout { get; }
    /// <summary>Live component catalog (key, type, name). Set by MainWindow at collector open; used to derive intelligent auto titles.</summary>
    internal static IReadOnlyList<(string key, ComponentType type, string name)>? StaticComponents { get; set; }
    public event Action<ComponentBox>? Moved;
    public event Action<ComponentBox>? Resized;
    public event Action<ComponentBox>? PickSensors;
    public event Action<ComponentBox>? RemoveClicked;
    public event Action<ComponentBox>? LayoutChanged;

    private static WpfSolidColorBrush B(string key) => ThemeService.Find(key);
    private static bool IsSourceButton(object? source)
    {
        var el = source as System.Windows.DependencyObject;
        while (el is not null)
        {
            if (el is WpfButton) return true;
            if (el is System.Windows.FrameworkElement fe && fe.Parent is System.Windows.DependencyObject p) el = p;
            else if (el is System.Windows.Media.Visual v) el = System.Windows.Media.VisualTreeHelper.GetParent(v);
            else break;
        }
        return false;
    }

    private readonly StackPanel _rows = new() { Margin = new Thickness(6, 1, 6, 1) };
    private readonly Dictionary<string, SensorRow> _rowCache = new(StringComparer.Ordinal);
    private ScrollViewer? _scroll;
    private readonly TextBlock _titleText;
    private readonly Canvas _canvasParent;
    private readonly List<System.Windows.Controls.Primitives.Thumb> _edgeThumbs = [];
    private TerminalHwndHost? _terminalHost;
    private System.Windows.Controls.ComboBox? _termCombo;
    private TextBlock? _termStatus;
    private FrameworkElement? _termOverlay;
    private TextBlock? _termOverlayText;
    private bool _suppressTermCombo;
    private bool _locked;
    private bool _dragging, _pendingDrag;
    private const double DragThreshold = 4;
    private WpfPoint _dragStart;
    private double _startLeft, _startTop;
    // Sticky maximum width seen so far in this box — value/name widths only ever grow, so text/bars/graphs stop jittering horizontally on every update.
    private readonly StickyMax _stickyNameW = new();
    private readonly StickyMax _stickyValueW = new();
    private string? _lastSensorSignature;
    internal string LatestGraphDebug = "";

    public ComponentBox(BoxLayout layout, Canvas canvas, double fontSize, double barHeight, double graphHeight)
    {
        Layout = layout;
        _canvasParent = canvas;
        Background = B("BoxBackgroundBrush");
        BorderBrush = B("BoxBorderDimBrush");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        Width = layout.Width;
        Height = layout.Height;
        // No per-box drop shadow: it is the closest thing to a GPU-expense we have, and it doesn't read.

        var dock = new DockPanel();
        var header = new Border
        {
            Background = B("BoxHeaderBackgroundBrush"),
            CornerRadius = new CornerRadius(10, 10, 0, 0),
            Padding = new Thickness(8, 5, 8, 5),
            Cursor = WpfCursors.SizeAll,
        };
        _titleText = new TextBlock
        {
            Text = layout.Title,
            FontFamily = new WpfFontFamily("Segoe UI"),
            FontSize = 12,
            FontWeight = WpfFontWeights.SemiBold,
            Foreground = B("TextPrimaryBrush"),
            VerticalAlignment = WpfVerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        UpdateTitleText();
        var btnPick = new WpfButton { Content = "＋ Sensors", Background = WpfBrushes.Transparent, BorderThickness = new Thickness(0), Foreground = B("AccentBrush"), FontSize = 11, Cursor = WpfCursors.Hand, Margin = new Thickness(6, 0, 4, 0) };
        if (!string.IsNullOrEmpty(Layout.CustomKind))
        {
            btnPick.Content = "⚙";
            btnPick.ToolTip = "Custom box settings";
        }
        btnPick.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(Layout.CustomKind)) OpenCustomSettings();
            else PickSensors?.Invoke(this);
        };
        var btnX = new WpfButton { Content = "✕", Background = WpfBrushes.Transparent, BorderThickness = new Thickness(0), Foreground = B("TextMutedBrush"), FontSize = 11, Cursor = WpfCursors.Hand };
        btnX.Click += (_, _) => RemoveClicked?.Invoke(this);
        var hdrPanel = new Grid();
        hdrPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hdrPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hdrPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_titleText, 0);
        Grid.SetRow(_titleText, 0);
        hdrPanel.Children.Add(_titleText);
        btnPick.Margin = new Thickness(4, 0, 2, 0);
        Grid.SetColumn(btnPick, 1);
        Grid.SetRow(btnPick, 0);
        hdrPanel.Children.Add(btnPick);
        Grid.SetColumn(btnX, 2);
        Grid.SetRow(btnX, 0);
        hdrPanel.Children.Add(btnX);
        header.Child = hdrPanel;
        header.SetValue(DockPanel.DockProperty, Dock.Top);
        dock.Children.Add(header);
        var boxMenu = new System.Windows.Controls.ContextMenu();
        var miCopy = new System.Windows.Controls.MenuItem { Header = "Copy box as image" };
        miCopy.Click += (_, _) => CopyAsImage();
        var miRename = new System.Windows.Controls.MenuItem { Header = "Rename box…" };
        miRename.Click += (_, _) => OpenRenameDialog();
        boxMenu.Items.Add(miRename);
        boxMenu.Items.Add(miCopy);
        ContextMenu = boxMenu;

        header.MouseLeftButtonDown += Header_MouseLeftButtonDown;
        header.MouseLeftButtonUp += Header_MouseLeftButtonUp;
        header.MouseMove += Header_MouseMove;
        // WPF Border has no MouseDoubleClick event — double-click is detected via ClickCount in MouseLeftButtonDown.

        if (Layout.CustomKind == "app")
        {
            BuildTerminalContent(dock, fontSize);
        }
        else
        {
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = WpfHorizontalAlignment.Left, VerticalContentAlignment = WpfVerticalAlignment.Top, Content = _rows };
            _scroll = scroll;
            dock.Children.Add(scroll);
        }

        var grid = new Grid();
        grid.Children.Add(dock);
        // Invisible edge/corner resize like a native window: no square handle, every side resizes.
        foreach (var item in new (string dir, WpfHorizontalAlignment hAlign, WpfVerticalAlignment vAlign, double wdt, double hgt, System.Windows.Input.Cursor cursor)[]
        {
            ("N", WpfHorizontalAlignment.Stretch, WpfVerticalAlignment.Top, double.NaN, 7, WpfCursors.SizeNS),
            ("S", WpfHorizontalAlignment.Stretch, WpfVerticalAlignment.Bottom, double.NaN, 7, WpfCursors.SizeNS),
            ("W", WpfHorizontalAlignment.Left, WpfVerticalAlignment.Stretch, 7, double.NaN, WpfCursors.SizeWE),
            ("E", WpfHorizontalAlignment.Right, WpfVerticalAlignment.Stretch, 7, double.NaN, WpfCursors.SizeWE),
            ("NW", WpfHorizontalAlignment.Left, WpfVerticalAlignment.Top, 16, 16, WpfCursors.SizeNWSE),
            ("NE", WpfHorizontalAlignment.Right, WpfVerticalAlignment.Top, 16, 16, WpfCursors.SizeNESW),
            ("SW", WpfHorizontalAlignment.Left, WpfVerticalAlignment.Bottom, 16, 16, WpfCursors.SizeNESW),
            ("SE", WpfHorizontalAlignment.Right, WpfVerticalAlignment.Bottom, 16, 16, WpfCursors.SizeNWSE),
        })
        {
            string dir = item.dir; var hAlign = item.hAlign; var vAlign = item.vAlign; double wdt = item.wdt; double hgt = item.hgt; var cursor = item.cursor;
            var thumb = new System.Windows.Controls.Primitives.Thumb
            {
                Width = wdt, Height = hgt,
                HorizontalAlignment = hAlign, VerticalAlignment = vAlign,
                Cursor = cursor, Background = WpfBrushes.Transparent, TabIndex = -1,
            };
            thumb.Tag = dir;
            // Truly invisible but hit-testable: template is a transparent Border filling the thumb.
            var transparentBorder = new FrameworkElementFactory(typeof(Border));
            transparentBorder.SetValue(Border.BackgroundProperty, WpfBrushes.Transparent);
            transparentBorder.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            thumb.Template = new ControlTemplate(typeof(System.Windows.Controls.Primitives.Thumb))
            {
                VisualTree = transparentBorder,
            };
            thumb.DragDelta += EdgeThumb_DragDelta;
            thumb.DragCompleted += (_, _) => Resized?.Invoke(this);
            System.Windows.Controls.Panel.SetZIndex(thumb, 20);
            grid.Children.Add(thumb);
            _edgeThumbs.Add(thumb);
        }
        Child = grid;
    }

    private void BuildTerminalContent(DockPanel dock, double fontSize)
    {
        // Toolbar (top): terminal picker + Restart / Kill / Focus + status.
        var toolbar = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(6, 4, 6, 4), VerticalAlignment = WpfVerticalAlignment.Center };
        toolbar.SetValue(DockPanel.DockProperty, Dock.Top);
        _termCombo = new System.Windows.Controls.ComboBox { Width = 150, Height = 24, VerticalAlignment = WpfVerticalAlignment.Center, ToolTip = "Terminal to embed in this box", Background = B("ButtonBackgroundBrush"), Foreground = B("TextPrimaryBrush") };
        _termCombo.ItemContainerStyle = new Style(typeof(System.Windows.Controls.ComboBoxItem))
        {
            Setters =
            {
                new Setter(System.Windows.Controls.ComboBoxItem.BackgroundProperty, B("DialogBackgroundBrush")),
                new Setter(System.Windows.Controls.ComboBoxItem.ForegroundProperty, B("TextPrimaryBrush")),
            }
        };
        foreach (string id in AppHelper.PresetIds)
        {
            var item = new System.Windows.Controls.ComboBoxItem { Content = AppHelper.DisplayName(id), Tag = id, ToolTip = id, Background = B("DialogBackgroundBrush"), Foreground = B("TextPrimaryBrush") };
            _termCombo.Items.Add(item);
            if (id == Layout.AppId) _termCombo.SelectedItem = item;
        }
        // Legacy custom terminal path (e.g. C:\PuTTY\putty.exe): keep it selectable.
        if (!AppHelper.PresetIds.Contains(Layout.AppId) && !string.IsNullOrWhiteSpace(Layout.AppId))
        {
            var cur = new System.Windows.Controls.ComboBoxItem { Content = $"Custom: {System.IO.Path.GetFileName(Layout.AppId)}", Tag = Layout.AppId, ToolTip = Layout.AppId, Background = B("DialogBackgroundBrush"), Foreground = B("TextPrimaryBrush") };
            _termCombo.Items.Add(cur);
            _termCombo.SelectedItem = cur;
        }
        _termCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressTermCombo) return;
            if (_termCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem sel) return;
            string? id = sel.Tag?.ToString(); if (string.IsNullOrWhiteSpace(id)) return;
            if (id == Layout.AppId) return;
            Layout.AppId = id;
            Layout.Title = AppHelper.DisplayName(id);
            UpdateTitleText();
            LayoutChanged?.Invoke(this);
            if (_terminalHost is not null)
            {
                ShowTerminalHost();
                TerminalHost.Restart(Layout, _terminalHost);
                RefreshTerminalOverlay();
            }
        };

        var btnRestart = new WpfButton { Content = "↻ Restart", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 2, 6, 2), FontSize = 11, Cursor = WpfCursors.Hand, ToolTip = "Restart embedded terminal" };
        btnRestart.Click += (_, _) =>
        {
            if (_terminalHost is null) return;
            ShowTerminalHost();
            TerminalHost.Restart(Layout, _terminalHost);
            RefreshTerminalOverlay();
        };
        var btnKill = new WpfButton { Content = "■ Stop", Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(6, 2, 6, 2), FontSize = 11, Cursor = WpfCursors.Hand, ToolTip = "Stop embedded terminal" };
        btnKill.Click += (_, _) =>
        {
            TerminalHost.Kill(Layout);
            RefreshTerminalOverlay();
        };
        var btnFocus = new WpfButton { Content = "⤢ Focus", Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(6, 2, 6, 2), FontSize = 11, Cursor = WpfCursors.Hand, ToolTip = "Bring embedded terminal to front" };
        btnFocus.Click += (_, _) => TerminalHost.Focus(Layout);
        _termStatus = new TextBlock { VerticalAlignment = WpfVerticalAlignment.Center, Margin = new Thickness(0), FontFamily = new WpfFontFamily("Segoe UI"), FontSize = Math.Max(9, fontSize - 1), Foreground = B("TextMutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis, Text = "Starting…" };
        // Footer (bottom): compact terminal status strip; always docked at the bottom, not affected by host box resizing.
        var footer = new System.Windows.Controls.Border
        {
            Background = B("BoxHeaderBackgroundBrush"),
            BorderBrush = B("BoxBorderDimBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(0, 0, 8, 8),
            Padding = new Thickness(8, 2, 8, 2),
            Child = _termStatus,
        };
        footer.SetValue(DockPanel.DockProperty, Dock.Bottom);

        toolbar.Children.Add(_termCombo);
        toolbar.Children.Add(btnRestart);
        toolbar.Children.Add(btnKill);
        toolbar.Children.Add(btnFocus);
        dock.Children.Add(toolbar);

        // Host area (fills box): HwndHost + overlay placeholder.
        var hostBorder = new System.Windows.Controls.Border { Background = WpfBrushes.Black, BorderBrush = B("BoxBorderDimBrush"), BorderThickness = new Thickness(1), Margin = new Thickness(6, 0, 6, 6), CornerRadius = new CornerRadius(0, 0, 8, 8), ClipToBounds = true, Visibility = Visibility.Visible };
        var hostGrid = new Grid();
        _terminalHost = new TerminalHwndHost { HorizontalAlignment = WpfHorizontalAlignment.Stretch, VerticalAlignment = WpfVerticalAlignment.Stretch, ToolTip = "Embedded terminal" };
        hostGrid.Children.Add(_terminalHost);

        var overlayPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Vertical, HorizontalAlignment = WpfHorizontalAlignment.Center, VerticalAlignment = WpfVerticalAlignment.Center, Margin = new Thickness(12) };
        _termOverlayText = new TextBlock { Text = TerminalHost.GetStatus(Layout), FontFamily = new WpfFontFamily("Segoe UI"), FontSize = Math.Max(10, fontSize), Foreground = B("TextPrimaryBrush"), TextWrapping = TextWrapping.Wrap, HorizontalAlignment = WpfHorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
        var btnStart = new WpfButton { Content = "▶ Start terminal", Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(8, 4, 8, 4), FontSize = 11, Cursor = WpfCursors.Hand, HorizontalAlignment = WpfHorizontalAlignment.Center, ToolTip = "Start terminal inside this box" };
        btnStart.Click += (_, _) =>
        {
            if (_terminalHost is null) return;
            ShowTerminalHost();
            TerminalHost.Restart(Layout, _terminalHost);
            RefreshTerminalOverlay();
        };
        var hint = new TextBlock { Text = "Terminal stays inside this box when moved/resized.", FontFamily = new WpfFontFamily("Segoe UI"), FontSize = Math.Max(9, fontSize - 1), Foreground = B("TextMutedBrush"), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = WpfHorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        overlayPanel.Children.Add(_termOverlayText);
        overlayPanel.Children.Add(btnStart);
        overlayPanel.Children.Add(hint);
        var overlay = new System.Windows.Controls.Border { Background = B("BoxBackgroundBrush"), Child = overlayPanel, Visibility = Visibility.Visible };
        System.Windows.Controls.Panel.SetZIndex(overlay, 10);
        _termOverlay = overlay;
        hostGrid.Children.Add(overlay);
        hostBorder.Child = hostGrid;
        dock.Children.Add(footer);
        dock.Children.Add(hostBorder);

        Loaded += (_, _) =>
        {
            if (_terminalHost is null) return;
            var host = _terminalHost;
            try
            {
                // Defer until after HwndHost.BuildWindowCore has run (host HWND exists).
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    try
                    {
                        if (!ReferenceEquals(_terminalHost, host)) return;
                        ShowTerminalHost();
                        TerminalHost.AttachOrStart(Layout, host);
                        RefreshTerminalOverlay();
                    }
                    catch (Exception ex) { App.LogError(ex); }
                }));
            }
            catch (Exception ex) { App.LogError(ex); }
        };
        Unloaded += (_, _) =>
        {
            // Keep the process alive across rebuild/hide; just detach so destroying the
            // old host HWND doesn't kill the terminal window.
            try { TerminalHost.Detach(Layout); } catch { }
        };
        SizeChanged += (_, _) =>
        {
            if (_terminalHost is null) return;
            try { TerminalHost.Position(Layout, _terminalHost); } catch { }
        };
    }

    private void ShowTerminalHost()
    {
        if (_terminalHost is not null) _terminalHost.Visibility = Visibility.Visible;
        if (_termOverlay is not null) _termOverlay.Visibility = Visibility.Collapsed;
        if (_termStatus is not null) _termStatus.Text = TerminalHost.GetStatus(Layout);
    }

    internal void RefreshTerminalOverlay()
    {
        if (_terminalHost is null || _termOverlay is null) return;
        try
        {
            bool running = TerminalHost.IsRunning(Layout);
            bool starting = !running && TerminalHost.IsStarting(Layout);
            if (running || starting)
            {
                _terminalHost.Visibility = Visibility.Visible;
                _termOverlay.Visibility = Visibility.Collapsed;
                // Keep the Win32 terminal confined: re-parent back if it drifted to the desktop.
                if (running) { try { TerminalHost.Position(Layout, _terminalHost); } catch { } }
            }
            else
            {
                _terminalHost.Visibility = Visibility.Collapsed;
                _termOverlay.Visibility = Visibility.Visible;
                if (_termOverlayText is not null) _termOverlayText.Text = TerminalHost.GetStatus(Layout);
            }
            if (_termStatus is not null) _termStatus.Text = TerminalHost.GetStatus(Layout);
        }
        catch { }
    }

    private void UpdateTitleText()
    {
        string title = Layout.Title ?? "";
        _titleText.Text = title;
        if (string.IsNullOrWhiteSpace(title))
            _titleText.ToolTip = string.IsNullOrWhiteSpace(Layout.SourceTitle)
                ? "(empty title) — double-click to rename"
                : $"(empty title) — created as “{Layout.SourceTitle}” — double-click to rename";
        else
            _titleText.ToolTip = $"{title} — double-click to rename";
    }

    public void SetLocked(bool locked)
    {
        _locked = locked;
        foreach (var t in _edgeThumbs) t.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
    }

    public void CopyAsImage()
    {
        try
        {
            UpdateLayout();
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(this);
            System.Windows.Clipboard.SetImage(rtb);
            MessageBox.Show("Box copied as image to clipboard.", "SystemInfo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Copy image", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void SyncFromLayout()
    {
        Width = Layout.Width;
        Height = Layout.Height;
        if (Layout.CustomKind == "app" && _terminalHost is not null)
        {
            try { TerminalHost.Position(Layout, _terminalHost); } catch { }
        }
        UpdateTitleText();
    }

    /// <summary>
    /// Measure the natural required size for this box given current content and fonts — used by Compact boxes.
    /// Natural width is computed from actual text measurements; never causes ellipsis.
    /// </summary>
    public (double Width, double Height) MeasureNaturalSize(IReadOnlyDictionary<string, SensorDescriptor> catalog, IReadOnlyDictionary<string, double?> values, IReadOnlyDictionary<string, SensorHistory> histories, double fontSize, double barHeight, double graphHeight)
    {
        double naturalW = MeasureNaturalWidth(catalog, values, fontSize);
        double naturalH = MeasureNaturalHeight(catalog, fontSize, barHeight, graphHeight);
        return (naturalW, naturalH);
    }

    /// <summary>Minimum width that renders all text (name, value, title, terminal) with no ellipsis.</summary>
    public double MeasureNaturalWidth(IReadOnlyDictionary<string, SensorDescriptor> catalog, IReadOnlyDictionary<string, double?> values, double fontSize)
    {
        double pad = 24; // Border(6*2) + _rows margin(6*2)

        double rowW = 200;
        bool anyText = false, anyBar = false, anyGraph = false;
        double nameMax = 0;
        double valMax = 0;

        foreach (var id in Layout.SensorIds)
        {
            int mode = GetMode(id, catalog.TryGetValue(id, out var d) ? d : null);
            if (ScaleHelper.ModeHasText(mode)) anyText = true;
            if (ScaleHelper.ModeHasBar(mode)) anyBar = true;
            if (ScaleHelper.ModeHasGraph(mode)) anyGraph = true;

            string name = ScaleHelper.ShortSensorLabel(d) is { Length: > 0 } shortName ? shortName : id;
            double nameW = ScaleHelper.MeasureTextWidth(name, fontSize) + 8;
            nameMax = Math.Max(nameMax, nameW);

            if (ScaleHelper.ModeHasText(mode))
            {
                values.TryGetValue(id, out double? v);
                string sample = d is null ? "9999.99" : ScaleHelper.Format(v, d, Layout.NetworkSpeedUnit);
                double valW = ScaleHelper.MeasureTextWidth(sample, fontSize + 1, semibold: true) + 8;
                valMax = Math.Max(valMax, valW);
            }
        }

        if (Layout.SensorIds.Count > 0)
        {
            double barW = anyBar ? ScaleHelper.BarMinWidth : 0;
            double valueW = anyText ? valMax : 0;
            rowW = pad + nameMax + valueW + (anyText && anyBar ? 2 : 0) + barW;
            if (anyGraph)
                rowW = Math.Max(rowW, 140); // graph minimum width
        }

        // Header title must fit — measure the ACTUAL title text so boxes expand to show it fully (no ellipsis).
        double titleW = ScaleHelper.MeasureTextWidth(Layout.Title, 12, semibold: true) + 8 /*start pad*/ + 4 /*newline pad*/ + 58 /*+ Sensors*/ + 50 /*+ gap + eye*/ + 34 /*+ gap + close*/;

        // Terminal toolbar natural width (if any).
        double toolbarW = Layout.CustomKind == "app" ? 380 : 0;

        double w = Math.Max(rowW, Math.Max(titleW, toolbarW));
        w = Math.Clamp(w, 200, 2000);
        return Math.Ceiling(w);
    }

    public double MeasureNaturalHeight(IReadOnlyDictionary<string, SensorDescriptor> catalog, double fontSize, double barHeight, double graphHeight)
    {
        double border = 2; // BorderThickness 1*2
        // Force WPF to compute natural heights using current width.
        double innerWidth = Math.Max(120, Width - 12);
        DockPanel? dock = GetDockPanel();
        if (dock is null) return Height;
        dock.Measure(new System.Windows.Size(innerWidth, double.MaxValue));
        double natural = dock.DesiredSize.Height + border + 12; // DockPanel margin 6*2
        if (Layout.CustomKind == "app")
            natural = Math.Max(natural, 160); // toolbar + terminal min
        return Math.Ceiling(natural);
    }

    private DockPanel? GetDockPanel()
    {
        var g = Child as Grid;
        if (g is null) return null;
        foreach (var c in g.Children) if (c is DockPanel dp) return dp;
        return null;
    }

    private void Header_MouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (_locked || e.ChangedButton != MouseButton.Left) return;
        if (IsSourceButton(e.OriginalSource)) return;
        if (e.ClickCount == 2)
        {
            OpenRenameDialog();
            e.Handled = true;
            return;
        }
        _pendingDrag = true;
        _dragStart = e.GetPosition(_canvasParent);
        _startLeft = Canvas.GetLeft(this);
        _startTop = Canvas.GetTop(this);
        try { (sender as FrameworkElement)?.CaptureMouse(); } catch { }
        e.Handled = true;
    }

    private void Header_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_locked) return;
        if (!_pendingDrag && !_dragging) return;
        var pos = e.GetPosition(_canvasParent);
        double dx = pos.X - _dragStart.X;
        double dy = pos.Y - _dragStart.Y;
        double maxX = Math.Max(0, _canvasParent.ActualWidth - Layout.Width);
        double maxY = Math.Max(0, _canvasParent.ActualHeight - Layout.Height);
        if (!_dragging)
        {
            if (dx * dx + dy * dy < DragThreshold * DragThreshold) return; // still a click, not a drag
            _pendingDrag = false;
            _dragging = true;
        }
        double nx = Math.Max(0, _startLeft + dx);
        double ny = Math.Max(0, _startTop + dy);
        nx = Math.Clamp(nx, 0, maxX);
        ny = Math.Clamp(ny, 0, maxY);
        // Snap together with minimal space (4px gap) to other boxes and canvas edges.
        nx = Snap(nx, ny, isX: true);
        ny = Snap(nx, ny, isX: false);
        nx = Math.Clamp(nx, 0, maxX);
        ny = Math.Clamp(ny, 0, maxY);
        Canvas.SetLeft(this, nx);
        Canvas.SetTop(this, ny);
        Layout.X = nx; Layout.Y = ny;
    }

    private const double SnapGap = 4;
    private const double SnapThresh = 8;

    /// <summary>Sibling edges once per drag event; avoids per-mousemove allocations-of-target-arrays.</summary>
    private List<(double start, double end)> SiblingEdges(bool isX)
    {
        var list = new List<(double, double)>(_canvasParent.Children.OfType<ComponentBox>().Count());
        foreach (var child in _canvasParent.Children)
        {
            if (child is not ComponentBox o || ReferenceEquals(o, this)) continue;
            double s = isX ? Canvas.GetLeft(o) : Canvas.GetTop(o);
            double size = isX ? o.Layout.Width : o.Layout.Height;
            list.Add((s, s + size));
        }
        return list;
    }

    private static double NearestSnap(List<double> targets, double candidate)
    {
        foreach (double t in targets)
        {
            if (t < 0) continue;
            if (Math.Abs(candidate - t) <= SnapThresh) return t;
        }
        return candidate;
    }

    private double Snap(double x, double y, bool isX)
    {
        double candidate = isX ? x : y;
        double canvasSize = isX ? _canvasParent.ActualWidth : _canvasParent.ActualHeight;
        double boxSize = isX ? Layout.Width : Layout.Height;
        double maxPos = Math.Max(0, canvasSize - boxSize);
        var targets = new List<double> { 0, maxPos };
        foreach (var (s, e) in SiblingEdges(isX))
        {
            targets.Add(s);
            targets.Add(e + SnapGap);
            targets.Add(s - boxSize - SnapGap);
        }
        return NearestSnap(targets, candidate);
    }

    private void Header_MouseLeftButtonUp(object sender, WpfMouseButtonEventArgs e)
    {
        bool wasDragging = _dragging;
        _pendingDrag = false;
        _dragging = false;
        if (!wasDragging) return;
        try { (sender as FrameworkElement)?.ReleaseMouseCapture(); } catch { }
        Moved?.Invoke(this);
    }

    private void OpenRenameDialog()
    {
        if (_locked) return;
        var win = new Window { Title = "Rename box", Width = 436, Height = 170, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = WpfApplication.Current?.MainWindow, ResizeMode = ResizeMode.NoResize };
        var tb = new System.Windows.Controls.TextBox { Text = Layout.Title, Margin = new Thickness(12, 4, 12, 4), FontFamily = new WpfFontFamily("Segoe UI"), HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch, VerticalAlignment = System.Windows.VerticalAlignment.Center };
        var ok = new WpfButton { Content = "OK", Width = 80, Height = 30, Margin = new Thickness(8, 0, 0, 0) };
        var auto = new WpfButton { Content = "Reset to automatic title", Width = 190, Height = 30, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        ok.IsDefault = true;
        ok.IsCancel = false;
        var panel = new Grid { Margin = new Thickness(4) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) }); // label
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // textbox
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) }); // buttons
        var label = new TextBlock { Text = "Box title:", FontFamily = new WpfFontFamily("Segoe UI"), Foreground = B("TextPrimaryBrush"), Margin = new Thickness(12, 8, 12, 2) };
        Grid.SetRow(label, 0); panel.Children.Add(label);
        tb.Margin = new Thickness(12, 0, 12, 0); tb.Padding = new Thickness(6, 4, 6, 4);
        tb.Height = 30; tb.VerticalContentAlignment = System.Windows.VerticalAlignment.Center;
        Grid.SetRow(tb, 1); panel.Children.Add(tb);
        var buttonRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 8, 12, 12) };
        buttonRow.Children.Add(auto);
        buttonRow.Children.Add(ok);
        Grid.SetRow(buttonRow, 2); panel.Children.Add(buttonRow);
        win.Content = panel;
        win.Background = B("DialogBackgroundBrush");
        tb.Focus();
        auto.Click += (_, _) => { tb.Text = DeriveAuto(); Apply(); };
        string DeriveAuto() => LayoutStore.DeriveAutoTitle(Layout, StaticComponents);
        void Apply()
        {
            string value = tb.Text.Trim();  // allow completely empty — erases the title text
            Layout.Title = value;
            UpdateTitleText();
            LayoutChanged?.Invoke(this);
            win.Close();
        }
        ok.Click += (_, _) => Apply();
        tb.KeyDown += (_, ke) => { if (ke.Key == Key.Enter) Apply(); };
        win.ShowDialog();
    }

    private void EdgeThumb_DragDelta(object sender, WpfDragDeltaEventArgs e)
    {
        if (_locked) return;
        string dir = (sender as FrameworkElement)?.Tag?.ToString() ?? "SE";
        double dx = e.HorizontalChange, dy = e.VerticalChange;
        double x = Layout.X, y = Layout.Y, w = Layout.Width, h = Layout.Height;
        double right = x + w, bottom = y + h;
        double canvasW = Math.Max(200, _canvasParent.ActualWidth);
        double canvasH = Math.Max(90, _canvasParent.ActualHeight);
        if (dir.Contains("E"))
        {
            double newW = Math.Clamp(w + dx, 60, Math.Min(2000, canvasW - x));
            newW = SnapResize(newW, isX: true);
            w = Math.Clamp(newW, 60, Math.Min(2000, canvasW - x));
        }
        if (dir.Contains("W"))
        {
            double newX = Math.Clamp(x + dx, 0, right - 60);
            newX = SnapLeadEdge(newX, isX: true);
            double newW = right - newX;
            if (newW >= 60 && newW <= 2000) { x = newX; w = newW; }
        }
        if (dir.Contains("S"))
        {
            double newH = Math.Clamp(h + dy, 60, Math.Min(2000, canvasH - y));
            newH = SnapResize(newH, isX: false);
            h = Math.Clamp(newH, 60, Math.Min(2000, canvasH - y));
        }
        if (dir.Contains("N"))
        {
            double newY = Math.Clamp(y + dy, 0, bottom - 60);
            newY = SnapLeadEdge(newY, isX: false);
            double newH = bottom - newY;
            if (newH >= 60 && newH <= 2000) { y = newY; h = newH; }
        }
        Layout.X = x; Layout.Y = y; Layout.Width = w; Layout.Height = h;
        Canvas.SetLeft(this, x); Canvas.SetTop(this, y);
        Width = w; Height = h;
        if (Layout.CustomKind == "app" && _terminalHost is not null)
        {
            try { TerminalHost.Position(Layout, _terminalHost); } catch { }
        }
        UpdateTitleText();
    }

    private double SnapLeadEdge(double leadPos, bool isX)
    {
        var targets = new List<double> { 0 };
        foreach (var (s, e) in SiblingEdges(isX))
        {
            targets.Add(s);
            targets.Add(e + SnapGap);
            targets.Add(e);
        }
        return NearestSnap(targets, leadPos);
    }

    private double SnapResize(double newValue, bool isX)
    {
        double origin = isX ? Layout.X : Layout.Y;
        double candidateEdge = origin + newValue;
        var targets = new List<double>(8);
        foreach (var (s, e) in SiblingEdges(isX))
        {
            targets.Add(s - SnapGap);
            targets.Add(e + SnapGap);
            targets.Add(s);
            targets.Add(e);
        }
        foreach (double t in targets)
        {
            if (Math.Abs(candidateEdge - t) <= SnapThresh) return t - origin;
        }
        return newValue;
    }

    /// <summary>Unsubscribe every cached row from the process-wide rendering event and detach them from the visual tree. Called on box rebuilds/removal and before box destruction.</summary>
    public void Detach()
    {
        foreach (var row in _rowCache.Values)
            row.UnsubscribeRenderingFrame();
        _rowCache.Clear();
        _rows.Children.Clear();
    }

    public void UpdateRows(IReadOnlyDictionary<string, SensorDescriptor> catalog, IReadOnlyDictionary<string, double?> values, IReadOnlyDictionary<string, SensorHistory> histories, double fontSize, double barHeight, double graphHeight)
    {
        if (Layout.CustomKind == "app")
        {
            // Hosted HWND must persist across 1s refreshes — never rebuild terminal UI here.
            RefreshTerminalOverlay();
            return;
        }
        // Custom (processes/power/app) boxes: rebuild on every frame — they're cheap and rarely change.
        if (!string.IsNullOrEmpty(Layout.CustomKind))
        {
            Detach();
            UpdateCustomRows(fontSize, barHeight);
            return;
        }
        // Sensor boxes: layout is usually stable between refreshes; reuse rows in-place.
        bool stable = _rows.Children.Count == Layout.SensorIds.Count
                      && _rowCache.Count == Layout.SensorIds.Count;
        if (stable)
        {
            foreach (string id in Layout.SensorIds)
                if (!_rowCache.ContainsKey(id)) { stable = false; break; }
        }
        if (!stable) Detach();
        // Recompute sticky widths from scratch when the sensor set changes (add/remove/reorder) — never keep stale maxima.
        string? sig = Layout.SensorIds.Count == 0 ? "" : string.Join("\u0001", Layout.SensorIds);
        if (sig != _lastSensorSignature) { _lastSensorSignature = sig; _stickyNameW.Reset(); _stickyValueW.Reset(); }
        if (Layout.SensorIds.Count == 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = "No sensors — use '＋ Sensors' to pick rows and optional per-sensor graphs.",
                FontFamily = new WpfFontFamily("Segoe UI"), FontSize = 10,
                Foreground = B("TextMutedBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }
        // Uniform row sizing: name/value get a capped share, bar fills the rest (largest possible, same for every row).
        // Single pass over SensorIds computing all mode flags (avoids three LINQ passes per box per tick).
        bool anyText = false, anyBar = false, anyGraph = false;
        foreach (string id in Layout.SensorIds)
        {
            int mode = GetMode(id, catalog.TryGetValue(id, out var dCat) ? dCat : null);
            if (ScaleHelper.ModeHasText(mode)) anyText = true;
            if (ScaleHelper.ModeHasBar(mode)) anyBar = true;
            if (ScaleHelper.ModeHasGraph(mode)) anyGraph = true;
            if (anyText && anyBar && anyGraph) break;
        }
        // inner is derived ONLY from the box's logical width plus fixed insets — never from a live
        // ScrollViewer measurement. The Auto scrollbar appearing/disappearing must not be able to
        // change row widths on a tick (that was the residual horizontal jitter). A constant
        // scrollbar reservation is applied instead: a few spare pixels when not needed, exact fit when shown.
        const double ScrollBarReserve = 17;
        double inner = Math.Max(120, Math.Floor(Layout.Width) - 14 - 12 - ScrollBarReserve);
        // Compute name width from the ACTUAL longest sensor name in this box — no content clipping allowed.
        double nameMin = 0;
        int maxValLen = 0;
        if (anyText)
        {
            foreach (string id in Layout.SensorIds)
            {
                catalog.TryGetValue(id, out SensorDescriptor? d2);
                string nm = ScaleHelper.ShortSensorLabel(d2) is { Length: > 0 } sn ? sn : id;
                nameMin = Math.Max(nameMin, ScaleHelper.MeasureTextWidth(nm, fontSize) + 8);
                if (!ScaleHelper.ModeHasText(GetMode(id, d2))) continue;
                values.TryGetValue(id, out double? vv);
                string s = d2 is null ? "—" : ScaleHelper.Format(vv, d2, Layout.NetworkSpeedUnit);
                maxValLen = Math.Max(maxValLen, s.Length);
            }
        }
        _stickyNameW.Add(nameMin);
        _stickyValueW.Add(Math.Max(44, (fontSize + 1) * (maxValLen * 0.58 + 0.5)));
        double nameW = anyText ? Math.Floor(Math.Clamp(_stickyNameW.Value, 60, 260)) : 0;
        double valueW = anyText ? Math.Floor(Math.Clamp(_stickyValueW.Value, 44, 160)) : 0;
        // Keep the whole row inside the box at narrow widths (fixes right-side cutoff).
        double minBar = anyBar ? ScaleHelper.BarMinWidth : 0;
        if (inner < nameW + valueW + 12 + minBar)
        {
            // Shrink only the name toward its minimum, never below readable width
            nameW = Math.Floor(Math.Max(60, inner - valueW - 12 - minBar));
        }
        double barWidth = anyBar ? Math.Floor(Math.Max(30, inner - nameW - (anyText ? valueW + 6 : 0) - 6)) : 0;
        var keep = new HashSet<string>(StringComparer.Ordinal);
        double boxWidth = inner + 16;
        foreach (string id in Layout.SensorIds)
        {
            catalog.TryGetValue(id, out SensorDescriptor? d);
            values.TryGetValue(id, out double? v);
            histories.TryGetValue(id, out SensorHistory? h);
            int mode = GetMode(id, d);
            bool showText = ScaleHelper.ModeHasText(mode);
            bool showBar = ScaleHelper.ModeHasBar(mode);
            bool showGraph = ScaleHelper.ModeHasGraph(mode);
            SensorRow.RowLayoutKey keyArgs = SensorRow.KeyOf(showText, showBar, showGraph, fontSize, barHeight, nameW, valueW, barWidth, boxWidth);
            if (_rowCache.TryGetValue(id, out var cached) && cached.KeyArgs == keyArgs)
            {
                cached.UpdateFrom(v, h, Layout.NetworkSpeedUnit);
                if (!stable) _rows.Children.Add(cached);
            }
            else
            {
                if (_rowCache.TryGetValue(id, out var evicted)) evicted.UnsubscribeRenderingFrame();
                var row = new SensorRow(id, d, v, h, boxWidth, fontSize, barHeight, showText, showBar, showGraph, barWidth, nameW, valueW, Layout.NetworkSpeedUnit);
                _rowCache[id] = row;
                _rows.Children.Add(row);
            }
            keep.Add(id);
        }
        // Drop cache entries for sensors no longer shown on this box.
        List<string>? staleKeys = null;
        foreach (string k in _rowCache.Keys)
        {
            if (!keep.Contains(k))
            {
                staleKeys ??= new();
                staleKeys.Add(k);
            }
        }
        if (staleKeys is not null)
        foreach (string stale in staleKeys)
        {
            if (_rowCache.TryGetValue(stale, out var staleRow)) staleRow.UnsubscribeRenderingFrame();
            _rowCache.Remove(stale);
        }
        if (keep.Count == 0) { _stickyNameW.Reset(); _stickyValueW.Reset(); }
    }


    private int GetMode(string id, SensorDescriptor? d)
    {
        if (Layout.SensorModes.TryGetValue(id, out int m) && m != 0) return m;
        if (d is null) return (int)(RowMode.Text | RowMode.Bar);
        return ScaleHelper.DefaultMode(d, false);
    }

    private void UpdateCustomRows(double fontSize, double barHeight)
    {
        if (Layout.CustomKind == "processes")
        {
            var top = ProcessHelper.GetTop(Layout.ProcessLimit, Layout.ProcessSort);
            foreach (var p in top)
            {
                double heatPct = p.HeatPct;
                // Frozen, cached — no allocation per refresh.
                var heat = ThemeService.HeatBrushTranslucent(heatPct);
                var outer = new Border { Background = heat, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0.5, 0, 0.5), Width = Math.Max(200, Layout.Width - 16) };
                var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(4, 1, 4, 1) };
                row.Children.Add(new TextBlock { Text = p.Name, FontFamily = new WpfFontFamily("Segoe UI"), FontSize = fontSize, Foreground = B("TextPrimaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis, Width = 150, VerticalAlignment = WpfVerticalAlignment.Center });
                string val = Layout.ProcessSort switch { "Cpu" => FormatPct(p.CpuPercent), "Ram" => FormatMB(p.RamMB), "Threads" => p.Threads.ToString(), _ => FormatPct(p.CpuPercent) };
                row.Children.Add(new TextBlock { Text = val, FontFamily = new WpfFontFamily("Segoe UI"), FontSize = fontSize + 1, FontWeight = WpfFontWeights.SemiBold, Foreground = B("AccentBrush"), Width = 76, HorizontalAlignment = WpfHorizontalAlignment.Right, VerticalAlignment = WpfVerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
                outer.Child = row;
                _rows.Children.Add(outer);
            }
        }
        else if (Layout.CustomKind == "power")
        {
            foreach (var (label, args) in new[] { ("Restart", "/r /t 30"), ("Shut down", "/s /t 30"), ("Sleep", "sleep"), ("Lock workstation", "lock"), ("Log off", "/l"), ("Abort shutdown", "/a") })
            {
                var btn = new WpfButton { Content = label, Margin = new Thickness(0, 0, 0, 1.5), FontFamily = new WpfFontFamily("Segoe UI"), FontSize = 11, Background = new WpfSolidColorBrush(WpfColor.FromRgb(0x1C, 0x33, 0x4A)), BorderBrush = new WpfSolidColorBrush(WpfColor.FromRgb(0x3E, 0x6A, 0x94)), Foreground = new WpfSolidColorBrush(WpfColor.FromRgb(0xEA, 0xF6, 0xFF)), Padding = new Thickness(6, 4, 6, 4), Cursor = WpfCursors.Hand };
                btn.Click += (_, _) => ConfirmAndRunPower(label, args);
                _rows.Children.Add(btn);
            }
        }
        else if (Layout.CustomKind == "app")
        {
            // Hosted by BuildTerminalContent — UpdateRows handles status; never rebuild here or the HWND is lost.
            RefreshTerminalOverlay();
        }
    }

    private static string FormatPct(double v) => $"{v:0}%";
    private static string FormatMB(long mb) => mb >= 1024 ? $"{mb / 1024.0:0.0} GB" : $"{mb} MB";

    private void ConfirmAndRunPower(string label, string key)
    {
        if (_locked) return;
        var win = new Window { Title = $"Confirm: {label}", Width = 380, Height = 170, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = WpfApplication.Current?.MainWindow, Background = B("DialogBackgroundBrush") };
        var txt = new TextBlock { Text = $"Are you sure you want to {label.ToLowerInvariant()}?\n\nThis will close apps. Shutdown/restart use a 30-second delay — use \"Abort shutdown\" to cancel.", FontFamily = new WpfFontFamily("Segoe UI"), Foreground = B("TextPrimaryBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 12, 12, 8) };
        var cancel = new WpfButton { Content = "Cancel", Width = 80, Margin = new Thickness(0, 0, 6, 0), Background = B("ButtonBackgroundBrush"), BorderBrush = B("ButtonBorderBrush"), Foreground = B("TextPrimaryBrush") };
        var confirm = new WpfButton { Content = "Confirm", Width = 80, Background = new WpfSolidColorBrush(WpfColor.FromRgb(0x7A, 0x2E, 0x2E)), BorderBrush = new WpfSolidColorBrush(WpfColor.FromRgb(0xFF, 0x5A, 0x4E)), Foreground = WpfBrushes.White, FontWeight = WpfFontWeights.SemiBold };
        var bar = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = WpfHorizontalAlignment.Right, Margin = new Thickness(12, 4, 12, 12) };
        bar.Children.Add(cancel); bar.Children.Add(confirm);
        var panel = new StackPanel();
        panel.Children.Add(txt); panel.Children.Add(bar);
        win.Content = panel;
        bool confirmed = false;
        confirm.Click += (_, _) => { confirmed = true; win.Close(); };
        cancel.Click += (_, _) => win.Close();
        // Require explicit Confirm click (Enter on Cancel only); safety: no default OK.
        win.ShowDialog();
        if (!confirmed) return;
        try
        {
            if (key == "sleep") Process.Start(new ProcessStartInfo("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0") { UseShellExecute = true });
            else if (key == "lock") Process.Start(new ProcessStartInfo("rundll32.exe", "user32.dll,LockWorkStation") { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo("shutdown", key) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed: {ex.Message}", "Power", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenCustomSettings()
    {
        if (_locked) return;
        if (Layout.CustomKind == "processes") OpenProcessSettings();
        else if (Layout.CustomKind == "app") OpenAppSettings();
        // power box has no settings.
    }

    private void OpenProcessSettings()
    {
        var win = new Window { Title = "Process box settings", Width = 360, Height = 220, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = WpfApplication.Current?.MainWindow, Background = B("DialogBackgroundBrush") };
        var sortBox = new System.Windows.Controls.ComboBox { Margin = new Thickness(12, 4, 12, 8), Background = B("ButtonBackgroundBrush"), Foreground = B("TextPrimaryBrush") };
        sortBox.ItemContainerStyle = new Style(typeof(System.Windows.Controls.ComboBoxItem)) { Setters = { new Setter(System.Windows.Controls.ComboBoxItem.BackgroundProperty, B("DialogBackgroundBrush")), new Setter(System.Windows.Controls.ComboBoxItem.ForegroundProperty, B("TextPrimaryBrush")) } };
        sortBox.Items.Add("Cpu"); sortBox.Items.Add("Ram"); sortBox.Items.Add("Name"); sortBox.Items.Add("Threads");
        sortBox.SelectedItem = Layout.ProcessSort;
        var limitBox = new System.Windows.Controls.ComboBox { Margin = new Thickness(12, 4, 12, 12), Background = B("ButtonBackgroundBrush"), Foreground = B("TextPrimaryBrush") };
        limitBox.ItemContainerStyle = new Style(typeof(System.Windows.Controls.ComboBoxItem)) { Setters = { new Setter(System.Windows.Controls.ComboBoxItem.BackgroundProperty, B("DialogBackgroundBrush")), new Setter(System.Windows.Controls.ComboBoxItem.ForegroundProperty, B("TextPrimaryBrush")) } };
        foreach (int n in new[] { 5, 10, 15, 20 }) limitBox.Items.Add(n);
        limitBox.SelectedItem = Layout.ProcessLimit;
        var ok = new WpfButton { Content = "OK", Width = 80, Margin = new Thickness(12, 4, 12, 12) };
        var panel = new StackPanel { Margin = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = "Sort processes by:", FontFamily = new WpfFontFamily("Segoe UI"), Foreground = B("TextPrimaryBrush"), Margin = new Thickness(12, 10, 12, 4) });
        panel.Children.Add(sortBox);
        panel.Children.Add(new TextBlock { Text = "Show top N:", FontFamily = new WpfFontFamily("Segoe UI"), Foreground = B("TextPrimaryBrush"), Margin = new Thickness(12, 0, 12, 4) });
        panel.Children.Add(limitBox);
        panel.Children.Add(ok);
        win.Content = panel;
        ok.Click += (_, _) =>
        {
            if (sortBox.SelectedItem is string s) Layout.ProcessSort = s;
            if (limitBox.SelectedItem is int n) Layout.ProcessLimit = n;
            LayoutChanged?.Invoke(this);
            win.Close();
        };
        win.ShowDialog();
    }

    private void OpenAppSettings()
    {
        var win = new Window { Title = "Terminal box settings (terminals only)", Width = 500, Height = 290, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = WpfApplication.Current?.MainWindow, Background = B("DialogBackgroundBrush") };
        var preset = new System.Windows.Controls.ComboBox { Margin = new Thickness(12, 4, 12, 8), Background = B("ButtonBackgroundBrush"), Foreground = B("TextPrimaryBrush") };
        preset.ItemContainerStyle = new Style(typeof(System.Windows.Controls.ComboBoxItem)) { Setters = { new Setter(System.Windows.Controls.ComboBoxItem.BackgroundProperty, B("DialogBackgroundBrush")), new Setter(System.Windows.Controls.ComboBoxItem.ForegroundProperty, B("TextPrimaryBrush")) } };
        foreach (var id in AppHelper.PresetIds)
        {
            var item = new System.Windows.Controls.ComboBoxItem { Content = $"{AppHelper.DisplayName(id)} ({id})", Tag = id, Background = B("DialogBackgroundBrush"), Foreground = B("TextPrimaryBrush") };
            preset.Items.Add(item);
            if (id == Layout.AppId) preset.SelectedItem = item;
        }
        if (preset.SelectedItem is null && preset.Items.Count > 0) preset.SelectedItem = preset.Items[0];
        string presetId = preset.SelectedItem is System.Windows.Controls.ComboBoxItem sel0 ? sel0.Tag?.ToString() ?? "cmd" : "cmd";
        var custom = new System.Windows.Controls.TextBox { Text = AppHelper.PresetIds.Contains(Layout.AppId) ? "" : Layout.AppId, Margin = new Thickness(12, 4, 12, 4), ToolTip = "Full path to terminal .exe only (cmd.exe / powershell.exe / pwsh.exe / putty.exe / wt.exe)" };
        var argsBox = new System.Windows.Controls.TextBox { Text = Layout.AppArgs, Margin = new Thickness(12, 4, 12, 4) };
        var browse = new WpfButton { Content = "Browse terminal .exe…", Margin = new Thickness(12, 4, 12, 8) };
        browse.Click += (_, _) =>
        {
            string? path = AppHelper.BrowseExe();
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!AppHelper.IsTerminalExe(path))
            {
                MessageBox.Show("Terminals only: choose cmd.exe, powershell.exe, pwsh.exe, putty.exe, or wt.exe.", "Terminal", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            custom.Text = path;
        };
        var ok = new WpfButton { Content = "OK", Width = 80, Margin = new Thickness(12, 4, 12, 12) };
        var panel = new StackPanel { Margin = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = "Embedded terminal (stays inside this box):", FontFamily = new WpfFontFamily("Segoe UI"), Foreground = B("TextPrimaryBrush"), Margin = new Thickness(12, 10, 12, 4) });
        panel.Children.Add(preset);
        panel.Children.Add(new TextBlock { Text = "Custom terminal path (terminals only, leave empty for preset):", FontFamily = new WpfFontFamily("Segoe UI"), Foreground = B("TextPrimaryBrush"), Margin = new Thickness(12, 0, 12, 4) });
        panel.Children.Add(custom); panel.Children.Add(browse);
        panel.Children.Add(new TextBlock { Text = "Arguments:", FontFamily = new WpfFontFamily("Segoe UI"), Foreground = B("TextPrimaryBrush"), Margin = new Thickness(12, 0, 12, 4) });
        panel.Children.Add(argsBox); panel.Children.Add(ok);
        win.Content = panel;
        ok.Click += (_, _) =>
        {
            string chosen;
            string customPath = custom.Text.Trim();
            if (!string.IsNullOrWhiteSpace(customPath))
            {
                if (!AppHelper.IsTerminalExe(customPath))
                {
                    MessageBox.Show("Terminals only: choose cmd.exe, powershell.exe, pwsh.exe, putty.exe, or wt.exe.", "Terminal", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                chosen = customPath;
            }
            else
            {
                chosen = preset.SelectedItem is System.Windows.Controls.ComboBoxItem sel ? sel.Tag?.ToString() ?? "cmd" : "cmd";
            }
            string oldId = Layout.AppId;
            Layout.AppId = chosen;
            Layout.AppArgs = argsBox.Text.Trim();
            Layout.Title = AppHelper.DisplayName(Layout.AppId);
            UpdateTitleText();
            LayoutChanged?.Invoke(this);
            // Apply immediately inside the hosted box (don't just relaunch separately).
            if (_termCombo is not null && _terminalHost is not null)
            {
                _suppressTermCombo = true;
                foreach (var it in _termCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>())
                {
                    if (it.Tag?.ToString() == chosen) { _termCombo.SelectedItem = it; break; }
                }
                if (_termCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem curSel || curSel.Tag?.ToString() != chosen)
                {
                    var cur = new System.Windows.Controls.ComboBoxItem { Content = $"Custom: {System.IO.Path.GetFileName(chosen)}", Tag = chosen, ToolTip = chosen };
                    _termCombo.Items.Add(cur);
                    _termCombo.SelectedItem = cur;
                }
                _suppressTermCombo = false;
                ShowTerminalHost();
                TerminalHost.Restart(Layout, _terminalHost);
                RefreshTerminalOverlay();
            }
            win.Close();
        };
        win.ShowDialog();
    }


}

public sealed class SensorRow : StackPanel
{
    // Cached references so a row can be updated in place on each 1-second refresh instead of being recreated.
    private readonly string _id;
    private readonly SensorDescriptor? _d;
    private readonly TextBlock? _valueBlock;
    private readonly Border? _barFill;
    private readonly double _barInnerW;
    private readonly bool _invertHeat;
    private readonly double _graphH;
    private Canvas? _graphCanvas;
    private readonly List<Line> _graphLines = new();
    private TextBlock? _awaitText;
    private bool _graphDirty;
    private SensorHistory? _lastHistory;
    private readonly RowLayoutKey _keyArgs;

    /// <summary>
    /// Identity of a row's static layout (visibility of text/bar/graph, sizes). Rows whose key args match can be reused and updated in place.
    /// </summary>
    public RowLayoutKey KeyArgs => _keyArgs;

    /// <summary>Value-semantics layout key — no string interpolation or parsing, zero allocations per tick.</summary>
    public readonly struct RowLayoutKey : IEquatable<RowLayoutKey>
    {
        public readonly bool ShowText, ShowBar, ShowGraph;
        public readonly double FontSize, BarHeight;
        public readonly int NameW, ValueW, BarWidth, BoxRight;

        private RowLayoutKey(bool showText, bool showBar, bool showGraph, double fontSize, double barHeight, int nameW, int valueW, int barWidth, int boxRight)
        {
            ShowText = showText; ShowBar = showBar; ShowGraph = showGraph;
            FontSize = fontSize; BarHeight = barHeight;
            NameW = nameW; ValueW = valueW; BarWidth = barWidth; BoxRight = boxRight;
        }

        public static RowLayoutKey Of(bool showText, bool showBar, bool showGraph, double fontSize, double barHeight, double nameW, double valueW, double barWidth, double boxWidth)
            => new(showText, showBar, showGraph, fontSize, barHeight,
                   (int)Math.Round(nameW), (int)Math.Round(valueW), (int)Math.Round(barWidth), (int)Math.Round(Math.Max(120, boxWidth - 16)));

        public bool Equals(RowLayoutKey other)
            => ShowText == other.ShowText && ShowBar == other.ShowBar && ShowGraph == other.ShowGraph
               && FontSize.Equals(other.FontSize) && BarHeight.Equals(other.BarHeight)
               && NameW == other.NameW && ValueW == other.ValueW && BarWidth == other.BarWidth && BoxRight == other.BoxRight;

        public override bool Equals(object? obj) => obj is RowLayoutKey k && Equals(k);
        public override int GetHashCode()
        {
            unchecked
            {
                int h = (ShowText ? 1 : 0) | (ShowBar ? 2 : 0) | (ShowGraph ? 4 : 0);
                h = h * 397 ^ NameW;
                h = h * 397 ^ ValueW;
                h = h * 397 ^ BarWidth;
                h = h * 397 ^ BoxRight;
                h = h * 397 ^ (int)(FontSize * 10);
                h = h * 397 ^ (int)(BarHeight * 10);
                return h;
            }
        }
        public static bool operator ==(RowLayoutKey a, RowLayoutKey b) => a.Equals(b);
        public static bool operator !=(RowLayoutKey a, RowLayoutKey b) => !a.Equals(b);
    }

    /// <summary>Static identity key used to reuse a row across refreshes.</summary>
    public static RowLayoutKey KeyOf(bool showText, bool showBar, bool showGraph, double fontSize, double barHeight, double nameW, double valueW, double barWidth, double boxWidth)
        => RowLayoutKey.Of(showText, showBar, showGraph, fontSize, barHeight, nameW, valueW, barWidth, boxWidth);

    public SensorRow(string id, SensorDescriptor? d, double? value, SensorHistory? history, double boxWidth, double fontSize, double barHeight, bool showText, bool showBar, bool showGraph, double barWidth = 70, double nameW = 150, double valueW = 76, string networkSpeedUnit = "Auto")
    {
        // Per-sensor graph (when enabled) sits directly BELOW the sensor's text/bar row.
        // By default its height is ~3x the sensor text height (fontSize * 3).
        Orientation = System.Windows.Controls.Orientation.Vertical;
        Width = Math.Max(120, boxWidth - 16);
        HorizontalAlignment = WpfHorizontalAlignment.Stretch;
        Margin = new Thickness(0, 0.5, 0, 0.5);
        double graphH = Math.Round(fontSize * 3);
        _graphH = graphH;
        _id = id;
        _d = d;
        _keyArgs = KeyOf(showText, showBar, showGraph, fontSize, barHeight, nameW, valueW, barWidth, boxWidth);
        var row = new Grid { Width = Math.Max(120, boxWidth - 16), HorizontalAlignment = WpfHorizontalAlignment.Stretch };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(nameW) });
        // Bar takes all remaining room between Name and right-aligned Value (Star). Star also pins Value right when Bar is hidden.
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(showText ? valueW : 0) });
        Children.Add(row);

        string name = ScaleHelper.ShortSensorLabel(d) is { Length: > 0 } label ? label : id;
        var nameText = new TextBlock { Text = name, FontFamily = new WpfFontFamily("Segoe UI"), FontSize = fontSize, Foreground = ThemeService.Find("TextPrimaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = WpfVerticalAlignment.Center };
        Grid.SetColumn(nameText, 0);
        row.Children.Add(nameText);

        if (showText)
        {
            var valBlock = new TextBlock
            {
                Text = d is null ? "—" : ScaleHelper.Format(value, d, networkSpeedUnit),
                FontFamily = new WpfFontFamily("Segoe UI"), FontSize = fontSize + 1, FontWeight = WpfFontWeights.SemiBold,
                Foreground = ThemeService.Find("AccentBrush"),
                HorizontalAlignment = WpfHorizontalAlignment.Right, VerticalAlignment = WpfVerticalAlignment.Center,
            };
            Grid.SetColumn(valBlock, 2);
            row.Children.Add(valBlock);
            _valueBlock = valBlock;
        }

        if (showBar)
        {
            var track = new Border { Background = ThemeService.Find("TrackBrush"), BorderBrush = ThemeService.Find("TrackBorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Height = barHeight, HorizontalAlignment = WpfHorizontalAlignment.Stretch, VerticalAlignment = WpfVerticalAlignment.Center };
            double pct = GetBarPercent(d, value, history);
            double fillW = pct * Math.Max(28, barWidth - 2);
            // Bar heat: good=green, bad=red. Sensors where higher is better flip the scale.
            _invertHeat = ScaleHelper.IsHigherBetter(d);
            _barInnerW = Math.Max(28, barWidth - 2);
            var filled = new Border { HorizontalAlignment = WpfHorizontalAlignment.Left, Width = fillW, Height = Math.Max(4, barHeight - 2), CornerRadius = new CornerRadius(3), Background = ThemeService.HeatBrush(_invertHeat ? 1 - pct : pct), Opacity = 1 };
            var clip = new Canvas { Width = _barInnerW, Height = Math.Max(4, barHeight - 2), ClipToBounds = true };
            clip.Children.Add(filled);
            track.Child = clip;
            Grid.SetColumn(track, 1);
            row.Children.Add(track);
            _barFill = filled;
        }

        if (showGraph)
        {
            var graph = BuildOrUpdateGraph(d, history, graphH, Math.Max(120, boxWidth - 16));
            Children.Add(graph);
            // Vsync-aligned graph redraw: set dirty in UpdateFrom() instead of rebuilding on the 1 Hz timer.
            System.Windows.Media.CompositionTarget.Rendering += OnRenderingFrame;
        }
    }

    private static double GetBarPercent(SensorDescriptor? d, double? value, SensorHistory? history)
    {
        if (d is not null && !ScaleHelper.ShouldShowBar(d.Category) && history is not null && history.Values.Count >= 2)
        {
            double mx = d.Max, mn = d.Min;
            foreach (double x in history.Values) { if (x > mx) mx = x; if (x < mn) mn = x; }
            if (mx <= mn) mx = mn + 1;
            return ScaleHelper.Normalize(value, mn, mx);
        }
        return d is null ? 0 : ScaleHelper.Normalize(value, d.Min, Math.Max(d.Max, value ?? 0));
    }

    /// <summary>Update the dynamic value/bar/graph of this row in place (used by the 1-second refresh).</summary>
    public void UpdateFrom(double? value, SensorHistory? history, string networkSpeedUnit)
    {
        if (_valueBlock is not null)
            _valueBlock.Text = _d is null ? "—" : ScaleHelper.Format(value, _d, networkSpeedUnit);
        if (_barFill is not null)
        {
            double pct = GetBarPercent(_d, value, history);
            _barFill.Width = Math.Max(0.1, pct * _barInnerW);
            _barFill.Background = ThemeService.HeatBrush(_invertHeat ? 1 - pct : pct);
        }
        if (_graphCanvas is not null && history is not null)
        {
            // Decouple graph redraw from the 1 Hz timer: mark dirty, paint on next vsync.
            _lastHistory = history;
            _graphDirty = true;
        }
    }

    /// <summary>Unsubscribe from the vsync-aligned rendering hook (called when a row is evicted from the reuse cache).</summary>
    public void UnsubscribeRenderingFrame()
    {
        System.Windows.Media.CompositionTarget.Rendering -= OnRenderingFrame;
    }

    private void OnRenderingFrame(object? sender, EventArgs e)
    {
        if (!_graphDirty || _graphCanvas is null || _lastHistory is null) return;
        _graphDirty = false;
        BuildOrUpdateGraph(_d, _lastHistory, _graphH, Math.Max(120, Width));
    }

    private Canvas BuildOrUpdateGraph(SensorDescriptor? d, SensorHistory? history, double graphH, double graphW)
    {
        graphW = Math.Max(120, graphW);
        bool hadCanvas = _graphCanvas is not null;
        if (!hadCanvas || Math.Abs(_graphCanvas!.Width - graphW) > 0.01 || Math.Abs(_graphCanvas.Height - graphH) > 0.01)
        {
            int insertAt = hadCanvas ? Children.IndexOf(_graphCanvas!) : -1;
            if (hadCanvas)
            {
                Children.Remove(_graphCanvas!);
                _graphCanvas = RebuildGraphCanvas(graphH, graphW);
                if (insertAt >= 0 && insertAt <= Children.Count) Children.Insert(insertAt, _graphCanvas!);
            }
            else
            {
                _graphCanvas = RebuildGraphCanvas(graphH, graphW);
                // first creation is attached by the ctor's Children.Add(graph)
            }
        }
        var canvas = _graphCanvas!;
        const int WindowSamples = SensorHistory.DefaultCapacity;
        if (d is null || history is null || history.Values.Count < 2)
        {
            if (_awaitText is not null) _awaitText.Visibility = Visibility.Visible;
            if (_graphLines.Count > 0)
            {
                foreach (var l in _graphLines) canvas.Children.Remove(l);
                _graphLines.Clear();
            }
            return canvas;
        }
        if (_awaitText is not null) _awaitText.Visibility = Visibility.Collapsed;
        var vals = history.Values;
        // Clip to the last WindowSamples values; never draw off-canvas.
        int win = Math.Min(vals.Count, WindowSamples);
        int start = vals.Count - win;
        double max = d.Max, min = d.Min;
        for (int i = start; i < vals.Count; i++) { if (vals[i] > max) max = vals[i]; if (vals[i] < min) min = vals[i]; }
        if (max <= min) max = min + 1;
        double step = (graphW - 4) / (double)WindowSamples;
        double x0 = graphW - 2 - step * win;
        int segCount = win - 1;
        // Trim/extend the segment pool to match history length (no allocation in steady state).
        while (_graphLines.Count > segCount)
        {
            var l = _graphLines[^1];
            _graphLines.RemoveAt(_graphLines.Count - 1);
            canvas.Children.Remove(l);
        }
        while (_graphLines.Count < segCount)
        {
            var l = new Line { StrokeThickness = 1.2 };
            canvas.Children.Add(l);
            _graphLines.Add(l);
        }
        for (int i = 0; i < segCount; i++)
        {
            var seg = _graphLines[i];
            double x1 = x0 + i * step;
            double v1 = vals[start + i];
            double y1 = graphH - 2 - ((v1 - min) / (max - min)) * (graphH - 4);
            double x2 = x0 + (i + 1) * step;
            double y2 = graphH - 2 - ((vals[start + i + 1] - min) / (max - min)) * (graphH - 4);
            double pct1 = (v1 - min) / (max - min);
            seg.X1 = x1; seg.Y1 = y1; seg.X2 = x2; seg.Y2 = y2;
            seg.Stroke = ThemeService.HeatBrush(pct1);
        }
        return canvas;
    }

    private Canvas RebuildGraphCanvas(double graphH, double graphW)
    {
        _graphLines.Clear();
        var canvas = new Canvas { Height = graphH, Width = graphW, Background = ThemeService.Find("GraphBackgroundBrush"), HorizontalAlignment = WpfHorizontalAlignment.Stretch, Margin = new Thickness(0, 0.5, 0, 0), ClipToBounds = true };
        // Gridlines (2 inner lines).
        var grid = ThemeService.Find("PatternDotBrush");
        for (int g = 1; g <= 2; g++)
        {
            double gy = graphH * g / 3.0;
            var gl = new Line { X1 = 2, Y1 = gy, X2 = Math.Max(20, graphW - 4), Y2 = gy, Stroke = grid, StrokeThickness = 0.5, Opacity = 0.35, StrokeDashArray = new DoubleCollection { 2, 3 } };
            canvas.Children.Add(gl);
        }
        _awaitText = new TextBlock { Text = "—", FontSize = 9, Foreground = ThemeService.Find("TextMutedBrush"), Margin = new Thickness(6, 4, 0, 0), Visibility = Visibility.Collapsed };
        canvas.Children.Add(_awaitText);
        return canvas;
    }
}

public static class ProcessHelper
{
    private static readonly Dictionary<int, TimeSpan> _lastCpu = new();
    private static readonly Dictionary<int, Queue<double>> _cpuHist = new();
    private static readonly Dictionary<int, double> _cpuSum = new();
    private static readonly Queue<int> _topHistory = new();
    private static readonly Dictionary<int, int> _topCount = new();
    private static DateTime _lastTime = DateTime.UtcNow;
    private static readonly object _lock = new();
    private static readonly int _procs = Environment.ProcessorCount;
    private const int HeatWindow = 60;
    private static readonly object GetTopLock = new();
    private static List<(string Name, double CpuPercent, long RamMB, int Threads, int Pid, double HeatPct)>? _topCache;
    private static int _cacheTick;
    private static int _cacheLimit;
    private static string _cacheSort = "";

    public static List<(string Name, double CpuPercent, long RamMB, int Threads, int Pid, double HeatPct)> GetTop(int limit, string sort)
    {
        // Two process boxes usually shown side-by-side share the same snapshot; cache for ~1.5 s.
        lock (GetTopLock)
        {
            if (sort == _cacheSort && limit == _cacheLimit && Environment.TickCount - _cacheTick < 1500 && _topCache is not null)
                return _topCache;
        }
        var list = new List<(string Name, double CpuPercent, long RamMB, int Threads, int Pid, double Avg)>();
        DateTime now = DateTime.UtcNow;
        double maxAvg = 0;
        bool needRam = string.Equals(sort, "Ram", StringComparison.OrdinalIgnoreCase);
        bool needThreads = string.Equals(sort, "Threads", StringComparison.OrdinalIgnoreCase);
        lock (_lock)
        {
            double elapsed = Math.Max(0.5, (now - _lastTime).TotalSeconds);
            var seen = new HashSet<int>();
            var raw = new List<(string Name, double CpuPercent, long RamMB, int Threads, int Pid, double Avg)>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    seen.Add(p.Id);
                    TimeSpan cpu;
                    try { cpu = p.TotalProcessorTime; } catch { cpu = TimeSpan.Zero; }
                    double cpuPct = 0;
                    if (_lastCpu.TryGetValue(p.Id, out var prev))
                    {
                        double delta = (cpu - prev).TotalSeconds;
                        if (delta >= 0) cpuPct = delta / elapsed / Math.Max(1, _procs) * 100.0;
                    }
                    _lastCpu[p.Id] = cpu;
                    if (!_cpuHist.TryGetValue(p.Id, out var q)) { q = new Queue<double>(); _cpuHist[p.Id] = q; _cpuSum[p.Id] = 0; }
                    double sum = _cpuSum[p.Id] + cpuPct;
                    q.Enqueue(cpuPct);
                    if (q.Count > HeatWindow) sum -= q.Dequeue();
                    _cpuSum[p.Id] = sum;
                    double avg = sum / q.Count;
                    long mb = 0;
                    if (needRam) { try { mb = p.WorkingSet64 / 1024 / 1024; } catch { mb = 0; } }
                    int th = 0;
                    if (needThreads) { try { th = p.Threads.Count; } catch { th = 0; } }
                    raw.Add((SafeName(p), cpuPct, mb, th, p.Id, avg));
                    p.Dispose();
                }
                catch { try { p.Dispose(); } catch { } }
            }
            foreach (int dead in _lastCpu.Keys.Where(id => !seen.Contains(id)).ToList()) { _lastCpu.Remove(dead); _cpuHist.Remove(dead); _cpuSum.Remove(dead); }

            if (raw.Count > 0)
            {
                double topPidCpu = double.NegativeInfinity;
                int topPid = 0;
                foreach (var r in raw) if (r.Item2 > topPidCpu) { topPidCpu = r.Item2; topPid = r.Item5; }
                _topHistory.Enqueue(topPid);
                if (!_topCount.TryGetValue(topPid, out int cnt)) cnt = 0;
                _topCount[topPid] = cnt + 1;
                while (_topHistory.Count > HeatWindow)
                {
                    int old = _topHistory.Dequeue();
                    if (_topCount.TryGetValue(old, out int oc))
                    {
                        if (oc <= 1) _topCount.Remove(old);
                        else _topCount[old] = oc - 1;
                    }
                }
            }
            _lastTime = now;
            foreach (var r in raw) if (r.Item6 > maxAvg) maxAvg = r.Item6;
            foreach (var item in raw)
            {
                list.Add((item.Item1, item.Item2, item.Item3, item.Item4, item.Item5, item.Avg));
            }
        }
        // Sort + take only the few entries that will be shown.
        // tuple.Item6 = rolling average of this process (used for the heat "share" term).
        var ordered = sort switch
        {
            "Ram" => list.OrderByDescending(x => x.Item3).ToList(),
            "Name" => list.OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).ToList(),
            "Threads" => list.OrderByDescending(x => x.Item4).ToList(),
            _ => list.OrderByDescending(x => x.Item2).ToList(),
        };
        var result = new List<(string Name, double CpuPercent, long RamMB, int Threads, int Pid, double HeatPct)>(Math.Min(limit, ordered.Count));
        lock (GetTopLock)
        {
            _topCache = result;
            _cacheTick = Environment.TickCount;
            _cacheSort = sort;
            _cacheLimit = limit;
        }
        double shareScale = maxAvg > 0 ? 1.0 / maxAvg : 0;
        int count = 0;
        foreach (var item in ordered)
        {
            double persistence = _topCount.TryGetValue(item.Item5, out int c) ? c / (double)HeatWindow : 0;
            double share = item.Item6 > 0 ? item.Item6 * shareScale : 0;
            double heat = Math.Clamp(0.6 * persistence + 0.4 * share, 0, 1);
            result.Add((item.Item1, item.Item2, item.Item3, item.Item4, item.Item5, heat));
            count++;
            if (count >= limit) break;
        }
        return result;
    }

    private static string SafeName(Process p)
    {
        try { return p.ProcessName; } catch { return "—"; }
    }
}

public static class AppHelper
{
    // Terminals only. The app box embeds one terminal and keeps it inside the box.
    public static readonly string[] PresetIds = ["cmd", "powershell", "pwsh", "putty", "wt"];

    public static string DisplayName(string appId) => appId switch
    {
        "cmd" => "Command Prompt",
        "powershell" => "Windows PowerShell",
        "pwsh" => "PowerShell 7",
        "putty" => "PuTTY",
        "wt" => "Windows Terminal",
        _ => System.IO.Path.GetFileNameWithoutExtension(appId ?? "cmd"),
    };

    public static string DefaultArgs(string appId) => appId switch
    {
        "powershell" => "-NoLogo",
        "pwsh" => "-NoLogo",
        _ => "",
    };

    public static string ResolvePath(string appId)
    {
        appId ??= "cmd";
        if (appId.Contains('\\') || appId.Contains('/')) return appId;
        return appId switch
        {
            "cmd" => FindExe("cmd.exe", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")),
            "powershell" => FindExe("powershell.exe", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "powershell.exe")),
            "pwsh" => FindExe("pwsh.exe",
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe")),
            "putty" => FindExe("putty.exe",
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PuTTY", "putty.exe"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PuTTY", "putty.exe")),
            "wt" => FindExe("wt.exe", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wt.exe")),
            _ => appId,
        };
    }

    private static string FindExe(string name, params string[] candidates)
    {
        foreach (string c in candidates)
        {
            try { if (File.Exists(c)) return c; } catch { }
        }
        // Fall back to plain name: Process will search PATH / System32.
        return name;
    }

    public static bool IsTerminalExe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string name = System.IO.Path.GetFileName(path).ToLowerInvariant();
        return name is "cmd.exe" or "powershell.exe" or "pwsh.exe" or "putty.exe" or "putty64.exe" or "plink.exe" or "wt.exe";
    }

    public static bool IsAllowedAppId(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return false;
        if (PresetIds.Contains(appId)) return true;
        return IsTerminalExe(appId);
    }

    public static string? BrowseExe()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose terminal executable (cmd / PowerShell / PuTTY / Windows Terminal)",
            Filter = "Terminal exes (cmd.exe;powershell.exe;pwsh.exe;putty.exe;wt.exe)|cmd.exe;powershell.exe;pwsh.exe;putty.exe;wt.exe|Executables (*.exe)|*.exe",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() == true) return dlg.FileName;
        return null;
    }
}
