; Inno Setup script for Loop for Windows. Build with installer\build.ps1, which publishes the app
; and passes AppVersion and SourceDir.
;
; Installs per user by default (no administrator rights needed), into
; %LOCALAPPDATA%\Programs\Loop for Windows; the first page also offers installing for all users.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif

#define AppName "Loop for Windows"
#define AppExe "LoopForWindows.exe"

[Setup]
AppId={{EAD31984-E1B7-4027-9594-832FB999AC8D}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
AppPublisherURL=https://github.com/WesleyJHu/Loop-for-Windows
AppSupportURL=https://github.com/WesleyJHu/Loop-for-Windows/issues
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\artifacts
OutputBaseFilename=LoopForWindows-Setup-{#AppVersion}
SetupIconFile=..\src\Loop.App\Assets\loop.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; Close a running Loop before replacing it (and restart it after an upgrade).
CloseApplications=yes
RestartApplications=yes

[Tasks]
Name: "startup"; Description: "Start Loop when I sign in"; GroupDescription: "Startup:"

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autoprograms}\{#AppName} settings"; Filename: "{app}\{#AppExe}"; Parameters: "--settings"

[Registry]
; The same value Loop's own "Start Loop when I sign in" setting uses.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LoopForWindows"; \
  ValueData: """{app}\{#AppExe}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start Loop now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop Loop, and remove what it may have registered itself (the startup task needs administrator
; rights if Loop created it while running as administrator; if this fails, Task Scheduler still lists it).
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "StopLoop"
Filename: "{sys}\reg.exe"; Parameters: "delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v LoopForWindows /f"; Flags: runhidden; RunOnceId: "RemoveRunKey"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN LoopForWindows /F"; Flags: runhidden; RunOnceId: "RemoveStartupTask"

; Settings in %APPDATA%\LoopForWindows are kept, so reinstalling keeps your preferences.
