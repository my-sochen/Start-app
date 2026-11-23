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
 Scenario '16-invalid-edit-restart' {
  Click 'Review one';Click 'Edit item';Text 'Edit item target' 'javascript:alert(1)';Click 'Save item edit';StopApp;StartApp;Click 'Review one'
  $o=Get-Content $store -Raw|ConvertFrom-Json;$m=@(($o.workspaces|Where-Object id -eq 'one').items|Where-Object id -eq 'm1')[0];$target=$o.items.PSObject.Properties[$m.itemId].Value.target
  Record 'Edited javascript target persists after restart' ($target -eq 'javascript:alert(1)') $target;Shot '16-invalid-edit-after-restart';Copy-Item $store "$out/16-invalid-store.json"
 }
 Scenario '17-global-schema-validation' {
  Mouse 'Options menu';Menu 'סביבה גלובלית';Click 'Add global item';Text 'Global entry name' 'Script URI sample';Text 'Global entry target' 'javascript:alert(1)';Click 'Confirm global add';StopApp;StartApp;Mouse 'Options menu';Menu 'סביבה גלובלית';Shot '17-global-restarted';Copy-Item $store "$out/17-global-store.json"
  Record 'Invalid global URI survives restart' ((Get-Content $store -Raw) -like '*javascript:alert(1)*') 'Stored and reloaded, never executed'
 }
 Scenario '18-all-items-selected-scope' {
  Mouse 'Options menu';Menu 'אייטמים';Context 'Shared sample';Menu 'ערוך';[void](Wait 'Choose item environments');Shot '18-picker-default'
  Record 'All-items picker initially confirms disabled' (-not (Wait 'Confirm item workspace choice' $CT::Button).Current.IsEnabled) 'No environments should be selected automatically'
  $row=Wait 'Review one' $CT::ListItem;$row.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();Click 'Confirm item workspace choice';Text 'Selected item name' 'Batch one only';Click 'Save selected item edit'
  $o=Get-Content $store -Raw|ConvertFrom-Json;Record 'All-items edit applies only selected environment' ((($o.workspaces|Where-Object id -eq 'one').items|Where-Object id -eq 'm1').name -eq 'Batch one only' -and -not (($o.workspaces|Where-Object id -eq 'two').items|Where-Object id -eq 'm3').name) 'Identical copies, only Review one chosen';Shot '18-batch-result'
 }
 Scenario '19-local-delete' {
  Click 'Review one';Context 'Shared sample';Menu 'מחיקה';Shot '19-delete-after-menu';Dump '19-delete-after-menu';Copy-Item $store "$out/19-delete-store.json"
  $o=Get-Content $store -Raw|ConvertFrom-Json;Record 'Delete in workspace preserves other copy' (@(($o.workspaces|Where-Object id -eq 'two').items|Where-Object itemId -eq 'site').Count -eq 1) 'Local deletion, inspect evidence for confirm/undo'
 }
 Scenario '20-default-replace' {
  Click 'Review one';Click 'Toggle default workspace';Click 'Back to workspaces';Click 'Review two';Click 'Toggle default workspace';Shot '20-default-replace';[void](Wait 'Cancel default-env replace');Click 'Cancel default-env replace'
  $o=Get-Content $store -Raw|ConvertFrom-Json;Record 'Cancel default replacement keeps original' ($o.defaultWorkspaceId -eq 'one') "default=$($o.defaultWorkspaceId)"
  Click 'Toggle default workspace';Click 'Confirm default-env replace';StopApp;StartApp;$o=Get-Content $store -Raw|ConvertFrom-Json;Record 'Default replacement persists' ($o.defaultWorkspaceId -eq 'two') "default=$($o.defaultWorkspaceId)";Shot '20-default-restart'
 }
 Scenario '21-whitelist-staging' {
  Mouse 'Options menu';Menu 'סביבה גלובלית';Click 'Add whitelist item';Text 'Global entry name' 'Priority sample';Text 'Global entry target' 'https://example.com/priority';Click 'Confirm global add';Click 'Back to workspaces';Click 'Review one';$before=Get-Content $store -Raw;Click 'Add from open items';[void](Wait 'Open now list');Start-Sleep -Seconds 8
  $list=Wait 'Open now list';$boxes=$list.FindAll($TS::Descendants,(New-Object $PC($A::ControlTypeProperty,$CT::CheckBox)));$sample=@($boxes|Where-Object{$_.Current.Name -like '*Priority sample*'})[0]
  Record 'Whitelist offered while not actually open' ($null -ne $sample) 'Global page says priority even when not open; actual runtime behavior'
  if($sample){$sample.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle();Click 'Add selected capture items';Shot '21-staged';Click 'Close capture';Shot '21-staged-guard';Dump '21-staged-guard';Record 'Staged capture close guard' ($null -ne (Find 'Back to editing')) 'Draft has an added row but not saved';Record 'Capture draft not persisted before save' ((Get-Content $store -Raw) -ceq $before) 'Synthetic whitelist staged item'}
 }
 Scenario '22-missing-only-open-results' {
  StopApp;$o=Get-Content $store -Raw|ConvertFrom-Json;($o.workspaces|Where-Object id -eq 'one').items=@(($o.workspaces|Where-Object id -eq 'one').items|Where-Object itemId -eq 'missing');$o|ConvertTo-Json -Depth 10|Set-Content $store;StartApp
  Click 'Review one';Click 'שגר';Click 'Confirm opening selection';[void](Wait 'Notification');Shot '22-missing-toast';Dump '22-missing-toast';Click 'Back to workspaces';Mouse 'Options menu';Menu 'תוצאות הפתיחה האחרונות';Shot '22-missing-results';Dump '22-missing-results'
  Record 'Missing-file pipeline returned without launchable resources' $true 'Only a synthetic nonexistent file selected; no browser/program/file can be opened'
 }
 Scenario '23-scroll-small-create' {
  [void][ReviewWin]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,12,12,720,400,0x40);Click 'New workspace button'
  Text 'Workspace name' 'Small viewport sample';$sw=[Diagnostics.Stopwatch]::StartNew();$found=$false
  foreach($sc in $win.FindAll($TS::Descendants,(New-Object $PC($A::ControlTypeProperty,$CT::Pane)))){try{$sp=$sc.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern);if($sp.Current.VerticallyScrollable){$sp.SetScrollPercent(-1,100);$found=$true}}catch{}}
  Shot '23-min-create-bottom';Dump '23-min-create-bottom';Record 'Create action reachable at min viewport' ($null -ne (Find 'Create workspace' $CT::Button)) "scrollAttempt=$found"
 }
}finally{StopApp;Flush;if(Test-Path "$storeDir/crash.log"){Copy-Item "$storeDir/crash.log" "$out/crash.log"};Get-ChildItem $out|Select-Object Name,Length}
