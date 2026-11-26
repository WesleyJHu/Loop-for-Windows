using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Loop.Platform;

/// <summary>Whether Loop runs with administrator rights, and restarting it with or without them.</summary>
public static class Elevation
{
    private const int ErrorCancelled = 1223; // the user said no to the UAC prompt

    /// <summary>True when this process runs elevated (with UAC on, merely being an admin isn't enough).</summary>
    public static bool IsElevated { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>Starts <paramref name="exePath"/> elevated (Windows asks the user). False if they declined.</summary>
    public static bool StartElevated(string exePath, string arguments = "")
    {
        try
        {
            Process.Start(new ProcessStartInfo(exePath, arguments) { UseShellExecute = true, Verb = "runas" })?.Dispose();
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return false;
        }
    }

    /// <summary>
    /// Starts <paramref name="exePath"/> without elevation, even from an elevated process: asking
    /// Explorer (which runs unelevated) to open it makes the new process inherit Explorer's rights.
    /// </summary>
    public static void StartUnelevated(string exePath) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{exePath}\"") { UseShellExecute = false })?.Dispose();
}
