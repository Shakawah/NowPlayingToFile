using System;
using System.Drawing;
using System.Windows.Forms;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.Control;
using static System.Runtime.InteropServices.JavaScript.JSType;
using TimelineSync = System.Collections.Generic.Dictionary<string, (double Position, System.DateTimeOffset At, string Status)>;

public sealed partial class NowPlayingApp
{
    async Task CheckForUpdatesAsync()
    {
        try
        {
            Console.WriteLine("CheckForUpdates");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NowPlayingToFile-UpdateChecker/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    
            using var response = await client.GetAsync(githubLatestReleaseApi);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    
            string? name = json.RootElement.GetProperty("name").GetString();
            string? releaseUrl = json.RootElement.GetProperty("html_url").GetString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(releaseUrl)) return;
    
            string versionText = name.Trim().TrimStart('v', 'V');
            string currentVersionText = currentVersion.Trim().TrimStart('v', 'V');
    
            if (!Version.TryParse(currentVersionText, out var current) ||
                !Version.TryParse(versionText, out var latest) || latest <= current)
                return;
    
            // MB_YESNO | MB_ICONINFORMATION | MB_SETFOREGROUND
            int answer = WindowsPopup.MessageBox(
                IntPtr.Zero,
                $"A new version of NowPlayingToFile is available\n" +
                $"Current version : {currentVersion}\n" +
                $"Latest version : {name}\n" +
                "Open the GitHub release page to download it ?",
                "NowPlayingToFile - Update available",
                0x00000004 | 0x00000040 | 0x00010000);
    
            if (answer == 6) // IDYES
            {
                Process.Start(new ProcessStartInfo(releaseUrl) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            if (debug) Console.WriteLine($"GitHub update check failed: {ex.Message}");
        }
    }
}
