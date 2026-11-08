// Embedded terminal hosting: runs cmd / PowerShell / PuTTY / Windows Terminal
// inside the app's own box so the program stays contained in that box.
// Clean approach: start the terminal as a Win32 top-level window, then
// SetParent it into a small invisible host HWND (HwndHost) inside the box
// and strip its caption/frame so it looks owned by the box. Moving or
// resizing the box moves the host (and therefore the terminal) with it.
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace SystemInfo;

/// <summary>WPF HwndHost that owns one hosted terminal child window.</summary>
public sealed class TerminalHwndHost : HwndHost
{
    private IntPtr _hwnd;

    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        int w = Math.Max(10, (int)ActualWidth);
        int h = Math.Max(10, (int)ActualHeight);
        _hwnd = Win32.CreateWindowEx(0, "STATIC", "",
            Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_CLIPCHILDREN | Win32.WS_CLIPSIBLINGS,
            0, 0, w, h, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef parent)
    {
        if (_hwnd != IntPtr.Zero)
        {
            Win32.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
    public IntPtr HostHwnd => _hwnd;

    public bool TryGetPhysicalSize(out int w, out int h)
    {
        w = Math.Max(10, (int)ActualWidth);
        h = Math.Max(10, (int)ActualHeight);
        try
        {
            var src = PresentationSource.FromVisual(this) as HwndSource;
            if (src?.CompositionTarget is not null)
            {
                Matrix m = src.CompositionTarget.TransformToDevice;
                w = Math.Max(10, (int)Math.Round(ActualWidth * m.M11));
                h = Math.Max(10, (int)Math.Round(ActualHeight * m.M22));
            }
        }
        catch { }
        return true;
    }

    public void ResizePhysical(int w, int h)
    {
        if (_hwnd == IntPtr.Zero) return;
        Win32.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, w, h, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }
}

internal sealed class TerminalSession
{
    public BoxLayout Layout { get; init; } = null!;
    public string AppId { get; set; } = "cmd";
    public string Args { get; set; } = "";
    public Process? Process { get; set; }
    public IntPtr ChildHwnd { get; set; }
    public TerminalHwndHost? CurrentHost { get; set; }
    public DispatcherTimer? PollTimer { get; set; }
    public int PollTicks { get; set; }
    public HashSet<IntPtr> BeforeWindows { get; set; } = new();
    public string? LastError { get; set; }
    public bool Exited { get; set; }

    public bool IsAlive()
    {
        if (Exited) return false;
        if (ChildHwnd == IntPtr.Zero) return false;
        if (!Win32.IsWindow(ChildHwnd)) return false;
        if (Process is null) return true; // window exists even if process object lost
        try { return !Process.HasExited; } catch { return true; }
    }
}

/// <summary>Owns one embedded terminal per app-box layout. Survives WPF box recreation.</summary>
public static class TerminalHost
{
    private static readonly Dictionary<BoxLayout, TerminalSession> s_sessions = new();

    public static void AttachOrStart(BoxLayout layout, TerminalHwndHost host)
    {
        if (s_sessions.TryGetValue(layout, out var existing))
        {
            if (existing.IsAlive())
            {
                existing.CurrentHost = host;
                Attach(existing, host);
                return;
            }
            // stale/exited session: drop it and start fresh
            existing.StopPoll();
            s_sessions.Remove(layout);
        }
        Start(layout, host);
    }

    public static void Detach(BoxLayout layout)
    {
        if (!s_sessions.TryGetValue(layout, out var s)) return;
        s.CurrentHost = null;
        IntPtr child = s.ChildHwnd;
        if (child != IntPtr.Zero && Win32.IsWindow(child))
        {
            try
            {
                Win32.SetParent(child, IntPtr.Zero);
                Win32.ShowWindow(child, Win32.SW_HIDE);
            }
            catch { }
        }
    }

    public static void Kill(BoxLayout layout)
    {
        if (!s_sessions.TryGetValue(layout, out var s)) return;
        s_sessions.Remove(layout);
        s.StopPoll();
        IntPtr child = s.ChildHwnd;
        try
        {
            if (s.Process is not null && !s.Process.HasExited)
                s.Process.Kill(entireProcessTree: true);
        }
        catch { try { s.Process?.Kill(); } catch { } }
        try
        {
            if (child != IntPtr.Zero && Win32.IsWindow(child))
            {
                Win32.SetParent(child, IntPtr.Zero);
                Win32.DestroyWindow(child);
            }
        }
        catch { }
        try { s.Process?.Dispose(); } catch { }
    }

    public static void KillAll()
    {
        foreach (var layout in s_sessions.Keys.ToList()) Kill(layout);
    }

    public static void Restart(BoxLayout layout, TerminalHwndHost host)
    {
        Kill(layout);
        Start(layout, host);
    }

    public static void Position(BoxLayout layout, TerminalHwndHost host)
    {
        if (!s_sessions.TryGetValue(layout, out var s)) return;
        if (!s.IsAlive()) return;
        IntPtr child = s.ChildHwnd;
        IntPtr hostHwnd = host.HostHwnd;
        if (child == IntPtr.Zero || hostHwnd == IntPtr.Zero) return;
        if (!Win32.IsWindow(child)) return;
        // Enforce confinement: if the child drifted to the desktop, re-parent it into the host.
        try
        {
            IntPtr parent = Win32.GetParent(child);
            if (parent != hostHwnd)
            {
                s.CurrentHost = host;
                Attach(s, host);
                return;
            }
        }
        catch { }
        if (host.TryGetPhysicalSize(out int w, out int h))
        {
            Win32.SetWindowPos(child, hostHwnd, 0, 0, w, h, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
        }
    }

    public static void Focus(BoxLayout layout)
    {
        if (!s_sessions.TryGetValue(layout, out var s) || !s.IsAlive()) return;
        try
        {
            Win32.SetForegroundWindow(s.ChildHwnd);
            Win32.SetFocus(s.ChildHwnd);
        }
        catch { }
    }

    public static string GetStatus(BoxLayout layout)
    {
        if (!s_sessions.TryGetValue(layout, out var s)) return "Not started";
        if (s.IsAlive()) return $"Running: {AppHelper.DisplayName(s.AppId)}";
        if (!string.IsNullOrWhiteSpace(s.LastError)) return s.LastError!;
        return "Exited — press Restart";
    }

    public static bool IsRunning(BoxLayout layout)
        => s_sessions.TryGetValue(layout, out var s) && s.IsAlive();

    public static bool IsStarting(BoxLayout layout)
        => s_sessions.TryGetValue(layout, out var s) && s.PollTimer is not null;

    private static void Start(BoxLayout layout, TerminalHwndHost host)
    {
        string appId = layout.AppId ?? "cmd";
        string args = layout.AppArgs ?? "";
        string resolved = AppHelper.ResolvePath(appId);
        // ResolvePath returns appId fallback if not found; check existence when it looks like a path/name.
        if (!File.Exists(resolved))
        {
            // Try PATH lookup winner (may return plain name that Process can find).
            string? inPath = FindInPath(resolved);
            if (inPath is not null) resolved = inPath;
            else if (resolved.Contains('\\') || resolved.Contains('/'))
            {
                var s = new TerminalSession { Layout = layout, AppId = appId, Args = args, LastError = $"Not found: {resolved}" };
                s_sessions[layout] = s;
                return;
            }
        }
        if (string.IsNullOrWhiteSpace(args)) args = AppHelper.DefaultArgs(appId);

        var session = new TerminalSession { Layout = layout, AppId = appId, Args = args, CurrentHost = host };
        s_sessions[layout] = session;

        try
        {
            session.BeforeWindows = SnapshotVisibleWindows();
            // On Windows 11 the default terminal is Windows Terminal: powershell/cmd open in a
            // separate WindowsTerminal.exe UWP window (Cascadia class) that cannot be SetParent-ed
            // reliably and stays outside the box. Force legacy conhost per-process so the shell
            // gets a classic ConsoleWindowClass window owned by conhost we can truly embed.
            bool forceConhost = appId is "cmd" or "powershell" or "pwsh";
            string fileName = resolved;
            string arguments = args;
            if (forceConhost)
            {
                string conhostPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "conhost.exe");
                if (!File.Exists(conhostPath)) conhostPath = "conhost.exe";
                fileName = conhostPath;
                arguments = $"\"{resolved}\"" + (string.IsNullOrWhiteSpace(args) ? "" : $" {args}");
                Log($"Start via conhost: {fileName} {arguments}");
            }
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal,
            };
            session.Process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            session.LastError = $"Failed to start {AppHelper.DisplayName(appId)}: {ex.Message}";
            return;
        }

        StartPoll(session, host);
    }

    private static void StartPoll(TerminalSession session, TerminalHwndHost host)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) { TryAttachOnce(session, host); return; }
        session.PollTicks = 0;
        session.StopPoll();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        session.PollTimer = timer;
        timer.Tick += (_, _) =>
        {
            session.PollTicks++;
            if (TryAttachOnce(session, host)) { session.StopPoll(); return; }
            if (session.PollTicks > 40) // ~10s
            {
                session.StopPoll();
                // Fallback: try MainWindowHandle one last time
                if (session.Process is not null && session.Process.MainWindowHandle != IntPtr.Zero)
                {
                    session.ChildHwnd = session.Process.MainWindowHandle;
                    if (Attach(session, host)) return;
                }
                // Never leave a floating terminal: hide any candidate window and stop the process.
                HideFloatingWindows(session);
                try { if (session.Process is not null && !session.Process.HasExited) session.Process.Kill(entireProcessTree: true); } catch { }
                session.LastError = $"Could not embed {AppHelper.DisplayName(session.AppId)} (no window captured; killed stray window to keep it confined). Press Restart.";
                try { Log($"Embed timeout {session.AppId}: killed stray window"); } catch { }
            }
        };
        timer.Start();
    }

    private static bool TryAttachOnce(TerminalSession session, TerminalHwndHost host)
    {
        if (session.Process is null) return true;
        if (host.HostHwnd == IntPtr.Zero) return false; // host HWND not built yet; keep polling
        // Quick path: MainWindowHandle (PuTTY and some console setups expose it)
        IntPtr direct = IntPtr.Zero;
        try { direct = session.Process.MainWindowHandle; } catch { }
        if (direct != IntPtr.Zero && Win32.IsWindow(direct) && !session.BeforeWindows.Contains(direct))
        {
            session.ChildHwnd = direct;
            return Attach(session, host);
        }
        IntPtr found = FindNewTerminalWindow(session.BeforeWindows, session.Process);
        if (found != IntPtr.Zero)
        {
            session.ChildHwnd = found;
            return Attach(session, host);
        }
        return false;
    }

    private static bool Attach(TerminalSession session, TerminalHwndHost host)
    {
        IntPtr child = session.ChildHwnd;
        IntPtr hostHwnd = host.HostHwnd;
        if (child == IntPtr.Zero || hostHwnd == IntPtr.Zero) return false;
        if (!Win32.IsWindow(child)) return false;
        try
        {
            // Already confined: don't re-parent (avoids flicker / log spam each tick).
            try
            {
                if (Win32.GetParent(child) == hostHwnd)
                {
                    if (host.TryGetPhysicalSize(out int w0, out int h0))
                        Win32.SetWindowPos(child, hostHwnd, 0, 0, w0, h0, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
                    return true;
                }
            }
            catch { }
            int style = Win32.GetWindowLong(child, Win32.GWL_STYLE);
            int newStyle = style & ~(Win32.WS_POPUP | Win32.WS_CAPTION | Win32.WS_THICKFRAME | Win32.WS_BORDER | Win32.WS_DLGFRAME | Win32.WS_MINIMIZEBOX | Win32.WS_MAXIMIZEBOX | Win32.WS_SYSMENU);
            newStyle |= Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_CLIPCHILDREN;
            Win32.SetWindowLong(child, Win32.GWL_STYLE, newStyle);
            int ex = Win32.GetWindowLong(child, Win32.GWL_EXSTYLE);
            ex &= ~Win32.WS_EX_TOPMOST;
            ex &= ~Win32.WS_EX_TOOLWINDOW;
            Win32.SetWindowLong(child, Win32.GWL_EXSTYLE, ex);

            Win32.SetParent(child, hostHwnd);
            Win32.ShowWindow(child, Win32.SW_SHOW);
            if (host.TryGetPhysicalSize(out int w, out int h))
                Win32.SetWindowPos(child, hostHwnd, 0, 0, w, h, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
            try { Log($"Attached {session.AppId} child=0x{child.ToInt64():X} host=0x{hostHwnd.ToInt64():X}"); } catch { }
            return true;
        }
        catch (Exception ex)
        {
            session.LastError = $"Could not embed {AppHelper.DisplayName(session.AppId)}: {ex.Message}";
            try { Log($"Attach failed {session.AppId}: {ex}"); } catch { }
            return false;
        }
    }

    private static void Log(string msg)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemInfo");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "terminal.log"), $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
        }
        catch { }
    }

    private static void HideFloatingWindows(TerminalSession session)
    {
        uint ownPid = (uint)Environment.ProcessId;
        uint targetPid = session.Process is null ? 0 : (uint)session.Process.Id;
        try
        {
            Win32.EnumWindows((hwnd, _) =>
            {
                if (session.BeforeWindows.Contains(hwnd)) return true;
                Win32.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == ownPid) return true;
                string cls = Win32.GetClassName(hwnd) ?? "";
                if (pid == targetPid || cls == "ConsoleWindowClass" || cls.Contains("Cascadia"))
                {
                    try { Win32.ShowWindow(hwnd, Win32.SW_HIDE); } catch { }
                }
                return true;
            }, IntPtr.Zero);
        }
        catch { }
    }

    private static HashSet<IntPtr> SnapshotVisibleWindows()
    {
        var set = new HashSet<IntPtr>();
        Win32.EnumWindows((hwnd, _) =>
        {
            if (Win32.IsWindowVisible(hwnd)) set.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return set;
    }

    private static IntPtr FindNewTerminalWindow(HashSet<IntPtr> before, Process? proc)
    {
        IntPtr found = IntPtr.Zero;
        uint ownPid = (uint)Environment.ProcessId;
        uint targetPid = proc is null ? 0 : (uint)proc.Id;
        Win32.EnumWindows((hwnd, _) =>
        {
            if (found != IntPtr.Zero) return false;
            if (before.Contains(hwnd)) return true;
            if (!Win32.IsWindowVisible(hwnd)) return true;
            Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ownPid) return true; // never steal our own window
            string cls = Win32.GetClassName(hwnd) ?? "";
            // Direct child of terminal process (PuTTY, some consoles)
            if (targetPid != 0 && pid == targetPid) { found = hwnd; return false; }
            // Console window hosted by conhost.exe (cmd / powershell open via conhost)
            if (cls == "ConsoleWindowClass") { found = hwnd; return false; }
            // Windows Terminal
            if (cls is "Cascadia_Window" or "Cascadia_WindowsTerminal" or "ApplicationFrameHistory" || cls.Contains("Cascadia")) { found = hwnd; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static string? FindInPath(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string exe = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (string dir in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    string cand = Path.Combine(dir.Trim().Trim('"'), exe);
                    if (File.Exists(cand)) return cand;
                }
                catch { }
            }
        }
        return null;
    }
}

internal static class Win32
{
    public const int WS_CHILD = 0x40000000;
    public const int WS_VISIBLE = 0x10000000;
    public const int WS_CLIPCHILDREN = 0x02000000;
    public const int WS_CLIPSIBLINGS = 0x04000000;
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_CAPTION = 0x00C00000;
    public const int WS_THICKFRAME = 0x00040000;
    public const int WS_BORDER = 0x00800000;
    public const int WS_DLGFRAME = 0x00400000;
    public const int WS_SYSMENU = 0x00080000;
    public const int WS_MINIMIZEBOX = 0x00020000;
    public const int WS_MAXIMIZEBOX = 0x00010000;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int SW_SHOW = 5;
    public const int SW_HIDE = 0;
    public const int SWP_NOSIZE = 0x0001;
    public const int SWP_SHOWWINDOW = 0x0040;
    public const int SWP_NOZORDER = 0x0004;
    public const int SWP_NOACTIVATE = 0x0010;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int w, int h, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    public static string GetClassName(IntPtr hWnd)
    {
        var sb = new System.Text.StringBuilder(256);
        int len = GetClassName(hWnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString() : "";
    }

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);
}

internal static class TerminalSessionExt
{
    public static void StopPoll(this TerminalSession s)
    {
        try { s.PollTimer?.Stop(); } catch { }
        s.PollTimer = null;
    }
}
