$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing,System.Windows.Forms
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class ReviewWin {
 [StructLayout(LayoutKind.Sequential)]public struct R{public int L,T,Rt,B;}
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int ht,uint f);
 [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")]public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out R r);
 [DllImport("user32.dll")]public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
 [DllImport("user32.dll")]public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc cb,IntPtr l);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
 delegate bool EnumProc(IntPtr h,IntPtr l);
 public static IntPtr[] Windows(uint pid){var hs=new System.Collections.Generic.List<IntPtr>();EnumWindows((h,l)=>{uint p;GetWindowThreadProcessId(h,out p);if(p==pid&&IsWindowVisible(h))hs.Add(h);return true;},IntPtr.Zero);return hs.ToArray();}
}
'@
[void][ReviewWin]::SetThreadDpiAwarenessContext([IntPtr](-4))
$out=Join-Path $PWD 'product-review';New-Item -ItemType Directory -Force $out|Out-Null
$storeDir=Join-Path $env:LOCALAPPDATA 'StartApp';$store=Join-Path $storeDir 'store.json'
$exe=(Get-ChildItem src/StartApp.App/bin/Release -Recurse -Filter StartApp.App.exe|Select-Object -First 1).FullName
$A=[Windows.Automation.AutomationElement];$TS=[Windows.Automation.TreeScope];$PC=[Windows.Automation.PropertyCondition];$CT=[Windows.Automation.ControlType]
$results=[Collections.Generic.List[object]]::new();$script:p=$null;$script:win=$null
function Flush { @{baseDev='a69364d160366f485232ef99a9a474b7d6638f77';harnessCommit=$env:GITHUB_SHA;syntheticOnly=$true;results=@($results.ToArray())}|ConvertTo-Json -Depth 12|Set-Content "$out/results.json" }
function Record($name,$ok,$detail){$results.Add(@{name=$name;ok=[bool]$ok;detail="$detail"});Write-Host "$name : $ok : $detail";Flush}
function StopApp {if($script:p -and -not $script:p.HasExited){$script:p.Kill();$script:p.WaitForExit()};$script:p=$null;$script:win=$null}
function StartApp {
 $script:p=Start-Process $exe -PassThru;$sw=[Diagnostics.Stopwatch]::StartNew()
 do{$script:p.Refresh();if($script:p.HasExited){throw 'App exited before readiness'};if($script:p.MainWindowHandle -ne 0){break};Start-Sleep -Milliseconds 100}while($sw.Elapsed.TotalSeconds -lt 20)
 if($script:p.MainWindowHandle -eq 0){throw 'No app window'}
 $script:win=$A::FromHandle($script:p.MainWindowHandle);[void][ReviewWin]::SetWindowPos($script:p.MainWindowHandle,[IntPtr]::Zero,12,12,960,650,0x40);Start-Sleep -Milliseconds 450
}
function Seed {
 StopApp;if(Test-Path $storeDir){Remove-Item $storeDir -Recurse -Force};New-Item -ItemType Directory -Force $storeDir|Out-Null
 $items=@{'site'=@{id='site';type='link';name='Shared sample';target='https://example.com/'};'missing'=@{id='missing';type='file';name='Missing sample';target='C:\ReviewSample\not-there.txt'}}
 @{schemaVersion=5;items=$items;workspaces=@(@{id='one';name='Review one';items=@(@{id='m1';itemId='site'},@{id='m2';itemId='missing'})},@{id='two';name='Review two';items=@(@{id='m3';itemId='site'})});customCategories=@();globalItems=@();captureWhitelist=@();captureBlacklist=@()}|ConvertTo-Json -Depth 10|Set-Content $store
}
function Roots {if($p -and -not $p.HasExited){@([ReviewWin]::Windows([uint32]$p.Id)|ForEach-Object{$A::FromHandle($_)})}}
function Find($name,$type=$null){
 $conds=@((New-Object $PC($A::NameProperty,$name)),(New-Object $PC($A::IsOffscreenProperty,$false)))
 if($type){$conds+=(New-Object $PC($A::ControlTypeProperty,$type))};$c=[Windows.Automation.AndCondition]::new([Windows.Automation.Condition[]]$conds)
 foreach($root in (Roots)){if($root.Current.Name -eq $name -and (-not $type -or $root.Current.ControlType -eq $type)){return $root};$e=$root.FindFirst($TS::Descendants,$c);if($e){return $e}}
 return $null
}
function Wait($name,$type=$null){$sw=[Diagnostics.Stopwatch]::StartNew();do{$e=Find $name $type;if($e){return $e};Start-Sleep -Milliseconds 100}while($sw.Elapsed.TotalSeconds -lt 8);throw "Not visible: $name"}
function Click($name){$e=Wait $name $CT::Button;if(-not $e.Current.IsEnabled){throw "Disabled: $name"};$e.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();Start-Sleep -Milliseconds 180}
function Text($name,$value){$e=Wait $name $CT::Edit;$e.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($value);Start-Sleep -Milliseconds 150}
function Value($name){(Wait $name $CT::Edit).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value}
function Shot($name){
 $index=0;foreach($root in (Roots)){$index++;$h=[IntPtr]$root.Current.NativeWindowHandle;[void][ReviewWin]::SetForegroundWindow($h);Start-Sleep -Milliseconds 160;$r=New-Object ReviewWin+R;[void][ReviewWin]::GetWindowRect($h,[ref]$r);$screen=[Windows.Forms.Screen]::PrimaryScreen.WorkingArea
  if($r.Rt-$r.L -le 0 -or $r.B-$r.T -le 0){continue}
  if($r.L -lt $screen.Left -or $r.T -lt $screen.Top -or $r.Rt -gt $screen.Right -or $r.B -gt $screen.Bottom){"$name window outside desktop: $r"|Add-Content "$out/clipping.txt";continue}
  $im=New-Object Drawing.Bitmap ($r.Rt-$r.L),($r.B-$r.T);$g=[Drawing.Graphics]::FromImage($im);try{$g.CopyFromScreen($r.L,$r.T,0,0,$im.Size);$im.Save("$out/$name-$index.png")}finally{$g.Dispose();$im.Dispose()}
 }
}
function Dump($name){$lines=@();foreach($root in (Roots)){foreach($e in $root.FindAll($TS::Descendants,[Windows.Automation.Condition]::TrueCondition)){try{if(-not $e.Current.IsOffscreen){$lines+="$($e.Current.ControlType.ProgrammaticName)|$($e.Current.Name)|enabled=$($e.Current.IsEnabled)|rect=$($e.Current.BoundingRectangle)"}}catch{}}};$lines|Set-Content "$out/$name-uia.txt"}
function Scenario($name,[scriptblock]$body){try{Seed;StartApp;&$body;Record "$name completed" $true 'Behavior observations recorded'}catch{Record "$name harness-or-product-stop" $false $_.Exception.Message;try{Shot "$name-stop";Dump "$name-stop"}catch{}}finally{StopApp}}
function Menu($name){$e=Wait $name $CT::MenuItem;$e.SetFocus();[Windows.Forms.SendKeys]::SendWait('{ENTER}');Start-Sleep -Milliseconds 250}
function Mouse($name){$e=Wait $name $CT::Button;$r=$e.Current.BoundingRectangle;[void][ReviewWin]::SetCursorPos([int]($r.X+$r.Width/2),[int]($r.Y+$r.Height/2));[ReviewWin]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[ReviewWin]::mouse_event(4,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 200}
function Context($name){$e=Wait $name $CT::Button;$e.SetFocus();[Windows.Forms.SendKeys]::SendWait('+{F10}');Start-Sleep -Milliseconds 250}
$env:STARTAPP_CRASHLOG='1'
try{
 Scenario '24-missing-results' {
  StopApp;$o=Get-Content $store -Raw|ConvertFrom-Json;($o.workspaces|Where-Object id -eq 'one').items=@(($o.workspaces|Where-Object id -eq 'one').items|Where-Object itemId -eq 'missing');$o|ConvertTo-Json -Depth 10|Set-Content $store;StartApp
  Click 'Review one';Click 'שגר';Click 'Confirm opening selection';[void](Wait 'Dismiss notification');Click 'Last opening results';Shot '24-missing-results';Dump '24-missing-results';Record 'Missing file results available' ($null -ne (Find 'Opening results')) 'No launchable resource in scenario; inspect plain-language result'
 }
 Scenario '25-context-delete' {
  Click 'Review one';$anchor=Wait 'Edit item' $CT::Button;$anchor.SetFocus();[Windows.Forms.SendKeys]::SendWait('+{F10}');Start-Sleep -Milliseconds 250
  $mi=$A::RootElement.FindFirst($TS::Descendants,(New-Object $PC($A::NameProperty,'מחיקה')))
  if(-not $mi){throw 'Deletion menu not opened from row edit anchor'}
  $mi.SetFocus();[Windows.Forms.SendKeys]::SendWait('{ENTER}');Start-Sleep -Milliseconds 250;Shot '25-delete-outcome';Dump '25-delete-outcome';Copy-Item $store "$out/25-delete-store.json"
  $o=Get-Content $store -Raw|ConvertFrom-Json;Record 'Local deletion leaves other environment copy' (@(($o.workspaces|Where-Object id -eq 'two').items|Where-Object itemId -eq 'site').Count -eq 1) 'First sample row deletion';Record 'Item removed immediately' (@(($o.workspaces|Where-Object id -eq 'one').items|Where-Object itemId -eq 'site').Count -eq 0) 'Check screenshot for confirm or undo'
 }
 Scenario '26-locked-write' {
  StopApp;$before=Get-Content $store -Raw;$held=[IO.File]::Open("$store.lock",[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
  try{StartApp;Click 'Review one';Text 'Workspace name' 'Locked save attempt';Click 'Save workspace details';Shot '26-locked-save';Dump '26-locked-save';Record 'Failed locked save preserves disk data' ((Get-Content $store -Raw) -ceq $before) 'Second process holds writer lock';Record 'Failed locked save leaves draft visible' ((Value 'Workspace name') -eq 'Locked save attempt') 'Prevent silent draft loss'}finally{StopApp;$held.Dispose()}
 }
 Scenario '27-staged-save-restart' {
  Mouse 'Options menu';Menu 'סביבה גלובלית';Click 'Add whitelist item';Text 'Global entry name' 'Saved staged sample';Text 'Global entry target' 'https://example.com/staged';Click 'Confirm global add';Click 'Back to workspaces';Click 'Review one';Click 'Add from open items';[void](Wait 'Open now list');Start-Sleep -Seconds 7
  $list=Wait 'Open now list';$boxes=$list.FindAll($TS::Descendants,(New-Object $PC($A::ControlTypeProperty,$CT::CheckBox)));$sample=@($boxes|Where-Object{$_.Current.Name -like '*Saved staged sample*'})[0];if(-not $sample){throw 'Synthetic whitelist candidate missing'};$sample.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle();Click 'Add selected capture items';Click 'Capture save';StopApp;StartApp;Click 'Review one';Shot '27-staged-restarted';$o=Get-Content $store -Raw|ConvertFrom-Json;$targets=@(($o.workspaces|Where-Object id -eq 'one').items|ForEach-Object{$o.items.PSObject.Properties[$_.itemId].Value.target});Record 'Saved staged item survives restart' ($targets -contains 'https://example.com/staged') ($targets -join ',')
 }
 Scenario '28-type-confusion' {
  Click 'Review one';Click '+ הוסף אייטם';Text 'Item target' 'https://example.com/wrong-type';Text 'Item name' 'Pasted website';Click 'Add';Shot '28-pasted-url';Dump '28-pasted-url';Copy-Item $store "$out/28-type-store.json";$o=Get-Content $store -Raw|ConvertFrom-Json;$it=@($o.items.PSObject.Properties.Value|Where-Object target -eq 'https://example.com/wrong-type')[0];Record 'Pasted web URL item type' ($it.type -eq 'link') "actualType=$($it.type)" 
 }
}finally{StopApp;Flush;if(Test-Path "$storeDir/crash.log"){Copy-Item "$storeDir/crash.log" "$out/crash.log"};Get-ChildItem $out|Select-Object Name,Length}
