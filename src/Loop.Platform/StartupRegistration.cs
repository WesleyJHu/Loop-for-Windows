using Microsoft.Win32;

namespace Loop.Platform;

/// <summary>"Launch at startup", via the per-user Run key (no admin rights needed).</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string DefaultValueName = "LoopForWindows";

    public static bool IsEnabled(string valueName = DefaultValueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(valueName) is string;
    }

    /// <summary>Registers (or unregisters) <paramref name="exePath"/> to start when the user signs in.</summary>
    public static void SetEnabled(bool enabled, string exePath, string valueName = DefaultValueName)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled) key.SetValue(valueName, $"\"{exePath}\"");
        else key.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
