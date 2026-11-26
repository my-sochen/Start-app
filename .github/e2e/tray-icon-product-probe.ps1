param([Parameter(Mandatory)][string]$Exe,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or -not $IsWindows){throw 'Hosted disposable runner only'}
New-Item -ItemType Directory -Force $Output | Out-Null;$Output=(Resolve-Path $Output).Path
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing,System.Windows.Forms
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class TrayProbeNative {
[DllImport("user32.dll")]public static extern bool SetCursorPos(int x,int y);
[DllImport("user32.dll")]public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
[DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr h);
}
'@
$A=[Windows.Automation.AutomationElement];$T=[Windows.Automation.TreeScope];$PC=[Windows.Automation.PropertyCondition]
$data=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'StartApp'
if(Get-Process StartApp.App -ErrorAction SilentlyContinue){throw 'Unexpected existing app'}
if(Test-Path $data){Move-Item $data ($data+'-saved-'+[guid]::NewGuid().ToString('N'))}
New-Item -ItemType Directory $data | Out-Null
# Create only synthetic schema-7 data, to exercise real persisted hide-on-close behavior.
@{schemaVersion=7;items=@{};workspaces=@();customCategories=@();keepRunningInTray=$true} | ConvertTo-Json -Depth 5 | Set-Content "$data/store.json"
$env:STARTAPP_CRASHLOG='1';$checks=[Collections.Generic.List[object]]::new()
function Check($name,$ok){$checks.Add(@{name=$name;ok=[bool]$ok});if(-not $ok){throw "Check failed: $name"}}
function Shot($name){$b=[Drawing.Bitmap]::new([Windows.Forms.Screen]::PrimaryScreen.Bounds.Width,[Windows.Forms.Screen]::PrimaryScreen.Bounds.Height);$g=[Drawing.Graphics]::FromImage($b);try{$g.CopyFromScreen(0,0,0,0,$b.Size);$b.Save("$Output/$name.png")}finally{$g.Dispose();$b.Dispose()}}
function TrayButton {
 function FindIcon {
  $all=$A::RootElement.FindAll($T::Descendants,$PC::new($A::ControlTypeProperty,[Windows.Automation.ControlType]::Button))
  foreach($el in $all){if($el.Current.Name -like '*Start App*' -and -not $el.Current.IsOffscreen){return $el}}
  return $null
 }
 $icon=FindIcon;if($null -ne $icon){return $icon}
 $overflow=$A::RootElement.FindFirst($T::Descendants,$PC::new($A::NameProperty,'Show Hidden Icons'))
 if($null -ne $overflow){$overflow.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();Start-Sleep -Milliseconds 350}
 $icon=FindIcon
 if($null -eq $icon){$A::RootElement.FindAll($T::Descendants,$PC::new($A::ControlTypeProperty,[Windows.Automation.ControlType]::Button))|ForEach-Object {$_.Current.Name}|Set-Content "$Output/tray-buttons.txt";Shot 'tray-not-found'}
 return $icon
}
function ClickElement($el,$right){$r=$el.Current.BoundingRectangle;Check 'tray target has screen bounds' ($r.Width -gt 0 -and $r.Height -gt 0);[void][TrayProbeNative]::SetCursorPos([int]($r.X+$r.Width/2),[int]($r.Y+$r.Height/2));if($right){[TrayProbeNative]::mouse_event(8,0,0,0,[UIntPtr]::Zero);[TrayProbeNative]::mouse_event(16,0,0,0,[UIntPtr]::Zero)}else{[TrayProbeNative]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[TrayProbeNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero)}}
$p=Start-Process $Exe -PassThru -RedirectStandardError "$Output/stderr.txt" -RedirectStandardOutput "$Output/stdout.txt"
try{
 Start-Sleep -Seconds 5;$p.Refresh();Check 'actual executable survives startup' (-not $p.HasExited);$h=$p.MainWindowHandle;Check 'real main window visible' ($h -ne 0 -and [TrayProbeNative]::IsWindowVisible($h));Shot '01-started'
 $tray=TrayButton;Check 'native notification icon exists' ($null -ne $tray)
 [void]$p.CloseMainWindow();Start-Sleep -Milliseconds 800;$p.Refresh();Check 'close hides and keeps actual process alive' (-not $p.HasExited -and -not [TrayProbeNative]::IsWindowVisible($h));Shot '02-hidden'
 ClickElement (TrayButton) $false;Start-Sleep -Milliseconds 700;Check 'native tray click restores window' ([TrayProbeNative]::IsWindowVisible($h));Shot '03-restored'
 ClickElement (TrayButton) $true;Start-Sleep -Milliseconds 500;Shot '04-tray-menu'
 $exit=$A::RootElement.FindFirst($T::Descendants,$PC::new($A::NameProperty,'יציאה מלאה'));Check 'native tray full-exit action present' ($null -ne $exit);$exit.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();Check 'native full exit ends actual process' ($p.WaitForExit(10000));Check 'actual process exits cleanly' ($p.ExitCode -eq 0)
}finally{
 $checks|ConvertTo-Json -Depth 5|Set-Content "$Output/checks.json";if(Test-Path "$data/crash.log"){Copy-Item "$data/crash.log" "$Output/crash.log"};if(-not $p.HasExited){$p.Kill($true);$p.WaitForExit()};$p.Dispose()
}
