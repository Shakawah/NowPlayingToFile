using System;
using System.Collections.Generic;
using System.IO;

public static class SettingsManager
{
    public static AppSettings Load()
    {
        ///// SETTINGS.INI
        string settingsPath = Path.Combine(AppContext.BaseDirectory, "NowPlayingToFile_Settings.ini");
        if (!File.Exists(settingsPath))
        {
            File.WriteAllText(settingsPath, """
                ### This is de Settings file for NowPlayingToFile.
                ### Only change the values, not the variables names. If you want to rollback you can delete this file and launch NowPlayingToFile again it will recreate this file with default values.
                ### If you want more details on the definitions read the ReadMe.txt in your outputFolder
        
                ### Reboot Now PLaying To File to Apply changes !
                
                # Show the welcome window on startup (def : true)
                showWelcome=true

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

        bool showWelcome = SettingBool("showWelcome");
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
        
        
        
        return new AppSettings(settingsPath, writefile, http, websocket, staticEnabled,
            dynamicEnabled, timingEnabled, port, outputFolder, timeCheck, mediaControl, debug, checkForUpdates, showWelcome);
    }
}
public sealed record AppSettings(
    string SettingsPath, bool WriteFile, bool Http, bool Websocket,
    bool StaticEnabled, bool DynamicEnabled, bool TimingEnabled,
    int Port, string OutputFolder, int TimeCheck, bool MediaControl,
    bool Debug, bool CheckForUpdates, bool ShowWelcome);
