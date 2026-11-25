$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing,System.Windows.Forms
$out=Join-Path $PWD 'installer-evidence';New-Item $out -ItemType Directory -Force | Out-Null
Add-Type @'
using System;using System.Runtime.InteropServices;
public class MouseInput { [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")]public static extern bool SetCursorPos(int x,int y);[DllImport("user32.dll")]public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e); }
'@
$checks=[Collections.Generic.List[object]]::new()
function ClickElement($b){$r=$b.Current.BoundingRectangle;[MouseInput]::SetCursorPos([int]($r.X+$r.Width/2),[int]($r.Y+$r.Height/2))|Out-Null;[MouseInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[MouseInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero)}
function Check($name,$ok){$checks.Add(@{name=$name;ok=[bool]$ok});if(-not $ok){throw $name}}
function Shot($name,$window=$null){
 if($window){$r=$window.Current.BoundingRectangle;$x=[int]$r.X;$y=[int]$r.Y;$width=[int]$r.Width;$height=[int]$r.Height}else{$x=0;$y=0;$width=[Windows.Forms.Screen]::PrimaryScreen.Bounds.Width;$height=[Windows.Forms.Screen]::PrimaryScreen.Bounds.Height}
 $b=New-Object Drawing.Bitmap($width,$height);$g=[Drawing.Graphics]::FromImage($b);$g.CopyFromScreen($x,$y,0,0,$b.Size);$b.Save((Join-Path $out "$name.png"));$g.Dispose();$b.Dispose()
}
function UninstallWindow(){ $until=[DateTime]::UtcNow.AddSeconds(20);do{$all=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.Condition]::TrueCondition);$w=$all|Where-Object {$_.Current.Name -match 'Start App' -and $_.Current.ControlType -eq [Windows.Automation.ControlType]::Window}|Select-Object -First 1;if($w){return $w};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow -lt $until);throw 'uninstall window missing'}

function Window($pidValue){$until=[DateTime]::UtcNow.AddSeconds(30);do{$all=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.Condition]::TrueCondition);$w=$all|Where-Object {$_.Current.Name -match 'Setup.*Start App|Start App.*Setup' -and $_.Current.ControlType -eq [Windows.Automation.ControlType]::Window}|Select-Object -First 1;if($w){return $w};Start-Sleep -Milliseconds 200}while([DateTime]::UtcNow -lt $until);throw 'installer window missing'}
function Buttons($w){return @($w.FindAll([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Button))))}
function InvokeName($w,$regex){$b=Buttons $w|Where-Object {$_.Current.Name -match $regex -and $_.Current.IsEnabled}|Select-Object -First 1;if(-not $b){throw "button absent $regex"};ClickElement $b}
$exe=(Get-ChildItem out/StartApp-Setup-*.exe|Select-Object -First 1).FullName
$target=Join-Path $env:LOCALAPPDATA 'Programs\StartApp'
$data=Join-Path $env:LOCALAPPDATA 'StartApp';New-Item $data -ItemType Directory -Force|Out-Null
$sentinel=Join-Path $data 'installer-preserve-test.txt';[IO.File]::WriteAllText($sentinel,'preserve-data')
$store=Join-Path $data 'store.json'
@{schemaVersion=6;items=@{fixture=@{id='fixture';type='link';name='בדיקת שימור';target='https://example.com/'}};workspaces=@(@{id='ws_fixture';name='סביבה שמורה';color='#12AB34';items=@(@{itemId='fixture';id='membership_fixture';name='שם מקומי';description='תיאור שנשמר'})});globalItems=@();captureWhitelist=@();captureBlacklist=@();customCategories=@();advancedColorHistory=@('#12AB34')}|ConvertTo-Json -Depth 12|Set-Content $store -Encoding utf8
$storeHash=(Get-FileHash $store -Algorithm SHA256).Hash
try{
 $p=Start-Process $exe -ArgumentList '/SP-','/NORESTART' -PassThru
 $w=Window $p.Id
 Start-Sleep -Milliseconds 500
 $all=$w.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
 $names=@($all|ForEach-Object {$_.Current.Name});$names|ConvertTo-Json|Set-Content (Join-Path $out 'first-page.json')
 $task=$all|Where-Object {$_.Current.Name -match 'desktop shortcut|setup finishes'}
 Check 'first page presents shortcut and launch options' ($task.Count -ge 2)
 # Inno custom task list does not expose TogglePattern/MSAA item state here.
 # Preserve pixels for manual verification; actual shortcut/launch side effects are asserted below.
 Shot '01-options' $w
 $r=$w.Current.BoundingRectangle
 $frames=Join-Path $out 'progress-frames';New-Item $frames -ItemType Directory -Force|Out-Null
 $capture=Start-ThreadJob -ArgumentList @($frames,[int]$r.X,[int]$r.Y,[int]$r.Width,[int]$r.Height) -ScriptBlock {
 param($folder,$x,$y,$width,$height)
 Add-Type -AssemblyName System.Drawing
 for($i=0;$i -lt 80;$i++){$b=New-Object Drawing.Bitmap($width,$height);$g=[Drawing.Graphics]::FromImage($b);$g.CopyFromScreen($x,$y,0,0,$b.Size);$b.Save((Join-Path $folder ("{0:D3}.png" -f $i)));$g.Dispose();$b.Dispose();Start-Sleep -Milliseconds 75}
 }
 InvokeName $w 'Next' 
 Shot '02-progress' $w
 $until=[DateTime]::UtcNow.AddSeconds(90)
 do{Start-Sleep -Milliseconds 300;$w=Window $p.Id;$finish=Buttons $w|Where-Object {$_.Current.Name -match 'Finish' -and $_.Current.IsEnabled}|Select-Object -First 1}while(-not $finish -and [DateTime]::UtcNow -lt $until)
 $capture|Wait-Job|Receive-Job
 Check 'finish page reached' ([bool]$finish);Shot '03-finish' $w
 Check 'app waits for Finish' (-not (Get-Process StartApp.App -ErrorAction SilentlyContinue))
 ClickElement $finish;$p.WaitForExit(10000)|Out-Null
 $until=[DateTime]::UtcNow.AddSeconds(20);do{Start-Sleep -Milliseconds 250;$app=Get-Process StartApp.App -ErrorAction SilentlyContinue}while(-not $app -and [DateTime]::UtcNow -lt $until)
 Check 'launch on completion' ([bool]$app);if($app){$app|Stop-Process -Force; $app|ForEach-Object {$_.WaitForExit(5000)|Out-Null}}
 Check 'initial install and launch preserve store bytes' ((Get-FileHash $store -Algorithm SHA256).Hash -eq $storeHash)
 Check 'desktop shortcut exists' (Test-Path (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Start App.lnk'))
 Check 'installed payload exists' (Test-Path (Join-Path $target 'StartApp.App.exe'))
 $identity=[Reflection.AssemblyName]::GetAssemblyName((Join-Path $target 'StartApp.App.dll'))
 $info=(Get-Item (Join-Path $target 'StartApp.App.dll')).VersionInfo.ProductVersion
 Check 'installed beta identity matches candidate' ($info -eq "$env:APP_VERSION+sha.$env:GITHUB_SHA")
 $run=Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Start App'
 Check 'existing logon route installed' ($run.'Start App' -match '--autostart')
 $p=Start-Process $exe -ArgumentList '/VERYSILENT','/NORESTART','/TASKS="!launchafter,!desktopicon"' -PassThru -Wait
 Check 'reinstall succeeds' ($p.ExitCode -eq 0)
 Check 'reinstall preserves data' ((Get-Content $sentinel -Raw) -eq 'preserve-data')
 Check 'reinstall preserves complete store' ((Get-FileHash $store -Algorithm SHA256).Hash -eq $storeHash)
 $un=Join-Path $target 'unins000.exe';$p=Start-Process $un -ArgumentList '/NORESTART' -PassThru
 $u=UninstallWindow;Shot '04-uninstall-confirm' $u;InvokeName $u 'Yes'
 $until=[DateTime]::UtcNow.AddSeconds(60);do{Start-Sleep -Milliseconds 200;$u=UninstallWindow;$ok=Buttons $u|Where-Object {$_.Current.Name -match 'OK'}|Select-Object -First 1}while(-not $ok -and [DateTime]::UtcNow -lt $until)
 Check 'uninstall completion dialog' ([bool]$ok);Shot '05-uninstall-complete' $u;ClickElement $ok;$p.WaitForExit(10000)|Out-Null
 Check 'uninstall succeeds' ($p.ExitCode -eq 0)
 Check 'uninstall removes payload' (-not(Test-Path (Join-Path $target 'StartApp.App.exe')))
 Check 'uninstall preserves user data' ((Get-Content $sentinel -Raw) -eq 'preserve-data')
 Check 'uninstall preserves complete store' ((Get-FileHash $store -Algorithm SHA256).Hash -eq $storeHash)
 $run=Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Start App' -ErrorAction SilentlyContinue
 Check 'uninstall removes logon route' (-not $run)
}catch{Shot 'failure';$_.ToString()|Set-Content (Join-Path $out 'error.txt');throw}finally{$checks|ConvertTo-Json|Set-Content (Join-Path $out 'checks.json')}
