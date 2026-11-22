; Start App - beta installer (Inno Setup 6, free).
; Per-user install: no admin, no UAC prompt. Program files go to
; %LOCALAPPDATA%\Programs\StartApp; user data lives separately in
; %LOCALAPPDATA%\StartApp and is never touched by install, update or uninstall.
; Wizard (agreed 2026-11-20): opens on two default-checked checkboxes
; (desktop shortcut, launch when done) -> "הבא" -> progress bar -> "סיום".

#ifndef AppVersion
  #define AppVersion "0.2.0-beta.0"
#endif
#ifndef SourceDir
  #define SourceDir "payload"
#endif

[Setup]
AppId={{B7E4A1C2-5F3D-4E8A-9C60-2D1F8A5B3E77}
AppName=Start App
AppVersion={#AppVersion}
AppPublisher=Start App
DefaultDirName={localappdata}\Programs\StartApp
DefaultGroupName=Start App
PrivilegesRequired=lowest
OutputDir=..\out
OutputBaseFilename=StartApp-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableWelcomePage=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
CloseApplications=yes
UninstallDisplayName=Start App
VersionInfoVersion=0.2.0.0
MinVersion=10.0

[Languages]
Name: "hebrew"; MessagesFile: "compiler:Languages\Hebrew.isl"

[Tasks]
Name: "desktopicon"; Description: "יצירת קיצור דרך על שולחן העבודה"; GroupDescription: "אפשרויות:"
Name: "launchafter"; Description: "פתיחת התוכנה בסיום ההתקנה"; GroupDescription: "אפשרויות:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userdesktop}\Start App"; Filename: "{app}\StartApp.App.exe"; Tasks: desktopicon
Name: "{group}\Start App"; Filename: "{app}\StartApp.App.exe"

[Run]
Filename: "{app}\StartApp.App.exe"; Tasks: launchafter; Flags: nowait skipifsilent
