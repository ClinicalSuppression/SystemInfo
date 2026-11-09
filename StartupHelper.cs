using Microsoft.Win32;
using System.Diagnostics;

namespace SystemInfo;

/// <summary>Registry Run + self-elevation helpers for startup options.</summary>
public static class StartupHelper
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SystemInfo";

    public static string ExePath => Environment.ProcessPath ?? string.Empty;

    public static void ApplyStartWithWindows(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunPath, true) ?? Registry.CurrentUser.CreateSubKey(RunPath);
            if (enabled)
            {
                string exe = ExePath;
                if (string.IsNullOrWhiteSpace(exe)) exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrWhiteSpace(exe))
                    key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch { }
    }

    public static bool IsElevated()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    /// <returns>True if a relaunch was requested (caller should shutdown).</returns>
    public static bool TryRelaunchElevated(string[] args)
    {
        if (args?.Contains("--elevated") == true) return false;
        if (IsElevated()) return false;
        try
        {
            string exe = ExePath;
            if (string.IsNullOrWhiteSpace(exe)) exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrWhiteSpace(exe)) return false;
            // Forward the original args in addition to --elevated so a re-launched instance
            // (UAC prompt) still honors --json/--minimized/--profile etc.
            string quoted = args is null ? "" : string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            var psi = new ProcessStartInfo(exe, quoted.Length > 0 ? quoted + " --elevated" : "--elevated") { Verb = "runas", UseShellExecute = true };
            Process.Start(psi);
            return true;
        }
        catch { return false; }
    }
}
