#define MyAppName "RMV grand INN Manager"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Your Company Name"
#define MyAppExeName "room_booking.exe"
#define MyAppIcon "kh.ico"

[Setup]
AppId={{B1A2C3D4-E5F6-4A7B-8C9D-1234567890AB}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=D:\room_booking\Output
OutputBaseFilename=EatAndMeSetup_{#MyAppVersion}
SetupIconFile=D:\room_booking\{#MyAppIcon}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "D:\room_booking\bin\Debug\net8.0-windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "D:\room_booking\kh.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppIcon}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppIcon}"; Tasks: desktopicon
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postlaunch skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"