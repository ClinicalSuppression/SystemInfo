using System.Windows;

namespace SystemInfo;

public partial class LayoutSettingsWindow : Window
{
    public WindowLayout Result { get; private set; }

    public LayoutSettingsWindow(WindowLayout current)
    {
        Result = current;
        InitializeComponent();
        for (int i = 0; i < 5; i++)
        {
            FontCombo.Items.Add($"{ScaleHelper.ScaleNames[i]} — {ScaleHelper.FontSizes[i]:0}px");
            BarCombo.Items.Add($"{ScaleHelper.ScaleNames[i]} — {ScaleHelper.BarHeights[i]:0}px bar");
            GraphCombo.Items.Add($"{ScaleHelper.ScaleNames[i]} — {ScaleHelper.GraphHeights[i]:0}px graph");
        }
        FontCombo.SelectedIndex = current.FontScale;
        BarCombo.SelectedIndex = current.BarScale;
        GraphCombo.SelectedIndex = current.GraphScale;
        foreach (var m in new[] { "System", "Dark", "Light" }) ThemeModeCombo.Items.Add(m);
        ThemeModeCombo.SelectedItem = current.ThemeMode;
        foreach (var s in new[] { "Midnight", "Aurora", "Ember", "Paper" }) ThemeSchemeCombo.Items.Add(s);
        ThemeSchemeCombo.SelectedItem = current.ThemeScheme;
        TempAlertBox.Text = current.TempAlertThreshold.ToString("0");
        TrayCheck.IsChecked = current.MinimizeToTray;
        StartupCheck.IsChecked = current.StartWithWindows;
        AdminCheck.IsChecked = current.StartAsAdministrator;
        TopMostCheck.IsChecked = current.KeepOnTop;
        WindowLockedCheck.IsChecked = current.WindowLocked;
        BoxesLockedCheck.IsChecked = current.BoxesLocked;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Result.FontScale = Math.Clamp(FontCombo.SelectedIndex, 0, 4);
        Result.BarScale = Math.Clamp(BarCombo.SelectedIndex, 0, 4);
        Result.GraphScale = Math.Clamp(GraphCombo.SelectedIndex, 0, 4);
        Result.ThemeMode = ThemeModeCombo.SelectedItem?.ToString() ?? "System";
        Result.ThemeScheme = ThemeSchemeCombo.SelectedItem?.ToString() ?? "Midnight";
        if (double.TryParse(TempAlertBox.Text, out double t)) Result.TempAlertThreshold = Math.Clamp(t, 50, 110);
        Result.MinimizeToTray = TrayCheck.IsChecked == true;
        Result.StartWithWindows = StartupCheck.IsChecked == true;
        Result.StartAsAdministrator = AdminCheck.IsChecked == true;
        try { StartupHelper.ApplyStartWithWindows(Result.StartWithWindows); } catch { }
        Result.KeepOnTop = TopMostCheck.IsChecked == true;
        Result.WindowLocked = WindowLockedCheck.IsChecked == true;
        Result.BoxesLocked = BoxesLockedCheck.IsChecked == true;
        DialogResult = true;
        Close();
    }
}
