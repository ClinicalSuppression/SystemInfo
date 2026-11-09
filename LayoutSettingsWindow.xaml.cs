using System.IO;
using System.Linq;
using System.Windows;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace SystemInfo;

public partial class LayoutSettingsWindow : Window
{
    public WindowLayout Result { get; private set; }

    private static readonly (string Display, int Value)[] UpdateIntervalPresets =
    [
        ("Normal — 1000 ms", 1000),
        ("Eco — 2000 ms", 2000),
        ("Fast — 500 ms", 500),
        ("Fastest — 250 ms", 250),
    ];

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
        TrayCheck.IsChecked = current.MinimizeToTray;
        StartupCheck.IsChecked = current.StartWithWindows;
        AdminCheck.IsChecked = current.StartAsAdministrator;
        TopMostCheck.IsChecked = current.KeepOnTop;
        WindowLockedCheck.IsChecked = current.WindowLocked;
        BoxesLockedCheck.IsChecked = current.BoxesLocked;
        HideBackgroundCheck.IsChecked = current.HideBackground;
        UpdatesCheck.IsChecked = current.CheckForUpdatesOnStartup;
        foreach (var kv in UpdateIntervalPresets) UpdateIntervalCombo.Items.Add(kv.Display);
        if (UpdateIntervalCombo.Items.Count > 0)
        {
            var match = UpdateIntervalPresets.FirstOrDefault(kv => kv.Value == current.UpdateIntervalMs);
            UpdateIntervalCombo.SelectedItem = match.Display;
            if (UpdateIntervalCombo.SelectedItem is null) UpdateIntervalCombo.SelectedIndex = 0;
        }

        // Background image state: restore UI from persisted values (path may have been cleared by Normalize when unreadable).
        BackgroundImageCheck.IsChecked = !string.IsNullOrWhiteSpace(current.BackgroundImagePath);
        BackgroundImagePathBox.Text = string.IsNullOrWhiteSpace(current.BackgroundImagePath) ? "(none)" : current.BackgroundImagePath;
        BackgroundOpacitySlider.Value = current.BackgroundImageOpacity;
        BackgroundSaturationSlider.Value = current.BackgroundImageSaturation;
        BackgroundBrightnessSlider.Value = current.BackgroundImageBrightness;
        UpdateOpacityLabel(); UpdateSaturationLabel(); UpdateBrightnessLabel();
        UpdateBackgroundPreview();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Result.FontScale = Math.Clamp(FontCombo.SelectedIndex, 0, 4);
        Result.BarScale = Math.Clamp(BarCombo.SelectedIndex, 0, 4);
        Result.GraphScale = Math.Clamp(GraphCombo.SelectedIndex, 0, 4);
        Result.ThemeMode = ThemeModeCombo.SelectedItem?.ToString() ?? "System";
        Result.ThemeScheme = ThemeSchemeCombo.SelectedItem?.ToString() ?? "Midnight";
        Result.MinimizeToTray = TrayCheck.IsChecked == true;
        Result.StartWithWindows = StartupCheck.IsChecked == true;
        Result.StartAsAdministrator = AdminCheck.IsChecked == true;
        try { StartupHelper.ApplyStartWithWindows(Result.StartWithWindows); } catch { }
        Result.KeepOnTop = TopMostCheck.IsChecked == true;
        Result.WindowLocked = WindowLockedCheck.IsChecked == true;
        Result.BoxesLocked = BoxesLockedCheck.IsChecked == true;
        Result.HideBackground = HideBackgroundCheck.IsChecked == true;
        Result.CheckForUpdatesOnStartup = UpdatesCheck.IsChecked == true;
        Result.UpdateIntervalMs = UpdateIntervalPresets.FirstOrDefault(kv => kv.Display == UpdateIntervalCombo.SelectedItem as string).Value;
        if (Result.UpdateIntervalMs < 250) Result.UpdateIntervalMs = 1000;
        Result.BackgroundImagePath = BackgroundImageCheck.IsChecked == true ? (string.IsNullOrWhiteSpace(BackgroundImagePathBox.Text) || BackgroundImagePathBox.Text == "(none)" ? "" : BackgroundImagePathBox.Text) : "";
        Result.BackgroundImageOpacity = (int)Math.Round(BackgroundOpacitySlider.Value);
        Result.BackgroundImageSaturation = (int)Math.Round(BackgroundSaturationSlider.Value);
        Result.BackgroundImageBrightness = (int)Math.Round(BackgroundBrightnessSlider.Value);
        DialogResult = true;
        Close();
    }

    // ---------- Background image ----------
    private void BackgroundImageCheck_Click(object sender, RoutedEventArgs e) => UpdateBackgroundPreview();

    private void BackgroundOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)Math.Round(e.NewValue);
        if (BackgroundOpacityValue is not null) BackgroundOpacityValue.Text = $"{v}%";
        UpdateBackgroundPreview();
    }
    private void BackgroundSaturationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)Math.Round(e.NewValue);
        if (BackgroundSaturationValue is not null) BackgroundSaturationValue.Text = $"{v}%";
        UpdateBackgroundPreview();
    }
    private void BackgroundBrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)Math.Round(e.NewValue);
        if (BackgroundBrightnessValue is not null) BackgroundBrightnessValue.Text = $"{v}%";
        UpdateBackgroundPreview();
    }
    private void BackgroundImageBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new OpenFileDialog
            {
                Title = "Choose a background image",
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All files (*.*)|*.*",
                FilterIndex = 1,
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(this) == true)
            {
                BackgroundImageCheck!.IsChecked = true;
                BackgroundImagePathBox.Text = dlg.FileName;
                UpdateBackgroundPreview();
            }
        }
        catch { }
    }
    private void ClearBackgroundImageButton_Click(object sender, RoutedEventArgs e)
    {
        BackgroundImageCheck!.IsChecked = false;
        BackgroundImagePathBox.Text = "(none)";
        UpdateBackgroundPreview();
    }
    private void UpdateOpacityLabel() { if (BackgroundOpacityValue is not null) BackgroundOpacityValue.Text = $"{(int)Math.Round(BackgroundOpacitySlider.Value)}%"; }
    private void UpdateSaturationLabel() { if (BackgroundSaturationValue is not null) BackgroundSaturationValue.Text = $"{(int)Math.Round(BackgroundSaturationSlider.Value)}%"; }
    private void UpdateBrightnessLabel() { if (BackgroundBrightnessValue is not null) BackgroundBrightnessValue.Text = $"{(int)Math.Round(BackgroundBrightnessSlider.Value)}%"; }
    private void UpdateBackgroundPreview()
    {
        if (BackgroundPreviewImage is null) return;
        string path = BackgroundImageCheck?.IsChecked == true ? BackgroundImagePathBox.Text : "";
        if (string.IsNullOrWhiteSpace(path) || path == "(none)" || !File.Exists(path))
        {
            BackgroundPreviewImage.Source = null;
            BackgroundPreviewImage.Opacity = 0.3;
            return;
        }
        int sat = (int)Math.Round(BackgroundSaturationSlider.Value);
        int bright = (int)Math.Round(BackgroundBrightnessSlider.Value);
        int opacity = (int)Math.Round(BackgroundOpacitySlider.Value);
        var wb = BackgroundImageService.Transform(path, sat, bright);
        BackgroundPreviewImage.Source = wb;
        BackgroundPreviewImage.Opacity = opacity / 100.0;
    }
}
