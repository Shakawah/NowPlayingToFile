# NowPlayingToFile
Grab Windows media informations (VLC, Chrome, Deezer, TIDAL, etc.) to files so you can use it as Text Source for OBS for instance.  

#### [▶️ DOWNLOAD THE LATEST RELEASE HERE](https://github.com/Shakawah/NowPlayingToFile/releases/download/V2.0/NowPlayingToFile.exe)
.NET 8.0 required


#### HOW DOES IT WORK ? 
When you play a song or video from any source (Chrome, TIDAL, Deezer, VLC, or anything captured by Windows), all the available data on the media will be stored here in C:/NowPlayingToFile folder.
The folder "Main" contains the data of the current active media session (determined by Windows).
Each other folder is named after the source of the media (e.g., Chrome, TIDAL, Deezer, VLC, etc.) and contains the data of that specific session.

Files are updated every second when changes are detected.
Not all files will contain information; I've tried to make it as comprehensive as possible to meet the widest range of expectations (see "FILE CONTENTS" below).



#### HOW TO USE IN OBS ? 
For each text information you want to show:
Add a Text Source and check the "Read from file" option, then select the file you want to display and adjust the font and other settings.

For the cover or image:
Add an Image Source and select the cover file.
Everything should be updated automatically.



#### FILE CONTENTS 
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

#### HTTP/WS Servers
You can now retrieves the media data on local HTTP & WS servers to make more automation with tools like Twitchat, Streamerbot or more

#### MEDIA CONTROL
You can also use HTTP calls to control your medias (play / pause / next ...)
(more details to come)

#### SUPPORT ME 
Feel free to share the project.
Made by Shakawah
Support me ♥ https://ko-fi.com/shakawah
