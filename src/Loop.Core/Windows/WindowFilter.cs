namespace Loop.Core.Windows;

/// <summary>Decides which top-level windows Loop should never move.</summary>
public static class WindowFilter
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",                    // desktop
        "WorkerW",                    // desktop wallpaper host
        "Shell_TrayWnd",              // primary taskbar
        "Shell_SecondaryTrayWnd",     // taskbar on other monitors
        "NotifyIconOverflowWindow",   // tray overflow
        "Windows.UI.Core.CoreWindow", // Start, search, and other shell flyouts
    };

    public static bool IsShellWindowClass(string? className) =>
        !string.IsNullOrEmpty(className) && ShellClasses.Contains(className);

    /// <summary>
    /// True when <paramref name="processName"/> is in the exclusion list. Matching ignores case and
    /// an optional ".exe" suffix on either side, so "Notepad", "notepad.exe" and "NOTEPAD" all match.
    /// </summary>
    public static bool IsExcluded(string? processName, IEnumerable<string> excludedApps)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        var name = Normalize(processName);
        return excludedApps.Any(e => !string.IsNullOrWhiteSpace(e) && Normalize(e) == name);
    }

    private static string Normalize(string name)
    {
        name = name.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.ToLowerInvariant();
    }
}
