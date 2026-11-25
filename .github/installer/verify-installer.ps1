$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing,System.Windows.Forms
$out=Join-Path $PWD 'installer-evidence';New-Item $out -ItemType Directory -Force | Out-Null
Add-Type @'
using System;using System.Runtime.InteropServices;
public class MouseInput { [DllImport("user32.dll")]public static extern bool SetCursorPos(int x,int y);[DllImport("user32.dll")]public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e); }
'@
$checks=[Collections.Generic.List[object]]::new()
function ClickElement($b){$r=$b.Current.BoundingRectangle;[MouseInput]::SetCursorPos([int]($r.X+$r.Width/2),[int]($r.Y+$r.Height/2))|Out-Null;[MouseInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[MouseInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero)}
function Check($name,$ok){$checks.Add(@{name=$name;ok=[bool]$ok});if(-not $ok){throw $name}}
function Shot($name){$b=New-Object Drawing.Bitmap([Windows.Forms.Screen]::PrimaryScreen.Bounds.Width),([Windows.Forms.Screen]::PrimaryScreen.Bounds.Height);$g=[Drawing.Graphics]::FromImage($b);$g.CopyFromScreen(0,0,0,0,$b.Size);$b.Save((Join-Path $out "$name.png"));$g.Dispose();$b.Dispose()}
function Window($pidValue){$until=[DateTime]::UtcNow.AddSeconds(30);do{$all=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.Condition]::TrueCondition);$w=$all|Where-Object {$_.Current.Name -match 'התקנה.*Start App|Setup.*Start App|Start App.*Setup' -and $_.Current.ControlType -eq [Windows.Automation.ControlType]::Window}|Select-Object -First 1;if($w){return $w};Start-Sleep -Milliseconds 200}while([DateTime]::UtcNow -lt $until);throw 'installer window missing'}
function Buttons($w){return @($w.FindAll([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Button))))}
function InvokeName($w,$regex){$b=Buttons $w|Where-Object {$_.Current.Name -match $regex -and $_.Current.IsEnabled}|Select-Object -First 1;if(-not $b){throw "button absent $regex"};ClickElement $b}
$exe=(Get-ChildItem out/StartApp-Setup-*.exe|Select-Object -First 1).FullName
$target=Join-Path $env:LOCALAPPDATA 'Programs\StartApp'
$data=Join-Path $env:LOCALAPPDATA 'StartApp';New-Item $data -ItemType Directory -Force|Out-Null
$sentinel=Join-Path $data 'installer-preserve-test.txt';[IO.File]::WriteAllText($sentinel,'preserve-data')
try{
 $p=Start-Process $exe -ArgumentList '/SP-','/NORESTART' -PassThru
 $w=Window $p.Id
 Start-Sleep -Milliseconds 500
 $all=$w.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
 $names=@($all|ForEach-Object {$_.Current.Name});$names|ConvertTo-Json|Set-Content (Join-Path $out 'first-page.json')
 $task=$all|Where-Object {$_.Current.Name -match 'קיצור|בסיום'}
 Check 'first page presents shortcut and launch options' ($task.Count -ge 2)
 # Inno custom task list does not expose TogglePattern/MSAA item state here.
 # Preserve pixels for manual verification; actual shortcut/launch side effects are asserted below.
 Shot '01-options'
 InvokeName $w 'הבא|Next'
 Shot '02-progress'
 $until=[DateTime]::UtcNow.AddSeconds(90)
 do{Start-Sleep -Milliseconds 300;$w=Window $p.Id;$finish=Buttons $w|Where-Object {$_.Current.Name -match 'סיום|Finish' -and $_.Current.IsEnabled}|Select-Object -First 1}while(-not $finish -and [DateTime]::UtcNow -lt $until)
 Check 'finish page reached' ([bool]$finish);Shot '03-finish'
 Check 'app waits for Finish' (-not (Get-Process StartApp.App -ErrorAction SilentlyContinue))
 ClickElement $finish;$p.WaitForExit(10000)|Out-Null
 $until=[DateTime]::UtcNow.AddSeconds(20);do{Start-Sleep -Milliseconds 250;$app=Get-Process StartApp.App -ErrorAction SilentlyContinue}while(-not $app -and [DateTime]::UtcNow -lt $until)
 Check 'launch on completion' ([bool]$app);if($app){$app|Stop-Process -Force}
 Check 'desktop shortcut exists' (Test-Path (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Start App.lnk'))
 Check 'installed payload exists' (Test-Path (Join-Path $target 'StartApp.App.exe'))
 $run=Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Start App'
 Check 'existing logon route installed' ($run.'Start App' -match '--autostart')
 $p=Start-Process $exe -ArgumentList '/VERYSILENT','/NORESTART','/TASKS="!launchafter,!desktopicon"' -PassThru -Wait
 Check 'reinstall succeeds' ($p.ExitCode -eq 0)
 Check 'reinstall preserves data' ((Get-Content $sentinel -Raw) -eq 'preserve-data')
 $un=Join-Path $target 'unins000.exe';$p=Start-Process $un -ArgumentList '/VERYSILENT','/NORESTART' -PassThru -Wait
 Check 'uninstall succeeds' ($p.ExitCode -eq 0)
 Check 'uninstall removes payload' (-not(Test-Path (Join-Path $target 'StartApp.App.exe')))
 Check 'uninstall preserves user data' ((Get-Content $sentinel -Raw) -eq 'preserve-data')
 $run=Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Start App' -ErrorAction SilentlyContinue
 Check 'uninstall removes logon route' (-not $run)
}catch{Shot 'failure';$_.ToString()|Set-Content (Join-Path $out 'error.txt');throw}finally{$checks|ConvertTo-Json|Set-Content (Join-Path $out 'checks.json')}
