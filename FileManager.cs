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
    
    void WriteCover(string path, byte[] image, string base64, string mime)
    {
        if (!DataEnabled(path)) return;
    
        if (writefile) File.WriteAllBytes(path, image);
    
        lock (filesLock)
        {
            lastWrittenFiles[path] = $"data:{mime};base64,{base64}";
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
                    lastCoversMime.Remove(sessionKey);
                }
            }
            else
            {
                try
                {
                    using var stream = await media.Thumbnail.OpenReadAsync();
                    string mime = stream.ContentType;
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
    
                            WriteCover(coverPath, newImage, base64img, mime);
                            lastCovers[sessionKey] = newImage;
                            lastCoversMime[sessionKey] = mime;
    
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
            lastCovers.TryGetValue(sessionKey, out byte[]? cover) &&
            lastCoversMime.TryGetValue(sessionKey, out string? coverMime))
        {
            string base64 = Convert.ToBase64String(cover);
    
            if (!lastWrittenFiles.TryGetValue(dynamicCoverPath, out string? oldBase64)
                || oldBase64 != base64)
            {
                WriteCover(dynamicCoverPath, cover, base64, coverMime);
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
}
