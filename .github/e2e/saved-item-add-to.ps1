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
function FindMenu($name) {
 $c=[Windows.Automation.AndCondition]::new([Windows.Automation.Condition[]]@((New-Object $PC($A::NameProperty,$name)),(New-Object $PC($A::ControlTypeProperty,$CT::MenuItem)),(New-Object $PC($A::IsOffscreenProperty,$false))))
 $all=$A::RootElement.FindAll($TS::Descendants,$c);foreach($e in $all){if($e.Current.ProcessId -eq $p.Id){return $e}};return $null
}
function WaitMenu($name){$sw=[Diagnostics.Stopwatch]::StartNew();do{$e=FindMenu $name;if($e){return $e};Start-Sleep -Milliseconds 80}while($sw.Elapsed.TotalSeconds -lt 8);throw "Menu missing: $name"}
function ChooseMenu($name){$e=WaitMenu $name;$e.SetFocus();[Windows.Forms.SendKeys]::SendWait('{ENTER}');Start-Sleep -Milliseconds 180}
function ExpandAdd { $e=WaitMenu 'הוסף ל...';$e.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand();Start-Sleep -Milliseconds 200 }
function MenuShot($name){$r=New-Object ReviewWin+R;[void][ReviewWin]::GetWindowRect($p.MainWindowHandle,[ref]$r);$im=New-Object Drawing.Bitmap ($r.Rt-$r.L),($r.B-$r.T);$g=[Drawing.Graphics]::FromImage($im);try{$g.CopyFromScreen($r.L,$r.T,0,0,$im.Size);$im.Save("$out/$name.png")}finally{$g.Dispose();$im.Dispose()}}
function DetailMenu {Context 'Edit item';ExpandAdd}
try{
 Scenario '01-details-submenu' {
  Click 'Review one';DetailMenu;MenuShot '01-right-click-submenu'
  foreach($n in @('סביבה חדשה','סביבה נבחרת','רשימה שחורה','רשימה לבנה','סביבה גלובלית')){[void](WaitMenu $n)};Record 'Five destinations in right-click submenu' $true 'Native popup inspected'
  ChooseMenu 'סביבה חדשה';[void](Wait 'New item environment name' $CT::Edit);Shot '01-new-env';Click 'Cancel item environment';$o=Get-Content $store -Raw|ConvertFrom-Json;Record 'Cancel new environment changes nothing' ($o.workspaces.Count -eq 2) 'No unnamed or empty environment'
  DetailMenu;ChooseMenu 'סביבה חדשה';Text 'New item environment name' 'Copied environment';Click 'Create item environment';$o=Get-Content $store -Raw|ConvertFrom-Json;Record 'New environment gets independent saved membership' (@(($o.workspaces|Where-Object name -eq 'Copied environment').items).Count -eq 1 -and @(($o.workspaces|Where-Object id -eq 'one').items).Count -eq 2) 'Copy, source unchanged';Shot '01-created'
 }
 Scenario '02-selected-destination' {
  StopApp;$o=Get-Content $store -Raw|ConvertFrom-Json;$o.workspaces+=@{id='empty';name='Empty destination';items=@()};$o|ConvertTo-Json -Depth 10|Set-Content $store;StartApp
  Click 'Review one';DetailMenu;ChooseMenu 'סביבה נבחרת';[void](Wait 'Choose item environments');Shot '02-destination-picker';$e=Wait 'Empty destination' $CT::ListItem;$e.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();Click 'Confirm item workspace choice';$o=Get-Content $store -Raw|ConvertFrom-Json;Record 'Chosen existing environment receives item' (@(($o.workspaces|Where-Object id -eq 'empty').items).Count -eq 1) 'Duplicate source destinations excluded'
 }
 Scenario '03-global-and-conflict' {
  Click 'Review one';DetailMenu;ChooseMenu 'רשימה לבנה';DetailMenu;ChooseMenu 'רשימה שחורה';[void](Wait 'Move capture list item' $CT::Button);Shot '03-move-confirm';Click 'Close capture list conflict';$o=Get-Content $store -Raw|ConvertFrom-Json;Record 'Cancel opposite list copy leaves original' (@($o.captureWhitelist).Count -eq 1 -and @($o.captureBlacklist).Count -eq 0) 'Cancel does not mutate'
  DetailMenu;ChooseMenu 'רשימה שחורה';Click 'Move capture list item';DetailMenu;ChooseMenu 'סביבה גלובלית';$o=Get-Content $store -Raw|ConvertFrom-Json;Record 'List move and startup copy preserve workspace source' (@($o.captureWhitelist).Count -eq 0 -and @($o.captureBlacklist).Count -eq 1 -and @($o.globalItems).Count -eq 1 -and @($o.workspaces[0].items).Count -eq 2) 'Only list conflict moves';Click 'Back to workspaces';Mouse 'Options menu';Menu 'סביבה גלובלית';$host=Wait 'Saved global item';$host.SetFocus();[Windows.Forms.SendKeys]::SendWait('+{F10}');ExpandAdd;MenuShot '03-global-row-submenu';ChooseMenu 'סביבה חדשה';Text 'New item environment name' 'From global';Click 'Create item environment';$o=Get-Content $store -Raw|ConvertFrom-Json;Record 'Global saved row can copy to new environment' (@($o.workspaces|Where-Object name -eq 'From global').Count -eq 1) 'Same five destinations'
 }
 Scenario '04-all-items' {
  Mouse 'Options menu';Menu 'אייטמים';$e=Wait 'Shared sample' $CT::Button;$e.SetFocus();[Windows.Forms.SendKeys]::SendWait('+{F10}');ExpandAdd;MenuShot '04-all-items-submenu';ChooseMenu 'סביבה חדשה';Text 'New item environment name' 'From all items';Click 'Create item environment';$o=Get-Content $store -Raw|ConvertFrom-Json;Record 'All Items saved row copies' (@($o.workspaces|Where-Object name -eq 'From all items').Count -eq 1) 'Effective saved instance snapshot'
 }
 Scenario '05-capture-saved-side' {
  Click 'Review one';Click 'Add from open items';[void](Wait 'Open now list');Start-Sleep -Seconds 6;$host=Wait 'Saved capture item';$host.SetFocus();[Windows.Forms.SendKeys]::SendWait('+{F10}');ExpandAdd;MenuShot '05-saved-capture-submenu';ChooseMenu 'סביבה חדשה';Text 'New item environment name' 'From capture saved';Click 'Create item environment';$o=Get-Content $store -Raw|ConvertFrom-Json;Record 'Saved capture destination row copies' (@($o.workspaces|Where-Object name -eq 'From capture saved').Count -eq 1) 'Newly scanned and unsaved staged rows have no new menu'
 }
}finally{StopApp;Flush;if(Test-Path "$storeDir/crash.log"){Copy-Item "$storeDir/crash.log" "$out/crash.log"}}
if(@($results|Where-Object{-not $_.ok}).Count -gt 0){throw 'Saved Add-to checks failed; inspect results.json'}
