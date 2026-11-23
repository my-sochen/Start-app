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
 Scenario '01-long-heading' {
  StopApp;$o=Get-Content $store -Raw|ConvertFrom-Json;$o.workspaces[0].name=('סביבה ארוכה לבדיקה ' * 15);$o|ConvertTo-Json -Depth 10|Set-Content $store;StartApp
  Click $o.workspaces[0].name;Shot '01-long-heading';Dump '01-long-heading';$title=Wait 'Workspace heading';$back=Wait 'Back to workspaces' $CT::Button;$tr=$title.Current.BoundingRectangle;$br=$back.Current.BoundingRectangle
  Record 'Long title does not overlap back control' (-not $tr.IntersectsWith($br)) "title=$tr back=$br"
  Click 'Back to workspaces'
 }
 Scenario '02-pasted-website' {
  Click 'Review one';Click '+ הוסף אייטם';Text 'Item target' 'https://example.com/pasted';Text 'Item name' 'Pasted website';Shot '02-hebrew-add';Click 'Add item';Shot '02-saved';$o=Get-Content $store -Raw|ConvertFrom-Json;$it=@($o.items.PSObject.Properties.Value|Where-Object target -eq 'https://example.com/pasted')[0];Record 'Pasted web URL saved as Link' ($it.type -eq 'link') "actual=$($it.type)"
 }
 Scenario '03-inline-website-rejection' {
  Click 'Review one';Click '+ הוסף אייטם';Text 'Item target' 'https://example.com/seed';Text 'Item target' 'javascript:alert(1)';Click 'Add item';Shot '03-address-reason';Dump '03-address-reason';$o=Get-Content $store -Raw|ConvertFrom-Json;Record 'Unsupported website not persisted' (@($o.items.PSObject.Properties.Value|Where-Object target -like 'javascript:*').Count -eq 0) 'Draft retained beside address error'
 }
 Scenario '04-opposite-lists' {
  Mouse 'Options menu';Menu 'סביבה גלובלית';Click 'Add whitelist item';Text 'Global entry name' 'List sample';Text 'Global entry target' 'https://example.com/lists';Click 'Confirm global add'
  Click 'Add blacklist item';$combo=Wait 'Global entry type' $CT::ComboBox;$combo.SetFocus();[Windows.Forms.SendKeys]::SendWait('{HOME}{ENTER}');Text 'Global entry name' 'List sample';Text 'Global entry target' 'https://example.com/lists';Click 'Confirm global add';Shot '04-confirm-move';Dump '04-confirm-move';[void](Wait 'Capture list conflict popup');Click 'Close capture list conflict';$o=Get-Content $store -Raw|ConvertFrom-Json;Record 'Cancel does not move' (@($o.captureWhitelist|Where-Object target -eq 'https://example.com/lists').Count -eq 1 -and @($o.captureBlacklist|Where-Object target -eq 'https://example.com/lists').Count -eq 0) 'Original list preserved';Record 'Cancel keeps target draft' ((Value 'Global entry target') -eq 'https://example.com/lists') 'Draft preserved';Click 'Confirm global add';Click 'Move capture list item';Shot '04-moved';$o=Get-Content $store -Raw|ConvertFrom-Json;Record 'Move switches list exactly once' (@($o.captureWhitelist|Where-Object target -eq 'https://example.com/lists').Count -eq 0 -and @($o.captureBlacklist|Where-Object target -eq 'https://example.com/lists').Count -eq 1) 'One confirmed atomic move'
  Click 'Add whitelist item';Text 'Global entry name' 'List sample';Text 'Global entry target' 'https://example.com/lists';Click 'Confirm global add';Click 'Move capture list item';$o=Get-Content $store -Raw|ConvertFrom-Json;Record 'Reverse move works' (@($o.captureWhitelist|Where-Object target -eq 'https://example.com/lists').Count -eq 1 -and @($o.captureBlacklist|Where-Object target -eq 'https://example.com/lists').Count -eq 0) 'Reverse direction'
 }
}finally{StopApp;Flush;if(Test-Path "$storeDir/crash.log"){Copy-Item "$storeDir/crash.log" "$out/crash.log"};Get-ChildItem $out|Select-Object Name,Length}
if(@($results|Where-Object{-not $_.ok}).Count -gt 0){throw 'Review fix checks failed, inspect results.json'}
