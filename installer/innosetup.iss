; Open Click — Inno Setup script (requires Inno Setup 6+).
; Build the self-contained app first:
;   dotnet publish src/OpenClick.App/OpenClick.App.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
; Then compile this script with ISCC.

#define MyAppName "Open Click"
#define MyAppExe "OpenClick.exe"
#define MyAppVersion "1.0.0"
#define PublishDir "..\publish\win-x64"

[Setup]
AppId={{3E4A2B1C-7D6F-4A9E-8C5B-0F1E2D3C4B5A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\Open Click
DefaultGroupName=Open Click
OutputDir=..\publish\installer
OutputBaseFilename=OpenClick-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
PrivilegesRequired=lowest
UninstallDisplayName={#MyAppName}
WizardStyle=modern

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Tasks]
Name: desktopicon; Description: "Create a &desktop icon"; Flags: unchecked
Name: startupicon; Description: "Start with Windows (can be changed later in Settings)"; Flags: unchecked

[Icons]
Name: "{group}\Open Click"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\Open Click"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon
Name: "{userstartup}\Open Click"; Filename: "{app}\{#MyAppExe}"; Parameters: "--minimized"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Launch Open Click"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Settings intentionally preserved. User may delete %LOCALAPPDATA%\OpenClick manually.
