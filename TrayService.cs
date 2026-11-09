using System.IO;
using System.Drawing;
using System.Windows;

using WpfNotifyIcon = System.Windows.Forms.NotifyIcon;
using WpfContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using WpfToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;

namespace SystemInfo;

public sealed class TrayService : IDisposable
{
    private readonly WpfNotifyIcon _icon;
    private readonly MainWindow _owner;
    private bool _disposed;
    private static Font? MenuFont;

    public TrayService(MainWindow owner)
    {
        if (MenuFont is null) MenuFont = new Font("Segoe UI", 9F);
        _owner = owner;
        _icon = new WpfNotifyIcon
        {
            Text = "SystemInfo",
            Visible = true,
        };
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "SystemInfo.ico");
            if (!File.Exists(iconPath))
                iconPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "SystemInfo.ico");
            if (File.Exists(iconPath))
                _icon.Icon = new Icon(iconPath);
            else
                _icon.Icon = SystemIcons.Application;
        }
        catch
        {
            _icon.Icon = SystemIcons.Application;
        }
        _icon.DoubleClick += (_, _) => _owner.ShowFromTray();
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left) _owner.ShowFromTray();
        };
        BalloonRequested += OnBalloonRequested;
        RebuildMenu();
    }

    public void RebuildMenu()
    {
        var old = _icon.ContextMenuStrip;
        var menu = new WpfContextMenuStrip();
        // Match the selected WPF theme (readable in both light and dark modes).
        try
        {
            var def = ThemeService.Current;
            System.Drawing.Color bg = System.Drawing.ColorTranslator.FromHtml(def.DialogBackground);
            System.Drawing.Color fg = System.Drawing.ColorTranslator.FromHtml(def.TextPrimary);
            System.Drawing.Color muted = System.Drawing.ColorTranslator.FromHtml(def.TextMuted);
            menu.RenderMode = System.Windows.Forms.ToolStripRenderMode.Professional;
            menu.BackColor = bg;
            menu.ForeColor = fg;
            void ThemeItem(System.Windows.Forms.ToolStripMenuItem mi)
            {
                mi.BackColor = bg; mi.ForeColor = fg; mi.Font = MenuFont;
                foreach (var sub in mi.DropDownItems.OfType<System.Windows.Forms.ToolStripMenuItem>()) ThemeItem(sub);
            }
        }
        catch { }
        void Add(string text, EventHandler onClick)
        {
            var mi = new WpfToolStripMenuItem(text);
            mi.Click += onClick;
            try
            {
                var def = ThemeService.Current;
                mi.BackColor = System.Drawing.ColorTranslator.FromHtml(def.DialogBackground);
                mi.ForeColor = System.Drawing.ColorTranslator.FromHtml(def.TextPrimary);
            }
            catch { }
            menu.Items.Add(mi);
        }
        Add("Open SystemInfo", (_, _) => _owner.ShowFromTray());
        Add("Settings…", (_, _) => _owner.OpenSettingsFromTray());
        var themeMenu = new WpfToolStripMenuItem("Theme");
        foreach (var scheme in new[] { "Midnight", "Aurora", "Ember", "Paper" })
            foreach (var mode in new[] { "System", "Dark", "Light" })
            {
                string s = scheme, m = mode;
                var mi = new WpfToolStripMenuItem($"{s} {m}");
                mi.Checked = (_owner.GetLayoutForTray().ThemeScheme == s && _owner.GetLayoutForTray().ThemeMode == m);
                mi.Click += (_, _) => _owner.ApplyThemeFromTray(s, m);
                themeMenu.DropDownItems.Add(mi);
            }
        menu.Items.Add(themeMenu);
        Add("Profiles", (_, _) => _owner.OpenProfilesFromTray());
        Add("Hide", (_, _) => _owner.HideToTray());
        Add("Exit", (_, _) => _owner.Close());
        try
        {
            var def = ThemeService.Current;
            System.Drawing.Color bg = System.Drawing.ColorTranslator.FromHtml(def.DialogBackground);
            System.Drawing.Color fg = System.Drawing.ColorTranslator.FromHtml(def.TextPrimary);
            void Paint(System.Windows.Forms.ToolStripMenuItem mi)
            {
                mi.BackColor = bg; mi.ForeColor = fg; mi.Font = MenuFont;
                foreach (var sub in mi.DropDownItems.OfType<System.Windows.Forms.ToolStripMenuItem>()) Paint(sub);
            }
            foreach (var it in menu.Items.OfType<System.Windows.Forms.ToolStripMenuItem>()) Paint(it);
        }
        catch { }
        _icon.ContextMenuStrip = menu;
        if (old is not null) { try { old.Dispose(); } catch { } }
    }

    public static event Action<string>? BalloonRequested;
    private void OnBalloonRequested(string msg)
    {
        try { _icon.ShowBalloonTip(2000, "SystemInfo", msg, ToolTipIcon.Info); } catch { }
    }
    /// <summary>Static hook for non-fatal notices. No-ops until a TrayService instance exists.</summary>
    public static void ShowBalloon(string msg) => BalloonRequested?.Invoke(msg);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        BalloonRequested -= OnBalloonRequested;
        try { _icon.Visible = false; _icon.Dispose(); } catch { }
        try { _icon.Icon?.Dispose(); } catch { }
    }
}
