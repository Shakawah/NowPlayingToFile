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

}
