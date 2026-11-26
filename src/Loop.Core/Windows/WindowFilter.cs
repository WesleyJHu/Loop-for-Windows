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
}
