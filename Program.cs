
using Windows.Media.Control;

string outputFolder = @"C:\NowPlayingToFile";
Directory.CreateDirectory(outputFolder);
string mainOutputFolder = @"C:\NowPlayingToFile\Main";
Directory.CreateDirectory(mainOutputFolder);

Dictionary<string, string> lastSongsBySource = new();

Console.WriteLine("NowPlayingToFile - STARTING");

Console.WriteLine("=========================================================");
Console.WriteLine("================== HOW DOES IT WORKS ? ==================");
Console.WriteLine("=========================================================");
Console.WriteLine($"When you play a song or videos from any source (Chrome, TIDAL, Deezer, VLC, or anything captured by Windows), all the available data on the media will be stored here : {outputFolder}");
Console.WriteLine("The folder \"Main\" contains the data of the current active media session.");
Console.WriteLine("Each other folder is named after the source of the media (e.g., Chrome, TIDAL, Deezer, VLC, etc.) and contains the data of that specific session.");
Console.WriteLine("");
Console.WriteLine("Not all files will contain information; I've tried to make it as comprehensive as possible to meet the widest range of expectations.");
Console.WriteLine("I also added a \"_playback_status.txt\" file that displays the current status (play/pause/stop/none) to help you create triggers with StreamerBot or other tools.");
Console.WriteLine("Files are updated every 5 seconds if there was any changes.");
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
Console.WriteLine("");
Console.WriteLine("=========================================================");
Console.WriteLine("====================== SUPPORT ME =======================");
Console.WriteLine("=========================================================");
Console.WriteLine("Feel free to share the Project.");
Console.WriteLine("Made by Shakawah");
Console.WriteLine("Support me ♥ https://ko-fi.com/shakawah");
Console.WriteLine("=========================================================");
Console.WriteLine("=========================================================");
Console.WriteLine("=========================================================");
Console.WriteLine("");
Console.WriteLine("");
Console.WriteLine("Please read the lines above ♥");
Console.WriteLine("");
Console.WriteLine("");
Console.WriteLine("=========================================================");
Console.WriteLine("The logs bellow will show you what informations NowPlayingToFile is grabbing");

while (true)
{
    try
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var mainSession = manager.GetCurrentSession();
        var allSessions = manager.GetSessions();
        
        if (mainSession == null)
        {
            Console.WriteLine($"{DateTime.Now:T} - No active media found");
            File.WriteAllText(Path.Combine(mainOutputFolder, "_playback_status.txt"), "none");

            await Task.Delay(5000);
            continue;
        }
        
        var media = await mainSession.TryGetMediaPropertiesAsync();

        var playbackInfo = mainSession.GetPlaybackInfo();
        string playbackStatus = GetPlaybackStatusText(mainSession);
        File.WriteAllText(Path.Combine(mainOutputFolder, "_playback_status.txt"), playbackStatus);

        string mainKey = "MAIN";
        
        string title = media.Title ?? "";
        string artist = media.Artist ?? "";
        string album = media.AlbumTitle ?? "";
        string albumTrackCount = media.AlbumTrackCount > 0 ? media.AlbumTrackCount.ToString() : "";
        string trackNumber = media.TrackNumber > 0 ? media.TrackNumber.ToString() : "";

        string full = string.IsNullOrWhiteSpace(artist) ? title : $"{artist} - {title}";

        if ((!string.IsNullOrWhiteSpace(full)) && (!lastSongsBySource.TryGetValue(mainKey, out string? lastMainSong) || full != lastMainSong))
        {
            lastSongsBySource[mainKey] = full;

            File.WriteAllText(Path.Combine(mainOutputFolder, "title.txt"), title);
            File.WriteAllText(Path.Combine(mainOutputFolder, "title_spaced.txt"), $"{title}          ");

            File.WriteAllText(Path.Combine(mainOutputFolder, "artist.txt"), artist);
            File.WriteAllText(Path.Combine(mainOutputFolder, "artist_spaced.txt"), $"{artist}          ");

            File.WriteAllText(Path.Combine(mainOutputFolder, "artist-title.txt"), full);
            File.WriteAllText(Path.Combine(mainOutputFolder, "artist-title_spaced.txt"), $"{full}          ");

            File.WriteAllText(Path.Combine(mainOutputFolder, "album.txt"), album);
            File.WriteAllText(Path.Combine(mainOutputFolder, "album_spaced.txt"), $"{album}          ");

            File.WriteAllText(Path.Combine(mainOutputFolder, "album_track_count.txt"), albumTrackCount);

            File.WriteAllText(Path.Combine(mainOutputFolder, "track_number.txt"), trackNumber);


            if (media.Thumbnail != null)
            {
                using var stream = await media.Thumbnail.OpenReadAsync();
                using var input = stream.AsStreamForRead();
                using var output = File.Create(Path.Combine(mainOutputFolder, "cover.jpg"));

                await input.CopyToAsync(output);
            }

            Console.WriteLine($"{DateTime.Now:T} - MAIN SESSION {mainSession.SourceAppUserModelId} - {full}");
        }

        foreach (var session_ in allSessions)
        {
            try
            {
                string sourceName = session_.SourceAppUserModelId ?? "Unknown";

                string safeFolderName = string.Concat(sourceName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                string sessionFolder = Path.Combine(outputFolder, safeFolderName);
                Directory.CreateDirectory(sessionFolder);

                var media_ = await session_.TryGetMediaPropertiesAsync();

                var playbackInfo_ = session_.GetPlaybackInfo();
                string playbackStatus_ = GetPlaybackStatusText(session_);
                File.WriteAllText(Path.Combine(sessionFolder, "_playback_status.txt"), playbackStatus_);


                string title_ = media_.Title ?? "";
                string artist_ = media_.Artist ?? "";
                string album_ = media_.AlbumTitle ?? "";
                string albumTrackCount_ = media_.AlbumTrackCount > 0 ? media_.AlbumTrackCount.ToString() : "";
                string TrackNumber_ = media_.TrackNumber > 0 ? media_.TrackNumber.ToString() : "";

                string full_ = string.IsNullOrWhiteSpace(artist_) ? title_ : $"{artist_} - {title_}";

                string sessionKey = sourceName;
                string songKey = $"{title_}|{artist_}|{album_}|{albumTrackCount_}|{TrackNumber_}";

                Console.WriteLine(lastSongsBySource[sessionKey]);

                if (lastSongsBySource.TryGetValue(sessionKey, out string? lastSessionSong) && songKey != lastSessionSong)
                {
                    lastSongsBySource[sessionKey] = songKey;

                    File.WriteAllText(Path.Combine(sessionFolder, "title.txt"), title_);
                    File.WriteAllText(Path.Combine(sessionFolder, "title_spaced.txt"), $"{title_}          ");

                    File.WriteAllText(Path.Combine(sessionFolder, "artist.txt"), artist_);
                    File.WriteAllText(Path.Combine(sessionFolder, "artist_spaced.txt"), $"{artist_}          ");

                    File.WriteAllText(Path.Combine(sessionFolder, "artist-title.txt"), full_);
                    File.WriteAllText(Path.Combine(sessionFolder, "artist-title_spaced.txt"), $"{full_}          ");

                    File.WriteAllText(Path.Combine(sessionFolder, "album.txt"), album_);
                    File.WriteAllText(Path.Combine(sessionFolder, "album_spaced.txt"), $"{album_}          ");

                    File.WriteAllText(Path.Combine(sessionFolder, "album_track_count.txt"), albumTrackCount_);
                    File.WriteAllText(Path.Combine(sessionFolder, "track_number.txt"), TrackNumber_);


                    if (media_.Thumbnail != null)
                    {
                        using var stream = await media_.Thumbnail.OpenReadAsync();
                        using var input = stream.AsStreamForRead();
                        using var output = File.Create(Path.Combine(sessionFolder, "cover.jpg"));

                        await input.CopyToAsync(output);
                    }

                    Console.WriteLine($"{DateTime.Now:T} - {session_.SourceAppUserModelId} - {full_}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error : {ex.Message}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{DateTime.Now:T} - Erreur : {ex.Message}");
    }

    await Task.Delay(5000);
}

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