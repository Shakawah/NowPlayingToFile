//Rappel pour moi pour compiler : dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
using System;
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

///// SETTINGS.INI
string settingsPath = Path.Combine(AppContext.BaseDirectory, "NowPlayingToFile_Settings.ini");
if (!File.Exists(settingsPath))
{
    File.WriteAllText(settingsPath, """
        ### This is de Settings file for NowPlayingToFile.
        ### Only change the values, not the variables names. If you want to rollback you can delete this file and launch NowPlayingToFile again it will recreate this file with default values.
        ### If you want more details on the definitions read the ReadMe.txt in your outputFolder

        #Write the files on your computer on the outputFolder (def : true)
        writefile=true
        #Local HTTP server (def : true)
        http=true
        #Local web server (def : true)
        websocket=true
        #Local port (def : 8937)
        port=8937
        
        #Write the static values (def : true)
        static=true
        #Write the dynamic values (def : true)
        dynamic=true
        #Write the timing datas (def : true)
        timing=true

        #Folder used for NowPlayingToFile files
        outputFolder=C:\NowPlayingToFile

        #Time between each media check in milliseconds (def : 1000 / min : 100)
        timeCheck=1000

        # Allow media playback controls through HTTP (def : true)
        mediaControl=true

        #Check GitHub Releases for new versions at startup (def : true)
        checkForUpdates=true

        #To check the settings loaded, and the time each loop takes (def : false)
        #-> A loop is the time it takes to check the medias & update the files
        debug=false
        """);
}

Dictionary<string, string> settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
foreach (string line in File.ReadAllLines(settingsPath))
{
    string trimmed = line.Trim();
    if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';')) continue;
    int separator = trimmed.IndexOf('=');
    if (separator > 0)
        settings[trimmed[..separator].Trim()] = trimmed[(separator + 1)..].Trim();
}

bool SettingBool(string key) => !settings.TryGetValue(key, out string? value) || !bool.TryParse(value, out bool enabled) || enabled;

bool writefile = SettingBool("writefile");
bool http = SettingBool("http");
bool websocket = SettingBool("websocket");
bool staticEnabled = SettingBool("static");
bool dynamicEnabled = SettingBool("dynamic");
bool timingEnabled = SettingBool("timing");
int port = settings.TryGetValue("port", out string? portText) &&
    int.TryParse(portText, out int configuredPort) && configuredPort is >= 1 and <= 65535
    ? configuredPort : 8937;
string outputFolder = settings.TryGetValue("outputFolder", out string? configuredFolder) &&
    !string.IsNullOrWhiteSpace(configuredFolder)
    ? Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredFolder))
    : @"C:\NowPlayingToFile";
int timeCheck =
    settings.TryGetValue("timeCheck", out string? timeCheckText) &&
    int.TryParse(timeCheckText, out int configuredTimeCheck) &&
    configuredTimeCheck >= 100
        ? configuredTimeCheck
        : 1000;
bool mediaControl = SettingBool("mediaControl");
bool debug = SettingBool("debug");
bool checkForUpdates = SettingBool("checkForUpdates");
const string currentVersion = "2.1";
const string githubLatestReleaseApi = "https://api.github.com/repos/Shakawah/NowPlayingToFile/releases/latest";

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

string mainOutputFolder = Path.Combine(outputFolder, "Main");
if (writefile)
{
    Directory.CreateDirectory(mainOutputFolder);
}

WriteStartConsole();
CreateReadme();
CreateApiDoc();

if (checkForUpdates)
    _ = Task.Run(CheckForUpdatesAsync);

Dictionary<string, string> lastWrittenFiles = new();
PublishedState currentState = new();
object filesLock = new();

string sessionsFile = Path.Combine(outputFolder, "sessions.txt");
List<string> knownSessions = File.Exists(sessionsFile) ? File.ReadAllLines(sessionsFile).ToList() : new List<string>();

Dictionary<string, string> lastSongsBySource = new();
Dictionary<string, bool> thumbnailFound = new();
Dictionary<string, bool> dynamicIsPlay = new();
Dictionary<string, byte[]> lastCovers = new();
Dictionary<string, DateTime> coverRetryUntil = new();
Dictionary<string, DateTime> coverCheckUntil = new();
Dictionary<string, DateTime> coverLastCheck = new();
Dictionary<string, DateTime> dynamicStopSince = new();

HashSet<char> invalidChars = Path.GetInvalidFileNameChars().ToHashSet();

var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
string mainKey = "MAIN";

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


/// ECRITURES DOSSIERS / FICHIERS
static string GetPlaybackStatusText(GlobalSystemMediaTransportControlsSession session)
{
    var status = session.GetPlaybackInfo().PlaybackStatus;

    return status switch
    {
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => "play",
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => "pause",
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => "stop",
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => "none",
        _ => "none"
    };
}

string MakeSafeFolderName(string name)
{
    return string.Concat(name.Select(c => invalidChars.Contains(c) ? '_' : c));
}

bool DataEnabled(string path)
{
    string name = Path.GetFileName(path);
    if (name.StartsWith("static_", StringComparison.OrdinalIgnoreCase)) return staticEnabled;
    if (name.StartsWith("dynamic_", StringComparison.OrdinalIgnoreCase)) return dynamicEnabled;
    if (name.StartsWith("time_", StringComparison.OrdinalIgnoreCase)) return timingEnabled;
    return true;
}

void WriteTextIfChanged(string path, string content)
{
    if (!DataEnabled(path)) return;

    lock (filesLock)
    {
        if (lastWrittenFiles.TryGetValue(path, out var old) && old == content)
            return;

        if (writefile) File.WriteAllText(path, content);
        lastWrittenFiles[path] = content;
    }
}

void WriteCover(string path, byte[] image, string base64)
{
    if (!DataEnabled(path)) return;

    if (writefile) File.WriteAllBytes(path, image);

    lock (filesLock)
    {
        lastWrittenFiles[path] = base64;
    }
}

void EmptyDynamicFiles(string path)
{
    lock (filesLock)
    {
        WriteTextIfChanged(Path.Combine(path, "dynamic_title.txt"), "");
        WriteTextIfChanged(Path.Combine(path, "dynamic_title_spaced.txt"), $"          ");

        WriteTextIfChanged(Path.Combine(path, "dynamic_artist.txt"), "");
        WriteTextIfChanged(Path.Combine(path, "dynamic_artist_spaced.txt"), $"          ");

        WriteTextIfChanged(Path.Combine(path, "dynamic_artist-title.txt"), "");
        WriteTextIfChanged(Path.Combine(path, "dynamic_artist-title_spaced.txt"), $"          ");

        WriteTextIfChanged(Path.Combine(path, "dynamic_album.txt"), "");
        WriteTextIfChanged(Path.Combine(path, "dynamic_album_spaced.txt"), $"          ");
    }
}

void EmptyImageFile(string path)
{
    if (!DataEnabled(path)) return;
    lock (filesLock)
    {
        if (!writefile || !DataEnabled(path))
        {
            lastWrittenFiles[path] = "";
            return;
        }

        if (
            lastWrittenFiles.TryGetValue(path, out string? old) &&
            old == "" &&
            File.Exists(path) &&
            new FileInfo(path).Length == 0
            )
            return;

        if (!File.Exists(path) || new FileInfo(path).Length != 0)
            File.WriteAllBytes(path, Array.Empty<byte>());

        lastWrittenFiles[path] = "";
    }
}


void UpdateDynamicStop(string folder, string sessionKey, bool isPlaying)
{
    if (isPlaying)
    {
        dynamicStopSince.Remove(sessionKey);
        return;
    }

    if (!dynamicStopSince.TryGetValue(sessionKey, out DateTime since))
    {
        dynamicStopSince[sessionKey] = DateTime.UtcNow;
        return;
    }

    if ((DateTime.UtcNow - since).TotalMilliseconds >= 1000)
    {
        EmptyDynamicFiles(folder);
        EmptyImageFile(Path.Combine(folder, "dynamic_cover.jpg"));
    }
}

static async Task<T?> WithTimeout<T>(IAsyncOperation<T> operation, int timeoutMs = 2000)
{
    try
    {
        var task = operation.AsTask();
        var completed = await Task.WhenAny(task, Task.Delay(timeoutMs));

        if (completed != task)
        {
            operation.Cancel();
            return default;
        }

        return await task;
    }
    catch
    {
        return default;
    }
}

async Task UpdateCover(GlobalSystemMediaTransportControlsSessionMediaProperties media, string folder, string sessionKey, bool isPlaying, bool songChanged)
{
    if (!staticEnabled && !dynamicEnabled) return;

    string coverPath = Path.Combine(folder, "static_cover.jpg");
    string dynamicCoverPath = Path.Combine(folder, "dynamic_cover.jpg");

    if (songChanged)
    {
        thumbnailFound[sessionKey] = false;
        coverCheckUntil[sessionKey] = DateTime.UtcNow.AddSeconds(3);
    }

    bool hasCover =
        thumbnailFound.TryGetValue(sessionKey, out bool found) &&
        found &&
        lastCovers.ContainsKey(sessionKey);

    bool withinCheckWindow =
        coverCheckUntil.TryGetValue(sessionKey, out DateTime until) &&
        DateTime.UtcNow < until;

    bool shouldCheck = !hasCover || withinCheckWindow;

    if (shouldCheck &&
        coverLastCheck.TryGetValue(sessionKey, out DateTime lastCheck) &&
        (DateTime.UtcNow - lastCheck).TotalMilliseconds < 300)
    {
        shouldCheck = false;
    }

    if (shouldCheck)
    {
        coverLastCheck[sessionKey] = DateTime.UtcNow;

        if (media.Thumbnail == null)
        {
            if (!hasCover)
            {
                EmptyImageFile(coverPath);
                EmptyImageFile(dynamicCoverPath);
                lastCovers.Remove(sessionKey);
            }
        }
        else
        {
            try
            {
                using var stream = await media.Thumbnail.OpenReadAsync();
                using var input = stream.AsStreamForRead();
                using var memory = new MemoryStream();

                await input.CopyToAsync(memory);

                if (memory.Length > 0)
                {
                    byte[] newImage = memory.ToArray();

                    bool imageChanged =
                        !lastCovers.TryGetValue(sessionKey, out byte[]? previousImage) ||
                        !previousImage.AsSpan().SequenceEqual(newImage);

                    if (imageChanged)
                    {
                        string base64img = Convert.ToBase64String(newImage);

                        WriteCover(coverPath, newImage, base64img);
                        lastCovers[sessionKey] = newImage;

                    }

                    thumbnailFound[sessionKey] = true;
                    hasCover = true;
                }
                else if (!hasCover)
                {
                    EmptyImageFile(coverPath);
                    EmptyImageFile(dynamicCoverPath);
                    lastCovers.Remove(sessionKey);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Cover error ({sessionKey}): {ex}");
            }
        }
    }
    if (isPlaying &&
        dynamicEnabled &&
        hasCover &&
        lastCovers.TryGetValue(sessionKey, out byte[]? cover))
    {
        string base64 = Convert.ToBase64String(cover);

        if (!lastWrittenFiles.TryGetValue(dynamicCoverPath, out string? oldBase64)
            || oldBase64 != base64)
        {
            WriteCover(dynamicCoverPath, cover, base64);
        }
    }
}

void ClearTimelineFiles(string folder)
{
    if (!timingEnabled) return;
    WriteTextIfChanged(Path.Combine(folder, "time_position.txt"), "");
    WriteTextIfChanged(Path.Combine(folder, "time_duration.txt"), "");
    WriteTextIfChanged(Path.Combine(folder, "time_progress.txt"), "");
    WriteTextIfChanged(Path.Combine(folder, "time_position_formatted.txt"), "");
    WriteTextIfChanged(Path.Combine(folder, "time_duration_formatted.txt"), "");
    WriteTextIfChanged(Path.Combine(folder, "time_remaining.txt"), "");
    WriteTextIfChanged(Path.Combine(folder, "time_remaining_formatted.txt"), "");
}

string FormatTimeline(TimeSpan time)
{
    int seconds = Math.Max(0, (int)Math.Floor(time.TotalSeconds));
    int hours = seconds / 3600;
    int minutes = (seconds / 60) % 60;
    int secs = seconds % 60;
    return hours > 0 ? $"{hours:00}:{minutes:00}:{secs:00}" : $"{minutes:00}:{secs:00}";
}

void UpdateTimelineFiles(GlobalSystemMediaTransportControlsSession session, string folder, string playbackStatus)
{
    if (!timingEnabled) return;

    try
    {
        var timeline = session.GetTimelineProperties();
        TimeSpan start = timeline.StartTime;
        TimeSpan end = timeline.EndTime;
        TimeSpan duration = end - start;

        if (duration <= TimeSpan.Zero)
        {
            ClearTimelineFiles(folder);
            return;
        }

        TimeSpan position = timeline.Position;
        if (playbackStatus == "play")
        {
            TimeSpan elapsed = DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
            if (elapsed > TimeSpan.Zero)
                position += elapsed;
        }

        position = TimeSpan.FromTicks(Math.Clamp(position.Ticks, start.Ticks, end.Ticks));
        TimeSpan elapsedFromStart = position - start;
        TimeSpan remaining = end - position;
        double progress = elapsedFromStart.TotalSeconds / duration.TotalSeconds * 100.0;

        string Number(double value, string format) =>
            value.ToString(format, CultureInfo.InvariantCulture);

        WriteTextIfChanged(Path.Combine(folder, "time_position.txt"), Number(elapsedFromStart.TotalSeconds, "F1"));
        WriteTextIfChanged(Path.Combine(folder, "time_duration.txt"), Number(duration.TotalSeconds, "F1"));
        WriteTextIfChanged(Path.Combine(folder, "time_progress.txt"), Number(progress, "F2"));
        WriteTextIfChanged(Path.Combine(folder, "time_position_formatted.txt"), FormatTimeline(elapsedFromStart));
        WriteTextIfChanged(Path.Combine(folder, "time_duration_formatted.txt"), FormatTimeline(duration));
        WriteTextIfChanged(Path.Combine(folder, "time_remaining.txt"), Number(remaining.TotalSeconds, "F1"));
        WriteTextIfChanged(Path.Combine(folder, "time_remaining_formatted.txt"), FormatTimeline(remaining));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Timeline error ({session.SourceAppUserModelId}): {ex}");
        ClearTimelineFiles(folder);
    }
}

////// README & DOC


void WriteStartConsole()
{
    Console.WriteLine("=========================================================");
    Console.WriteLine("================== HOW DOES IT WORK ? ===================");
    Console.WriteLine("=========================================================");
    Console.WriteLine("When you play a song or video from any source (Chrome, TIDAL, Deezer, VLC, or anything captured by Windows), all the available data on the media will be stored here in C:/ NowPlayingToFile folder.");
    Console.WriteLine("The folder \"Main\" contains the data of the current active media session (determined by Windows..).");
    Console.WriteLine("Each other folder is named after the source of the media (e.g., Chrome, TIDAL, Deezer, VLC, etc.) and contains the data of that specific session.");
    Console.WriteLine("");
    Console.WriteLine("Files are updated every second when changes are detected.");
    Console.WriteLine("Not all files will contain information; I've tried to make it as comprehensive as possible to meet the widest range of expectations (see \"READ ME\" in your folder).");
    Console.WriteLine("");
    Console.WriteLine("");
    Console.WriteLine("=========================================================");
    Console.WriteLine("================== HOW TO USE IN OBS ? ==================");
    Console.WriteLine("=========================================================");
    Console.WriteLine("For each text information you want to show :");
    Console.WriteLine("Add a Text Source and check the \"Read from file\" option, then select the file you want to display and adjust the Font and everything you like");
    Console.WriteLine("For the cover or image :");
    Console.WriteLine("Add Image Source and select the Cover file");
    Console.WriteLine("Everything should be updated automatically");
    Console.WriteLine("");
    Console.WriteLine("=========================================================");
    Console.WriteLine("===================== MORE DETAILS ======================");
    Console.WriteLine("=========================================================");
    Console.WriteLine("You can find the README.txt and API_Doc.txt on the Now Playing To File Output Folder (default: C:/NowPlayingToFile/");
    Console.WriteLine("");
    Console.WriteLine("=========================================================");
    Console.WriteLine("The logs bellow will show you what informations NowPlayingToFile is grabbing");
    Console.WriteLine("");
    Console.WriteLine("=========================================================");
    Console.WriteLine("====================== SUPPORT ME =======================");
    Console.WriteLine("=========================================================");
    Console.WriteLine("Feel free to share the Project.");
    Console.WriteLine("Made by Shakawah");
    Console.WriteLine("");
    Console.WriteLine("If you like this free tool, you can support me ♥ https://ko-fi.com/shakawah");
    Console.WriteLine("");
    Console.WriteLine("=========================================================");
    Console.WriteLine("            ♥ Please read the lines above ♥");
    Console.WriteLine("=========================================================");
    Console.WriteLine("");
}

void WriteDocumentationIfChanged(string path, string content)
{
    if (File.Exists(path) &&
        File.ReadAllText(path) == content)
    {
        return;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);

    Console.WriteLine($"Documentation updated: {Path.GetFileName(path)}");
}

void CreateReadme()
{
    string readmePath = Path.Combine(outputFolder, "README.txt");

    string readme = """
=========================================================
================== HOW DOES IT WORK ? ==================
=========================================================
When you play a song or video from any source (Chrome, TIDAL, Deezer, VLC, or anything captured by Windows), all the available data on the media will be stored here in C:/NowPlayingToFile folder.
The folder "Main" contains the data of the current active media session (determined by Windows).
Each other folder is named after the source of the media (e.g., Chrome, TIDAL, Deezer, VLC, etc.) and contains the data of that specific session.

Files are updated every second when changes are detected.
Not all files will contain information; I've tried to make it as comprehensive as possible to meet the widest range of expectations (see "FILE CONTENTS" below).


=========================================================
================== HOW TO USE IN OBS ? ==================
=========================================================
For each text information you want to show:
Add a Text Source and check the "Read from file" option, then select the file you want to display and adjust the font and other settings.

For the cover or image:
Add an Image Source and select the cover file.
Everything should be updated automatically.


=========================================================
==================== FILE CONTENTS ======================
=========================================================
Files starting with "static_" and "dynamic_" contain information on the media playing.
The difference is that "dynamic" files will be emptied when media is not playing.
This can be used to show what's playing only during playback.

Files ending with "_spaced" have ten spaces at the end of the text.
This is useful for looping text, creating more space between repetitions.

    - album: Name of the album
    - artist: Artist name for music or channel name for videos / streams
    - title: Title of the song / video / stream
    - artist-title: Both artist and title in the same file
    - cover: Album cover or video thumbnail

Files starting with "time_" are used to track playback progress.
Files ending with "_formatted" display time as HH:MM:SS.

    - duration: Music/video duration in seconds
    - position: Playback position in seconds
    - progress: Progress in %
    - remaining: Remaining time in seconds

The _playback_status.txt file displays the current status
(play/pause/stop/none) to help create triggers with Streamer.bot or other tools.


=========================================================
====================== SUPPORT ME =======================
=========================================================
Feel free to share the project.
Made by Shakawah
Support me ♥ https://ko-fi.com/shakawah

=========================================================
=========================================================
""";

    WriteDocumentationIfChanged(readmePath, readme);
}


void CreateApiDoc()
{
    string apiDocPath = Path.Combine(outputFolder, "API_Doc.txt");

    string apiDoc = """
=========================================================
============== NOWPLAYINGTOFILE API DOC =================
=========================================================
NowPlayingToFile provides an HTTP API and a WebSocket server to retrieve media information and control playback 
Both are only on local : 127.0.0.1

You can change the port in the NowPlayingToFile_Settings.ini created in your .exe folder.
Default values for this are : 
http=true
websocket=true
mediaControl=true
port=8937

=========================================================
======================= HTTP ============================
=========================================================
==============================
=== HTTP : GET INFORMATION ===
==============================
HTTP URL : http://127.0.0.1:8937/
You can see the current medias playing (and refresh to get updated datas)
main            : Current main Windows media session (defined by Windows so use with care)
sessions        : All detected media sessions
playbackStatus  : Show media status : play, pause, stop or none
static          : Static media informations
dynamic         : Dynamic media informations
timeline        : Playback position and duration
controls        : Available playback controls

==============================
==== HTTP : MEDIA CONTROL ====
==============================
Endpoint :
    POST http://127.0.0.1:8937/control
Parameters :
    source : Media session name.
        Default is Main but i would recommand giving the media since only Windows controls what's the "main" media.
        Use the same name as you can see in the HTTP sessions
    action : Playback action (play, pause, toggle, next, previous, stop)

Exemple to pause TIDAL (which is shown in the sessions as "com.squirrel.TIDAL.TIDAL" :
    POST "http://127.0.0.1:8937/control?source=com.squirrel.TIDAL.TIDAL&action=pause"
(if you want to test with PowerShell : Invoke-RestMethod -Method POST "http://127.0.0.1:8937/control?source=com.squirrel.TIDAL.TIDAL&action=pause"")

If it works you should have a response :
{
  "success": true,
  "source": "com.squirrel.TIDAL.TIDAL",
  "action": "pause"
}

/!\ Not every media player supports every playback action (ie : you can't send "next" on a Twitch live)


=========================================================
====================== WEBSOCKET ========================
=========================================================
==============================
==== WS : GET INFORMATION ====
==============================
WebSocket URL: ws://127.0.0.1:8937/ws/
The server will send you a snapshot on connection, and then automatically sends all the changes.

Exception for the timeline, i wont send it each second when it's just playing, instead i send it when it's usefull :
        - When the media changes so you know you have to start over
        - When the media is paused
        - When the media resumes
        - When the user moves it manually
The timeline is not sent every second during normal playback, instead, the server sends a timeline update when:
    - The media changes
    - Playback is paused or resumed
    - A significant timeline difference is detected (when the user changes the timeline position manually)

You can use the last received position and timestamp to estimate the current playback position between updates.

With PowerShell you can try connecting like this :
    wscat -c ws://127.0.0.1:8937/ws/

You should receive the snapshot on the current media state.
The cover is stored on base64 so if you see a lot of random letters and numbers that should be it !

It should look like this : 
{
  "type": "snapshot",
  "data": {
    "main": {},
    "sessions": {}
  }
}

With the "main" object containing the main media session and the "sessions" object containing all detected media sessions with their source names (that can be just Numbers sometimes),
and you should have all the datas available in it like this for exemple : 
"com.squirrel.TIDAL.TIDAL": {
  "source": "com.squirrel.TIDAL.TIDAL",
  "playbackStatus": "play",
  "controls": {
    "Play": false,
    "Pause": true,
    "Toggle": true,
    "Next": true,
    "Previous": true,
    "Stop": true
  },
  "static": {
    "title": "Bien cordialement",
    "artist": "The Toxic Avenger",
    "artistTitle": "The Toxic Avenger - Bien cordialement",
    "album": "Yes Future (Extended)",
    "cover": "*ImageBase64*"
  },
  "dynamic": {
    "title": "Bien cordialement",
    "artist": "The Toxic Avenger",
    "artistTitle": "The Toxic Avenger - Bien cordialement",
    "album": "Yes Future (Extended)",
    "cover": "*ImageBase64*"
  },
  "timeline": {
    "position": "140.5",
    "duration": "423.4",
    "progress": "33.18",
    "remaining": "282.9",
    "positionFormatted": "02:20",
    "durationFormatted": "07:03",
    "remainingFormatted": "04:42"
  }
}



After the initial snapshot, the server will automatically send updates when media information changes (with only the only changed informations)
Exemple :
{
  "type": "update",
  "main": {
    "playbackStatus": "pause"
  }
}

==============================
===== WS : MEDIA CONTROL =====
==============================
Exemple to send a playback command:
{
  "type": "control",
  "source": "com.squirrel.TIDAL.TIDAL",
  "action": "next"
}
This would skip to the next song on TIDAL
If you want to try it on PowerShell, once connected to the ws serv, just send the json you want on one line : {"type": "control","source": "com.squirrel.TIDAL.TIDAL","action": "next"}

"source" is optionnal : it will change the "Main" session if not specified
Available actions : play, pause, toggle, next, previous and stop

Exemple of succedded response once you sent the control commande : 
{
  "type": "controlResult",
  "source": "Main",
  "action": "toggle",
  "success": true
}

/!\ Not every media player supports every playback action (ie : you can't send "next" on a Twitch live)
""";

    WriteDocumentationIfChanged(apiDocPath, apiDoc);
}


////// DONNEES SESSION
void PublishState(Dictionary<string, MediaControls> controlsByFolder)
{
    Dictionary<string, string> files;

    lock (filesLock)
    {
        files = new Dictionary<string, string>(lastWrittenFiles);
    }

    Interlocked.Exchange(ref currentState, new PublishedState
    {
        Files = files,
        Sessions = knownSessions.ToArray(),
        Controls = controlsByFolder,
        PublishedAt = DateTimeOffset.UtcNow
    });
}

object ReadSessionSnapshot(string folder, string source, PublishedState state)
{

    string Read(string name)
    {
        string path = Path.Combine(folder, name);
        return DataEnabled(path) && state.Files.TryGetValue(path, out string? value) ? value : "";
    }

    var result = new JsonObject
    {
        ["source"] = source,
        ["playbackStatus"] = Read("_playback_status.txt")
    };

    if (mediaControl && state.Controls.TryGetValue(folder, out var capabilities))
    {
        result["controls"] = JsonSerializer.SerializeToNode(capabilities);
    }

    if (staticEnabled)
    {
        result["static"] = new JsonObject
        {
            ["title"] = Read("static_title.txt"),
            ["artist"] = Read("static_artist.txt"),
            ["artistTitle"] = Read("static_artist-title.txt"),
            ["album"] = Read("static_album.txt"),
            ["cover"] = Read("static_cover.jpg")
        };
    }

    if (dynamicEnabled)
    {
        result["dynamic"] = new JsonObject
        {
            ["title"] = Read("dynamic_title.txt"),
            ["artist"] = Read("dynamic_artist.txt"),
            ["artistTitle"] = Read("dynamic_artist-title.txt"),
            ["album"] = Read("dynamic_album.txt"),
            ["cover"] = Read("dynamic_cover.jpg")
        };
    }

    if (timingEnabled)
    {
        result["timeline"] = new JsonObject
        {
            ["position"] = Read("time_position.txt"),
            ["duration"] = Read("time_duration.txt"),
            ["progress"] = Read("time_progress.txt"),
            ["remaining"] = Read("time_remaining.txt"),
            ["positionFormatted"] = Read("time_position_formatted.txt"),
            ["durationFormatted"] = Read("time_duration_formatted.txt"),
            ["remainingFormatted"] = Read("time_remaining_formatted.txt")
        };
    }

    return result;
}


///// HTTP & WS SERV
// Construction Json
object BuildJsonState(PublishedState state)
{
    var sessions = new SortedDictionary<string, object>(
        StringComparer.OrdinalIgnoreCase);

    foreach (string name in state.Sessions)
    {
        if (name == "Main")
            continue;

        string folder = Path.Combine(outputFolder, name);

        sessions[name] = ReadSessionSnapshot(folder, name, state);
    }

    return new
    {
        main = ReadSessionSnapshot(mainOutputFolder, "Main", state),
        sessions
    };
}

async Task RunHttpServerAsync(HttpListener listener)
{
    while (listener.IsListening)
    {
        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync();
        }
        catch (HttpListenerException) { break; }
        catch (ObjectDisposedException) { break; }

        if (websocket && context.Request.IsWebSocketRequest &&
            context.Request.Url?.AbsolutePath == "/ws/")
        {
            _ = Task.Run(() => HandleWebSocketAsync(context));
        }
        else if (mediaControl &&
                 context.Request.HttpMethod == "POST" &&
                 context.Request.Url?.AbsolutePath == "/control")
        {
            _ = Task.Run(() => HandleMediaControlRequestAsync(context));
        }
        else if (http && !context.Request.IsWebSocketRequest)
        {
            _ = Task.Run(() => HandleHttpRequestAsync(context));
        }
        else
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
        }
    }
}

async Task HandleHttpRequestAsync(HttpListenerContext context)
{
    HttpListenerResponse response = context.Response;

    var state = Volatile.Read(ref currentState);

    try
    {
        await SendJsonAsync(response, BuildJsonState(state));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"HTTP error: {ex}");
        try
        {
            if (response.OutputStream.CanWrite) await SendJsonAsync(response, new { error = "Internal server error" }, 500);
        }
        catch { /* Déconnecté */ }
    }
    finally
    {
        try
        {
            response.Close();
        }
        catch { }
    }
}

// Construit un delta pour UNE session. Les valeurs sont comparees en memoire,
JsonObject? BuildSessionDelta(string folder, PublishedState before, PublishedState after, TimelineSync sync)
{
    string Read(PublishedState state, string file) => DataEnabled(Path.Combine(folder, file)) && state.Files.TryGetValue(Path.Combine(folder, file), out string? value) ? value : "";

    var delta = new JsonObject();
    var media = new JsonObject();
    var dynamic = new JsonObject();

    string oldStatus = Read(before, "_playback_status.txt");
    string newStatus = Read(after, "_playback_status.txt");
    bool statusChanged = oldStatus != newStatus;
    if (statusChanged) delta["playbackStatus"] = newStatus;

    if (mediaControl)
    {
        before.Controls.TryGetValue(folder, out MediaControls? oldControls);
        after.Controls.TryGetValue(folder, out MediaControls? newControls);
        if (oldControls != newControls)
            delta["controls"] = JsonSerializer.SerializeToNode(newControls);
    }
    // Compare la position actuelle a la derniere timeline envoyee a CE client.
    bool readGapDetected = false;
    if (sync.TryGetValue(folder, out var reference) &&
        double.TryParse(Read(after, "time_position.txt"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out double actualPosition))
    {
        double expected = reference.Position;
        if (reference.Status == "play")
            expected += (after.PublishedAt - reference.At).TotalSeconds;

        // En fin de morceau, la position attendue ne depasse pas la duree.
        if (double.TryParse(Read(after, "time_duration.txt"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out double duration) && duration > 0)
            expected = Math.Clamp(expected, 0, duration);

        readGapDetected = Math.Abs(actualPosition - expected) > 5.0;
    }

    // Un changement de morceau doit synchroniser toute la timeline,
    // meme si certains champs de metadata sont identiques.
    bool songChanged = false;

    void Compare(JsonObject target, string property, string file, bool identifiesSong = false)
    {
        string oldValue = Read(before, file);
        string newValue = Read(after, file);
        if (oldValue == newValue) return;
        target[property] = newValue;
        if (identifiesSong) songChanged = true;
    }

    if (staticEnabled)
    {
        Compare(media, "title", "static_title.txt", true);
        Compare(media, "artist", "static_artist.txt", true);
        Compare(media, "artistTitle", "static_artist-title.txt");
        Compare(media, "album", "static_album.txt", true);
        Compare(media, "cover", "static_cover.jpg");
    }

    if (dynamicEnabled)
    {
        Compare(dynamic, "title", "dynamic_title.txt");
        Compare(dynamic, "artist", "dynamic_artist.txt");
        Compare(dynamic, "artistTitle", "dynamic_artist-title.txt");
        Compare(dynamic, "album", "dynamic_album.txt");
        Compare(dynamic, "cover", "dynamic_cover.jpg");
    }

    if (media.Count > 0) delta["static"] = media;
    if (dynamic.Count > 0) delta["dynamic"] = dynamic;

    // Timeline COMPLETE uniquement lors d'un changement de morceau ou d'etat.
    if (timingEnabled && (songChanged || statusChanged || readGapDetected))
    {
        delta["timeline"] = new JsonObject
        {
            ["position"] = Read(after, "time_position.txt"),
            ["duration"] = Read(after, "time_duration.txt"),
            ["progress"] = Read(after, "time_progress.txt"),
            ["remaining"] = Read(after, "time_remaining.txt"),
            ["positionFormatted"] = Read(after, "time_position_formatted.txt"),
            ["durationFormatted"] = Read(after, "time_duration_formatted.txt"),
            ["remainingFormatted"] = Read(after, "time_remaining_formatted.txt")
        };
    }

    return delta.Count == 0 ? null : delta;
}

JsonObject? BuildWebSocketDelta(PublishedState before, PublishedState after, TimelineSync sync)
{
    var update = new JsonObject { ["type"] = "update" };
    bool changed = false;

    JsonObject? main = BuildSessionDelta(mainOutputFolder, before, after, sync);
    if (main != null)
    {
        update["main"] = main;
        changed = true;
    }

    var sessions = new JsonObject();
    var oldNames = new HashSet<string>(before.Sessions, StringComparer.OrdinalIgnoreCase);
    foreach (string name in after.Sessions)
    {
        if (name.Equals("Main", StringComparison.OrdinalIgnoreCase)) continue;
        string folder = Path.Combine(outputFolder, name);
        if (!oldNames.Contains(name))
        {
            // Une nouvelle source doit etre envoyee en entier.
            sessions[name] = JsonSerializer.SerializeToNode(ReadSessionSnapshot(folder, name, after));
        }
        else
        {
            JsonObject? sessionDelta = BuildSessionDelta(folder, before, after, sync);
            if (sessionDelta != null) sessions[name] = sessionDelta;
        }
    }

    if (sessions.Count > 0)
    {
        update["sessions"] = sessions;
        changed = true;
    }
    return changed ? update : null;
}

// TIMELINE
void RememberTimeline(string folder, PublishedState state,
    TimelineSync sync)
{
    if (!timingEnabled) return;

    string Read(string file) => state.Files.TryGetValue(Path.Combine(folder, file), out var value) ? value : "";
    if (double.TryParse(Read("time_position.txt"), NumberStyles.Float,
        CultureInfo.InvariantCulture, out double position))
        sync[folder] = (position, state.PublishedAt, Read("_playback_status.txt"));
    else
        sync.Remove(folder);
}

async Task HandleWebSocketAsync(HttpListenerContext context)
{
    WebSocket? ws = null;
    using var sendLock = new SemaphoreSlim(1, 1);
    try
    {
        ws = (await context.AcceptWebSocketAsync(null)).WebSocket;
        Console.WriteLine("WebSocket client connected");

        PublishedState previous = Volatile.Read(ref currentState);

        byte[] snapshot = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "snapshot",
            data = BuildJsonState(previous)
        });

        await SendWsAsync(ws, sendLock, snapshot);

        TimelineSync sync = new();
        RememberTimeline(mainOutputFolder, previous, sync);
        foreach (string name in previous.Sessions)
            if (!name.Equals("Main", StringComparison.OrdinalIgnoreCase))
                RememberTimeline(Path.Combine(outputFolder, name), previous, sync);

        Task receiveTask = ReceiveWebSocketAsync(ws, sendLock);
        while (ws.State == WebSocketState.Open && !receiveTask.IsCompleted)
        {
            await Task.Delay(250);
            PublishedState next = Volatile.Read(ref currentState);
            if (ReferenceEquals(previous, next)) continue;

            JsonObject? delta = BuildWebSocketDelta(previous, next, sync);
            if (delta != null)
            {
                await SendWsAsync(ws, sendLock, JsonSerializer.SerializeToUtf8Bytes(delta));
                if (delta["main"] is JsonObject mainDelta && mainDelta.ContainsKey("timeline"))
                    RememberTimeline(mainOutputFolder, next, sync);

                if (delta["sessions"] is JsonObject sessionDeltas)
                    foreach (var item in sessionDeltas)
                        if (item.Value is JsonObject sessionDelta && sessionDelta.ContainsKey("timeline"))
                            RememberTimeline(Path.Combine(outputFolder, item.Key), next, sync);
            }
            previous = next;
        }
        await receiveTask;
    }
    catch (WebSocketException) { /* Client disconnected */ }
    catch (Exception ex) { Console.WriteLine($"WebSocket error: {ex}"); }
    finally
    {
        ws?.Dispose();
        Console.WriteLine("WebSocket client disconnected");
    }
}

async Task SendJsonAsync(HttpListenerResponse response, object payload, int statusCode = 200)
{
    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
    response.StatusCode = statusCode;
    response.ContentType = "application/json; charset=utf-8";
    response.ContentLength64 = bytes.Length;
    await response.OutputStream.WriteAsync(bytes);
}

// One sender at a time, per WebSocket client.
async Task SendWsAsync(WebSocket ws, SemaphoreSlim sendLock, byte[] data)
{
    await sendLock.WaitAsync();
    try
    {
        if (ws.State == WebSocketState.Open)
            await ws.SendAsync(new ArraySegment<byte>(data),
                WebSocketMessageType.Text, true, CancellationToken.None);
    }
    finally { sendLock.Release(); }
}

async Task ReceiveWebSocketAsync(WebSocket ws, SemaphoreSlim sendLock)
{
    byte[] buffer = new byte[4096];
    const int maxMessageSize = 16 * 1024;

    try
    {
        while (ws.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                Console.WriteLine(
                    $"WS RECEIVED: {Encoding.UTF8.GetString(buffer, 0, result.Count)}"
                );
                if (result.MessageType == WebSocketMessageType.Close ||
                    result.MessageType != WebSocketMessageType.Text ||
                    message.Length + result.Count > maxMessageSize)
                {
                    await sendLock.WaitAsync();
                    try
                    {
                        if (ws.State == WebSocketState.Open)
                            await ws.CloseOutputAsync(
                                message.Length + result.Count > maxMessageSize
                                    ? WebSocketCloseStatus.MessageTooBig
                                    : WebSocketCloseStatus.NormalClosure,
                                "Closing", CancellationToken.None);
                    }
                    finally { sendLock.Release(); }
                    return;
                }
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            try
            {
                using var doc = JsonDocument.Parse(message.ToArray());
                var json = doc.RootElement;
                if (json.ValueKind != JsonValueKind.Object ||
                    !json.TryGetProperty("type", out var type) ||
                    type.ValueKind != JsonValueKind.String ||
                    type.GetString() != "control") continue;

                string source = json.TryGetProperty("source", out var s) &&
                    s.ValueKind == JsonValueKind.String ? s.GetString() ?? "Main" : "Main";
                string action = json.TryGetProperty("action", out var a) &&
                    a.ValueKind == JsonValueKind.String ? a.GetString() ?? "" : "";

                bool success = mediaControl && await ControlMediaAsync(source, action);
                await SendWsAsync(ws, sendLock, JsonSerializer.SerializeToUtf8Bytes(new
                {
                    type = "controlResult",
                    source,
                    action,
                    success
                }));
            }
            catch (JsonException)
            {
                if (debug) Console.WriteLine("WebSocket: invalid JSON");
            }
        }
    }
    catch (WebSocketException) { /* Client déconnecté */ }
    catch (ObjectDisposedException) { }
}

////// GITHUB UPDATE CHECK
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

////// MEDIA CONTROL
static MediaControls ReadMediaControls(GlobalSystemMediaTransportControlsSession session)
{
    try
    {
        var controls = session.GetPlaybackInfo().Controls;
        return new MediaControls(
            controls.IsPlayEnabled,
            controls.IsPauseEnabled,
            controls.IsPlayPauseToggleEnabled,
            controls.IsNextEnabled,
            controls.IsPreviousEnabled,
            controls.IsStopEnabled);
    }
    catch
    {
        return new MediaControls(false, false, false, false, false, false);
    }
}


async Task HandleMediaControlRequestAsync(HttpListenerContext context)
{
    try
    {
        string source = context.Request.QueryString["source"] ?? "Main";
        string action = context.Request.QueryString["action"] ?? "";

        bool success = await ControlMediaAsync(source, action);

        await SendJsonAsync(context.Response, new
        {
            success,
            source,
            action
        }, success ? 200 : 400);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Control HTTP error: {ex}");
    }
    finally
    {
        try { context.Response.Close(); }
        catch { }
    }
}

async Task<bool> ControlMediaAsync(string source, string action)
{
    GlobalSystemMediaTransportControlsSession? session = null;

    if (source.Equals("Main", StringComparison.OrdinalIgnoreCase))
    {
        session = manager.GetCurrentSession();
    }
    else
    {
        session = manager.GetSessions().FirstOrDefault(s =>
            MakeSafeFolderName(s.SourceAppUserModelId ?? "Unknown")
                .Equals(source, StringComparison.OrdinalIgnoreCase));
    }

    if (session == null)
        return false;

    var controls = session.GetPlaybackInfo().Controls;

    try
    {
        switch (action.ToLowerInvariant())
        {
            case "play":
                if (!controls.IsPlayEnabled) return false;
                return await session.TryPlayAsync();

            case "pause":
                if (!controls.IsPauseEnabled) return false;
                return await session.TryPauseAsync();

            case "toggle":
                if (!controls.IsPlayPauseToggleEnabled) return false;
                return await session.TryTogglePlayPauseAsync();

            case "next":
                if (!controls.IsNextEnabled) return false;
                return await session.TrySkipNextAsync();

            case "previous":
                if (!controls.IsPreviousEnabled) return false;
                return await session.TrySkipPreviousAsync();

            case "stop":
                if (!controls.IsStopEnabled) return false;
                return await session.TryStopAsync();

            default:
                return false;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Media control error ({source}/{action}): {ex}");
        return false;
    }
}

////// CLASSES
public sealed record MediaControls(
    bool Play,
    bool Pause,
    bool Toggle,
    bool Next,
    bool Previous,
    bool Stop);

public sealed class PublishedState
{
    public Dictionary<string, string> Files { get; init; } = new();
    public string[] Sessions { get; init; } = Array.Empty<string>();
    public Dictionary<string, MediaControls> Controls { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset PublishedAt { get; init; }
}

public static class WindowsPopup
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
