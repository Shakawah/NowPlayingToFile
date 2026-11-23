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
    private bool writefile, http, websocket, staticEnabled, dynamicEnabled, timingEnabled, mediaControl, debug, checkForUpdates, showWelcome;
    private int port, timeCheck;
    private string outputFolder = "", mainOutputFolder = "", sessionsFile = "", mainKey = "";
    private const string currentVersion = "3.0";
    private const string githubLatestReleaseApi = "https://api.github.com/repos/Shakawah/NowPlayingToFile/releases/latest";
    private Dictionary<string, string> lastWrittenFiles = new();
    private PublishedState currentState = new();
    private object filesLock = new();
    private List<string> knownSessions = new();
    private Dictionary<string, string> lastSongsBySource = new();
    private Dictionary<string, bool> thumbnailFound = new();
    private Dictionary<string, bool> dynamicIsPlay = new();
    private Dictionary<string, byte[]> lastCovers = new();
    private Dictionary<string, string> lastCoversMime = new();
    private Dictionary<string, DateTime> coverRetryUntil = new();
    private Dictionary<string, DateTime> coverCheckUntil = new();
    private Dictionary<string, DateTime> coverLastCheck = new();
    private Dictionary<string, DateTime> dynamicStopSince = new();
    private HashSet<char> invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
    private GlobalSystemMediaTransportControlsSessionManager manager = null!;

    public async Task RunAsync()
    {
        // Settings and tray UI are maintained in separate files.
        var logWriter = new BufferedConsoleWriter();
        Console.SetOut(logWriter);
        Console.SetError(logWriter);

        var appSettings = SettingsManager.Load();
        string settingsPath = appSettings.SettingsPath;
        writefile = appSettings.WriteFile;
        http = appSettings.Http;
        websocket = appSettings.Websocket;
        staticEnabled = appSettings.StaticEnabled;
        dynamicEnabled = appSettings.DynamicEnabled;
        timingEnabled = appSettings.TimingEnabled;
        port = appSettings.Port;
        outputFolder = appSettings.OutputFolder;
        timeCheck = appSettings.TimeCheck;
        mediaControl = appSettings.MediaControl;
        debug = appSettings.Debug;
        checkForUpdates = appSettings.CheckForUpdates;
        showWelcome = appSettings.ShowWelcome;

        
        var trayManager = new TrayManager(settingsPath, outputFolder, logWriter, showWelcome);
        trayManager.Start();

        Console.WriteLine("==========================================");
        Console.WriteLine($"=== NowPlayingToFile v{currentVersion} - STARTING ===");
        Console.WriteLine("==========================================");
        if (debug)
        {
            Console.WriteLine($"Loading NowPlayingToFile_settings.ini : writefile={writefile} \n" +
                $"http={http} \n" +
                $"websocket={websocket} \n" +
                $"staticEnabled={staticEnabled} \n" +
                $"dynamicEnabled={dynamicEnabled}  \n" +
                $"timingEnabled={timingEnabled}  \n" +
                $"port={port}  \n" +
                $"outputFolder={outputFolder} \n" +
                $"timeCheck={timeCheck} ms \n" +
                $"mediaControl={mediaControl} \n" +
                $"checkForUpdates={checkForUpdates} \n" +
                $"debug={debug} \n"
                );

            Console.WriteLine("==========================================");
        }

        mainOutputFolder = Path.Combine(outputFolder, "Main");
        if (writefile)
        {
            Directory.CreateDirectory(mainOutputFolder);
        }

        DocumentationManager.WriteStartConsole();
        DocumentationManager.CreateReadme(outputFolder);
        DocumentationManager.CreateApiDoc(outputFolder);

        if (checkForUpdates)
            _ = Task.Run(CheckForUpdatesAsync);


        sessionsFile = Path.Combine(outputFolder, "sessions.txt");
        knownSessions = File.Exists(sessionsFile) ? File.ReadAllLines(sessionsFile).ToList() : new List<string>();

        manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        mainKey = "MAIN";

        using var listener = new HttpListener();
        Stopwatch sw;

        if (http || websocket || mediaControl)
        {
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                listener.Start();
                _ = RunHttpServerAsync(listener);
                if (http) Console.WriteLine($"HTTP SERV : http://127.0.0.1:{port}/");
                if (websocket) Console.WriteLine($"WS SERV   : ws://127.0.0.1:{port}/ws/");
            }
            catch (HttpListenerException ex)
            {
                Console.WriteLine($"HTTP server unavailable: {ex}");
            }
        }


        while (true)
        {
            sw = Stopwatch.StartNew();

            try
            {
                var controlsByFolder = new Dictionary<string, MediaControls>(StringComparer.OrdinalIgnoreCase);

                var mainSession = manager.GetCurrentSession();
                var allSessions = manager.GetSessions();

                if (mainSession == null)
                {
                    Console.WriteLine($"{DateTime.Now:T} - No active media found");
                    WriteTextIfChanged(Path.Combine(mainOutputFolder, "_playback_status.txt"), "none");

                    EmptyDynamicFiles(mainOutputFolder);
                    dynamicIsPlay[mainKey] = false;

                    EmptyImageFile(Path.Combine(mainOutputFolder, "static_cover.jpg"));
                    EmptyImageFile(Path.Combine(mainOutputFolder, "dynamic_cover.jpg"));
                    lastCovers.Remove(mainKey);
                    lastCoversMime.Remove(mainKey);

                    thumbnailFound[mainKey] = false;
                    ClearTimelineFiles(mainOutputFolder);
                }
                else
                {
                    if (mediaControl) controlsByFolder[mainOutputFolder] = ReadMediaControls(mainSession);
                    string playbackStatus = GetPlaybackStatusText(mainSession);
                    WriteTextIfChanged(Path.Combine(mainOutputFolder, "_playback_status.txt"), playbackStatus);
                    UpdateTimelineFiles(mainSession, mainOutputFolder, playbackStatus);

                    bool isPlaying = playbackStatus == "play";
                    bool wasPlaying = dynamicIsPlay.TryGetValue(mainKey, out bool previous) && previous;
                    bool rewrite = !wasPlaying && isPlaying;

                    UpdateDynamicStop(mainOutputFolder, mainKey, isPlaying);

                    dynamicIsPlay[mainKey] = isPlaying;

                    var media = await WithTimeout(mainSession.TryGetMediaPropertiesAsync());
                    if (media != null)
                    {
                        string title = media.Title ?? "";
                        string artist = media.Artist ?? "";
                        string album = media.AlbumTitle ?? "";

                        string full = string.IsNullOrWhiteSpace(artist) ? title : $"{artist} - {title}";
                        string songKey = $"{title}|{artist}|{album}";
                        bool hasMediaInfo = !string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(artist) || !string.IsNullOrWhiteSpace(album);

                        bool songChanged = hasMediaInfo && (!lastSongsBySource.TryGetValue(mainKey, out string? lastMainSong) || songKey != lastMainSong);

                        if (songChanged)
                        {
                            lastSongsBySource[mainKey] = songKey;
                            thumbnailFound[mainKey] = false;
                        }

                        if (songChanged || rewrite)
                        {
                            WriteTextIfChanged(Path.Combine(mainOutputFolder, "static_title.txt"), title);
                            WriteTextIfChanged(Path.Combine(mainOutputFolder, "static_title_spaced.txt"), $"{title}          ");

                            WriteTextIfChanged(Path.Combine(mainOutputFolder, "static_artist.txt"), artist);
                            WriteTextIfChanged(Path.Combine(mainOutputFolder, "static_artist_spaced.txt"), $"{artist}          ");

                            WriteTextIfChanged(Path.Combine(mainOutputFolder, "static_artist-title.txt"), full);
                            WriteTextIfChanged(Path.Combine(mainOutputFolder, "static_artist-title_spaced.txt"), $"{full}          ");

                            WriteTextIfChanged(Path.Combine(mainOutputFolder, "static_album.txt"), album);
                            WriteTextIfChanged(Path.Combine(mainOutputFolder, "static_album_spaced.txt"), $"{album}          ");

                            Console.WriteLine($"{DateTime.Now:T} - MAIN SESSION {mainSession.SourceAppUserModelId} - {full}");
                        }

                        if (isPlaying && (songChanged || rewrite))
                        {
                            WriteTextIfChanged(Path.Combine(mainOutputFolder, "dynamic_title.txt"), title);
                            WriteTextIfChanged(Path.Combine(mainOutputFolder, "dynamic_title_spaced.txt"), $"{title}          ");

                            WriteTextIfChanged(Path.Combine(mainOutputFolder, "dynamic_artist.txt"), artist);
                            WriteTextIfChanged(Path.Combine(mainOutputFolder, "dynamic_artist_spaced.txt"), $"{artist}          ");

                            WriteTextIfChanged(Path.Combine(mainOutputFolder, "dynamic_artist-title.txt"), full);
                            WriteTextIfChanged(Path.Combine(mainOutputFolder, "dynamic_artist-title_spaced.txt"), $"{full}          ");

                            WriteTextIfChanged(Path.Combine(mainOutputFolder, "dynamic_album.txt"), album);
                            WriteTextIfChanged(Path.Combine(mainOutputFolder, "dynamic_album_spaced.txt"), $"{album}          ");
                        }

                        await UpdateCover(media, mainOutputFolder, mainKey, isPlaying, songChanged);
                    }
                }

                List<string> currentSessions = new();

                bool sessionsChanged = false;

                foreach (var session_ in allSessions)
                {
                    try
                    {
                        string sourceName = session_.SourceAppUserModelId ?? "Unknown";

                        string safeFolderName = MakeSafeFolderName(sourceName);
                        string sessionFolder = Path.Combine(outputFolder, safeFolderName);
                        if (writefile) Directory.CreateDirectory(sessionFolder);

                        if (!knownSessions.Contains(safeFolderName))
                        {
                            knownSessions.Add(safeFolderName);
                            sessionsChanged = true;
                        }

                        currentSessions.Add(safeFolderName);
                        string sessionKey = safeFolderName;

                        if (mediaControl) controlsByFolder[sessionFolder] = ReadMediaControls(session_);
                        string playbackStatus_ = GetPlaybackStatusText(session_);
                        WriteTextIfChanged(Path.Combine(sessionFolder, "_playback_status.txt"), playbackStatus_);
                        UpdateTimelineFiles(session_, sessionFolder, playbackStatus_);

                        bool isPlaying_ = playbackStatus_ == "play";
                        bool wasPlaying_ = dynamicIsPlay.TryGetValue(sessionKey, out bool previous_) && previous_;

                        bool rewrite_ = !wasPlaying_ && isPlaying_;

                        UpdateDynamicStop(sessionFolder, sessionKey, isPlaying_);

                        dynamicIsPlay[sessionKey] = isPlaying_;

                        var media_ = await WithTimeout(session_.TryGetMediaPropertiesAsync());
                        if (media_ == null)
                        {
                            continue;
                        }

                        string title_ = media_.Title ?? "";
                        string artist_ = media_.Artist ?? "";
                        string album_ = media_.AlbumTitle ?? "";

                        string full_ = string.IsNullOrWhiteSpace(artist_) ? title_ : $"{artist_} - {title_}";

                        string songKey_ = $"{title_}|{artist_}|{album_}";
                        bool hasMediaInfo_ = !string.IsNullOrWhiteSpace(title_) || !string.IsNullOrWhiteSpace(artist_) || !string.IsNullOrWhiteSpace(album_);

                        bool songChanged_ = hasMediaInfo_ && (!lastSongsBySource.TryGetValue(sessionKey, out string? lastSessionSong) || songKey_ != lastSessionSong);

                        if (songChanged_)
                        {
                            lastSongsBySource[sessionKey] = songKey_;
                            thumbnailFound[sessionKey] = false;
                        }

                        if (songChanged_ || rewrite_)
                        {
                            WriteTextIfChanged(Path.Combine(sessionFolder, "static_title.txt"), title_);
                            WriteTextIfChanged(Path.Combine(sessionFolder, "static_title_spaced.txt"), $"{title_}          ");

                            WriteTextIfChanged(Path.Combine(sessionFolder, "static_artist.txt"), artist_);
                            WriteTextIfChanged(Path.Combine(sessionFolder, "static_artist_spaced.txt"), $"{artist_}          ");

                            WriteTextIfChanged(Path.Combine(sessionFolder, "static_artist-title.txt"), full_);
                            WriteTextIfChanged(Path.Combine(sessionFolder, "static_artist-title_spaced.txt"), $"{full_}          ");

                            WriteTextIfChanged(Path.Combine(sessionFolder, "static_album.txt"), album_);
                            WriteTextIfChanged(Path.Combine(sessionFolder, "static_album_spaced.txt"), $"{album_}          ");

                            Console.WriteLine($"{DateTime.Now:T} - {session_.SourceAppUserModelId} - {full_}");
                        }

                        if (isPlaying_ && (songChanged_ || rewrite_))
                        {
                            WriteTextIfChanged(Path.Combine(sessionFolder, "dynamic_title.txt"), title_);
                            WriteTextIfChanged(Path.Combine(sessionFolder, "dynamic_title_spaced.txt"), $"{title_}          ");

                            WriteTextIfChanged(Path.Combine(sessionFolder, "dynamic_artist.txt"), artist_);
                            WriteTextIfChanged(Path.Combine(sessionFolder, "dynamic_artist_spaced.txt"), $"{artist_}          ");

                            WriteTextIfChanged(Path.Combine(sessionFolder, "dynamic_artist-title.txt"), full_);
                            WriteTextIfChanged(Path.Combine(sessionFolder, "dynamic_artist-title_spaced.txt"), $"{full_}          ");

                            WriteTextIfChanged(Path.Combine(sessionFolder, "dynamic_album.txt"), album_);
                            WriteTextIfChanged(Path.Combine(sessionFolder, "dynamic_album_spaced.txt"), $"{album_}          ");
                        }


                        await UpdateCover(media_, sessionFolder, sessionKey, isPlaying_, songChanged_);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("========== ERROR ==========");
                        Console.WriteLine(ex.ToString());
                        Console.WriteLine("===========================");
                    }
                }

                if (sessionsChanged)
                {
                    if (writefile) File.WriteAllLines(sessionsFile, knownSessions);
                }

                foreach (var sessionName in knownSessions)
                {
                    if (currentSessions.Contains(sessionName))
                        continue;

                    string folder = Path.Combine(outputFolder, sessionName);

                    WriteTextIfChanged(Path.Combine(folder, "_playback_status.txt"), "none");

                    if (dynamicIsPlay.TryGetValue(sessionName, out bool wasPlaying_) && wasPlaying_)
                    {
                        EmptyDynamicFiles(folder);
                    }

                    dynamicIsPlay[sessionName] = false;

                    EmptyImageFile(Path.Combine(folder, "static_cover.jpg"));
                    EmptyImageFile(Path.Combine(folder, "dynamic_cover.jpg"));
                    lastCovers.Remove(sessionName);
                    lastCoversMime.Remove(sessionName);

                    thumbnailFound[sessionName] = false;
                    ClearTimelineFiles(folder);
                }

                //On met à dispo les infos mise à jour pour les requêtes HTTP/WebSocket
                PublishState(controlsByFolder);

                sw.Stop();

                if (sw.ElapsedMilliseconds > timeCheck)
                {
                    Console.WriteLine(
                        $"=== Warning === Loop time ({sw.ElapsedMilliseconds} ms) " +
                        $"exceeds timeCheck ({timeCheck} ms)"
                    );
                }
                else if (debug)
                {
                    Console.WriteLine($"Loop time: {sw.ElapsedMilliseconds} ms");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("========== ERROR ==========");
                Console.WriteLine(ex.ToString());
                Console.WriteLine("===========================");
            }

            await Task.Delay(Math.Max(0, timeCheck - (int)sw.ElapsedMilliseconds));
        }
    }
}
