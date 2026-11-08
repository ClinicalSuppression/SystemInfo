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
    public event Action<ComponentBox>? Moved;
    public event Action<ComponentBox>? Resized;
    public event Action<ComponentBox>? PickSensors;
    public event Action<ComponentBox>? RemoveClicked;
    public event Action<ComponentBox>? LayoutChanged;
    public event Action<ComponentBox>? HideBox;

    private static WpfSolidColorBrush B(string key) => ThemeService.Find(key);
    private static WpfSolidColorBrush GraphBrush(int idx) => ThemeService.Find($"GraphPalette{idx % 6}");

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
    private ScrollViewer? _scroll;
    private readonly TextBlock _titleText;
    private readonly Canvas? _graphCanvas;
    private WrapPanel? _legendPanel;
    private readonly Canvas _canvasParent;
    private readonly List<System.Windows.Controls.Primitives.Thumb> _edgeThumbs = [];
    private TerminalHwndHost? _terminalHost;
    private System.Windows.Controls.ComboBox? _termCombo;
    private TextBlock? _termStatus;
    private FrameworkElement? _termOverlay;
    private TextBlock? _termOverlayText;
    private bool _suppressTermCombo;
    private bool _locked;
    private bool _dragging;
    private WpfPoint _dragStart;
    private double _startLeft, _startTop;
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
        Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 12,
            ShadowDepth = 2,
            Opacity = 0.35,
            Color = System.Windows.Media.Colors.Black,
        };

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
            ToolTip = "Double-click to rename",
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
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
        var btnEye = new WpfButton { Content = "👁", Background = WpfBrushes.Transparent, BorderThickness = new Thickness(0), Foreground = B("TextMutedBrush"), FontSize = 11, Cursor = WpfCursors.Hand, Margin = new Thickness(4, 0, 0, 0), ToolTip = "Hide box (restore via Show hidden boxes)" };
        btnEye.Click += (_, _) => HideBox?.Invoke(this);
        var hdrPanel = new Grid();
        hdrPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hdrPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hdrPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hdrPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_titleText, 0);
        Grid.SetRow(_titleText, 0);
        hdrPanel.Children.Add(_titleText);
        btnPick.Margin = new Thickness(4, 0, 2, 0);
        Grid.SetColumn(btnPick, 1);
        Grid.SetRow(btnPick, 0);
        hdrPanel.Children.Add(btnPick);
        Grid.SetColumn(btnEye, 2);
        Grid.SetRow(btnEye, 0);
        hdrPanel.Children.Add(btnEye);
        Grid.SetColumn(btnX, 3);
        Grid.SetRow(btnX, 0);
        hdrPanel.Children.Add(btnX);
        header.Child = hdrPanel;
        header.SetValue(DockPanel.DockProperty, Dock.Top);
        dock.Children.Add(header);
        var boxMenu = new System.Windows.Controls.ContextMenu();
        var miCopy = new System.Windows.Controls.MenuItem { Header = "Copy box as image" };
        miCopy.Click += (_, _) => CopyAsImage();
        var miHide = new System.Windows.Controls.MenuItem { Header = "Hide box" };
        miHide.Click += (_, _) => HideBox?.Invoke(this);
        boxMenu.Items.Add(miCopy); boxMenu.Items.Add(miHide);
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
            _legendPanel = new WrapPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(6, 4, 6, 0) };
            _legendPanel.SetValue(DockPanel.DockProperty, Dock.Top);
            dock.Children.Add(_legendPanel);
            _graphCanvas = new Canvas { Height = graphHeight, Margin = new Thickness(6, 4, 6, 0), Background = B("GraphBackgroundBrush") };
            _graphCanvas.SetValue(DockPanel.DockProperty, Dock.Top);
            dock.Children.Add(_graphCanvas);
            _graphCanvas.MouseWheel += (_, e) =>
            {
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    int cur = Layout.GraphZoom <= 0 ? 48 : Layout.GraphZoom;
                    int next = Math.Clamp(cur + (e.Delta > 0 ? 24 : -24), 10, 300);
                    if (next != cur) { Layout.GraphZoom = next; LayoutChanged?.Invoke(this); }
                    e.Handled = true;
                }
            };

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
        _suppressTermCombo = true;
        _suppressTermCombo = false;
        _termCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressTermCombo) return;
            if (_termCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem sel) return;
            string? id = sel.Tag?.ToString(); if (string.IsNullOrWhiteSpace(id)) return;
            if (id == Layout.AppId) return;
            Layout.AppId = id;
            Layout.Title = AppHelper.DisplayName(id);
            _titleText.Text = Layout.Title;
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
        _termStatus = new TextBlock { VerticalAlignment = WpfVerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), FontFamily = new WpfFontFamily("Segoe UI"), FontSize = Math.Max(9, fontSize - 1), Foreground = B("TextMutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis, Text = "Starting…" };
        toolbar.Children.Add(_termCombo);
        toolbar.Children.Add(btnRestart);
        toolbar.Children.Add(btnKill);
        toolbar.Children.Add(btnFocus);
        toolbar.Children.Add(_termStatus);
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

    public void SetLocked(bool locked)
    {
        _locked = locked;
        foreach (var t in _edgeThumbs) t.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetAlert(bool alerted)
    {
        BorderBrush = alerted ? B("BarHotBrush") : B("BoxBorderDimBrush");
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
        _titleText.Text = Layout.Title;
        if (Layout.CustomKind == "app" && _terminalHost is not null)
        {
            try { TerminalHost.Position(Layout, _terminalHost); } catch { }
        }
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
        _dragging = true;
        _dragStart = e.GetPosition(_canvasParent);
        _startLeft = Canvas.GetLeft(this);
        _startTop = Canvas.GetTop(this);
        try { (sender as FrameworkElement)?.CaptureMouse(); } catch { }
        e.Handled = true;
    }

    private void Header_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_dragging || _locked) return;
        var pos = e.GetPosition(_canvasParent);
        double nx = Math.Max(0, _startLeft + (pos.X - _dragStart.X));
        double ny = Math.Max(0, _startTop + (pos.Y - _dragStart.Y));
        double maxX = Math.Max(0, _canvasParent.ActualWidth - Layout.Width);
        double maxY = Math.Max(0, _canvasParent.ActualHeight - Layout.Height);
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

    private double Snap(double x, double y, bool isX)
    {
        const double Gap = 4;
        const double Thresh = 8;
        double candidate = isX ? x : y;
        double canvasSize = isX ? _canvasParent.ActualWidth : _canvasParent.ActualHeight;
        double boxSize = isX ? Layout.Width : Layout.Height;
        double maxPos = Math.Max(0, canvasSize - boxSize);
        double[] targets = isX ? [0, maxPos] : [0, maxPos];
        // Gather edges from sibling boxes
        var others = _canvasParent.Children.OfType<ComponentBox>().Where(b => b != this).ToList();
        foreach (var o in others)
        {
            double ox = Canvas.GetLeft(o) - (isX ? Layout.Width : Layout.Height);
            double oy = Canvas.GetTop(o) - (isX ? Layout.Width : Layout.Height);
            // edges: left/top and right/bottom + gap
            if (isX)
            {
                targets = targets.Append(Canvas.GetLeft(o)).Append(Canvas.GetLeft(o) + o.Layout.Width + Gap).ToArray();
                targets = targets.Append(Canvas.GetLeft(o) - Layout.Width - Gap).ToArray();
            }
            else
            {
                targets = targets.Append(Canvas.GetTop(o)).Append(Canvas.GetTop(o) + o.Layout.Height + Gap).ToArray();
                targets = targets.Append(Canvas.GetTop(o) - Layout.Height - Gap).ToArray();
            }
        }
        foreach (double t in targets)
        {
            if (t < 0) continue;
            if (Math.Abs(candidate - t) <= Thresh) return t;
        }
        return candidate;
    }

    private void Header_MouseLeftButtonUp(object sender, WpfMouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        try { (sender as FrameworkElement)?.ReleaseMouseCapture(); } catch { }
        Moved?.Invoke(this);
    }

    private void OpenRenameDialog()
    {
        if (_locked) return;
        var win = new Window { Title = "Rename box", Width = 340, Height = 140, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = WpfApplication.Current?.MainWindow };
        // Simple rename dialog
        var tb = new System.Windows.Controls.TextBox { Text = Layout.Title, Margin = new Thickness(12), FontFamily = new WpfFontFamily("Segoe UI") };
        var ok = new WpfButton { Content = "OK", Width = 80, Margin = new Thickness(12) };
        var panel = new StackPanel { Margin = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = "Box title:", FontFamily = new WpfFontFamily("Segoe UI"), Foreground = B("TextPrimaryBrush"), Margin = new Thickness(12, 10, 12, 4) });
        panel.Children.Add(tb);
        ok.Margin = new Thickness(12, 8, 12, 12);
        panel.Children.Add(ok);
        win.Content = panel;
        win.Background = B("DialogBackgroundBrush");
        ok.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(tb.Text)) { Layout.Title = tb.Text.Trim(); _titleText.Text = Layout.Title; win.Close(); }
        };
        tb.KeyDown += (_, ke) => { if (ke.Key == Key.Enter) ok.RaiseEvent(new RoutedEventArgs(WpfButton.ClickEvent)); };
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
            double newW = Math.Clamp(w + dx, 200, Math.Min(2000, canvasW - x));
            newW = SnapResize(newW, isX: true);
            w = Math.Clamp(newW, 200, Math.Min(2000, canvasW - x));
        }
        if (dir.Contains("W"))
        {
            double newX = Math.Clamp(x + dx, 0, right - 200);
            newX = SnapLeadEdge(newX, isX: true, size: right - newX);
            double newW = right - newX;
            if (newW >= 200 && newW <= 2000) { x = newX; w = newW; }
        }
        if (dir.Contains("S"))
        {
            double newH = Math.Clamp(h + dy, 90, Math.Min(2000, canvasH - y));
            newH = SnapResize(newH, isX: false);
            h = Math.Clamp(newH, 90, Math.Min(2000, canvasH - y));
        }
        if (dir.Contains("N"))
        {
            double newY = Math.Clamp(y + dy, 0, bottom - 90);
            newY = SnapLeadEdge(newY, isX: false, size: bottom - newY);
            double newH = bottom - newY;
            if (newH >= 90 && newH <= 2000) { y = newY; h = newH; }
        }
        Layout.X = x; Layout.Y = y; Layout.Width = w; Layout.Height = h;
        Canvas.SetLeft(this, x); Canvas.SetTop(this, y);
        Width = w; Height = h;
        if (Layout.CustomKind == "app" && _terminalHost is not null)
        {
            try { TerminalHost.Position(Layout, _terminalHost); } catch { }
        }
    }

    private double SnapLeadEdge(double leadPos, bool isX, double size)
    {
        const double Gap = 4;
        const double Thresh = 8;
        double[] targets = [0];
        foreach (var o in _canvasParent.Children.OfType<ComponentBox>().Where(b => b != this))
        {
            if (isX)
            {
                double oLeft = Canvas.GetLeft(o);
                double oRight = oLeft + o.Layout.Width;
                targets = targets.Append(oLeft).Append(oRight + Gap).Append(oRight).ToArray();
            }
            else
            {
                double oTop = Canvas.GetTop(o);
                double oBottom = oTop + o.Layout.Height;
                targets = targets.Append(oTop).Append(oBottom + Gap).Append(oBottom).ToArray();
            }
        }
        foreach (double t in targets)
        {
            if (t < 0) continue;
            if (Math.Abs(leadPos - t) <= Thresh) return t;
        }
        return leadPos;
    }

    private double SnapResize(double newValue, bool isX)
    {
        const double Gap = 4;
        const double Thresh = 8;
        double origin = isX ? Layout.X : Layout.Y;
        double candidateEdge = origin + newValue;
        double[] targets = [];
        var others = _canvasParent.Children.OfType<ComponentBox>().Where(b => b != this).ToList();
        foreach (var o in others)
        {
            if (isX)
            {
                double oLeft = Canvas.GetLeft(o);
                double oRight = oLeft + o.Layout.Width;
                targets = targets.Append(oLeft - Gap).Append(oRight + Gap).Append(oLeft).Append(oRight).ToArray();
            }
            else
            {
                double oTop = Canvas.GetTop(o);
                double oBottom = oTop + o.Layout.Height;
                targets = targets.Append(oTop - Gap).Append(oBottom + Gap).Append(oTop).Append(oBottom).ToArray();
            }
        }
        foreach (double t in targets)
        {
            if (Math.Abs(candidateEdge - t) <= Thresh) return t - origin;
        }
        return newValue;
    }

    public void UpdateRows(IReadOnlyDictionary<string, SensorDescriptor> catalog, IReadOnlyDictionary<string, double?> values, IReadOnlyDictionary<string, SensorHistory> histories, double fontSize, double barHeight, double graphHeight)
    {
        if (Layout.CustomKind == "app")
        {
            // Hosted HWND must persist across 1s refreshes — never rebuild terminal UI here.
            if (_graphCanvas is not null) _graphCanvas.Visibility = Visibility.Collapsed;
            RefreshTerminalOverlay();
            return;
        }
        _rows.Children.Clear();
        if (!string.IsNullOrEmpty(Layout.CustomKind))
        {
            if (_graphCanvas is not null) _graphCanvas.Visibility = Visibility.Collapsed;
            if (_legendPanel is not null) _legendPanel.Visibility = Visibility.Collapsed;
            UpdateCustomRows(fontSize, barHeight);
            return;
        }
        // No default "main" graphs: header graph is removed. Graphs appear only per-sensor,
        // directly above the sensor row, and only when that sensor's Graph mode is on.
        if (_graphCanvas is not null) { _graphCanvas.Visibility = Visibility.Collapsed; _graphCanvas.Children.Clear(); }
        if (_legendPanel is not null) { _legendPanel.Visibility = Visibility.Collapsed; _legendPanel.Children.Clear(); }
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
        bool anyText = Layout.SensorIds.Any(id => ScaleHelper.ModeHasText(GetMode(id, catalog.TryGetValue(id, out var dd) ? dd : null)));
        bool anyBar = Layout.SensorIds.Any(id => ScaleHelper.ModeHasBar(GetMode(id, catalog.TryGetValue(id, out var dd2) ? dd2 : null)));
        // inner is the ACTUAL ScrollViewer viewport (minus _rows margins) so a visible 6px scrollbar can't clip the right edge.
        double viewport = (_scroll is not null && _scroll.ViewportWidth > 0) ? _scroll.ViewportWidth : (_scroll is not null && _scroll.ActualWidth > 0 ? _scroll.ActualWidth : Layout.Width - 14);
        double inner = Math.Max(120, viewport - 12);
        // Right-justified value must never clip: size it to the longest formatted value in this box.
        int maxValLen = 0;
        if (anyText)
        {
            foreach (string vid in Layout.SensorIds)
            {
                if (!ScaleHelper.ModeHasText(GetMode(vid, catalog.TryGetValue(vid, out var vd) ? vd : null))) continue;
                values.TryGetValue(vid, out double? vv);
                string s = vd is null ? "—" : ScaleHelper.Format(vv, vd, Layout.NetworkSpeedUnit);
                maxValLen = Math.Max(maxValLen, s.Length);
            }
        }
        double valueW = anyText ? Math.Clamp(Math.Max(44, (fontSize + 1) * (maxValLen * 0.58 + 0.5)), 44, 140) : 0;
        double nameW = Math.Min(150, Math.Max(90, inner * 0.42));
        // Keep the whole row inside the box at narrow widths (fixes right-side cutoff).
        double minBar = anyBar ? 30 : 0;
        if (inner < nameW + valueW + 12 + minBar)
        {
            nameW = Math.Max(60, inner - valueW - 12 - minBar);
        }
        double barWidth = anyBar ? Math.Max(30, inner - nameW - (anyText ? valueW + 6 : 0) - 6) : 0;
        bool anyAlert = false;
        foreach (string id in Layout.SensorIds)
        {
            catalog.TryGetValue(id, out SensorDescriptor? d);
            values.TryGetValue(id, out double? v);
            histories.TryGetValue(id, out SensorHistory? h);
            int mode = GetMode(id, d);
            bool showText = ScaleHelper.ModeHasText(mode);
            bool showBar = ScaleHelper.ModeHasBar(mode);
            bool showGraph = ScaleHelper.ModeHasGraph(mode);
            if (d is not null && v.HasValue)
            {
                if (d.Category.Equals("Temperature", StringComparison.OrdinalIgnoreCase) && v.Value > 85) anyAlert = true;
                if (d.Category.Equals("Load", StringComparison.OrdinalIgnoreCase) && v.Value > 98) anyAlert = true;
            }
            _rows.Children.Add(new SensorRow(id, d, v, h, inner + 16, fontSize, barHeight, showText, showBar, showGraph, barWidth, nameW, valueW, Layout.NetworkSpeedUnit));
        }
        SetAlert(anyAlert);
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
                var heat = ThemeService.HeatBrush(heatPct);
                heat.Opacity = 0.35;
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
            _titleText.Text = Layout.Title;
            LayoutChanged?.Invoke(this);
            // Apply immediately inside the hosted box (don't just relaunch separately).
            if (_termCombo is not null && _terminalHost is not null && (oldId != chosen || true))
            {
                _suppressTermCombo = true;
                foreach (var it in _termCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>())
                {
                    if (it.Tag?.ToString() == chosen || (it.Tag?.ToString() != null && (it.Tag?.ToString() == chosen))) { _termCombo.SelectedItem = it; break; }
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

    private void DrawGraph(IReadOnlyDictionary<string, SensorDescriptor> catalog, IReadOnlyDictionary<string, SensorHistory> histories, IReadOnlyList<string>? effectiveGraphIds = null)
    {
        if (_graphCanvas is null) return;
        _graphCanvas.Children.Clear();
        // Legend (above the plot): one swatch + short name per header series, in list order.
        if (_legendPanel is not null)
        {
            _legendPanel.Children.Clear();
            var idsForLegend = (effectiveGraphIds ?? Layout.GraphSensorIds).Where(id => catalog.ContainsKey(id)).ToList();
            for (int li = 0; li < idsForLegend.Count; li++)
            {
                string lid = idsForLegend[li];
                var d = catalog[lid];
                int pal = Layout.GraphSeriesColors != null && Layout.GraphSeriesColors.TryGetValue(lid, out int ci) ? ci : li;
                var sw = new System.Windows.Controls.Border { Width = 16, Height = 3, Background = GraphBrush(pal), VerticalAlignment = WpfVerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
                var lab = new TextBlock { Text = d.Name, FontFamily = new WpfFontFamily("Segoe UI"), FontSize = 10, Foreground = B("TextMutedBrush"), VerticalAlignment = WpfVerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0), MaxWidth = 110, ToolTip = $"{d.Name} ({d.Category} {d.Unit})" };
                var item = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 4) };
                item.Children.Add(sw); item.Children.Add(lab);
                _legendPanel.Children.Add(item);
            }
        }
        var grid = ThemeService.Find("PatternDotBrush");
        for (int g = 1; g <= 3; g++)
        {
            double gy = _graphCanvas.Height * g / 4.0;
            var gl = new Line { X1 = 2, Y1 = gy, X2 = Math.Max(100, Layout.Width - 22), Y2 = gy, Stroke = grid, StrokeThickness = 0.5, Opacity = 0.35, StrokeDashArray = new DoubleCollection { 2, 3 } };
            _graphCanvas.Children.Add(gl);
        }
        var graphIds = effectiveGraphIds ?? Layout.GraphSensorIds;
        if (graphIds.Count == 0) return;
        var series = graphIds.Where(id => catalog.ContainsKey(id) && histories.ContainsKey(id)).ToList();
        if (series.Count == 0) return;
        double w = Math.Max(100, Layout.Width - 20);
        double h = _graphCanvas.Height;
        if (h < 10) h = 40;
        int zoom = Layout.GraphZoom <= 0 ? 48 : Layout.GraphZoom;
        double thickness = Layout.GraphLineThickness <= 0 ? 1.4 : Layout.GraphLineThickness;
        bool fixedScale = Layout.GraphScaleMode == "Fixed";
        // Group by GraphGroup when combined so per-core lines share one scale (comparable).
        List<List<string>> groups = Layout.CombineGraphs
            ? series.GroupBy(id => catalog[id].GraphGroup).Select(g => g.ToList()).ToList()
            : series.Select(id => new List<string> { id }).ToList();
        int seriesIndex = 0;
        foreach (var grp in groups)
        {
            // Shared scale for this group.
            double gmin = double.MaxValue, gmax = double.MinValue;
            var hists = new List<(string id, SensorDescriptor d, double[] hist)>();
            foreach (string id in grp)
            {
                var d = catalog[id];
                var all = histories[id].Values.ToArray();
                var hist = all.Length > zoom ? all.Skip(all.Length - zoom).ToArray() : all;
                if (hist.Length < 2) continue;
                hists.Add((id, d, hist));
                if (fixedScale)
                {
                    gmin = Math.Min(gmin, d.Min); gmax = Math.Max(gmax, d.Max);
                }
                else
                {
                    gmin = Math.Min(gmin, Math.Min(d.Min, hist.Min()));
                    gmax = Math.Max(gmax, Math.Max(d.Max, hist.Max()));
                }
            }
            if (gmax <= gmin) { gmax = gmin + 1; }
            foreach (var (id, d, hist) in hists)
            {
                var pts = new WpfPointCollection();
                for (int i = 0; i < hist.Length; i++)
                {
                    double x = i * (w - 4) / (hist.Length - 1) + 2;
                    double y = h - 2 - ((hist[i] - gmin) / (gmax - gmin)) * (h - 4);
                    pts.Add(new WpfPoint(x, y));
                }
                int pal = Layout.GraphSeriesColors != null && Layout.GraphSeriesColors.TryGetValue(id, out int ci2) ? ci2 : seriesIndex;
                Wpfbrush brush = GraphBrush(pal);
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    var seg = new Line { X1 = pts[i].X, Y1 = pts[i].Y, X2 = pts[i + 1].X, Y2 = pts[i + 1].Y, Stroke = brush, StrokeThickness = thickness };
                    _graphCanvas.Children.Add(seg);
                }
                seriesIndex++;
            }
        }
    }

    internal sealed class SensorSlot { public string SensorId { get; set; } = ""; public DisplayMode Mode { get; set; } = DisplayMode.Bar; }
}

public enum DisplayMode { Bar, Graph, Value }

public sealed class SensorRow : StackPanel
{
    public SensorRow(string id, SensorDescriptor? d, double? value, SensorHistory? history, double boxWidth, double fontSize, double barHeight, bool showText, bool showBar, bool showGraph, double barWidth = 70, double nameW = 150, double valueW = 76, string networkSpeedUnit = "Auto")
    {
        // Per-sensor graph (when enabled) sits directly BELOW the sensor's text/bar row.
        // By default its height is ~3x the sensor text height (fontSize * 3).
        Orientation = System.Windows.Controls.Orientation.Vertical;
        Width = Math.Max(120, boxWidth - 16);
        HorizontalAlignment = WpfHorizontalAlignment.Stretch;
        Margin = new Thickness(0, 0.5, 0, 0.5);
        double graphH = Math.Round(fontSize * 3);
        var row = new Grid { Width = Math.Max(120, boxWidth - 16), HorizontalAlignment = WpfHorizontalAlignment.Stretch };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(nameW) });
        // Bar takes all remaining room between Name and right-aligned Value (Star). Star also pins Value right when Bar is hidden.
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(showText ? valueW : 0) });
        Children.Add(row);

        string name = d?.Name ?? id;
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
        }

        if (showBar)
        {
            var track = new Border { Background = ThemeService.Find("TrackBrush"), BorderBrush = ThemeService.Find("TrackBorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Height = barHeight, HorizontalAlignment = WpfHorizontalAlignment.Stretch, VerticalAlignment = WpfVerticalAlignment.Center };
            double pct;
            if (d is not null && !ScaleHelper.ShouldShowBar(d.Category) && history is not null && history.Values.Count >= 2)
            {
                var vals = history.Values.ToArray();
                double max = Math.Max(d.Max, vals.Max());
                double min = Math.Min(d.Min, vals.Min());
                if (max <= min) max = min + 1;
                pct = ScaleHelper.Normalize(value, min, max);
            }
            else
            {
                double pct2 = d is null ? 0 : ScaleHelper.Normalize(value, d.Min, Math.Max(d.Max, value ?? 0));
                pct = pct2;
            }
            double fillW = pct * Math.Max(28, barWidth - 2);
            // "Life" (LHM Storage Smart.Life = % rated lifetime REMAINING, 100=new, 0=dead) — reverse heat so healthy=green, worn=red.
            bool invertHeat = d is not null && (d.Name.Contains("Life", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("Remaining", StringComparison.OrdinalIgnoreCase) || (d.ComponentType == ComponentType.Battery && d.Category.Equals("Level", StringComparison.OrdinalIgnoreCase)));
            var filled = new Border { HorizontalAlignment = WpfHorizontalAlignment.Left, Width = Math.Max(28, barWidth - 2), Height = Math.Max(4, barHeight - 2), CornerRadius = new CornerRadius(3), Background = ThemeService.HeatBrush(invertHeat ? 1 - pct : pct), Opacity = 1 };
            var clip = new Canvas { Width = Math.Max(28, barWidth - 2), Height = Math.Max(4, barHeight - 2), ClipToBounds = true };
            filled.Width = Math.Max(28, barWidth - 2);
            clip.Children.Add(filled);
            filled.Width = fillW;
            filled.HorizontalAlignment = WpfHorizontalAlignment.Left;
            track.Child = clip;
            Grid.SetColumn(track, 1);
            row.Children.Add(track);
        }
        if (showGraph)
        {
            var graph = MakePerSensorGraph(d, history, graphH, Math.Max(120, boxWidth - 16));
            Children.Add(graph);
        }
    }

    private static Canvas MakePerSensorGraph(SensorDescriptor? d, SensorHistory? history, double graphH, double graphW)
    {
        graphW = Math.Max(120, graphW);
        var canvas = new Canvas { Height = graphH, Width = graphW, Background = ThemeService.Find("GraphBackgroundBrush"), HorizontalAlignment = WpfHorizontalAlignment.Stretch, Margin = new Thickness(0, 0.5, 0, 0) };
        // Gridlines (2 inner lines).
        var grid = ThemeService.Find("PatternDotBrush");
        for (int g = 1; g <= 2; g++)
        {
            double gy = graphH * g / 3.0;
            var gl = new Line { X1 = 2, Y1 = gy, X2 = Math.Max(20, graphW - 4), Y2 = gy, Stroke = grid, StrokeThickness = 0.5, Opacity = 0.35, StrokeDashArray = new DoubleCollection { 2, 3 } };
            canvas.Children.Add(gl);
        }
        // Fixed 5-minute window: right-anchored fill — show only the most recent N samples in the right N/300 of the width.
        const int WindowSamples = 300;
        if (d is null || history is null || history.Values.Count < 2)
        {
            var awaiting = new TextBlock { Text = "—", FontSize = 9, Foreground = ThemeService.Find("TextMutedBrush"), Margin = new Thickness(6, 4, 0, 0) };
            canvas.Children.Add(awaiting);
            return canvas;
        }
        var vals = history.Values.ToArray();
        double max = Math.Max(d.Max, vals.Max());
        double min = Math.Min(d.Min, vals.Min());
        if (max <= min) max = min + 1;
        double step = (graphW - 4) / (double)WindowSamples;
        double x0 = graphW - 2 - step * vals.Length;
        for (int i = 0; i < vals.Length - 1; i++)
        {
            double x1 = x0 + i * step;
            double y1 = graphH - 2 - ((vals[i] - min) / (max - min)) * (graphH - 4);
            double x2 = x0 + (i + 1) * step;
            double y2 = graphH - 2 - ((vals[i + 1] - min) / (max - min)) * (graphH - 4);
            double pct1 = (vals[i] - min) / (max - min);
            var seg = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = ThemeService.HeatBrush(pct1), StrokeThickness = 1.2 };
            canvas.Children.Add(seg);
        }
        return canvas;
    }

    private static Wpfbrush LoadBrush(double pct) => pct switch
    {
        < 0.5 => ThemeService.Find("BarOkayBrush"),
        < 0.8 => ThemeService.Find("BarWarnBrush"),
        _ => ThemeService.Find("BarHotBrush"),
    };
}

public static class ProcessHelper
{
    private static readonly Dictionary<int, TimeSpan> _lastCpu = new();
    private static readonly Dictionary<int, Queue<double>> _cpuHist = new();
    private static readonly Queue<int> _topHistory = new();
    private static DateTime _lastTime = DateTime.UtcNow;
    private static readonly object _lock = new();
    private static readonly int _procs = Environment.ProcessorCount;
    private const int HeatWindow = 60;

    public static List<(string Name, double CpuPercent, long RamMB, int Threads, int Pid, double HeatPct)> GetTop(int limit, string sort)
    {
        var list = new List<(string Name, double CpuPercent, long RamMB, int Threads, int Pid, double HeatPct)>();
        DateTime now = DateTime.UtcNow;
        lock (_lock)
        {
            double elapsed = Math.Max(0.5, (now - _lastTime).TotalSeconds);
            var seen = new HashSet<int>();
            var raw = new List<(string Name, double CpuPercent, long RamMB, int Threads, int Pid)>();
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
                    if (!_cpuHist.TryGetValue(p.Id, out var q)) { q = new Queue<double>(); _cpuHist[p.Id] = q; }
                    q.Enqueue(cpuPct);
                    while (q.Count > HeatWindow) q.Dequeue();
                    long mb;
                    try { mb = p.WorkingSet64 / 1024 / 1024; } catch { mb = 0; }
                    int th;
                    try { th = p.Threads.Count; } catch { th = 0; }
                    raw.Add((SafeName(p), cpuPct, mb, th, p.Id));
                    p.Dispose();
                }
                catch { try { p.Dispose(); } catch { } }
            }
            foreach (int dead in _lastCpu.Keys.Where(id => !seen.Contains(id)).ToList()) { _lastCpu.Remove(dead); _cpuHist.Remove(dead); }
            for (int i = _topHistory.Count - 1; i >= 0; i--) { /* keep PIDs even if dead — they age out after 60s */ }
            if (raw.Count > 0)
            {
                int topPid = raw.OrderByDescending(x => x.CpuPercent).First().Pid;
                _topHistory.Enqueue(topPid);
                while (_topHistory.Count > HeatWindow) _topHistory.Dequeue();
            }
            _lastTime = now;
            double maxAvg = 0;
            var avgs = new Dictionary<int, double>();
            foreach (var item in raw)
            {
                double avg = _cpuHist.TryGetValue(item.Pid, out var qq) && qq.Count > 0 ? qq.Average() : item.CpuPercent;
                avgs[item.Pid] = avg;
                maxAvg = Math.Max(maxAvg, avg);
            }
            foreach (var item in raw)
            {
                double persistence = _topHistory.Count(p => p == item.Pid) / (double)HeatWindow;
                double share = maxAvg > 0 ? avgs[item.Pid] / maxAvg : 0;
                double heat = Math.Clamp(0.6 * persistence + 0.4 * share, 0, 1);
                list.Add((item.Name, item.CpuPercent, item.RamMB, item.Threads, item.Pid, heat));
            }
        }
        return sort switch
        {
            "Ram" => list.OrderByDescending(x => x.RamMB).Take(limit).ToList(),
            "Name" => list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Take(limit).ToList(),
            "Threads" => list.OrderByDescending(x => x.Threads).Take(limit).ToList(),
            _ => list.OrderByDescending(x => x.CpuPercent).Take(limit).ToList(),
        };
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

    public static void Launch(string appId, string args)
    {
        // Fallback only (embedding failed / external). Normal path is TerminalHost embed.
        try
        {
            var path = ResolvePath(appId);
            var psi = new ProcessStartInfo(path)
            {
                Arguments = string.IsNullOrWhiteSpace(args) ? DefaultArgs(appId) : args,
                UseShellExecute = false,
                CreateNoWindow = false,
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to launch {DisplayName(appId)}: {ex.Message}", "Terminal", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
