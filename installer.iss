#define MyAppName "WinClean 95"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "dojer"
#define MyAppExeName "Cleaner.App.exe"

[Setup]
AppId={{C7D0E6B7-7A4A-4CDE-A4E6-9C8B4C9E5F6A}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\WinClean 95
DefaultGroupName={#MyAppName}
OutputDir=installer
OutputBaseFilename=WinClean95-Setup
SetupIconFile=Cleaner.App\Assets\WinCleanLogo.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts"; Flags: unchecked

[Files]
Source: "Cleaner.App\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch WinClean 95"; Flags: nowait postinstall skipifsilent
