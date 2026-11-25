; Start App - beta installer (Inno Setup 6, free).
; Per-user install: no admin, no UAC prompt. Program files go to
; %LOCALAPPDATA%\Programs\StartApp; user data lives separately in
; %LOCALAPPDATA%\StartApp and is never touched by install, update or uninstall.
; Wizard: desktop shortcut and launch when done default checked; Windows startup
; is a separate opt-in, default unchecked, including upgrades.

#ifndef AppVersion
  #define AppVersion "0.2.0-beta.0"
#endif
#ifndef SourceDir
  #define SourceDir "payload"
#endif

; One source icon, shared with StartApp.App.csproj. Replace the source asset
; only after the owner chooses the new design, then rebuild app + installer.
#define AppIcon "..\src\StartApp.App\Assets\start-app-ufo.ico"

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
UsePreviousTasks=no
CloseApplications=yes
UninstallDisplayName=Start App
SetupIconFile={#AppIcon}
UninstallDisplayIcon={app}\Assets\start-app-ufo.ico
VersionInfoVersion=0.2.0.0
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Options:"
Name: "launchafter"; Description: "Launch Start App when setup finishes"; GroupDescription: "Options:"
Name: "startup"; Description: "Start Start App when I sign in to Windows"; GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; BeforeInstall: FileCopyStarting

[Icons]
Name: "{userdesktop}\Start App"; Filename: "{app}\StartApp.App.exe"; IconFilename: "{app}\Assets\start-app-ufo.ico"; Tasks: desktopicon
Name: "{group}\Start App"; Filename: "{app}\StartApp.App.exe"; IconFilename: "{app}\Assets\start-app-ufo.ico"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Start App"; ValueData: """{app}\StartApp.App.exe"" --autostart"; Flags: uninsdeletevalue; Tasks: startup
; Unchecked also removes the older installer's unconditional registration on upgrade.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Start App"; Flags: deletevalue; Tasks: not startup

[Code]
var
  Heading, Subtitle, PercentLabel, CopyHint: TNewStaticText;
  DetailsButton: TNewButton;
  DetailsMemo: TNewMemo;
  ShortcutCheck, LaunchCheck, StartupCheck: TNewCheckBox;
  ShortcutCard, LaunchCard, StartupCard, SuccessCard, ProgressTrack, ProgressFill: TPanel;
  Art: TBitmapImage;

procedure DetailsClick(Sender: TObject);
begin
  DetailsMemo.Visible := not DetailsMemo.Visible;
  if DetailsMemo.Visible then DetailsButton.Caption := 'Hide details'
  else DetailsButton.Caption := 'Show details';
end;

function Card(Y: Integer; const Text: String): TPanel;
begin
  Result := TPanel.Create(WizardForm);
  Result.Parent := WizardForm;
  Result.SetBounds(ScaleX(32), ScaleY(Y), ScaleX(756), ScaleY(48));
  Result.ParentBackground := False;
  Result.BevelOuter := bvNone;
  Result.Color := $00FCF9F7;
  Result.Caption := Text;
end;

function TextLabel(X, Y, W, H, Size: Integer; const Text: String): TNewStaticText;
begin
  Result := TNewStaticText.Create(WizardForm);
  Result.Parent := WizardForm;
  Result.AutoSize := False;
  Result.SetBounds(ScaleX(X), ScaleY(Y), ScaleX(W), ScaleY(H));
  Result.Font.Size := Size;
  Result.Font.Color := $00332822;
  Result.Caption := Text;
end;

procedure InitializeWizard;
begin
  WizardForm.ClientWidth := ScaleX(820);
  WizardForm.ClientHeight := ScaleY(372);
  WizardForm.Position := poScreenCenter;
  WizardForm.Caption := 'Install Start App';
  WizardForm.Color := clWhite;
  WizardForm.Font.Name := 'Segoe UI';
  WizardForm.Font.Size := 10;
  WizardForm.OuterNotebook.Visible := False;
  WizardForm.MainPanel.Visible := False;
  WizardForm.Bevel1.Visible := False;
  WizardForm.Bevel.SetBounds(0, ScaleY(314), ScaleX(820), ScaleY(2));
  WizardForm.NextButton.SetBounds(ScaleX(584), ScaleY(330), ScaleX(96), ScaleY(30));
  WizardForm.CancelButton.SetBounds(ScaleX(692), ScaleY(330), ScaleX(96), ScaleY(30));
  Heading := TextLabel(32, 26, 650, 34, 18, 'Install Start App');
  Heading.Font.Style := [fsBold];
  Subtitle := TextLabel(32, 70, 700, 26, 10, 'Choose shortcuts, launch, and Windows startup options.');
  Subtitle.Font.Color := $00816D60;
  Art := TBitmapImage.Create(WizardForm);
  Art.Parent := WizardForm;
  Art.Bitmap.Assign(WizardForm.WizardSmallBitmapImage.Bitmap);
  Art.Stretch := True;
  Art.SetBounds(ScaleX(732), ScaleY(23), ScaleX(56), ScaleY(56));
  ShortcutCard := Card(108, '');
  LaunchCard := Card(168, '');
  StartupCard := Card(228, '');
  ShortcutCheck := TNewCheckBox.Create(WizardForm);
  ShortcutCheck.Parent := ShortcutCard;
  ShortcutCheck.SetBounds(ScaleX(16), ScaleY(10), ScaleX(720), ScaleY(28));
  ShortcutCheck.Caption := 'Create a desktop shortcut';
  ShortcutCheck.Checked := WizardIsTaskSelected('desktopicon');
  LaunchCheck := TNewCheckBox.Create(WizardForm);
  LaunchCheck.Parent := LaunchCard;
  LaunchCheck.SetBounds(ScaleX(16), ScaleY(10), ScaleX(720), ScaleY(28));
  LaunchCheck.Caption := 'Launch Start App when setup finishes';
  LaunchCheck.Checked := WizardIsTaskSelected('launchafter');
  StartupCheck := TNewCheckBox.Create(WizardForm);
  StartupCheck.Parent := StartupCard;
  StartupCheck.SetBounds(ScaleX(16), ScaleY(10), ScaleX(720), ScaleY(28));
  StartupCheck.Caption := 'Start Start App when I sign in to Windows';
  StartupCheck.Checked := WizardIsTaskSelected('startup');
  WizardForm.ProgressGauge.Parent := WizardForm;
  WizardForm.ProgressGauge.SetBounds(ScaleX(32), ScaleY(142), ScaleX(756), ScaleY(10));
  WizardForm.ProgressGauge.Visible := False;
  ProgressTrack := TPanel.Create(WizardForm);
  ProgressTrack.Parent := WizardForm;
  ProgressTrack.SetBounds(ScaleX(32), ScaleY(142), ScaleX(756), ScaleY(10));
  ProgressTrack.ParentBackground := False;
  ProgressTrack.BevelOuter := bvNone;
  ProgressTrack.Color := $00F3EAE5;
  ProgressTrack.Visible := False;
  ProgressFill := TPanel.Create(WizardForm);
  ProgressFill.Parent := ProgressTrack;
  ProgressFill.SetBounds(0, 0, 0, ProgressTrack.Height);
  ProgressFill.ParentBackground := False;
  ProgressFill.BevelOuter := bvNone;
  ProgressFill.Color := $00C55C31;
  CopyHint := TextLabel(32, 113, 660, 24, 10, 'Copying application files');
  CopyHint.Visible := False;
  PercentLabel := TextLabel(714, 113, 74, 24, 10, '0%');
  PercentLabel.Alignment := taRightJustify;
  PercentLabel.Visible := False;
  DetailsButton := TNewButton.Create(WizardForm);
  DetailsButton.Parent := WizardForm;
  DetailsButton.SetBounds(ScaleX(32), ScaleY(170), ScaleX(124), ScaleY(28));
  DetailsButton.Caption := 'Show details';
  DetailsButton.OnClick := @DetailsClick;
  DetailsButton.Visible := False;
  DetailsMemo := TNewMemo.Create(WizardForm);
  DetailsMemo.Parent := WizardForm;
  DetailsMemo.SetBounds(ScaleX(32), ScaleY(210), ScaleX(756), ScaleY(78));
  DetailsMemo.ReadOnly := True;
  DetailsMemo.ScrollBars := ssBoth;
  DetailsMemo.WordWrap := False;
  DetailsMemo.Font.Name := 'Consolas';
  DetailsMemo.Font.Size := 9;
  DetailsMemo.Visible := False;
  SuccessCard := Card(115, 'Start App was installed successfully');
  SuccessCard.Color := $00F1F7ED;
  SuccessCard.Visible := False;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  WizardForm.Bevel.SetBounds(0, ScaleY(314), ScaleX(820), ScaleY(2));
  WizardForm.NextButton.SetBounds(ScaleX(584), ScaleY(330), ScaleX(96), ScaleY(30));
  WizardForm.CancelButton.SetBounds(ScaleX(692), ScaleY(330), ScaleX(96), ScaleY(30));
  if CurPageID = wpSelectTasks then
  begin
    ShortcutCheck.Checked := WizardIsTaskSelected('desktopicon');
    LaunchCheck.Checked := WizardIsTaskSelected('launchafter');
    StartupCheck.Checked := WizardIsTaskSelected('startup');
  end;
  WizardForm.BackButton.Visible := False;
  ShortcutCard.Visible := CurPageID = wpSelectTasks;
  LaunchCard.Visible := CurPageID = wpSelectTasks;
  StartupCard.Visible := CurPageID = wpSelectTasks;
  WizardForm.ProgressGauge.Visible := False;
  ProgressTrack.Visible := CurPageID = wpInstalling;
  CopyHint.Visible := CurPageID = wpInstalling;
  PercentLabel.Visible := CurPageID = wpInstalling;
  DetailsButton.Visible := CurPageID = wpInstalling;
  if CurPageID <> wpInstalling then DetailsMemo.Visible := False;
  SuccessCard.Visible := CurPageID = wpFinished;
  if CurPageID = wpInstalling then
  begin
    Heading.Caption := 'Installing Start App';
    Subtitle.Caption := 'You can keep working while setup completes.';
  end
  else if CurPageID = wpFinished then
  begin
    Heading.Caption := 'Installation complete';
    if WizardIsTaskSelected('launchafter') then
      Subtitle.Caption := 'Click Finish to open Start App.'
    else Subtitle.Caption := 'Start App is ready to use.';
  end;
end;

procedure FileCopyStarting;
begin
  { Called by Inno for each actual destination, not a simulated file list. }
  DetailsMemo.Lines.Add(ExpandConstant(CurrentFilename));
  DetailsMemo.SelStart := Length(DetailsMemo.Text);
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
begin
  if MaxProgress > 0 then
  begin
    PercentLabel.Caption := IntToStr(Round(CurProgress * 100.0 / MaxProgress)) + '%';
    ProgressFill.Width := Round(ProgressTrack.Width * (CurProgress * 1.0 / MaxProgress));
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if (CurPageID = wpSelectTasks) and (not WizardSilent) then
  begin
    if ShortcutCheck.Checked then WizardSelectTasks('desktopicon')
    else WizardSelectTasks('!desktopicon');
    if LaunchCheck.Checked then WizardSelectTasks('launchafter')
    else WizardSelectTasks('!launchafter');
    if StartupCheck.Checked then WizardSelectTasks('startup')
    else WizardSelectTasks('!startup');
  end;
  if (CurPageID = wpFinished) and (not WizardSilent) and WizardIsTaskSelected('launchafter') then
  begin
    if not Exec(ExpandConstant('{app}\StartApp.App.exe'), '', '', SW_SHOWNORMAL, ewNoWait, ResultCode) then
    begin
      MsgBox('Unable to launch Start App: ' + SysErrorMessage(ResultCode), mbError, MB_OK);
      Result := False;
    end;
  end;
end;
