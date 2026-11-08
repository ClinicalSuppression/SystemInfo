using System.Windows.Media;
using Microsoft.Win32;

using WpfApplication = System.Windows.Application;
using Wpfbrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;

namespace SystemInfo;

public sealed class ThemeDefinition
{
    public string Scheme { get; set; } = "Midnight";
    public string Mode { get; set; } = "Dark";
    // Core surfaces
    public string WindowTop { get; set; } = "#142A3E";
    public string WindowBottom { get; set; } = "#0E1A28";
    public string BoxBackground { get; set; } = "#122537";
    public string BoxHeader { get; set; } = "#0D1C2E";
    public string BoxBorder { get; set; } = "#3E6A94";
    public string BoxBorderDim { get; set; } = "#26425F";
    public string WindowBorder { get; set; } = "#3E6A94";
    public string GraphBackground { get; set; } = "#08121E";
    public string DialogBackground { get; set; } = "#0E1A28";
    public string TextPrimary { get; set; } = "#EAF6FF";
    public string TextMuted { get; set; } = "#8FB0CC";
    public string Accent { get; set; } = "#6DD5FA";
    public string AccentDim { get; set; } = "#2E4A6B";
    public string AccentHover { get; set; } = "#3E6A94";
    public string Track { get; set; } = "#0A1624";
    public string TrackBorder { get; set; } = "#1C334A";
    public string BarOkay { get; set; } = "#3BD67A";
    public string BarWarn { get; set; } = "#FFC53D";
    public string BarHot { get; set; } = "#FF5A4E";
    public string PatternDot { get; set; } = "#2A4A6B";
    public string ButtonBackground { get; set; } = "#1C334A";
    public string ButtonBorder { get; set; } = "#3E6A94";
    public string CloseBackground { get; set; } = "#7A2E2E";
    public string ScrollbarThumb { get; set; } = "#2E4A6B";
    public string ScrollbarHover { get; set; } = "#3E6A94";
    public string ResizeGrip { get; set; } = "#1C3049";
    public string[] GraphPalette { get; set; } = ["#6DD5FA", "#3BD67A", "#FFC53D", "#FF5A4E", "#C5A3FF", "#5DD6FF"];
}

public static class ThemeService
{
    private static readonly Dictionary<string, ThemeDefinition> Definitions = new(StringComparer.Ordinal)
    {
        ["MidnightDark"] = new() { Scheme="Midnight", Mode="Dark", WindowTop="#142A3E", WindowBottom="#0E1A28", BoxBackground="#122537", BoxHeader="#0D1C2E", BoxBorder="#3E6A94", BoxBorderDim="#26425F", WindowBorder="#3E6A94", GraphBackground="#08121E", DialogBackground="#0E1A28", TextPrimary="#EAF6FF", TextMuted="#8FB0CC", Accent="#6DD5FA", AccentDim="#2E4A6B", AccentHover="#3E6A94", Track="#0A1624", TrackBorder="#1C334A", BarOkay="#3BD67A", BarWarn="#FFC53D", BarHot="#FF5A4E", PatternDot="#2A4A6B", ButtonBackground="#1C334A", ButtonBorder="#3E6A94", ScrollbarThumb="#2E4A6B", ScrollbarHover="#3E6A94", ResizeGrip="#1C3049" },
        ["MidnightLight"] = new() { Scheme="Midnight", Mode="Light", WindowTop="#EAF2FA", WindowBottom="#D9E6F2", BoxBackground="#FFFFFF", BoxHeader="#D6E4F0", BoxBorder="#B9CDE3", BoxBorderDim="#D0D8E4", WindowBorder="#B9CDE3", GraphBackground="#EEF4FA", DialogBackground="#F2F4F8", TextPrimary="#10233A", TextMuted="#4A6B8A", Accent="#2E6B9E", AccentDim="#8FB0CC", AccentHover="#3E6A94", Track="#E6EAF0", TrackBorder="#B9CDE3", BarOkay="#0F8A4A", BarWarn="#B7791F", BarHot="#C81E1E", PatternDot="#B9CDE3", ButtonBackground="#E6EAF0", ButtonBorder="#B9CDE3", ScrollbarThumb="#8FB0CC", ScrollbarHover="#3E6A94", ResizeGrip="#D6E4F0" },
        ["AuroraDark"] = new() { Scheme="Aurora", Mode="Dark", WindowTop="#122830", WindowBottom="#0B1A1E", BoxBackground="#10232A", BoxHeader="#0A181C", BoxBorder="#2F5D5A", BoxBorderDim="#1E3E3B", WindowBorder="#2F5D5A", GraphBackground="#08181D", DialogBackground="#0B1A1E", TextPrimary="#E8FFF6", TextMuted="#8AC7B5", Accent="#4AE3B2", AccentDim="#2F5D5A", AccentHover="#3A8A7A", Track="#0A1A20", TrackBorder="#1E3E3B", BarOkay="#4AE3B2", BarWarn="#FFC53D", BarHot="#FF5A4E", PatternDot="#2F5D5A", ButtonBackground="#1A2E2A", ButtonBorder="#2F5D5A", ScrollbarThumb="#2F5D5A", ScrollbarHover="#3A8A7A", ResizeGrip="#10232A", GraphPalette=["#4AE3B2","#6DD5FA","#FFC53D","#FF5A4E","#C5A3FF","#5DD6FF"] },
        ["AuroraLight"] = new() { Scheme="Aurora", Mode="Light", WindowTop="#E8F7F2", WindowBottom="#D6EFE6", BoxBackground="#FFFFFF", BoxHeader="#D6EFE6", BoxBorder="#B9D9CC", BoxBorderDim="#D0D8E4", WindowBorder="#B9D9CC", GraphBackground="#EFFAF6", DialogBackground="#F2FAF7", TextPrimary="#0E2E28", TextMuted="#4A7A68", Accent="#0F7A5A", AccentDim="#8AC7B5", AccentHover="#2F5D5A", Track="#E6EAF0", TrackBorder="#B9D9CC", BarOkay="#0F7A5A", BarWarn="#B7791F", BarHot="#C81E1E", PatternDot="#B9D9CC", ButtonBackground="#E6EAF0", ButtonBorder="#B9D9CC", ScrollbarThumb="#5AA68A", ScrollbarHover="#2F5D5A", ResizeGrip="#D6EFE6" },
        ["EmberDark"] = new() { Scheme="Ember", Mode="Dark", WindowTop="#2A1E22", WindowBottom="#1A121A", BoxBackground="#241A20", BoxHeader="#1D1318", BoxBorder="#5A3A3A", BoxBorderDim="#3A2529", WindowBorder="#5A3A3A", GraphBackground="#1D1216", DialogBackground="#1A121A", TextPrimary="#FFF3E6", TextMuted="#C9AFA0", Accent="#FFB86B", AccentDim="#5A3A3A", AccentHover="#7A4A3A", Track="#1D1216", TrackBorder="#3A2529", BarOkay="#7AC74F", BarWarn="#FFB86B", BarHot="#FF5A4E", PatternDot="#5A3A3A", ButtonBackground="#332224", ButtonBorder="#5A3A3A", ScrollbarThumb="#5A3A3A", ScrollbarHover="#7A4A3A", ResizeGrip="#241A20", GraphPalette=["#FFB86B","#FF5A4E","#FFC53D","#FF8A80","#C5A3FF","#6DD5FA"] },
        ["EmberLight"] = new() { Scheme="Ember", Mode="Light", WindowTop="#FFF7ED", WindowBottom="#F5E6D8", BoxBackground="#FFFFFF", BoxHeader="#F5E6D8", BoxBorder="#E0C5B0", BoxBorderDim="#D0D8E4", WindowBorder="#E0C5B0", GraphBackground="#FFF7ED", DialogBackground="#FFF7ED", TextPrimary="#3A2415", TextMuted="#9A7A60", Accent="#B5533C", AccentDim="#E0C5B0", AccentHover="#5A3A3A", Track="#F5E6D8", TrackBorder="#E0C5B0", BarOkay="#0F7A3A", BarWarn="#B7791F", BarHot="#C81E1E", PatternDot="#E0C5B0", ButtonBackground="#F5E6D8", ButtonBorder="#E0C5B0", ScrollbarThumb="#C9AFA0", ScrollbarHover="#5A3A3A", ResizeGrip="#F5E6D8" },
        ["PaperDark"] = new() { Scheme="Paper", Mode="Dark", WindowTop="#1A1E24", WindowBottom="#111418", BoxBackground="#1A1E24", BoxHeader="#111418", BoxBorder="#3A4048", BoxBorderDim="#2A2E34", WindowBorder="#3A4048", GraphBackground="#0C0E10", DialogBackground="#111418", TextPrimary="#FFFFFF", TextMuted="#A0A8B0", Accent="#FFFFFF", AccentDim="#3A4048", AccentHover="#5A6068", Track="#0C0E10", TrackBorder="#2A2E34", BarOkay="#FFFFFF", BarWarn="#C0C8D0", BarHot="#FF5A4E", PatternDot="#2A2E34", ButtonBackground="#2A2E34", ButtonBorder="#3A4048", ScrollbarThumb="#3A4048", ScrollbarHover="#5A6068", ResizeGrip="#1A1E24" },
        ["PaperLight"] = new() { Scheme="Paper", Mode="Light", WindowTop="#FAF7F2", WindowBottom="#F0EAE0", BoxBackground="#FFFFFF", BoxHeader="#EFE9DD", BoxBorder="#E0D6C2", BoxBorderDim="#D0D8E4", WindowBorder="#E0D6C2", GraphBackground="#FAF7F2", DialogBackground="#FAF7F2", TextPrimary="#342A1E", TextMuted="#8A7A64", Accent="#B5533C", AccentDim="#E0D6C2", AccentHover="#9A7A60", Track="#EFE9DD", TrackBorder="#E0D6C2", BarOkay="#3A7D44", BarWarn="#B7791F", BarHot="#C81E1E", PatternDot="#E0D6C2", ButtonBackground="#EFE9DD", ButtonBorder="#E0D6C2", ScrollbarThumb="#C9BFB0", ScrollbarHover="#9A7A60", ResizeGrip="#EFE9DD" },
    };

    private static ThemeDefinition _current = Definitions["MidnightDark"];

    public static ThemeDefinition Current => _current;

    public static string ResolveEffectiveMode(string themeMode)
    {
        if (themeMode == "Light" || themeMode == "Dark") return themeMode;
        // System: HKCU\...\Personalize\AppsUseLightTheme
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = key?.GetValue("AppsUseLightTheme");
            if (v is int i) return i == 0 ? "Dark" : "Light";
        }
        catch { }
        return "Dark";
    }

    public static void Apply(string scheme, string mode, bool colorBlindSafe = false)
    {
        string key = (scheme ?? "Midnight") + (mode == "Light" ? "Light" : "Dark");
        if (!Definitions.TryGetValue(key, out var def))
            def = Definitions["MidnightDark"];
        _current = def;
        // Publish tokens as Application resources so DynamicResource / FindResource work.
        try
        {
            var res = WpfApplication.Current?.Resources;
            if (res is not null)
            {
                res["WindowBackgroundBrush"] = WindowBackgroundBrush(def);
                res["WindowBorderBrush"] = Brush(def.WindowBorder);
                res["BoxBackgroundBrush"] = Brush(def.BoxBackground);
                res["BoxHeaderBackgroundBrush"] = Brush(def.BoxHeader);
                res["BoxBorderBrush"] = Brush(def.BoxBorder);
                res["BoxBorderDimBrush"] = Brush(def.BoxBorderDim);
                res["BoxHighlightBrush"] = Brush("#FFFFFF20");
                res["ShellBackgroundBrush"] = Brush(def.WindowTop);
                res["TextPrimaryBrush"] = Brush(def.TextPrimary);
                res["TextMutedBrush"] = Brush(def.TextMuted);
                res["AccentBrush"] = Brush(def.Accent);
                res["AccentDimBrush"] = Brush(def.AccentDim);
                res["AccentHoverBrush"] = Brush(def.AccentHover);
                res["TrackBrush"] = Brush(def.Track);
                res["TrackBorderBrush"] = Brush(def.TrackBorder);
                res["BarOkayBrush"] = Brush(def.BarOkay);
                res["BarWarnBrush"] = Brush(def.BarWarn);
                res["BarHotBrush"] = Brush(def.BarHot);
                res["PatternDotBrush"] = Brush(def.PatternDot);
                res["DialogBackgroundBrush"] = Brush(def.DialogBackground);
                res["ButtonBackgroundBrush"] = Brush(def.ButtonBackground);
                res["ButtonBorderBrush"] = Brush(def.ButtonBorder);
                res["ScrollbarThumbBrush"] = Brush(def.ScrollbarThumb);
                res["ScrollbarHoverBrush"] = Brush(def.ScrollbarHover);
                res["ResizeGripBrush"] = Brush(def.ResizeGrip);
                res["GraphBackgroundBrush"] = Brush(def.GraphBackground);
                res["WindowPatternBrush"] = CreatePatternBrush(def.PatternDot);
                string[] palette = colorBlindSafe
                    ? new[] { "#0072B2", "#E69F00", "#009E73", "#CC79A7", "#56B4E9", "#F0E442" }
                    : def.GraphPalette;
                for (int i = 0; i < palette.Length; i++)
                    res[$"GraphPalette{i}"] = Brush(palette[i]);
                if (colorBlindSafe)
                {
                    res["BarOkayBrush"] = Brush("#0072B2");
                    res["BarWarnBrush"] = Brush("#E69F00");
                    res["BarHotBrush"] = Brush("#CC79A7");
                }
            }
        }
        catch { }
    }

    public static Wpfbrush WindowBackgroundBrush(ThemeDefinition d)
    {
        var gb = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(0, 1),
        };
        gb.GradientStops.Add(new GradientStop(Color(d.WindowTop), 0));
        gb.GradientStops.Add(new GradientStop(Color(d.WindowBottom), 1));
        gb.Freeze();
        return gb;
    }

    public static SolidColorBrush Brush(string hex) => new(Color(hex));
    public static SolidColorBrush Brush(string hex, byte a) => new(Color(hex)) { Opacity = a / 255.0 };

    public static WpfColor Color(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 8) return WpfColor.FromArgb(Convert.ToByte(hex[0..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16), Convert.ToByte(hex[6..8], 16));
        return WpfColor.FromRgb(Convert.ToByte(hex[0..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
    }

    public static SolidColorBrush Find(string key)
    {
        try
        {
            if (WpfApplication.Current?.FindResource(key) is SolidColorBrush b) return b;
        }
        catch { }
        return new SolidColorBrush(Colors.Transparent);
    }

    public static SolidColorBrush HeatBrush(double pct01)
    {
        pct01 = Math.Clamp(pct01, 0, 1);
        WpfColor lo = Color(_current.BarOkay);
        WpfColor mid = Color(_current.BarWarn);
        WpfColor hi = Color(_current.BarHot);
        if (pct01 < 0.5)
        {
            double t = pct01 / 0.5;
            return new SolidColorBrush(Lerp(lo, mid, t));
        }
        double t2 = (pct01 - 0.5) / 0.5;
        return new SolidColorBrush(Lerp(mid, hi, t2));
    }

    private static WpfColor Lerp(WpfColor a, WpfColor b, double t)
    {
        return WpfColor.FromRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    public static Wpfbrush CreatePatternBrush(string dotHex)
    {
        var c = Color(dotHex);
        var b = new SolidColorBrush(WpfColor.FromArgb(28, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}
