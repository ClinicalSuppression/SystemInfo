using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace SystemInfo
{
    /// <summary>
    /// Optional startup update check (off by default; only enabled in Settings).
    /// Reads the latest GitHub release for the whole app; when a newer version exists it offers the release page,
    /// which ships the new installer. Model data ships with normal releases, so there is no separate model update.
    /// </summary>
    internal static class ReleaseChecker
    {
        private const string Owner = "ClinicalSuppression";
        private const string Repo = "SystemInfo";

        public static void CheckForUpdatesOnStartup()
        {
            // Fire-and-forget: never block the UI thread and never surface network failures.
            _ = Task.Run(async () =>
            {
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("SystemInfo-UpdateCheck");

                    string url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
                    using var resp = await client.GetAsync(url).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) return;

                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("tag_name", out var tagEl)) return;
                    string tag = (tagEl.GetString() ?? "").TrimStart('v', 'V');
                    if (tag.Length == 0) return;

                    string current = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
                    if (!IsNewer(tag, current)) return;

                    string page = root.TryGetProperty("html_url", out var pageEl) && pageEl.GetString() is { Length: > 0 } p
                        ? p
                        : $"https://github.com/{Owner}/{Repo}/releases/latest";

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var answer = System.Windows.MessageBox.Show(
                            $"SystemInfo {tag} is available (you have {current}).\n\nOpen the release page to download the new installer?",
                            "SystemInfo update", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information);
                        if (answer == System.Windows.MessageBoxResult.Yes)
                        {
                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(page) { UseShellExecute = true }); }
                            catch { }
                        }
                    });
                }
                catch
                {
                    // Network unavailable / repo not published yet / malformed response: stay silent.
                }
            });
        }

        /// <summary>True when <paramref name="tag"/> is a higher dotted version than <paramref name="current"/>.</summary>
        internal static bool IsNewer(string tag, string current)
        {
            static int[] Parts(string s) => s.Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out int n) ? n : 0).ToArray();
            int[] a = Parts(tag), b = Parts(current);
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                int x = i < a.Length ? a[i] : 0;
                int y = i < b.Length ? b[i] : 0;
                if (x != y) return x > y;
            }
            return false;
        }
    }
}
