using System.Security;

namespace Loop.Core.Settings;

/// <summary>How Loop gets started when the user signs in.</summary>
public enum StartupMethod
{
    None,
    /// <summary>The per-user Run registry key. Can't start an app with administrator rights without a prompt.</summary>
    RunKey,
    /// <summary>A Task Scheduler logon task with highest privileges: starts Loop elevated, no prompt.</summary>
    ScheduledTask,
}

public static class StartupPlan
{
    public const string TaskName = "LoopForWindows";

    /// <summary>The startup method for the chosen settings.</summary>
    public static StartupMethod Choose(bool launchAtStartup, bool runAsAdministrator) =>
        !launchAtStartup ? StartupMethod.None
        : runAsAdministrator ? StartupMethod.ScheduledTask
        : StartupMethod.RunKey;

    /// <summary>
    /// The Task Scheduler definition (for <c>schtasks /Create /XML</c>, saved as UTF-16) that starts
    /// <paramref name="exePath"/> with highest privileges when <paramref name="userSid"/> signs in.
    /// </summary>
    public static string ScheduledTaskXml(string exePath, string userSid) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Description>Starts Loop for Windows with administrator rights when you sign in.</Description>
          </RegistrationInfo>
          <Triggers>
            <LogonTrigger>
              <Enabled>true</Enabled>
              <UserId>{SecurityElement.Escape(userSid)}</UserId>
            </LogonTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>{SecurityElement.Escape(userSid)}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>HighestAvailable</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <AllowHardTerminate>true</AllowHardTerminate>
            <StartWhenAvailable>false</StartWhenAvailable>
            <IdleSettings>
              <StopOnIdleEnd>false</StopOnIdleEnd>
              <RestartOnIdle>false</RestartOnIdle>
            </IdleSettings>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>false</Hidden>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Priority>4</Priority>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{SecurityElement.Escape(exePath)}</Command>
            </Exec>
          </Actions>
        </Task>
        """;
}
