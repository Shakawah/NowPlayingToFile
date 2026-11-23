using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

public sealed class TrayManager
{
    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private readonly string settingsPath;
    private readonly string outputFolder;
    private readonly BufferedConsoleWriter logs;
    private readonly bool showWelcome;

    public TrayManager(string settingsPath, string outputFolder, BufferedConsoleWriter logs, bool showWelcome)
    {
        this.settingsPath = settingsPath;
        this.outputFolder = outputFolder;
        this.logs = logs;
        this.showWelcome = showWelcome;
    }

    public void Start()
    {
        var thread = new Thread(() =>
        {
            using var tray = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application,
                Text = "NowPlayingToFile",
                Visible = true,
                BalloonTipIcon = ToolTipIcon.Info,
                BalloonTipTitle = "NowPlayingToFile",
                BalloonTipText = "NowPlayingToFile is running in the background. Right-click the tray icon to show the console or exit."
            };
            using var menu = new ContextMenuStrip();
            menu.Items.Add("README", null, (_, _) => OpenFile(Path.Combine(outputFolder, "README.txt")));
            menu.Items.Add("Settings", null, (_, _) => OpenFile(settingsPath));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open Media Folder", null, (_, _) => OpenFolder(outputFolder));
            menu.Items.Add(new ToolStripSeparator());
            var toggle = menu.Items.Add("Show Console");
            menu.Opening += (_, _) => toggle.Text = IsWindowVisible(GetConsoleWindow())
                ? "Hide Console" : "Show Console";
            toggle.Click += (_, _) => ToggleConsole();
            tray.DoubleClick += (_, _) => ToggleConsole();
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Support Shakawah", null, (_, _) =>
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://ko-fi.com/shakawah",
                    UseShellExecute = true
                });
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit Now Playing To File", null, (_, _) =>
            {
                tray.Visible = false;
                Environment.Exit(0);
            });
            tray.ContextMenuStrip = menu;

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

            using var timer = new System.Windows.Forms.Timer { Interval = 3000 };
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

    void SaveSetting(string key, string value)
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
            if (AllocConsole()) logs.AttachConsole();
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

    private static void OpenFile(string path)
    {
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        else
            MessageBox.Show($"File not found:\n{path}", "NowPlayingToFile",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
