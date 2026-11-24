
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

public static class SourceNameResolver
{
    private static readonly Dictionary<string, string> appNames = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object cacheLock = new();

    private static DateTime lastRefresh = DateTime.MinValue;
    private static readonly TimeSpan refreshInterval = TimeSpan.FromMinutes(5);

    
    public static string GetDisplayName(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return sourceId ?? "";
        }

        lock (cacheLock)
        {
            // Cache des applis
            if (DateTime.UtcNow - lastRefresh >= refreshInterval)
            {
                appNames.Clear();
                LoadWindowsApps();
                lastRefresh = DateTime.UtcNow;
            }

            // 1e tentative : Recherche dans le cache des applis (AppShells)
            if (appNames.TryGetValue(sourceId, out string? name))
            {
                return name;
            }

            // 2e tentative : Recherches dans les Windows App
            try
            {
                Console.WriteLine("2");
                var app = AppInfo.GetFromAppUserModelId(sourceId);
                name = app.DisplayInfo.DisplayName;

                if (!string.IsNullOrWhiteSpace(name) && !name.Equals(sourceId, StringComparison.OrdinalIgnoreCase))
                {
                    appNames[sourceId] = name;
                }
            }
            catch { }

            // 3e tentative : Dans les process en cours
            name = FindProcessName(sourceId);
            if (!string.IsNullOrWhiteSpace(name))
            {
                appNames[sourceId] = name;
                return name;
            }

            // Sinon on garde la base
            appNames[sourceId] = sourceId;

            return sourceId;
        }
    }

    private static string? FindProcessName(string sourceId)
    {
        string processName = sourceId;

        if (sourceId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            processName = Path.GetFileNameWithoutExtension(sourceId);
        }

        Process[] processes;

        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch
        {
            return null;
        }

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    string? name = process.MainModule?
                        .FileVersionInfo.FileDescription;

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }
                catch { }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        return null;
    }

    private static void LoadWindowsApps()
    {
        Type? shellType = Type.GetTypeFromProgID("Shell.Application");

        if (shellType == null)
            return;

        object? shell = null;
        object? folder = null;
        object? items = null;

        try
        {
            shell = Activator.CreateInstance(shellType);

            if (shell == null)
                return;

            dynamic shellApp = shell;

            folder = shellApp.NameSpace("shell:AppsFolder");

            if (folder == null)
                return;

            items = ((dynamic)folder).Items();

            if (items == null)
                return;

            dynamic appItems = items;
            int count = appItems.Count;

            for (int i = 0; i < count; i++)
            {
                object? item = null;

                try
                {
                    item = appItems.Item(i);

                    if (item == null)
                        continue;

                    string id = ((dynamic)item).Path;
                    string name = ((dynamic)item).Name;

                    if (!string.IsNullOrWhiteSpace(id) &&
                        !string.IsNullOrWhiteSpace(name))
                    {
                        appNames[id] = name;
                    }
                }
                catch { }
                finally
                {
                    ReleaseComObject(item);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"App name lookup error: {ex.Message}");
        }
        finally
        {
            ReleaseComObject(items);
            ReleaseComObject(folder);
            ReleaseComObject(shell);
        }
    }

    private static void ReleaseComObject(object? obj)
    {
        if (obj != null && Marshal.IsComObject(obj))
        {
            Marshal.ReleaseComObject(obj);
        }
    }
}
