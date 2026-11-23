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
}
