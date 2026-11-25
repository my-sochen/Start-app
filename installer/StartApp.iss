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
DisableReadyPage=yes
DisableFinishedPage=no
DisableDirPage=yes
DisableProgramGroupPage=yes
CloseApplications=yes
UninstallDisplayName=Start App
VersionInfoVersion=0.2.0.0
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Options:"
Name: "launchafter"; Description: "Launch Start App when setup finishes"; GroupDescription: "Options:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userdesktop}\Start App"; Filename: "{app}\StartApp.App.exe"; Tasks: desktopicon
Name: "{group}\Start App"; Filename: "{app}\StartApp.App.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Start App"; ValueData: """{app}\StartApp.App.exe"" --autostart"; Flags: uninsdeletevalue

[Code]
function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if (CurPageID = wpFinished) and (not WizardSilent) and WizardIsTaskSelected('launchafter') then
  begin
    if not Exec(ExpandConstant('{app}\StartApp.App.exe'), '', '', SW_SHOWNORMAL, ewNoWait, ResultCode) then
    begin
      MsgBox('Unable to launch Start App: ' + SysErrorMessage(ResultCode), mbError, MB_OK);
      Result := False;
    end;
  end;
end;

