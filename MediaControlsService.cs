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


}
