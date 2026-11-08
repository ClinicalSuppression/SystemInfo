using System.IO;
using System.Text.Json;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace SystemInfo;

public static class ProfileStore
{
    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemInfo", "profiles");
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static void SaveProfile(Window owner, WindowLayout layout)
    {
        Directory.CreateDirectory(Dir);
        string? path = AskSavePath(owner);
        if (path is null) return;
        File.WriteAllText(path, JsonSerializer.Serialize(LayoutStore.Normalize(layout), Opts));
        MessageBox.Show($"Profile saved to {path}", "Profiles", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public static WindowLayout? LoadProfile(Window owner)
    {
        Directory.CreateDirectory(Dir);
        string? path = AskOpenPath(owner);
        if (path is null) return null;
        try
        {
            var w = JsonSerializer.Deserialize<WindowLayout>(File.ReadAllText(path), Opts);
            if (w is null) return null;
            return LayoutStore.Normalize(w);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Profiles", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    private static string? AskSavePath(Window owner)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save profile",
            Filter = "SystemInfo profiles (*.json)|*.json",
            InitialDirectory = Dir,
        };
        if (dlg.ShowDialog(owner) == true) return dlg.FileName;
        return null;
    }

    private static string? AskOpenPath(Window owner)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Load profile",
            Filter = "SystemInfo profiles (*.json)|*.json",
            InitialDirectory = Dir,
        };
        if (dlg.ShowDialog(owner) == true) return dlg.FileName;
        return null;
    }
}
