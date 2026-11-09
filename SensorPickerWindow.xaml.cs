using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfColor = System.Windows.Media.Color;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace SystemInfo;

public partial class SensorPickerWindow : Window
{
    private readonly string _componentKey;
    private readonly IReadOnlyDictionary<string, SensorDescriptor> _catalog;
    private readonly IReadOnlyDictionary<string, double?> _live;
    private readonly string _lhmSummary;
    private readonly HashSet<string> _show = new(StringComparer.Ordinal);
    private readonly HashSet<string> _text = new(StringComparer.Ordinal);
    private readonly HashSet<string> _bar = new(StringComparer.Ordinal);
    private readonly HashSet<string> _mini = new(StringComparer.Ordinal);
    private readonly HashSet<string> _header = new(StringComparer.Ordinal);
    private readonly HashSet<string> _collapsedGroups = new(StringComparer.Ordinal);
    private Dictionary<string, int> _colors = new(StringComparer.Ordinal);
    public List<string> SensorIds { get; private set; } = [];
    public List<string> GraphSensorIds { get; private set; } = [];
    public Dictionary<string, int> SensorModes { get; private set; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> GraphSeriesColors { get; private set; } = new(StringComparer.Ordinal);
    public string GraphScaleMode { get; private set; } = "Auto";
    public double GraphLineThickness { get; private set; } = 1.4;
    public string NetworkSpeedUnit { get; private set; } = "Auto";

    public SensorPickerWindow(string boxTitle, string componentKey, IReadOnlyDictionary<string, SensorDescriptor> catalog, IEnumerable<string> existingShow, IEnumerable<string> existingGraph)
        : this(boxTitle, componentKey, catalog, existingShow, existingGraph, null, null, "Auto", 1.4, null, "", "Auto")
    {
    }

    public SensorPickerWindow(string boxTitle, string componentKey, IReadOnlyDictionary<string, SensorDescriptor> catalog, IEnumerable<string> existingShow, IEnumerable<string> existingGraph, IReadOnlyDictionary<string, int>? existingModes)
        : this(boxTitle, componentKey, catalog, existingShow, existingGraph, existingModes, null, "Auto", 1.4, null, "", "Auto")
    {
    }

    public SensorPickerWindow(string boxTitle, string componentKey, IReadOnlyDictionary<string, SensorDescriptor> catalog, IEnumerable<string> existingShow, IEnumerable<string> existingGraph, IReadOnlyDictionary<string, int>? existingModes, IReadOnlyDictionary<string, double?>? live, string scaleMode, double lineThickness, IReadOnlyDictionary<string, int>? colors, string lhmSummary, string networkSpeedUnit)
    {
        _componentKey = componentKey;
        _catalog = catalog;
        _live = live ?? new Dictionary<string, double?>(StringComparer.Ordinal);
        _lhmSummary = lhmSummary ?? "";
        NetworkSpeedUnit = networkSpeedUnit is "B/s" or "KB/s" or "MB/s" or "GB/s" ? networkSpeedUnit : "Auto";
        GraphScaleMode = scaleMode is "Fixed" ? "Fixed" : "Auto";
        GraphLineThickness = Math.Clamp(lineThickness <= 0 ? 1.4 : lineThickness, 0.8, 3.0);
        if (colors is not null) _colors = new Dictionary<string, int>(colors, StringComparer.Ordinal);
        GraphSeriesColors = _colors;
        foreach (var id in existingGraph)
            _header.Add(id); // Legacy header-graph series: migrate shown ones into per-sensor Graph.
        foreach (var id in existingShow)
        {
            _show.Add(id);
            if (existingModes is not null && existingModes.TryGetValue(id, out int m) && m != 0)
            {
                if (ScaleHelper.ModeHasText(m)) _text.Add(id);
                if (ScaleHelper.ModeHasBar(m)) _bar.Add(id);
                if (ScaleHelper.ModeHasGraph(m)) _mini.Add(id);
            }
            else
            {
                _text.Add(id);
                if (_catalog.TryGetValue(id, out var d) && ScaleHelper.ShouldShowBar(d.Category))
                    _bar.Add(id);
            }
            // Legacy header series for a shown sensor become its above-graph.
            if (_header.Contains(id)) _mini.Add(id);
        }
        InitializeComponent();
        TitleText.Text = $"Sensors — {ShortComponentName(boxTitle)}";
        LhmText.Text = string.IsNullOrWhiteSpace(_lhmSummary) ? "Source: LibreHardwareMonitor (live values shown below)." : _lhmSummary;
        foreach (System.Windows.Controls.ComboBoxItem it in ThicknessBox.Items.OfType<System.Windows.Controls.ComboBoxItem>())
            if (it.Tag?.ToString() == GraphLineThickness.ToString()) ThicknessBox.SelectedItem = it;
        if (ThicknessBox.SelectedItem is null) ThicknessBox.SelectedIndex = 1;
        foreach (System.Windows.Controls.ComboBoxItem it in ScaleBox.Items.OfType<System.Windows.Controls.ComboBoxItem>())
            if (it.Tag?.ToString() == GraphScaleMode) ScaleBox.SelectedItem = it;
        if (ScaleBox.SelectedItem is null) ScaleBox.SelectedIndex = 0;
        foreach (System.Windows.Controls.ComboBoxItem it in NetworkSpeedBox.Items.OfType<System.Windows.Controls.ComboBoxItem>())
            if (it.Tag?.ToString() == NetworkSpeedUnit) NetworkSpeedBox.SelectedItem = it;
        if (NetworkSpeedBox.SelectedItem is null) NetworkSpeedBox.SelectedIndex = 0;
        SearchBox.TextChanged += (_, _) => Rebuild();
        Rebuild();
    }

    private static string ShortComponentName(string full)
    {
        if (string.IsNullOrWhiteSpace(full)) return full;
        string s = full.Trim();
        // Remove vendor prefixes (keep the brand/model: Ryzen, Core, RX, RTX, Arc, ...).
        foreach (string v in new[] { "Advanced Micro Devices", "AMD", "Intel", "NVIDIA", "Qualcomm", "With " })
        {
            if (s.StartsWith(v + " ", StringComparison.OrdinalIgnoreCase))
                s = s[(v.Length + 1)..].TrimStart();
        }
        // Remove product-line words that carry no model identity.
        string[] drop = { "Radeon", "GeForce", "with", "Graphics", "Processor" };
        // Split "with ..." suffix first (e.g. "... with Radeon Graphics").
        int withIdx = s.IndexOf(" with ", StringComparison.OrdinalIgnoreCase);
        if (withIdx >= 0) s = s[..withIdx].Trim();
        var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !drop.Contains(p, StringComparer.OrdinalIgnoreCase))
            .ToList();
        string s2 = string.Join(" ", parts);
        if (string.IsNullOrWhiteSpace(s2)) s2 = full;
        // Keep it short: prefer the longest meaningful tail (model number + name).
        if (s2.Length > 26)
        {
            var toks = s2.Split(' ');
            // e.g. "Ryzen 7 5700G" -> keep last needed toks from the end while keeping first token.
            if (toks.Length > 3) s2 = toks[0] + " " + string.Join(" ", toks.Skip(1).TakeLast(Math.Min(4, toks.Length - 1)));
        }
        return s2.Trim();
    }

    private static string GroupHeading(string category)
    {
        return category.ToLowerInvariant() switch
        {
            "clock" or "frequency" => "Frequency",
            "load" => "Load",
            "temperature" => "Temperature",
            "power" => "Power",
            "voltage" => "Voltage",
            "current" => "Current",
            "fan" => "Fan",
            "flow" => "Flow",
            "throughput" => "Throughput",
            "level" => "Level",
            "data" or "smalldata" => "Data",
            "factor" => "Factor",
            "control" => "Control",
            _ => category,
        };
    }

    private static int CategoryOrder(string heading) => heading switch
    {
        "Load" => 0,
        "Temperature" => 1,
        "Power" => 2,
        "Frequency" => 3,
        "Voltage" => 4,
        "Current" => 5,
        "Fan" => 6,
        "Throughput" => 7,
        "Flow" => 8,
        "Level" => 9,
        "Control" => 10,
        "Factor" => 11,
        "Data" => 12,
        _ => 13,
    };

    private static List<string> Tokenize(string s)
    {
        var list = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            int j = i;
            bool isNum = char.IsDigit(s[i]);
            while (j < s.Length && char.IsDigit(s[j]) == isNum) j++;
            list.Add(s[i..j]);
            i = j;
        }
        return list;
    }

    private static int NaturalCompare(string a, string b)
    {
        var ta = Tokenize(a);
        var tb = Tokenize(b);
        int n = Math.Min(ta.Count, tb.Count);
        for (int i = 0; i < n; i++)
        {
            bool an = long.TryParse(ta[i], out long av);
            bool bn = long.TryParse(tb[i], out long bv);
            int cmp;
            if (an && bn) cmp = av.CompareTo(bv);
            else cmp = string.Compare(ta[i], tb[i], StringComparison.OrdinalIgnoreCase);
            if (cmp != 0) return cmp;
        }
        return ta.Count.CompareTo(tb.Count);
    }

    private void Rebuild()
    {
        ListPanel.Children.Clear();
        string filter = (SearchBox.Text ?? "").Trim().ToLowerInvariant();
        IEnumerable<SensorDescriptor> pool = _catalog.Values;
        // For component boxes show that hardware's sensors; custom shows all. Header picks within same pool.
        if (!string.IsNullOrEmpty(_componentKey) && _componentKey.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
        {
            string want = _componentKey[5..].ToLowerInvariant();
            pool = pool.Where(d => d.ComponentType.ToString().ToLowerInvariant() == want);
        }
        else if (!string.IsNullOrEmpty(_componentKey) && _componentKey != "custom")
            pool = pool.Where(d => d.ComponentKey == _componentKey);
        var allPool = pool.ToList();
        int distinctComponents = allPool.Select(d => d.ComponentKey).Distinct(StringComparer.Ordinal).Count();
        bool multiComponent = distinctComponents > 1;
        var shortNames = allPool.GroupBy(d => d.ComponentKey)
            .ToDictionary(g => g.Key, g => ShortComponentName(g.First().ComponentName), StringComparer.Ordinal);
        var eligible = allPool
            .Where(d => filter.Length == 0 || d.Name.ToLowerInvariant().Contains(filter) || d.Category.ToLowerInvariant().Contains(filter) || d.ComponentName.ToLowerInvariant().Contains(filter) || shortNames[d.ComponentKey].ToLowerInvariant().Contains(filter))
            .ToList();
        // Group by friendly category heading for a collapsible tree.
        var groups = eligible.GroupBy(d => GroupHeading(d.Category))
            .OrderBy(g => CategoryOrder(g.Key))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            string heading = group.Key;
            var sensors = group.OrderBy(d => d, Comparer<SensorDescriptor>.Create((a, b) => NaturalCompare(a.Name, b.Name))).ToList();
            var expander = new System.Windows.Controls.Expander
            {
                Header = new TextBlock { Text = $"{heading}  ({sensors.Count})", FontFamily = new WpfFontFamily("Segoe UI"), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = ThemeService.Find("TextMutedBrush") },
                IsExpanded = !_collapsedGroups.Contains(heading),
                Margin = new Thickness(0, 6, 0, 2),
            };
            expander.Expanded += (_, _) => _collapsedGroups.Remove(heading);
            expander.Collapsed += (_, _) => _collapsedGroups.Add(heading);
            var inner = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
            foreach (var d in sensors)
            {
                var panel = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                var cbShow = new WpfCheckBox { IsChecked = _show.Contains(d.Id), Tag = d.Id, Width = 52, FontFamily = new WpfFontFamily("Segoe UI"), FontSize = 11, VerticalAlignment = System.Windows.VerticalAlignment.Center };
                var cbText = new WpfCheckBox { Content = "Text", IsChecked = _text.Contains(d.Id), Tag = d.Id, IsEnabled = _show.Contains(d.Id), FontFamily = new WpfFontFamily("Segoe UI"), FontSize = 11, VerticalAlignment = System.Windows.VerticalAlignment.Center, ToolTip = "Numeric value, always far-right" };
                var cbBar = new WpfCheckBox { Content = "Bar", IsChecked = _bar.Contains(d.Id), Tag = d.Id, IsEnabled = _show.Contains(d.Id), FontFamily = new WpfFontFamily("Segoe UI"), FontSize = 11, VerticalAlignment = System.Windows.VerticalAlignment.Center, ToolTip = ScaleHelper.ShouldShowBar(d.Category) ? "Bar between name and text (all bars in the box share one width)" : "Bar between name and text (scaled to recent history)" };
                var cbMini = new WpfCheckBox { Content = "Graph (below)", IsChecked = _mini.Contains(d.Id), Tag = d.Id, IsEnabled = _show.Contains(d.Id), FontFamily = new WpfFontFamily("Segoe UI"), FontSize = 11, VerticalAlignment = System.Windows.VerticalAlignment.Center, ToolTip = "Graph directly below this row (≈3× text height)" };
                int palIdx = _colors.TryGetValue(d.Id, out int ci) ? ci % 6 : 0;
                string hex = ThemeService.Current.GraphPalette[palIdx];
                var sw = new System.Windows.Controls.Border { Width = 18, Height = 8, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(6, 0, 2, 0), ToolTip = "Cycle line colour" };
                try { sw.Background = new WpfSolidColorBrush((WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(hex)); } catch { sw.Background = System.Windows.Media.Brushes.Gray; }
                sw.MouseLeftButtonUp += (_, _) =>
                {
                    int cur = _colors.TryGetValue(d.Id, out int c) ? c : 0;
                    int nxt = (cur + 1) % 6;
                    _colors[d.Id] = nxt; GraphSeriesColors = _colors;
                    try { sw.Background = new WpfSolidColorBrush((WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(ThemeService.Current.GraphPalette[nxt])); } catch { }
                };
                string liveStr = "—";
                if (_live.TryGetValue(d.Id, out var lv)) liveStr = ScaleHelper.Format(lv, d, NetworkSpeedUnit);
                string shortName = shortNames[d.ComponentKey];
                string rowLabel = multiComponent
                    ? $"{shortName} — {d.Name}  = {liveStr}"
                    : $"{d.Name}  = {liveStr}";
                var name = new TextBlock { Text = rowLabel, FontFamily = new WpfFontFamily("Segoe UI"), FontSize = 11, Foreground = ThemeService.Find("TextPrimaryBrush"), VerticalAlignment = System.Windows.VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Width = 330, ToolTip = $"{d.ComponentName} — {d.Name} ({d.Category} [{d.Unit}]) = {liveStr}" };
                cbShow.Checked += (_, _) => { _show.Add(d.Id); if (!_text.Contains(d.Id)) _text.Add(d.Id); if (ScaleHelper.ShouldShowBar(d.Category) && !_bar.Contains(d.Id)) _bar.Add(d.Id); cbText.IsEnabled = cbBar.IsEnabled = cbMini.IsEnabled = true; };
                cbShow.Unchecked += (_, _) => { _show.Remove(d.Id); cbText.IsEnabled = cbBar.IsEnabled = cbMini.IsEnabled = false; };
                cbText.Checked += (_, _) => _text.Add(d.Id);
                cbText.Unchecked += (_, _) => _text.Remove(d.Id);
                cbBar.Checked += (_, _) => _bar.Add(d.Id);
                cbBar.Unchecked += (_, _) => _bar.Remove(d.Id);
                cbMini.Checked += (_, _) => _mini.Add(d.Id);
                cbMini.Unchecked += (_, _) => _mini.Remove(d.Id);
                panel.Children.Add(cbShow);
                panel.Children.Add(cbText);
                panel.Children.Add(cbBar);
                panel.Children.Add(cbMini);
                panel.Children.Add(sw);
                panel.Children.Add(name);
                inner.Children.Add(panel);
            }
            expander.Content = inner;
            ListPanel.Children.Add(expander);
        }
        if (eligible.Count == 0)
            ListPanel.Children.Add(new TextBlock { Text = "No sensors for this component (or no match).", FontFamily = new WpfFontFamily("Segoe UI"), Foreground = ThemeService.Find("TextMutedBrush") });
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        SensorIds = _show.ToList();
        GraphSensorIds = _mini.Intersect(SensorIds).ToList(); // Per-sensor above-graphs; no separate main/header graph.
        SensorModes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string id in SensorIds)
        {
            int m = 0;
            if (_text.Contains(id)) m |= (int)RowMode.Text;
            if (_bar.Contains(id)) m |= (int)RowMode.Bar;
            if (_mini.Contains(id)) m |= (int)RowMode.Graph;
            if (m == 0) m = (int)RowMode.Text;
            SensorModes[id] = m;
        }
        GraphScaleMode = (ScaleBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "Auto";
        if (ThicknessBox.SelectedItem is System.Windows.Controls.ComboBoxItem tb && tb.Tag?.ToString() is string ts && double.TryParse(ts, System.Globalization.CultureInfo.InvariantCulture, out double td))
            GraphLineThickness = td;
        NetworkSpeedUnit = (NetworkSpeedBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "Auto";
        GraphSeriesColors = new Dictionary<string, int>(_colors, StringComparer.Ordinal);
        DialogResult = true;
        Close();
    }
}
