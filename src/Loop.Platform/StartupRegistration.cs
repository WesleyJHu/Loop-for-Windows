using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using Loop.Core.Settings;
using Microsoft.Win32;

namespace Loop.Platform;

/// <summary>
/// "Start Loop when I sign in". Normally the per-user Run registry key (no admin rights needed).
/// When Loop runs as administrator, a Task Scheduler logon task with highest privileges instead:
/// the Run key can't start an elevated app without a UAC prompt at every sign-in.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "LoopForWindows";

    /// <summary>True when Loop is registered to start at sign-in, either way.</summary>
    public static bool IsEnabled() => RunKeyExists() || TaskExists();

    /// <summary>
    /// Makes the registration match the settings: registers the right way and removes the other.
    /// Returns a user-facing problem, or null when everything is as requested.
    /// </summary>
    /// <param name="launchAtStartup">Whether Loop should start at sign-in.</param>
    /// <param name="runAsAdministrator">Whether it should start with administrator rights.</param>
    /// <param name="exePath">Loop's executable.</param>
    public static string? Apply(bool launchAtStartup, bool runAsAdministrator, string exePath)
    {
        var method = StartupPlan.Choose(launchAtStartup, runAsAdministrator);

        // Not elevated yet (e.g. the UAC prompt was declined): leave everything as it is for now.
        if (method == StartupMethod.ScheduledTask && !Elevation.IsElevated)
            return "Starting Loop as administrator at sign-in can only be set up while Loop runs as administrator.";

        SetRunKey(method == StartupMethod.RunKey ? exePath : null);

        if (method == StartupMethod.ScheduledTask)
            return CreateTask(exePath) ? null : "Could not create the startup task in Task Scheduler.";

        if (TaskExists() && !DeleteTask())
            return "Loop's startup task was created with administrator rights, so it can only be removed while Loop runs as administrator.";
        return null;
    }

    private static bool RunKeyExists()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    /// <summary>Sets the Run value to start <paramref name="exePath"/>, or removes it when null.</summary>
    private static void SetRunKey(string? exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (exePath is not null) key.SetValue(ValueName, $"\"{exePath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static bool TaskExists() => Schtasks($"/Query /TN \"{StartupPlan.TaskName}\"") == 0;

    private static bool DeleteTask() => Schtasks($"/Delete /TN \"{StartupPlan.TaskName}\" /F") == 0;

    private static bool CreateTask(string exePath)
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (sid is null) return false;
        var xmlPath = Path.Combine(Path.GetTempPath(), $"LoopForWindows-task-{Environment.ProcessId}.xml");
        try
        {
            File.WriteAllText(xmlPath, StartupPlan.ScheduledTaskXml(exePath, sid), Encoding.Unicode); // the XML declares UTF-16
            return Schtasks($"/Create /TN \"{StartupPlan.TaskName}\" /XML \"{xmlPath}\" /F") == 0;
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    /// <summary>Runs schtasks.exe without a window; returns its exit code (-1 if it couldn't run).</summary>
    private static int Schtasks(string arguments)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (p is null) return -1;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            if (!p.WaitForExit(10_000)) return -1;
            return p.ExitCode;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return -1;
        }
    }
}
