using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

////// CLASSES
public sealed record MediaControls(
    bool Play,
    bool Pause,
    bool Toggle,
    bool Next,
    bool Previous,
    bool Stop);

public sealed class PublishedState
{
    public Dictionary<string, string> Files { get; init; } = new();
    public string[] Sessions { get; init; } = Array.Empty<string>();
    public Dictionary<string, MediaControls> Controls { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> SourceIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset PublishedAt { get; init; }
}

public static class WindowsPopup
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
