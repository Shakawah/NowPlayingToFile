using System;
using System.IO;
using System.Text;

public sealed class BufferedConsoleWriter : TextWriter
{
    private const int MaxLines = 5000;
    private const int MaxChars = 1024 * 1024;
    private readonly object sync = new();
    private readonly StringBuilder history = new();
    private TextWriter? consoleWriter;
    private int lineCount;

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value) => Write(value.ToString());

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        lock (sync)
        {
            history.Append(value);
            foreach (char c in value)
                if (c == '\n') lineCount++;
            TrimHistory();
            consoleWriter?.Write(value);
        }
    }

    public override void WriteLine(string? value) => Write((value ?? "") + Environment.NewLine);

    private void TrimHistory()
    {
        // Remove complete oldest lines until both limits are respected.
        while (lineCount > MaxLines || history.Length > MaxChars)
        {
            int end = 0;
            while (end < history.Length && history[end] != '\n') end++;
            if (end == history.Length)
            {
                // A single oversized line: keep its most recent portion.
                history.Remove(0, Math.Max(1, history.Length - MaxChars));
                break;
            }
            history.Remove(0, end + 1);
            lineCount--;
        }
    }

    public void AttachConsole()
    {
        lock (sync)
        {
            Console.OutputEncoding = Encoding.UTF8;

            consoleWriter ??= new StreamWriter(
                Console.OpenStandardOutput(),
                new UTF8Encoding(false))
            {
                AutoFlush = true
            };

            consoleWriter.Write(history.ToString());
        }
    }

    public void DetachConsole()
    {
        lock (sync)
        {
            consoleWriter?.Flush();
            consoleWriter = null;
        }
    }
}
