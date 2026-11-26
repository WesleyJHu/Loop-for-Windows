namespace Loop.Platform;

/// <summary>
/// Minimal diagnostics. Always writes to the debugger; also appends to the file named by the
/// LOOP_LOG environment variable when it is set. Never log key presses: only actions and geometry.
/// </summary>
public static class Log
{
    private static readonly string? FilePath = Environment.GetEnvironmentVariable("LOOP_LOG");
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        System.Diagnostics.Debug.WriteLine($"[Loop] {line}");
        if (FilePath is null) return;
        try
        {
            lock (Gate) File.AppendAllText(FilePath, line + Environment.NewLine);
        }
        catch (IOException)
        {
            // Diagnostics must never break window management.
        }
    }
}
