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
function Menu($name){$e=Wait $name $CT::MenuItem;$e.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();Start-Sleep -Milliseconds 200}
function Mouse($name){$e=Wait $name $CT::Button;$r=$e.Current.BoundingRectangle;[void][ReviewWin]::SetCursorPos([int]($r.X+$r.Width/2),[int]($r.Y+$r.Height/2));[ReviewWin]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[ReviewWin]::mouse_event(4,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 200}
function Context($name){$e=Wait $name $CT::Button;$e.SetFocus();[Windows.Forms.SendKeys]::SendWait('+{F10}');Start-Sleep -Milliseconds 250}
$env:STARTAPP_CRASHLOG='1'
try{
 Scenario '01-empty-create' {
  Click 'New workspace button';Record 'Blank workspace creation blocked' (-not (Wait 'Create workspace' $CT::Button).Current.IsEnabled) 'Blank name'
  Text 'Workspace name' '   ';Record 'Whitespace-only workspace creation blocked' (-not (Wait 'Create workspace' $CT::Button).Current.IsEnabled) 'Spaces only';Shot '01-create-whitespace'
  Text 'Workspace name' ('א'*240);Click 'Create workspace';Shot '01-long-workspace';Dump '01-long-workspace'
  $o=Get-Content $store -Raw|ConvertFrom-Json;Record 'Long workspace name persisted length' $true (@($o.workspaces|Where-Object{$_.name.Length -gt 200})[0].name.Length)
 }
 Scenario '02-create-unsaved' {
  Click 'New workspace button';Text 'Workspace name' 'Unsaved sample';Click 'ביטול וחזרה';Shot '02-create-unsaved-guard';Dump '02-create-unsaved-guard'
  Record 'Dirty new workspace guard visible' ($null -ne (Find 'Back to editing')) 'Cancel/back should not silently discard typed name'
 }
 Scenario '03-workspace-persistence' {
  Click 'Review one';Text 'Workspace name' 'Changed sample';Click 'Save workspace details';StopApp;StartApp
  Record 'Saved workspace survives restart' ($null -ne (Find 'Changed sample' $CT::Button)) 'Read persisted UI after process restart'
 }
 Scenario '04-workspace-unsaved-navigation' {
  Click 'Review one';Text 'Workspace description' 'Unsaved description';Click 'Back to workspaces';Shot '04-workspace-guard';Dump '04-workspace-guard'
  Record 'Dirty workspace navigation guard visible' ($null -ne (Find 'Back to editing')) 'Back from changed workspace details'
 }
 Scenario '05-item-identity' {
  Click 'Review one';Click 'Edit item';Text 'Edit item name' 'Only first workspace';Click 'Save item edit'
  $o=Get-Content $store -Raw|ConvertFrom-Json;$first=($o.workspaces|Where-Object id -eq 'one').items|Where-Object itemId -eq 'site';$other=($o.workspaces|Where-Object id -eq 'two').items|Where-Object itemId -eq 'site'
  Record 'Local item edit does not rename other workspace' ($first.name -eq 'Only first workspace' -and $other.name -ne 'Only first workspace') ($o|ConvertTo-Json -Depth 8 -Compress)
 }
 Scenario '06-invalid-item-edit' {
  Click 'Review one';Click 'Edit item';Text 'Edit item target' 'javascript:alert(1)';Click 'Save item edit';Shot '06-invalid-target';Dump '06-invalid-target'
  $o=Get-Content $store -Raw|ConvertFrom-Json;Record 'Script scheme not persisted as website' ($o.items.site.target -notlike 'javascript:*') 'Unsafe/non-web scheme rejected by normal editor'
 }
 Scenario '07-item-cancel-guard' {
  Click 'Review one';Click 'Edit item';Text 'Edit item name' 'Unsaved item';Click 'Cancel item edit';Shot '07-item-guard';Dump '07-item-guard'
  Record 'Dirty item cancel guard visible' ($null -ne (Find 'Keep item editing')) 'Cancel after item name change'
 }
 Scenario '08-search' {
  Text 'Workspace search' 'no-match-xyz';Shot '08-search-empty';Dump '08-search-empty';Record 'Search hides unmatched card' ($null -eq (Find 'Review one' $CT::Button)) 'Unmatched workspace search'
  Text 'Workspace search' '';Record 'Clearing search restores cards' ($null -ne (Find 'Review one' $CT::Button)) 'Clear search without restart'
 }
 Scenario '09-small-viewport' {
  [void][ReviewWin]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,12,12,720,400,0x40);Start-Sleep -Milliseconds 300;Shot '09-min-home';Dump '09-min-home';Click 'Settings';Shot '09-min-settings';Dump '09-min-settings';Click 'Back to workspaces';Click 'New workspace button';Shot '09-min-create';Dump '09-min-create'
 }
 Scenario '10-global-invalid' {
  Mouse 'Options menu';Menu 'סביבה גלובלית';Click 'Add global item';Text 'Global entry name' 'Invalid website';Text 'Global entry target' 'javascript:alert(1)';Shot '10-global-invalid-before';Dump '10-global-invalid-before'
  $confirm=Wait 'Confirm global add' $CT::Button;if($confirm.Current.IsEnabled){Click 'Confirm global add'}
  $o=Get-Content $store -Raw|ConvertFrom-Json;Record 'Global list rejects javascript target' (-not (@($o.globalItems)|Where-Object{$_.target -like 'javascript:*'})) 'Default type and error visibility captured';Shot '10-global-invalid-after';Dump '10-global-invalid-after'
 }
 Scenario '11-global-unsaved' {
  Mouse 'Options menu';Menu 'סביבה גלובלית';Click 'Add global item';Text 'Global entry name' 'Unsaved global';Text 'Global entry target' 'https://example.com/unsaved';Click 'Cancel global add';Shot '11-global-cancel';Dump '11-global-cancel'
  Record 'Global add cancel outcome' $true 'Inspect whether typed draft is discarded without guard; do not label as confirmed data loss'
 }
 Scenario '12-capture-cancel' {
  $before=Get-Content $store -Raw;Click 'Review one';Click 'Add from open items';[void](Wait 'Open now list');Start-Sleep -Seconds 8;Shot '12-capture';Dump '12-capture';Click 'Close capture'
  Record 'Capture scan and close do not mutate store' ((Get-Content $store -Raw) -ceq $before) 'No rows added/saved'
 }
 Scenario '13-item-delete' {
  Click 'Review one';Context 'Shared sample';Menu 'מחיקה';Shot '13-item-delete';Dump '13-item-delete';$o=Get-Content $store -Raw|ConvertFrom-Json
  Record 'Delete local item leaves other workspace membership' (@(($o.workspaces|Where-Object id -eq 'two').items|Where-Object itemId -eq 'site').Count -eq 1) 'Deletion via item right-click'
  Record 'Delete interaction evidence' $true 'Inspect whether confirmation or undo exists after one click'
 }
 Scenario '14-launch-cancel' {
  Click 'Review one';$before=Get-Content $store -Raw;Click 'שגר';[void](Wait 'Confirm opening selection');Shot '14-selection';Click 'Close opening selection';Record 'Launch-selection cancel leaves store unchanged' ((Get-Content $store -Raw) -ceq $before) 'No final launch confirmation clicked'
 }
 foreach($mode in @('corrupt','too-new','lock')){
  try{
   Seed;if($mode -eq 'corrupt'){Set-Content $store '{bad-json'};if($mode -eq 'too-new'){$o=Get-Content $store -Raw|ConvertFrom-Json;$o.schemaVersion=999;$o|ConvertTo-Json -Depth 10|Set-Content $store}
   $before=[IO.File]::ReadAllBytes($store);$lock=$null;if($mode -eq 'lock'){$lock=[IO.File]::Open("$store.lock",[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}
   StartApp;Shot "15-store-$mode";Dump "15-store-$mode";Record "Store $mode preserves original bytes" ([Convert]::ToBase64String([IO.File]::ReadAllBytes($store)) -ceq [Convert]::ToBase64String($before)) 'Fresh synthetic failure fixture';Record "Store $mode create affordance" $true "New-workspace visible=$($null -ne (Find 'New workspace button'))"
  }catch{Record "Store $mode harness-or-product-stop" $false $_.Exception.Message}finally{StopApp;if($lock){$lock.Dispose()}}
 }
}finally{StopApp;Flush;if(Test-Path "$storeDir/crash.log"){Copy-Item "$storeDir/crash.log" "$out/crash.log"};Get-ChildItem $out|Select-Object Name,Length}
