using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

public sealed class TrayManager
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private readonly string settingsPath;
    private readonly string outputFolder;
    private readonly BufferedConsoleWriter logs;
    private readonly bool showWelcome;

    private readonly int port;
    private bool http;
    private bool websocket;

    public event Action<bool>? HttpChanged;
    public event Action<bool>? WebSocketChanged;
    private volatile bool serverRunning;

    public TrayManager(
        string settingsPath,
        string outputFolder,
        BufferedConsoleWriter logs,
        bool showWelcome,
        int port,
        bool http,
        bool websocket)
    {
        this.settingsPath = settingsPath;
        this.outputFolder = outputFolder;
        this.logs = logs;
        this.showWelcome = showWelcome;
        this.port = port;
        this.http = http;
        this.websocket = websocket;
    }

    public void SetServerRunning(bool running)
    {
        serverRunning = running;
    }

    private string GetServerStatus(bool enabled)
    {
        if (!enabled) return "Disabled";

        return serverRunning ? "Running" : "Offline";
    }

    public void Start()
    {
        var thread = new Thread(() =>
        {
            using var tray = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
                    ?? SystemIcons.Application,

                Text = "NowPlayingToFile",
                Visible = true,
                BalloonTipIcon = ToolTipIcon.Info,
                BalloonTipTitle = "NowPlayingToFile",
                BalloonTipText = "NowPlayingToFile is running in the background."
            };

            using var menu = new ContextMenuStrip();

            string httpUrl = $"http://127.0.0.1:{port}/";
            string wsUrl = $"ws://127.0.0.1:{port}/ws/";

            // MAIN MENU


            menu.Items.Add("Open Media Folder", null, (_, _) => OpenFolder(outputFolder));

            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("README", null, (_, _) => OpenFile(Path.Combine(outputFolder, "README.txt")));

            menu.Items.Add("Settings", null, (_, _) => OpenFile(settingsPath));


            menu.Items.Add(new ToolStripSeparator());

            // DEVELOPER TOOLS

            var developerMenu = new ToolStripMenuItem("Developer Tools");

            // Console

            var toggle = developerMenu.DropDownItems.Add("Show Console");

            toggle.Click += (_, _) => ToggleConsole();

            tray.DoubleClick += (_, _) => ToggleConsole();

            developerMenu.DropDownItems.Add(new ToolStripSeparator());

            // HTTP API

            var httpMenu = new ToolStripMenuItem("HTTP API");

            var httpToggle = new ToolStripMenuItem("Enabled")
            {
                CheckOnClick = true,
                Checked = http
            };

            httpToggle.Click += (_, _) =>
            {
                try
                {
                    SaveSetting("http", httpToggle.Checked ? "true" : "false");
                }
                catch (Exception ex)
                {
                    httpToggle.Checked = http;
                    MessageBox.Show(ex.Message, "NowPlayingToFile");
                    return;
                }

                http = httpToggle.Checked;
                HttpChanged?.Invoke(http);
            };

            httpMenu.DropDownItems.Add(httpToggle);
            httpMenu.DropDownItems.Add(new ToolStripSeparator());

            var httpOpen = httpMenu.DropDownItems.Add(
                "Open in Browser", null, (_, _) => OpenUrl(httpUrl));

            var httpCopy = httpMenu.DropDownItems.Add(
                "Copy HTTP URL", null, (_, _) => Clipboard.SetText(httpUrl));

            developerMenu.DropDownItems.Add(httpMenu);


            // WebSocket

            var wsMenu = new ToolStripMenuItem("WebSocket");

            var wsToggle = new ToolStripMenuItem("Enabled")
            {
                CheckOnClick = true,
                Checked = websocket
            };

            wsToggle.Click += (_, _) =>
            {
                try
                {
                    SaveSetting("websocket", wsToggle.Checked ? "true" : "false");
                }
                catch (Exception ex)
                {
                    wsToggle.Checked = websocket;
                    MessageBox.Show(ex.Message, "NowPlayingToFile");
                    return;
                }

                websocket = wsToggle.Checked;
                WebSocketChanged?.Invoke(websocket);
            };

            wsMenu.DropDownItems.Add(wsToggle);
            wsMenu.DropDownItems.Add(new ToolStripSeparator());

            var wsCopy = wsMenu.DropDownItems.Add(
                "Copy WebSocket URL", null, (_, _) => Clipboard.SetText(wsUrl));

            developerMenu.DropDownItems.Add(wsMenu);

            // Doc et GitHub

            developerMenu.DropDownItems.Add("API Documentation", null, (_, _) =>
                OpenFile(Path.Combine(outputFolder, "API_Doc.txt")));

            developerMenu.DropDownItems.Add("GitHub", null, (_, _) =>
                OpenUrl("https://github.com/Shakawah/NowPlayingToFile"));

            menu.Items.Add(developerMenu);

            menu.Items.Add(new ToolStripSeparator());

            // Support

            menu.Items.Add("Support Me ♥", null, (_, _) =>
                OpenUrl("https://ko-fi.com/shakawah"));

            menu.Items.Add(new ToolStripSeparator());

            // Quit

            menu.Items.Add("Quit", null, (_, _) =>
            {
                tray.Visible = false;
                Environment.Exit(0);
            });

            // Refresh developer tools when opened

            void RefreshDeveloperMenu()
            {
                toggle.Text = IsWindowVisible(GetConsoleWindow())
                    ? "Hide Console"
                    : "Show Console";

                httpMenu.Text = $"HTTP API ({GetServerStatus(http)})";
                wsMenu.Text = $"WebSocket ({GetServerStatus(websocket)})";

                httpToggle.Checked = http;
                wsToggle.Checked = websocket;

                httpOpen.Enabled = http && serverRunning;
                httpCopy.Enabled = http && serverRunning;
                wsCopy.Enabled = websocket && serverRunning;
            }

            developerMenu.DropDownOpening += (_, _) => RefreshDeveloperMenu();

            httpMenu.DropDownOpening += (_, _) => RefreshDeveloperMenu();
            wsMenu.DropDownOpening += (_, _) => RefreshDeveloperMenu();

            httpToggle.Click += (_, _) => RefreshDeveloperMenu();
            wsToggle.Click += (_, _) => RefreshDeveloperMenu();

            tray.ContextMenuStrip = menu;

            // WELCOME WINDOW

            if (showWelcome)
            {
                var welcome = new WelcomeForm();

                welcome.FormClosed += (_, _) =>
                {
                    if (welcome.DontShowAgain)
                    {
                        SaveSetting("showWelcome", "false");
                    }

                    welcome.Dispose();
                };

                welcome.Show();
            }

            // STARTUP NOTIFICATION

            using var timer = new System.Windows.Forms.Timer
            {
                Interval = 3000
            };

            timer.Tick += (_, _) =>
            {
                timer.Stop();

                Console.WriteLine("Sending startup notification...");

                tray.ShowBalloonTip(
                    3000,
                    "NowPlayingToFile",
                    "NowPlayingToFile is running in the background.",
                    ToolTipIcon.Info
                );
            };

            timer.Start();

            Application.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    private void SaveSetting(string key, string value)
    {
        string[] lines = File.ReadAllLines(settingsPath);
        bool found = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();

            if (trimmed.StartsWith("#") || trimmed.StartsWith(";"))
                continue;

            int separator = trimmed.IndexOf('=');

            if (separator > 0 &&
                trimmed[..separator].Trim().Equals(
                    key, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = $"{key}={value}";
                found = true;
                break;
            }
        }

        if (!found)
            lines = [.. lines, $"{key}={value}"];

        File.WriteAllLines(settingsPath, lines);
    }

    private void ToggleConsole()
    {
        IntPtr console = GetConsoleWindow();

        if (console == IntPtr.Zero)
        {
            if (AllocConsole())
                logs.AttachConsole();

            return;
        }

        if (IsWindowVisible(console))
        {
            logs.DetachConsole();
            ShowWindow(console, 0);
        }
        else
        {
            ShowWindow(console, 5);
            logs.AttachConsole();
        }
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private static void OpenFile(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }
        else
        {
            MessageBox.Show(
                $"File not found:\n{path}",
                "NowPlayingToFile",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
        }
    }

    private static void OpenFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        else
        {
            MessageBox.Show(
                $"Folder not found:\n{path}",
                "NowPlayingToFile",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
        }
    }
}