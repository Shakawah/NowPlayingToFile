using System;
using System.IO;

public static class DocumentationManager
{
    public static void WriteStartConsole()
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
        Console.WriteLine("            Please read the lines above ");
        Console.WriteLine("=========================================================");
        Console.WriteLine("");
    }
    
    private static void WriteDocumentationIfChanged(string path, string content)
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
    
    public static void CreateReadme(string outputFolder)
    {
        string readmePath = Path.Combine(outputFolder, "README.txt");
    
        string readme = """
    # HOW DOES IT WORK ? 
    When you **play a song or video from any source** (Chrome, TIDAL, Deezer, or anything captured by Windows), all the available data on the media will be stored locally in **C:/NowPlayingToFile folder**.
    The folder "Main" contains the data of the current active media session (determined by Windows).
    Each other folder is named after the source of the media (e.g., Chrome, TIDAL, Deezer, VLC, etc.) and contains the data of that specific session.
    
    Files are updated every second when changes are detected.
    Not all files will contain information; I've tried to make it as comprehensive as possible to meet the widest range of expectations (see "FILE CONTENTS" below).
    
    
    # HOW TO USE IN OBS ? 
    For each text information you want to show:
    - Add a Text Source
    - Check the "Read from file" option
    - Select the file you want to display
    - Adjust the font and other settings
    
    For the cover or image:
    - Add an Image Source
    - Select the cover file
    
    Everything will be updated automatically.
    
    
    # FILE CONTENTS 
    Files starting with "**static_**" and "**dynamic_**" contain **information on the media playing**.
    The difference is that "dynamic" files will be emptied when media is not playing.
    This can be used to show what's playing only during playback.
    
    Files ending with "_spaced" have ten spaces at the end of the text.
    This is useful for looping text, creating more space between repetitions.
    - **album** : Name of the album
    - **artist** : Artist name for music or channel name for videos / streams
    - **title** : Title of the song / video / stream
    - **artist-title** : Both artist and title in the same file
    - **cover** : Album cover or video thumbnail
    
    Files starting with "**time_**" are used to **track playback progress**.
    Files ending with "_formatted" display time as HH:MM:SS.
    - **duration** : Music/video duration in seconds
    - **position** : Playback position in seconds
    - **progress** : Progress in %
    - **remaining** : Remaining time in seconds
    
    The _playback_status.txt file displays the current status
    (play/pause/stop/none) to help create triggers with Streamer.bot or other tools.
    
    # HTTP & WS Servers
    NowPlayingToFile also provides local **HTTP and WebSocket** servers on wich you can
    - Retrieve your media datas
    - Control your medias (play/pause/skip/next)
    All the informations are on the API_Doc.txt.
    
    # MY PLAYER IS NOT SHOWING
    NowPlayingToFile detects applications that use Windows SMTC (System Media Transport Controls).
    Some players (VLC, Winamp, Qobuz, and others) don't support SMTC natively, but plugins or extensions may be available to make them compatible.
    Compatibility should be similar to Nutty's list, which you can find here:
    https://nuttylmao.notion.site/38f19969b23780359d57e0173c382266?v=38f19969b23780358036000c8cb21800
    We're not using exactly the same technology, but most of the solutions listed there should work with NowPlayingToFile too!

    # SUPPORT ME 
    Feel free to share the project,
    Made by **Shakawah**
    **If you like this free tool, you can support me ♥ https://ko-fi.com/shakawah**
    """;
    
        WriteDocumentationIfChanged(readmePath, readme);
    }
    
    
    public static void CreateApiDoc(string outputFolder)
    {
        string apiDocPath = Path.Combine(outputFolder, "API_Doc.txt");
    
        string apiDoc = """
    
    #  NOWPLAYINGTOFILE API DOC 
    **NowPlayingToFile** provides an **HTTP API and a WebSocket server** to retrieve **media information and control playback**
    *Both are only on local : 127.0.0.1*
    
    You can change the port in the **NowPlayingToFile_Settings.ini** created in your .exe folder.
    
    Default values for this are :
    ``` 
    http=true
    websocket=true
    mediaControl=true
    port=8937
    ```
    
    ## HTTP 
    ### HTTP : GET INFORMATION 
    **HTTP URL : http://127.0.0.1:8937/**
    You can see the current medias playing (and refresh to get updated datas)
    - main            : Current main Windows media session (defined by Windows so use with care)
    - sessions        : All detected media sessions
    - playbackStatus  : Show media status : play, pause, stop or none
    - static          : Static media informations
    - dynamic         : Dynamic media informations
    - timeline        : Playback position and duration
    - controls        : Available playback controls
    
    The JSON looks like this :
    ```
    {
        "main": {},
        "sessions": {}
    }
    ```
    With the "main" object containing the main media session and the "sessions" object containing all detected media sessions with their source names (that can be just Numbers sometimes),
    and you should have all the datas available in it like this for exemple : 
    ```
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
        "cover": "data:image/png;base64,*ImageBase64*"
      },
      "dynamic": {
        "title": "Bien cordialement",
        "artist": "The Toxic Avenger",
        "artistTitle": "The Toxic Avenger - Bien cordialement",
        "album": "Yes Future (Extended)",
        "cover": "data:image/png;base64,*ImageBase64*"
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
    ```
    
    ### HTTP : MEDIA CONTROL 
    - Endpoint : **POST http://127.0.0.1:8937/control**
    - Parameters :
    	-    source : Media session name.
    	        *Default is Main but i would recommand giving the media since only Windows controls what's the "main" media.
    	        Use the same session name as you can see in the HTTP sessions*
    	-    action : Playback action (play, pause, toggle, next, previous, stop)
    
    Exemple to pause TIDAL (which is shown in the sessions as "com.squirrel.TIDAL.TIDAL") :
    ```
    POST "http://127.0.0.1:8937/control?source=com.squirrel.TIDAL.TIDAL&action=pause"
    ```
    If you want to test with PowerShell :
    ```
    Invoke-RestMethod -Method POST "http://127.0.0.1:8937/control?source=com.squirrel.TIDAL.TIDAL&action=pause"")
    ```
    If it works you should have a response :
    ```
    {
      "success": true,
      "source": "com.squirrel.TIDAL.TIDAL",
      "action": "pause"
    }
    ```
    **/!\\** Not every media player supports every playback action (ie : you can't send "next" on a Twitch live)
    
    
    ## WEBSOCKET
    ### WS : SUBSCRIBE TO LIVE CHANGES
    **WebSocket URL : ws://127.0.0.1:8937/ws/**
    The server will send you a snapshot on connection, and then automatically sends all the changes.
    
    The timeline is not sent every second during normal playback, instead, the server sends a timeline update when:
    - The media changes
    - Playback is paused or resumed
    - A significant timeline difference is detected (when the user changes the timeline position manually)
    
    You can use the last received position and timestamp to estimate the current playback position between updates.
    
    With PowerShell you can try connecting like this :
    ```
        wscat -c ws://127.0.0.1:8937/ws/
    ```
    
    You should receive the snapshot on the current media state.
    The cover is stored on base64 so if you see a lot of random letters and numbers that should be it !
    
    It should look like this : 
    ```
    {
      "type": "snapshot",
      "data": {
        "main": {},
        "sessions": {}
      }
    }
    ```
    With the "main" object containing the main media session and the "sessions" object containing all detected media sessions with their source names (that can be just Numbers sometimes),
    and you should have all the datas available in it like this for exemple : 
    ```
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
        "cover": "data:image/png;base64,*ImageBase64*"
      },
      "dynamic": {
        "title": "Bien cordialement",
        "artist": "The Toxic Avenger",
        "artistTitle": "The Toxic Avenger - Bien cordialement",
        "album": "Yes Future (Extended)",
        "cover": "data:image/png;base64,*ImageBase64*"
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
    ```
    
    
    After the initial snapshot, the server will automatically send updates when media information changes (with only the only changed informations)
    Exemple :
    ```
    {
      "type": "update",
      "main": {
        "playbackStatus": "pause"
      }
    }
    ```
    ### WS : MEDIA CONTROL 
    Exemple to send a playback command :
    ```
    {
      "type": "control",
      "source": "com.squirrel.TIDAL.TIDAL",
      "action": "next"
    }
    ```
    This would skip to the next song on TIDAL
    
    If you want to try it on PowerShell, once connected to the ws serv, just send the json you want on one line :
    ```{"type": "control","source": "com.squirrel.TIDAL.TIDAL","action": "next"}```
    
    - "source" is optionnal : it will change the "Main" session if not specified
    - Available actions : play, pause, toggle, next, previous and stop
    
    Exemple of succedded response once you sent the control commande : 
    ```
    {
      "type": "controlResult",
      "source": "Main",
      "action": "toggle",
      "success": true
    }
    ```
    
    **/!\\** Not every media player supports every playback action (ie : you can't send "next" on a Twitch live)
    
    
    """;
    
        WriteDocumentationIfChanged(apiDocPath, apiDoc);
    }

}
