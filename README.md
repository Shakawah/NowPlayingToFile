

# NowPlayingToFile
Grab Windows media informations (VLC, Chrome, Deezer, TIDAL, etc.) to files so you can use it as Text Source for OBS for instance.  

#### [▶️ DOWNLOAD THE LATEST RELEASE HERE](https://github.com/Shakawah/NowPlayingToFile/releases/download/V1.2_RELEASE/NowPlayingToFile.exe)


## HOW DOES IT WORKS ? 
When you play a song or videos from any source (Chrome, TIDAL, Deezer, VLC, or anything captured by Windows), **all the available data on the media will be stored here : C:\NowPlayingToFile**  
The folder "Main" contains the data of the current active media session.  
Each other folder is named after the source of the media (e.g., Deezer, VLC, Chrome, TIDAL, etc.) and contains the data of that specific session.  
  
Not all files will contain information; I've tried to make it as comprehensive as possible to meet the widest range of expectations.  
I also added a "_playback_status.txt" file that displays the current status (play/pause/stop/none) to help you create triggers with StreamerBot or other tools.  
Files are updated every 5 seconds if there was any changes.  
  
#### FOLDER OVERVIEW
```
C:\NowPlayingToFile\
└───Main
│   _playback_status.txt
│   album.txt
│   album_spaced.txt
│   album_track_count.txt
│   artist.txt
│   artist_spaced.txt
│   artist-title.txt
│   artist-title_spaced.txt
│   title.txt
│   title_spaced.txt
│   track_number.txt
│   cover.jpg
│   
└───Deezer
│   _playback_status.txt
│   album.txt
│   ...
│   
└───VLC
│   _playback_status.txt
│   album.txt
│   ...
│   
└───etc.
```

## HOW TO USE IN OBS ? 
For each text information you want to show :  
Add a Text Source and check the "Read from file" option, then select the file you want to display and adjust the Font and everything you like  
For the cover or image :  
Add Image Source and select the Cover file  
Everything should be updated automatically  
  
  
## SUPPORT ME 
Feel free to share the Project.  
Made by Shakawah  
Support me  https://ko-fi.com/shakawah
 
