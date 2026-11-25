$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing,System.Windows.Forms
$out=Join-Path $PWD 'installer-evidence';New-Item $out -ItemType Directory -Force | Out-Null
$checks=[Collections.Generic.List[object]]::new()
function Check($name,$ok){$checks.Add(@{name=$name;ok=[bool]$ok});if(-not $ok){throw $name}}
function Shot($name){$b=New-Object Drawing.Bitmap([Windows.Forms.Screen]::PrimaryScreen.Bounds.Width),([Windows.Forms.Screen]::PrimaryScreen.Bounds.Height);$g=[Drawing.Graphics]::FromImage($b);$g.CopyFromScreen(0,0,0,0,$b.Size);$b.Save((Join-Path $out "$name.png"));$g.Dispose();$b.Dispose()}
function Window($pidValue){$until=[DateTime]::UtcNow.AddSeconds(30);do{$w=[Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children,(New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ProcessIdProperty,$pidValue)));if($w){return $w};Start-Sleep -Milliseconds 200}while([DateTime]::UtcNow -lt $until);throw 'installer window missing'}
function Buttons($w){return @($w.FindAll([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Button))))}
function InvokeName($w,$regex){$b=Buttons $w|Where-Object {$_.Current.Name -match $regex -and $_.Current.IsEnabled}|Select-Object -First 1;if(-not $b){throw "button absent $regex"};$b.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()}
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
 # Inno task lists expose checked state through MSAA; query the list items rather than assuming defaults from script.
 $taskItems=@($all|Where-Object {$_.Current.ControlType -eq [Windows.Automation.ControlType]::ListItem})
 $states=@();foreach($i in $taskItems){try{$pat=$i.GetCurrentPattern([Windows.Automation.LegacyIAccessiblePattern]::Pattern);$states+=@{name=$i.Current.Name;state=$pat.Current.State}}catch{}}
 $states|ConvertTo-Json|Set-Content (Join-Path $out 'task-states.json')
 Check 'both task choices default checked' (($states|Where-Object {($_.state -band 16) -ne 0}).Count -ge 2)
 Shot '01-options'
 InvokeName $w 'הבא|Next'
 Shot '02-progress'
 $until=[DateTime]::UtcNow.AddSeconds(90)
 do{Start-Sleep -Milliseconds 300;$w=Window $p.Id;$finish=Buttons $w|Where-Object {$_.Current.Name -match 'סיום|Finish' -and $_.Current.IsEnabled}|Select-Object -First 1}while(-not $finish -and [DateTime]::UtcNow -lt $until)
 Check 'finish page reached' ([bool]$finish);Shot '03-finish'
 $finish.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();$p.WaitForExit(10000)|Out-Null
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
}finally{$checks|ConvertTo-Json|Set-Content (Join-Path $out 'checks.json')}
