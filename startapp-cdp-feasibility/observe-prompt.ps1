# LOCAL C1 default-directory consent+metadata candidate. No push/run authority. Clean ephemeral runner ONLY.
param([string]$OutDir='cdp-enumerate-out')
$ErrorActionPreference='Stop'
function ReviewedProductAllowed($value) {
 if($value -isnot [string] -or $value -cnotmatch '\A7\.5\.4 SHA: (?<commit>[0-9a-f]{7,40})\z'){return $false}
 return '7c8d9e7e0ed2fc1f2caf50746fe9fef720ca2a0a'.StartsWith($Matches.commit,[StringComparison]::Ordinal)
}
function ReviewedRuntimeAllowed($f) {
 return ($f.windows -eq $true -and $f.ps -ceq '7.5.4' -and $f.gitCommit -ceq '7.5.4' -and $f.edition -ceq 'Core' -and $f.runtime -ceq '9.0.10' -and $f.commandType -ceq 'Cmdlet' -and $f.module -ceq 'Microsoft.PowerShell.Management' -and $f.assembly -ceq 'Microsoft.PowerShell.Commands.Management' -and (ReviewedProductAllowed $f.product) -and $f.assemblyInPSHome -eq $true -and $f.nodeApplication -eq $true)
}
function AssertReviewedRuntime {
 $cmd=Get-Command 'Microsoft.PowerShell.Management\Start-Process' -CommandType Cmdlet -ErrorAction Stop
 $node=Get-Command node.exe -CommandType Application -ErrorAction Stop
 $location=$cmd.ImplementingType.Assembly.Location
 $expected=Join-Path $PSHOME 'Microsoft.PowerShell.Commands.Management.dll'
 $facts=@{windows=[Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows);ps=$PSVersionTable.PSVersion.ToString();gitCommit=[string]$PSVersionTable.GitCommitId;edition=[string]$PSVersionTable.PSEdition;runtime=[Environment]::Version.ToString();commandType=$cmd.CommandType.ToString();module=$cmd.ModuleName;assembly=$cmd.ImplementingType.Assembly.GetName().Name;product=(Get-Item -LiteralPath $location).VersionInfo.ProductVersion;assemblyInPSHome=[StringComparer]::OrdinalIgnoreCase.Equals([IO.Path]::GetFullPath($location),[IO.Path]::GetFullPath($expected));nodeApplication=($node.CommandType -eq [Management.Automation.CommandTypes]::Application -and [IO.Path]::IsPathFullyQualified($node.Path))}
 if(-not(ReviewedRuntimeAllowed $facts)){throw 'Reviewed experiment unavailable: requires exact Windows PowerShell7.5.4/Core/.NET9.0.10 and reviewed built-in current-user redirected launch path; no alternate runtime or retry'}
 return $node.Path
}
try{$script:reviewedNode=AssertReviewedRuntime}catch{Write-Host ('OBS_RUNTIME_UNAVAILABLE '+$_.Exception.Message);exit 2}
$OutDir=[IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force $OutDir | Out-Null
$records=[Collections.Generic.List[object]]::new()
function Record($browser,$step,$status,$detail) {
 $r=[pscustomobject]@{browser=$browser;step=$step;status=$status;detail=$detail;utc=[DateTime]::UtcNow.ToString('o')}
 $records.Add($r)
 Write-Host ('OBS_RECORD '+($r | ConvertTo-Json -Depth 8 -Compress))
 $records | ConvertTo-Json -Depth 10 | Set-Content -Encoding utf8 (Join-Path $OutDir 'assertions.json')
}
# Normal default-directory launches have no profile argument in their command line.
# Do not replace old attribution with a broad executable-name match: fail on ANY unknown process.
$script:ownership=$null
$script:helperCleanupUnconfirmed=$false
function BirthKey($row) {
 if($null -eq $row.CreationDate){throw 'Missing process birth identity'}
 return ([DateTime]$row.CreationDate).ToUniversalTime().Ticks.ToString()
}
function ProcessAllowed($row,$state) {
 if($null -eq $row -or -not $row.ProcessId -or -not $row.ExecutablePath -or -not $row.CommandLine){return $false}
 if(-not [StringComparer]::OrdinalIgnoreCase.Equals([IO.Path]::GetFullPath($row.ExecutablePath),$state.exe)){return $false}
 # No debugging switch or custom user-data directory, even if the path happens to be expected.
 if($row.CommandLine -match '(?i)--(?:user-data-dir|remote-debugging-port|remote-debugging-pipe)(?:[=\s]|$)'){return $false}
 try{$birth=BirthKey $row}catch{return $false}
 $id=[string]$row.ProcessId
 if(-not $state.known.ContainsKey($id)){return $false}
 $known=$state.known[$id]
 return ($known.birth -ceq $birth -and $known.parent -eq [int]$row.ParentProcessId)
}
function Processes($profile,$exeName) {
 $state=$script:ownership
 if($null -eq $state -or -not [StringComparer]::OrdinalIgnoreCase.Equals($profile,$state.profile) -or $exeName -cne $state.exeName){throw 'No exact default-session ownership context'}
 $allRows=@(Get-CimInstance Win32_Process)
 $rows=@($allRows | Where-Object {[StringComparer]::OrdinalIgnoreCase.Equals($_.Name,$exeName)})
 # Read current process tree once, expand only from an already recorded birth-bound root.
 $pending=[Collections.Generic.List[object]]::new();foreach($row in $rows){$pending.Add($row)}
 for($round=0;$round -le $rows.Count -and $pending.Count;$round++){
  $progress=$false
  foreach($row in @($pending.ToArray())){
   $id=[string]$row.ProcessId;$parent=[string]$row.ParentProcessId
   if($state.known.ContainsKey($id)){
    if(-not(ProcessAllowed $row $state)){throw 'Owned PID identity changed or prohibited browser switch'}
    [void]$pending.Remove($row);$progress=$true;continue
   }
   if(-not $state.known.ContainsKey($parent)){continue}
   $parentKnown=$state.known[$parent];$birth=BirthKey $row
   if([long]$birth -lt [long]$parentKnown.birth){throw 'Process predates recorded parent'}
   $liveParent=@($allRows | Where-Object {[string]$_.ProcessId -ceq $parent})
   if($liveParent.Count -ne 1){throw 'Unrecorded child requires its exact live parent; historical ancestry insufficient'}
   if(-not(ProcessAllowed $liveParent[0] $state)){throw 'Parent PID reused or changed'}
   $state.known[$id]=@{birth=$birth;parent=[int]$row.ParentProcessId;launch=$false}
   if(-not(ProcessAllowed $row $state)){[void]$state.known.Remove($id);throw 'Foreign child process or prohibited browser switch'}
   [void]$pending.Remove($row);$progress=$true
  }
  if(-not $progress){break}
 }
 if($pending.Count){throw 'Unattributed same-browser process; no broad name fallback'}
 return $rows
}
function StartOwnedBrowser($exe,$arguments) {
 $state=$script:ownership
 # Detect any foreign same-browser process BEFORE adding another deliberately launched root.
 if($state.known.Count){[void]@(Processes $state.profile $state.exeName)}
 elseif(@(Get-CimInstance Win32_Process -Filter "Name='$($state.exeName)'").Count){throw 'Browser appeared before initial launch'}
 [void](AssertReviewedRuntime)
 $started=Microsoft.PowerShell.Management\Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
 $started.Refresh()
 [void]$started.Handle # Bind exact live process object before accepting CIM identity.
 if($started.HasExited){throw 'Launch root exited before birth-bound ownership could be recorded'}
 $row=@(Get-CimInstance Win32_Process -Filter "ProcessId=$($started.Id)")
 if($row.Count -ne 1 -or $row[0].ParentProcessId -ne $PID){throw 'Exact launch root/parent not proven'}
 if((BirthKey $row[0]) -cne $started.StartTime.ToUniversalTime().Ticks.ToString()){throw 'Launch process birth mismatch; no timestamp-tolerance fallback'}
 $id=[string]$started.Id
 if($state.known.ContainsKey($id)){throw 'Launch PID already recorded'}
 $state.known[$id]=@{birth=(BirthKey $row[0]);parent=[int]$row[0].ParentProcessId;launch=$true}
 if(-not(ProcessAllowed $row[0] $state)){throw 'Launch path/switch identity refused'}
 Diagnostic $state.exeName 'owned-normal-launch' @{pid=[int]$started.Id;birth=$state.known[$id].birth;parent=[int]$row[0].ParentProcessId;exe=$state.exe;defaultRoot=$state.profile;normalSwitchesOnly=$true}
 return $started
}
function StopOwnedProcess($row) {
 $state=$script:ownership
 if(-not(ProcessAllowed $row $state)){throw 'Cleanup row no longer birth/path owned'}
 $proc=$null
 try {
  $proc=[Diagnostics.Process]::GetProcessById([int]$row.ProcessId)
  # Force opening the exact process handle before comparing birth, then kill through that object.
  [void]$proc.Handle
  if($proc.StartTime.ToUniversalTime().Ticks.ToString() -cne (BirthKey $row)){throw 'Cleanup process handle birth mismatch; no kill'}
  $proc.Kill()
 }catch{
  # An exited PID is harmless only if it is still absent; never kill a replacement.
  if(@(Get-CimInstance Win32_Process -Filter "ProcessId=$($row.ProcessId)").Count){throw}
 }finally{if($proc){$proc.Dispose()}}
}
function Windows($profile,$exeName) {
 $ids=@(Processes $profile $exeName | ForEach-Object {[int]$_.ProcessId})
 @([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.Condition]::TrueCondition) | Where-Object {$ids -contains $_.Current.ProcessId -and $_.Current.ControlType -eq [Windows.Automation.ControlType]::Window})
}
function WaitUntil([scriptblock]$condition,[int]$seconds=20) {
 $until=[DateTime]::UtcNow.AddSeconds($seconds)
 do {if (& $condition){return $true};Start-Sleep -Milliseconds 300}while([DateTime]::UtcNow -lt $until)
 return $false
}
function Omnibox($window) {
 @($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Edit)) | Where-Object {$_.Current.ClassName -eq 'OmniboxViewViews'})
}
function NavigateInspect($profile,$exeName,$intended) {
 # Navigation input is allowed ONLY inside the one uniquely attributed disposable window.
 $windows=@(Windows $profile $exeName)
 if($windows.Count -ne 1){throw 'Navigation requires exactly one attributed synthetic window'}
 $window=$windows[0];$handle=[IntPtr]$window.Current.NativeWindowHandle
 $boxes=@(Omnibox $window)
 if($boxes.Count -ne 1){throw 'Exactly one synthetic OmniboxViewViews edit required'}
 $box=$boxes[0];$pattern=$box.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
 if($pattern.Current.IsReadOnly){throw 'Synthetic omnibox is read-only'}
 # Never type permission input or click controls. No fallback coordinates or key shortcuts.
 if(-not [ObserveNative]::SetForegroundWindow($handle)){throw 'Could not foreground synthetic navigation window'}
 if([ObserveNative]::GetForegroundWindow() -ne $handle){throw 'Synthetic navigation window is not foreground'}
 $box.SetFocus()
 $focused=[Windows.Automation.AutomationElement]::FocusedElement
 if(-not [Windows.Automation.Automation]::Compare($focused,$box)){throw 'Omnibox focus identity not proven'}
 $ids=@(Processes $profile $exeName | ForEach-Object {[int]$_.ProcessId})
 if($ids -notcontains $focused.Current.ProcessId){throw 'Focused element not attributed to synthetic profile'}
 $pattern.SetValue($intended)
 if($pattern.Current.Value -cne $intended){throw 'Intended URL input readback mismatch'}
 # Revalidate focus AND foreground immediately before the sole Enter keystroke.
 if([ObserveNative]::GetForegroundWindow() -ne $handle -or -not [Windows.Automation.Automation]::Compare([Windows.Automation.AutomationElement]::FocusedElement,$box)){throw 'Navigation focus changed; no Enter sent'}
 [Windows.Forms.SendKeys]::SendWait('{ENTER}')
 $confirmed=WaitUntil {
  $ws=@(Windows $profile $exeName);if($ws.Count -ne 1){return $false}
  $edits=@(Omnibox $ws[0]);if($edits.Count -ne 1){return $false}
  try {
   $current=$edits[0].GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
   if($current -cne $intended){return $false}
   $docs=@($ws[0].FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Document)))
   foreach($doc in $docs){try{if($doc.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ceq $intended){return $true}}catch{}}
  }catch{}
  return $false
 } 20
 $currentValue=$null
 try{$ws=@(Windows $profile $exeName);if($ws.Count -eq 1){$edits=@(Omnibox $ws[0]);if($edits.Count -eq 1){$currentValue=$edits[0].GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value}}}catch{}
 [pscustomobject]@{confirmed=$confirmed;intended=$intended;omnibox=$currentValue;note='PASS only exact omnibox plus Document ValuePattern URL readback; not settings/capability proof'}
}
function Structure($window) {
 $walker=[Windows.Automation.TreeWalker]::ControlViewWalker
 $q=[Collections.Generic.Queue[object]]::new();$q.Enqueue(@($window,0))
 $nodes=[Collections.Generic.List[object]]::new();$truncated=$false
 while($q.Count -gt 0 -and $nodes.Count -lt 1200) {
  $entry=$q.Dequeue();$e=$entry[0];$depth=[int]$entry[1]
  try {
   $c=$e.Current;$rect=$c.BoundingRectangle
   $patterns=@($e.GetSupportedPatterns() | ForEach-Object {$_.ProgrammaticName})
   $state=@{};$rid=@();$parentRid=@();try{$rid=@($e.GetRuntimeId());$parent=$walker.GetParent($e);if($null -ne $parent){$parentRid=@($parent.GetRuntimeId())}}catch{}
   if($patterns -contains 'TogglePatternIdentifiers.Pattern') {try{$state.toggle=$e.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState.ToString()}catch{}}
   $nodes.Add([pscustomobject]@{depth=$depth;pid=$c.ProcessId;hwnd=$c.NativeWindowHandle;runtimeId=$rid;parentRuntimeId=$parentRid;name=$c.Name;type=$c.ControlType.ProgrammaticName;automationId=$c.AutomationId;class=$c.ClassName;framework=$c.FrameworkId;enabled=$c.IsEnabled;offscreen=$c.IsOffscreen;bounds=@($rect.X,$rect.Y,$rect.Width,$rect.Height);patterns=$patterns;state=$state})
   if($depth -ge 14){$truncated=$true;continue}
   $child=$walker.GetFirstChild($e)
   while($null -ne $child){if($q.Count -ge 1200){$truncated=$true;break};$q.Enqueue(@($child,($depth+1)));$child=$walker.GetNextSibling($child)}
  } catch {$nodes.Add([pscustomobject]@{depth=$depth;error=$_.Exception.Message})}
 }
 if($q.Count){$truncated=$true}
 [pscustomobject]@{nodes=@($nodes.ToArray());truncated=$truncated}
}
function Capture($key,$window) {
 $rect=$window.Current.BoundingRectangle
 $screen=[Windows.Forms.SystemInformation]::VirtualScreen
 $left=[Math]::Max([int]$rect.Left,$screen.Left);$top=[Math]::Max([int]$rect.Top,$screen.Top)
 $right=[Math]::Min([int]$rect.Right,$screen.Right);$bottom=[Math]::Min([int]$rect.Bottom,$screen.Bottom)
 $width=$right-$left;$height=$bottom-$top
 if($width -lt 300 -or $height -lt 200 -or $width -gt 1600 -or $height -gt 1100){throw 'Attributed window capture dimensions outside approved bounds'}
 $bitmap=[Drawing.Bitmap]::new($width,$height);$graphics=[Drawing.Graphics]::FromImage($bitmap)
 $stream=[IO.MemoryStream]::new()
 try {
  $graphics.CopyFromScreen($left,$top,0,0,$bitmap.Size)
  $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
  $bytes=$stream.ToArray()
  if($bytes.Length -gt 262144){throw 'PNG exceeds 256KiB console evidence cap; no image emitted'}
  $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
  [IO.File]::WriteAllBytes((Join-Path $OutDir "$key.png"),$bytes)
  Write-Host "OBS_PNG_BEGIN $key $($bytes.Length) $hash $width $height"
  $base64=[Convert]::ToBase64String($bytes)
  for($i=0;$i -lt $base64.Length;$i+=3000){Write-Host ("OBS_PNG_CHUNK $key "+$base64.Substring($i,[Math]::Min(3000,$base64.Length-$i)))}
  Write-Host "OBS_PNG_END $key"
  [pscustomobject]@{bytes=$bytes.Length;sha256=$hash;width=$width;height=$height;note='Screen crop of attributed window, not proof against occlusion; actual pixels must be reviewed'}
 }finally{$stream.Dispose();$graphics.Dispose();$bitmap.Dispose()}
}
function Descendants($window) {@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition))}
function Selection($profile,$exeName) {
 $items=@()
 foreach($w in (Windows $profile $exeName)) {
  foreach($tab in (Descendants $w | Where-Object {$_.Current.ControlType -eq [Windows.Automation.ControlType]::TabItem -and $_.Current.Name -match '^StartAppFixture[ABC](?:\s.*)?$'})) {
   $sp=$tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)
   $items+=[pscustomobject]@{hwnd=$w.Current.NativeWindowHandle;fixture=($tab.Current.Name -split '\s')[0];title=$tab.Current.Name;selected=$sp.Current.IsSelected}
  }
 }
 @($items | Sort-Object hwnd,title)
}
function CanonSelection($items) {
 @($items | Sort-Object hwnd,fixture | ForEach-Object {[pscustomobject]@{hwnd=$_.hwnd;fixture=$_.fixture;selected=$_.selected}}) | ConvertTo-Json -Compress
}
function ValidSelection($items) {
 if($items.Count -ne 3){return $false}
 foreach($x in $items){
  if($null -eq $x -or $x.hwnd -le 0 -or $x.selected -isnot [bool] -or $x.fixture -cnotmatch '^StartAppFixture[ABC]$' -or ($x.title -split '\s')[0] -cne $x.fixture){return $false}
 }
 $a=@($items | Where-Object {$_.fixture -ceq 'StartAppFixtureA'})
 $b=@($items | Where-Object {$_.fixture -ceq 'StartAppFixtureB'})
 $c=@($items | Where-Object {$_.fixture -ceq 'StartAppFixtureC'})
 if($a.Count -ne 1 -or $b.Count -ne 1 -or $c.Count -ne 1){return $false}
 return ($a[0].hwnd -eq $b[0].hwnd -and $c[0].hwnd -ne $a[0].hwnd -and $a[0].selected -eq $true -and $b[0].selected -eq $false -and $c[0].selected -eq $true)
}
function Picture($name,$phase,$window) {
 try {$d=Capture "$name-$phase" $window;Record $name $phase 'PASS' ($d | ConvertTo-Json -Compress);return $true}
 catch {Record $name $phase 'UNKNOWN' $_.Exception.Message;return $false}
}
# Diagnostic-only stream, not OBS_RECORD: never supplies a phase PASS or replaces pixels.
# Fixed field allowlists at each call site; synthetic HWNDs/states only, bounded samples, no document content.
function Diagnostic($name,$stage,$detail) {
 Write-Host ('OBS_DIAG '+(@{browser=$name;stage=$stage;detail=$detail;utc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json -Depth 6 -Compress))
}
function ActivateSynthetic($name,$stage,$profile,$exeName,$handle) {
 $eligible=@(Windows $profile $exeName | Where-Object {$_.Current.NativeWindowHandle -eq $handle.ToInt64()})
 if($eligible.Count -ne 1){Diagnostic $name $stage @{target=$handle.ToInt64();eligible=$eligible.Count;attempted=$false};return $false}
 # Exactly ONE activation call. Passive reads do not bypass Windows foreground restrictions.
 $prior=[ObserveNative]::GetForegroundWindow().ToInt64()
 $accepted=[ObserveNative]::SetForegroundWindow($handle)
 $clock=[Diagnostics.Stopwatch]::StartNew();$samples=@();$matched=$false
 for($i=0;$i -lt 21;$i++){
  $actual=[ObserveNative]::GetForegroundWindow().ToInt64()
  $samples+=@{elapsedMs=$clock.ElapsedMilliseconds;hwnd=$actual}
  if($actual -eq $handle.ToInt64()){$matched=$true;break}
  if($clock.ElapsedMilliseconds -ge 2000){break};Start-Sleep -Milliseconds 100
 }
 Diagnostic $name $stage @{target=$handle.ToInt64();eligible=1;attempted=$true;prior=$prior;apiReturned=$accepted;samples=$samples;matched=$matched}
 # A false API return alone no longer hides the independent actual-HWND read.
 return $matched
}
function FreshInspectToggle($profile,$exeName,$handle,$uri) {
 $ws=@(Windows $profile $exeName | Where-Object {$_.Current.NativeWindowHandle -eq $handle.ToInt64()})
 if($ws.Count -ne 1){throw 'Fresh inspect HWND attribution unavailable'}
 $edits=@(Omnibox $ws[0]);if($edits.Count -ne 1 -or $edits[0].GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -cne $uri){throw 'Fresh inspect omnibox URI mismatch'}
 $docs=@(Descendants $ws[0] | Where-Object {$_.Current.ControlType -eq [Windows.Automation.ControlType]::Document})
 $docMatch=$false
 foreach($doc in $docs){try{if($doc.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ceq $uri){$docMatch=$true}}catch{}}
 if(-not $docMatch){throw 'Fresh inspect Document URL unavailable'}
 $boxes=@(Descendants $ws[0] | Where-Object {$_.Current.ControlType -eq [Windows.Automation.ControlType]::CheckBox -and $_.Current.AutomationId -eq 'remote-debugging-enabled' -and $_.Current.Name -ceq 'Allow remote debugging for this browser instance'})
 if($boxes.Count -ne 1 -or -not $boxes[0].Current.IsEnabled -or $boxes[0].Current.IsOffscreen){throw 'Fresh exact enabled visible inspect checkbox unavailable'}
 $boxes[0].GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
}
function WaitToggleState($name,$stage,$profile,$exeName,$handle,$uri,$desired) {
 $clock=[Diagnostics.Stopwatch]::StartNew();$samples=@();$matched=$false;$readError=$false
 for($i=0;$i -lt 21;$i++){
  try{$fresh=FreshInspectToggle $profile $exeName $handle $uri;$state=$fresh.Current.ToggleState.ToString();$samples+=@{elapsedMs=$clock.ElapsedMilliseconds;state=$state}}
  catch{$samples+=@{elapsedMs=$clock.ElapsedMilliseconds;state='UNAVAILABLE';exceptionType=$_.Exception.GetType().FullName};$readError=$true;break}
  if($state -ceq $desired){$matched=$true;break}
  if($clock.ElapsedMilliseconds -ge 2000){break};Start-Sleep -Milliseconds 100
 }
 Diagnostic $name $stage @{desired=$desired;samples=$samples;matched=$matched;readError=$readError}
 return $matched
}
function FixturePictures($name,$stage,$profile,$exeName,$expected) {
 $all=@(Windows $profile $exeName);$ok=$true
 $handles=@($expected.hwnd | Sort-Object -Unique)
 if($handles.Count -ne 2){return $false}
 for($i=0;$i -lt $handles.Count;$i++){
  $w=@($all | Where-Object {$_.Current.NativeWindowHandle -eq $handles[$i]})
  if($w.Count -ne 1){return $false}
  $h=[IntPtr]$handles[$i]
  if(-not(ActivateSynthetic $name "$stage-window$($i+1)-activation" $profile $exeName $h)){Record $name "$stage-window$($i+1)" 'UNKNOWN' 'Synthetic fixture window foreground not verified after one activation and bounded passive readback';$ok=$false;continue}
  if(-not(Picture $name "$stage-window$($i+1)" $w[0])){$ok=$false}
 }
 return $ok
}
# Native identity/capture functions use no input. The sole consent Invoke is separately gated below.
# Identity is native-owner-chain AND native/UIA PID/HWND agreement, never title-only.
function ObservationIdentityAllowed($s) {
 if(-not $s.inspectUnique -or -not $s.inspectFresh -or -not $s.foregroundStable){return $false}
 if($s.foreground -eq 0 -or $s.foreground -eq $s.inspect -or $s.rootOwner -ne $s.inspect){return $false}
 if($s.chain.Count -lt 2 -or $s.chain.Count -gt 8 -or $s.chain[0].hwnd -ne $s.foreground -or $s.chain[-1].hwnd -ne $s.inspect){return $false}
 if(@($s.chain.hwnd | Select-Object -Unique).Count -ne $s.chain.Count){return $false}
 for($i=0;$i -lt $s.chain.Count;$i++){
  $n=$s.chain[$i]
  if($s.profilePids -notcontains $n.pid -or $n.pid -le 0){return $false}
  if($i -lt $s.chain.Count-1 -and $n.owner -ne $s.chain[$i+1].hwnd){return $false}
 }
 if($s.uiaPid -ne $s.chain[0].pid -or $s.inspectUiaPid -ne $s.chain[-1].pid -or $s.uiaHwnd -ne $s.foreground -or -not $s.visible -or $s.minimized -or $s.offscreen){return $false}
 if(-not $s.rectAgrees -or -not $s.rectWithinParent -or -not $s.parentWithinScreen -or -not $s.sizeAllowed){return $false}
 if(-not $s.runtimeId -or -not $s.parentRuntimeId){return $false}
 return $true
}
function ObservationSnapshot($profile,$exeName,$inspectHandle,$uri) {
 $readStage='initial';$s=[ordered]@{inspect=$inspectHandle.ToInt64();inspectUnique=$false;inspectFresh=$false;foregroundStable=$false;foreground=0;rootOwner=0;chain=@();profilePids=@();uiaPid=0;inspectUiaPid=0;uiaHwnd=0;visible=$false;minimized=$true;offscreen=$true;rectAgrees=$false;rectWithinParent=$false;parentWithinScreen=$false;sizeAllowed=$false;runtimeId=@();parentRuntimeId=@();eligible=$false}
 try {
  $ws=@(Windows $profile $exeName | Where-Object {$_.Current.NativeWindowHandle -eq $inspectHandle.ToInt64()})
  $s.inspectUnique=$ws.Count -eq 1
  if(-not $s.inspectUnique){return [pscustomobject]$s}
  # No Toggle call: this existing routine only freshly reads inspect URL/control identity.
  $readStage='fresh-inspect';[void](FreshInspectToggle $profile $exeName $inspectHandle $uri);$s.inspectFresh=$true
  $readStage='native-ownership';$s.profilePids=@(Processes $profile $exeName | ForEach-Object {[int]$_.ProcessId} | Sort-Object -Unique)
  $foreground=[PromptNative]::GetForegroundWindow();$s.foreground=$foreground.ToInt64()
  $s.rootOwner=[PromptNative]::GetAncestor($foreground,3).ToInt64()
  $h=$foreground;$chain=@()
  for($i=0;$i -lt 8 -and $h -ne [IntPtr]::Zero;$i++){
   [uint32]$nativePid=0;[void][PromptNative]::GetWindowThreadProcessId($h,[ref]$nativePid)
   $owner=[PromptNative]::GetWindow($h,4)
   $chain+=@{hwnd=$h.ToInt64();pid=[int]$nativePid;owner=$owner.ToInt64()}
   if($h -eq $inspectHandle){break};$h=$owner
  }
  $s.chain=$chain
  # Refuse to inspect unrelated UI trees. Native chain must already reach the one inspect HWND.
  if($s.foreground -eq $s.inspect -or $s.rootOwner -ne $s.inspect -or $chain.Count -lt 2 -or $chain[-1].hwnd -ne $s.inspect -or @($chain | Where-Object {$s.profilePids -notcontains $_.pid}).Count){return [pscustomobject]$s}
  $readStage='uia-identity';$popup=[Windows.Automation.AutomationElement]::FromHandle($foreground)
  $s.inspectUiaPid=$ws[0].Current.ProcessId;$s.uiaPid=$popup.Current.ProcessId;$s.uiaHwnd=$popup.Current.NativeWindowHandle
  $s.runtimeId=@($popup.GetRuntimeId());$s.parentRuntimeId=@($ws[0].GetRuntimeId())
  $s.uiaParentChain=@();$node=$popup;$walker=[Windows.Automation.TreeWalker]::ControlViewWalker
  for($i=0;$i -lt 8 -and $null -ne $node;$i++){
   if($s.profilePids -notcontains $node.Current.ProcessId){break}
   $s.uiaParentChain+=@{pid=$node.Current.ProcessId;hwnd=$node.Current.NativeWindowHandle;runtimeId=@($node.GetRuntimeId());type=$node.Current.ControlType.ProgrammaticName;class=$node.Current.ClassName}
   $node=$walker.GetParent($node)
  }
  $focus=[Windows.Automation.AutomationElement]::FocusedElement
  if($null -ne $focus -and $s.profilePids -contains $focus.Current.ProcessId){$s.focused=@{pid=$focus.Current.ProcessId;hwnd=$focus.Current.NativeWindowHandle;runtimeId=@($focus.GetRuntimeId());type=$focus.Current.ControlType.ProgrammaticName;class=$focus.Current.ClassName}}
  $s.visible=[PromptNative]::IsWindowVisible($foreground);$s.minimized=[PromptNative]::IsIconic($foreground);$s.offscreen=$popup.Current.IsOffscreen
  $pr=[PromptNative+RECT]::new();$ir=[PromptNative+RECT]::new()
  if(-not [PromptNative]::GetWindowRect($foreground,[ref]$pr) -or -not [PromptNative]::GetWindowRect($inspectHandle,[ref]$ir)){return [pscustomobject]$s}
  $ur=$popup.Current.BoundingRectangle;$screen=[Windows.Forms.SystemInformation]::VirtualScreen
  $s.rectAgrees=([Math]::Abs($ur.Left-$pr.Left) -le 1 -and [Math]::Abs($ur.Top-$pr.Top) -le 1 -and [Math]::Abs($ur.Right-$pr.Right) -le 1 -and [Math]::Abs($ur.Bottom-$pr.Bottom) -le 1)
  $s.rectWithinParent=($pr.Left -ge $ir.Left -and $pr.Top -ge $ir.Top -and $pr.Right -le $ir.Right -and $pr.Bottom -le $ir.Bottom)
  $s.parentWithinScreen=($ir.Left -ge $screen.Left -and $ir.Top -ge $screen.Top -and $ir.Right -le $screen.Right -and $ir.Bottom -le $screen.Bottom)
  $s.sizeAllowed=(($ir.Right-$ir.Left) -ge 300 -and ($ir.Bottom-$ir.Top) -ge 200 -and ($ir.Right-$ir.Left) -le 1600 -and ($ir.Bottom-$ir.Top) -le 1100)
  $s.parentRect=@($ir.Left,$ir.Top,$ir.Right,$ir.Bottom);$s.popupRect=@($pr.Left,$pr.Top,$pr.Right,$pr.Bottom)
  $readStage='foreground-readback';$s.foregroundStable=[PromptNative]::GetForegroundWindow() -eq $foreground
  $s.eligible=ObservationIdentityAllowed ([pscustomobject]$s)
 }catch{$s.errorType=$_.Exception.GetType().FullName;$s.errorStage=$readStage}
 [pscustomobject]$s
}
function SameObservationIdentity($a,$b) {
 if(-not $a.eligible -or -not $b.eligible){return $false}
 if((@($a.chain | ForEach-Object {,@($_.hwnd,$_.pid,$_.owner)}) | ConvertTo-Json -Depth 8 -Compress) -cne (@($b.chain | ForEach-Object {,@($_.hwnd,$_.pid,$_.owner)}) | ConvertTo-Json -Depth 8 -Compress)){return $false}
 foreach($key in @('inspect','foreground','rootOwner','profilePids','uiaPid','inspectUiaPid','uiaHwnd','runtimeId','parentRuntimeId','parentRect','popupRect')){
  if(($a.$key | ConvertTo-Json -Depth 8 -Compress) -cne ($b.$key | ConvertTo-Json -Depth 8 -Compress)){return $false}
 }
 return $true
}
function ObservePromptEvidence($name,$stage,$profile,$exeName,$inspectHandle,$uri) {
 $a=ObservationSnapshot $profile $exeName $inspectHandle $uri
 if(-not $a.eligible){Diagnostic $name $stage @{identity=$a;emitted=$false};return $false}
 $bitmap=$null;$graphics=$null;$stream=$null
 try {
  # Full native inspect rectangle, containing the complete verified owned foreground popup.
  # No desktop clip, resize, focus change or coordinates used for input. Screen pixels are
  # still not an occlusion proof; executor and main must inspect original PNGs.
  $r=$a.parentRect;$width=$r[2]-$r[0];$height=$r[3]-$r[1]
  $bitmap=[Drawing.Bitmap]::new($width,$height);$graphics=[Drawing.Graphics]::FromImage($bitmap);$stream=[IO.MemoryStream]::new()
  $graphics.CopyFromScreen($r[0],$r[1],0,0,$bitmap.Size)
  $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png);$bytes=$stream.ToArray()
  if($bytes.Length -gt 262144){throw 'Observation PNG cap exceeded'}
  # Nothing is persisted/emitted before independent fresh post-capture identity agreement.
  $b=ObservationSnapshot $profile $exeName $inspectHandle $uri
  if(-not(SameObservationIdentity $a $b)){Diagnostic $name $stage @{before=$a;after=$b;emitted=$false};return $false}
  $w=[Windows.Automation.AutomationElement]::FromHandle([IntPtr]$a.foreground)
  $tree=Structure $w
  $c=ObservationSnapshot $profile $exeName $inspectHandle $uri
  if(-not(SameObservationIdentity $b $c) -or $tree.truncated){Diagnostic $name $stage @{identity=$c;truncated=$tree.truncated;emitted=$false};return $false}
  Diagnostic $name $stage @{identity=$c;truncated=$false;nodes=$tree.nodes;emitted=$true}
  $key="$name-$stage";$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
  [IO.File]::WriteAllBytes((Join-Path $OutDir "$key.png"),$bytes)
  Write-Host "OBS_PNG_BEGIN $key $($bytes.Length) $hash $width $height"
  $base64=[Convert]::ToBase64String($bytes)
  for($i=0;$i -lt $base64.Length;$i+=3000){Write-Host ("OBS_PNG_CHUNK $key "+$base64.Substring($i,[Math]::Min(3000,$base64.Length-$i)))}
  Write-Host "OBS_PNG_END $key"
  Record $name $stage 'INFO' (@{bytes=$bytes.Length;sha256=$hash;width=$width;height=$height;identity=$c;note='Native-owned foreground and full parent-window screen pixels only; not consent/metadata/occlusion proof'} | ConvertTo-Json -Depth 12 -Compress)
  return [pscustomobject]@{identity=$c}
 }catch{Diagnostic $name $stage @{errorType=$_.Exception.GetType().FullName;emitted=$false};return $false}
 finally{if($stream){$stream.Dispose()};if($graphics){$graphics.Dispose()};if($bitmap){$bitmap.Dispose()}}
}
function ConsentTreeAllowed($tree,$identity,$name) {
 if($tree.truncated -or -not $identity.eligible){return $false}
 $nodes=@($tree.nodes)
 if(-not $nodes.Count -or $nodes[0].name -cne 'Allow remote debugging?' -or $nodes[0].hwnd -ne $identity.foreground -or $nodes[0].pid -ne $identity.uiaPid){return $false}
 $allow=@($nodes | Where-Object {$_.type -ceq 'ControlType.Button' -and $_.name -ceq 'Allow'})
 $cancel=@($nodes | Where-Object {$_.type -ceq 'ControlType.Button' -and $_.name -ceq 'Cancel'})
 if($allow.Count -ne 1 -or $cancel.Count -ne 1){return $false}
 foreach($node in @($allow[0],$cancel[0])){
  if($node.pid -ne $identity.uiaPid -or -not $node.enabled -or $node.offscreen -or $node.patterns -notcontains 'InvokePatternIdentifiers.Pattern'){return $false}
  $r=$node.bounds;$p=$identity.popupRect
  if($r.Count -ne 4 -or @($r | Where-Object {-not [double]::IsFinite([double]$_)}).Count -or $r[2] -le 0 -or $r[3] -le 0 -or $r[0] -lt $p[0] -or $r[1] -lt $p[1] -or $r[0]+$r[2] -gt $p[2] -or $r[1]+$r[3] -gt $p[3]){return $false}
 }
 # Chrome exact wording seen sixth pixels+tree; Microsoft Edge exact wording seen fifth UIA only.
 # Fresh runtime attribution still mandatory; prior text never authenticates a new popup.
 $brand=if($name -ceq 'chrome'){'Chrome'}elseif($name -ceq 'edge'){'Microsoft Edge'}else{return $false}
 $warning="An external app wants full control over this $brand session to debug it. This includes access to your saved data, cookies and site data, and the ability to navigate to any URL."
 if(@($nodes | Where-Object {$_.type -ceq 'ControlType.Text' -and $_.name -ceq $warning -and $_.pid -eq $identity.uiaPid -and $_.enabled -and -not $_.offscreen}).Count -ne 1){return $false}
 return $true
}
function ConsentImagesMatch($onset,$end,$current) {
 return ($null -ne $onset -and $null -ne $end -and (SameObservationIdentity $onset $end) -and (SameObservationIdentity $end $current))
}
function ConsentHelperLive($process,$clock) {
 # Conservative local cutoff leaves nominal margin before the helper's 45s timeout.
 # Native/provider calls and cross-process races are not hard real-time guarantees.
 try { $process.Refresh(); return (-not $process.HasExited -and $clock.ElapsedMilliseconds -lt 40000) } catch { return $false }
}
function InvokeOwnedConsent($name,$profile,$exeName,$inspectHandle,$uri,$onsetIdentity,$endIdentity,$helper,$clock) {
 $a=ObservationSnapshot $profile $exeName $inspectHandle $uri
 if(-not(ConsentImagesMatch $onsetIdentity $endIdentity $a) -or -not(ConsentHelperLive $helper $clock)){return $false}
 $popup=[Windows.Automation.AutomationElement]::FromHandle([IntPtr]$a.foreground)
 $tree=Structure $popup
 if(-not(ConsentTreeAllowed $tree $a $name)){Diagnostic $name 'consent-refused' @{reason='exact known prompt tree not proven'};return $false}
 $buttons=@(Descendants $popup | Where-Object {$_.Current.ControlType -eq [Windows.Automation.ControlType]::Button -and $_.Current.Name -ceq 'Allow'})
 if($buttons.Count -ne 1){return $false}
 $button=$buttons[0];$node=@($tree.nodes | Where-Object {$_.type -ceq 'ControlType.Button' -and $_.name -ceq 'Allow'})[0]
 if(($button.GetRuntimeId() | ConvertTo-Json -Compress) -cne ($node.runtimeId | ConvertTo-Json -Compress) -or $button.Current.ProcessId -ne $a.uiaPid -or -not $button.Current.IsEnabled -or $button.Current.IsOffscreen){return $false}
 $invoke=$button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)
 $b=ObservationSnapshot $profile $exeName $inspectHandle $uri
 if(-not(SameObservationIdentity $a $b)){return $false}
 # Fresh fulltree must exactly match before input, no cross-popup stale UIA handle.
 $tree2=Structure $popup
 if(-not(ConsentTreeAllowed $tree2 $b $name) -or ($tree.nodes | ConvertTo-Json -Depth 12 -Compress) -cne ($tree2.nodes | ConvertTo-Json -Depth 12 -Compress)){return $false}
 $c=ObservationSnapshot $profile $exeName $inspectHandle $uri
 if(-not(SameObservationIdentity $b $c) -or -not(ConsentImagesMatch $onsetIdentity $endIdentity $c)){return $false}
 Diagnostic $name 'consent-call' @{attempted=$true;returned=$false;identity=$c;buttonRuntimeId=$node.runtimeId}
 # Exactly ONE Invoke, no catch-and-retry, no focus/keyboard/coordinates.
 if(-not(ConsentHelperLive $helper $clock)){return $false}
 $invoke.Invoke()
 Diagnostic $name 'consent-return' @{returned=$true;buttonRuntimeId=$node.runtimeId}
 return [pscustomobject]@{invoked=$true;identity=$c;button=$node;root=$tree.nodes[0];warningMatched=$true;treeStable=$true;attempted=$true;returned=$true}
}
function StopRetainedHelper($helper,$retainedHandle) {
 # Never resolve a numeric PID during helper cleanup. An exited handle remains the original process.
 if($null -eq $helper -or $retainedHandle -eq [IntPtr]::Zero){throw 'No retained helper handle; no termination attempted'}
 if($helper.Handle -ne $retainedHandle){throw 'Helper handle changed; no termination attempted'}
 if(-not $helper.HasExited){$helper.Kill()}
 if(-not $helper.WaitForExit(5000)){throw 'Retained helper exit not confirmed'}
}
function ObserveSocketAttempt($name,$phase,$profile,$exeName,$inspectWindow) {
 $inspectHandle=[IntPtr]$inspectWindow.Current.NativeWindowHandle
 $eligible=@(Windows $profile $exeName | Where-Object {$_.Current.NativeWindowHandle -eq $inspectHandle})
 if($eligible.Count -ne 1 -or [ObserveNative]::GetForegroundWindow() -ne $inspectHandle){throw 'Attributed inspect foreground not retained before connection; no activation inside bracket'}
 $uri=if($name -ceq 'chrome'){'chrome://inspect/#remote-debugging'}else{'edge://inspect/#remote-debugging'}
 $result=Join-Path $OutDir "$name-$phase-result.json";$stdout=Join-Path $OutDir "$name-$phase-stdout.txt";$stderr=Join-Path $OutDir "$name-$phase-stderr.txt"
 $signal=Join-Path $OutDir "$name-invoked.json";$manifest=Join-Path $OutDir "$name-manifest.json"
 if(Test-Path $signal){throw 'Stale consent signal'}
 $nodePath=AssertReviewedRuntime
 $p=Microsoft.PowerShell.Management\Start-Process -FilePath $nodePath -ArgumentList @('"'+(Join-Path $PSScriptRoot 'consent-targets.mjs')+'"','"'+(Join-Path $profile 'DevToolsActivePort')+'"','"'+$result+'"','"'+$signal+'"','"'+$manifest+'"') -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
 $retainedHandle=[IntPtr]::Zero
 $onset=$false;$end=$false;$attempted=$false;$invoked=$false;$proof=$null;$clock=[Diagnostics.Stopwatch]::StartNew()
 try {
  $retainedHandle=$p.Handle
  if($retainedHandle -eq [IntPtr]::Zero){throw 'Helper process handle unavailable; no consent input'}
  for($i=0;$i -lt 193 -and $clock.ElapsedMilliseconds -le 48000;$i++){
   $s=ObservationSnapshot $profile $exeName $inspectHandle $uri
   if($s.eligible -and -not $attempted){
    $attempted=$true
    $onset=ObservePromptEvidence $name 'prompt-onset' $profile $exeName $inspectHandle $uri
    if($onset){$end=ObservePromptEvidence $name 'prompt-end' $profile $exeName $inspectHandle $uri}
    # Both fullparent pre-consent frames must pass unchanged strictattribution before input.
    if($onset -and $end){$proof=InvokeOwnedConsent $name $profile $exeName $inspectHandle $uri $onset.identity $end.identity $p $clock;$invoked=($null -ne $proof -and $proof -isnot [bool] -and $proof.invoked -eq $true)}
    if($invoked){
     $tempSignal="$signal.tmp"
     @{invoked=$true;kind='native-owned-single-Allow'} | ConvertTo-Json -Compress | Set-Content -Encoding utf8 $tempSignal
     Move-Item -LiteralPath $tempSignal -Destination $signal
    }else{break}
   }
   if($p.HasExited){break};Start-Sleep -Milliseconds 250
  }
  StopRetainedHelper $p $retainedHandle
  $p.Refresh();$exitCode=if($p.HasExited){$p.ExitCode}else{-1}
  $r=if(Test-Path $result){Get-Content -Raw $result | ConvertFrom-Json}else{[pscustomobject]@{state='no_result'}}
  [pscustomobject]@{result=$r;helperExit=$exitCode;promptSeen=[bool]($onset -and $end);decisionApplied=$invoked;decision='single-Allow';onsetEvidence=[bool]$onset;endEvidence=[bool]$end;inputAttempted=$attempted;consentProof=$proof}
 }finally{
  try{StopRetainedHelper $p $retainedHandle}catch{$script:helperCleanupUnconfirmed=$true;Record $name 'probe' 'UNKNOWN' ('Retained helper cleanup not confirmed: '+$_.Exception.Message)}
  finally{$p.Dispose()}
 }
}

try {
 Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing,System.Windows.Forms
 Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class ObserveNative { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); }'
 if(-not(Get-Command node.exe -ErrorAction SilentlyContinue)){throw 'Node unavailable'}

 Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PromptNative {
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint command);
 [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT rect);
}
'@
}catch{Record 'all' 'preflight' 'UNKNOWN' $_.Exception.Message;Record 'all' 'complete' 'INFO' 'Preflight incomplete';exit 2}
Record 'all' 'scope' 'INFO' 'C1 only: fresh default user-data roots in a pristine ephemeral runner account, normal launches, synthetic fixtures only. One exact owned Allow Invoke and ONE Target.getTargets per browser; no other protocol method, activation or selection input during bracket. No personal profile/product/release changes. Full gate remains NOT_PASS.'
$browsers=@(
 @{name='chrome';defaultRoot=(Join-Path $env:LOCALAPPDATA 'Google/Chrome/User Data');exe="${env:ProgramFiles}\Google\Chrome\Application\chrome.exe";versionUri='chrome://version';uri='chrome://inspect/#remote-debugging'},
 @{name='edge';defaultRoot=(Join-Path $env:LOCALAPPDATA 'Microsoft/Edge/User Data');exe="${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe";versionUri='edge://version';uri='edge://inspect/#remote-debugging'}
)
foreach($browser in $browsers) {
 $name=$browser.name;$exe=$browser.exe;$exeName=[IO.Path]::GetFileName($exe)
 $root=Join-Path $env:TEMP ('startapp-default-c1-'+$name+'-'+[Guid]::NewGuid().ToString('N'));$profile=[IO.Path]::GetFullPath($browser.defaultRoot)
 $script:ownership=$null;$defaultInitiallyAbsent=$false;$defaultCreated=$false
 $toggle=$null;$inspectHandle=[IntPtr]::Zero
 try {
  if(-not(Test-Path $exe)){Record $name 'version' 'UNAVAILABLE' 'Browser binary missing';continue}
  $version=(Get-Item $exe).VersionInfo.FileVersion;Record $name 'version' 'INFO' $version
  if([int]($version.Split('.')[0]) -lt 144){Record $name 'version-floor' 'UNAVAILABLE' 'Consent route conservative floor144';continue}
  # Hard refusal, never back up, copy, move or delete an existing default profile.
  if(Test-Path -LiteralPath $profile){Record $name 'default-session' 'UNKNOWN' 'Default user-data root already exists; untouched, no launch';continue}
  if(@(Get-CimInstance Win32_Process -Filter "Name='$exeName'").Count){Record $name 'default-session' 'UNKNOWN' 'Existing browser process; untouched, no launch';continue}
  $defaultInitiallyAbsent=$true
  $script:ownership=@{profile=$profile;exe=[IO.Path]::GetFullPath($exe);exeName=$exeName;known=@{}}
  New-Item -ItemType Directory $root -ErrorAction Stop | Out-Null
  [void](StartOwnedBrowser $exe @('--no-first-run','--no-default-browser-check','--window-position=0,0','--window-size=1000,680','--new-window','about:blank'))
  $defaultCreated=WaitUntil {[void]@(Processes $profile $exeName);Test-Path -LiteralPath $profile}
  if(-not $defaultCreated){throw 'Default root not created by normal launch'}

  if(-not(WaitUntil {@(Windows $profile $exeName).Count -eq 1})){Record $name 'navigation' 'UNKNOWN' 'Unique initial window not established';continue}
  # Read the browser-owned version page to bind its actual Profile Path, not merely a conventional path guess.
  $versionNav=NavigateInspect $profile $exeName $browser.versionUri
  if(-not $versionNav.confirmed){throw 'Exact internal version page unavailable; default-profile binding unproven'}
  $vw=@(Windows $profile $exeName);if($vw.Count -ne 1){throw 'Unique version window lost'}
  $profilePath=Join-Path $profile 'Default'
  $versionTexts=@(Descendants $vw[0] | Where-Object {$_.Current.ControlType -eq [Windows.Automation.ControlType]::Text})
  $pathHits=@($versionTexts | Where-Object {[StringComparer]::OrdinalIgnoreCase.Equals($_.Current.Name,$profilePath)})
  $labelHits=@($versionTexts | Where-Object {$_.Current.Name -ceq 'Profile Path'})
  if($pathHits.Count -ne 1 -or $labelHits.Count -ne 1){throw 'Exact browser version Profile Path label/value not uniquely proven; no alternate path or data copy'}
  $pathPid=$pathHits[0].Current.ProcessId;$allowedIds=@(Processes $profile $exeName | ForEach-Object {[int]$_.ProcessId})
  if($allowedIds -notcontains $pathPid -or $allowedIds -notcontains $labelHits[0].Current.ProcessId){throw 'Version Profile Path elements not in owned process closure'}
  Record $name 'default-session' 'INFO' (@{defaultRoot=$profile;profilePath=$profilePath;profilePathVerified=$true;versionUri=$browser.versionUri;initiallyAbsent=$true;normalLaunch=$true;customUserDataSwitch=$false;rootCount=$script:ownership.known.Count;note='Fresh unique browser-owned version Profile Path exact label/value plus launch birth/exe/ancestry; hostedServer synthetic only'} | ConvertTo-Json -Compress)
  $nav=NavigateInspect $profile $exeName $browser.uri
  Record $name 'navigation' $(if($nav.confirmed){'PASS'}else{'UNKNOWN'}) ($nav | ConvertTo-Json -Compress)
  $ws=@(Windows $profile $exeName);if($ws.Count -ne 1){throw 'Unique inspect window lost'};$inspect=$ws[0]
  [void](Picture $name 'inspect-before' $inspect)
  if(-not $nav.confirmed){continue}
  $boxes=@(Descendants $inspect | Where-Object {$_.Current.ControlType -eq [Windows.Automation.ControlType]::CheckBox -and $_.Current.AutomationId -eq 'remote-debugging-enabled' -and $_.Current.Name -ceq 'Allow remote debugging for this browser instance'})
  if($boxes.Count -ne 1){Record $name 'inspect-toggle' 'UNKNOWN' 'Unique verified-ID checkbox unavailable';continue}
  $toggle=$boxes[0].GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
  if($toggle.Current.ToggleState -ne [Windows.Automation.ToggleState]::Off){Record $name 'inspect-toggle' 'UNKNOWN' 'Fresh profile checkbox unexpectedly not Off';$toggle=$null;continue}
  $urls=@()
  foreach($letter in @('A','B','C')){$f=Join-Path $root "fixture-$letter.html";Set-Content -Encoding utf8 $f "<!doctype html><title>StartAppFixture$letter</title><h1>Fixture $letter</h1>";$urls+=([Uri]$f).AbsoluteUri}
  [void](StartOwnedBrowser $exe @('--new-window',$urls[0],$urls[1]))
  if(-not(WaitUntil {@(Selection $profile $exeName).Count -eq 2})){Record $name 'fixture' 'UNKNOWN' 'Initial two fixture tabs unavailable';continue}
  [void](StartOwnedBrowser $exe @('--new-window',$urls[2]))
  if(-not(WaitUntil {ValidSelection @(Selection $profile $exeName)})){Record $name 'fixture' 'UNKNOWN' 'Required Aselected/Binactive samewindow plus Cselected separatewindow not proven';continue}
  Record $name 'fixture' 'PASS' 'Aselected and Binactive in same fixture window; Cselected in separate fixture window'
  $initial=@(Selection $profile $exeName)
  if(-not(FixturePictures $name 'before' $profile $exeName $initial)){Record $name 'inspect-toggle' 'UNKNOWN' 'Required fixture-before evidence incomplete; enable and connection not attempted';continue}
  $inspectHandle=[IntPtr]$inspect.Current.NativeWindowHandle
  if(-not(ActivateSynthetic $name 'enable-activation' $profile $exeName $inspectHandle)){Record $name 'inspect-toggle' 'UNKNOWN' 'Inspect foreground unavailable; enable and connection not attempted';continue}
  $toggle=FreshInspectToggle $profile $exeName $inspectHandle $browser.uri
  $preState=$toggle.Current.ToggleState.ToString()
  Diagnostic $name 'enable-prestate' @{target=$inspectHandle.ToInt64();state=$preState}
  if($preState -cne 'Off'){Record $name 'inspect-toggle' 'UNKNOWN' 'Fresh enable prestate not Off; no toggle attempted';continue}
  if([ObserveNative]::GetForegroundWindow() -ne $inspectHandle){Record $name 'inspect-toggle' 'UNKNOWN' 'Foreground changed immediately before enable; no toggle attempted';continue}
  Diagnostic $name 'enable-call' @{attempted=$true;returned=$false;target=$inspectHandle.ToInt64()}
  try{$toggle.Toggle();Diagnostic $name 'enable-return' @{returned=$true}}
  catch{Diagnostic $name 'enable-exception' @{returned=$false;exceptionType=$_.Exception.GetType().FullName};throw}
  if(-not(WaitToggleState $name 'enable-state' $profile $exeName $inspectHandle $browser.uri 'On')){Record $name 'inspect-toggle' 'UNKNOWN' 'On not established by bounded fresh reads; see diagnostic states/errors';continue}
  Record $name 'inspect-toggle' 'PASS' 'One exact disposable-checkbox enable call returned and fresh On state observed; synthetic only'
  if(-not(WaitUntil {Test-Path (Join-Path $profile 'DevToolsActivePort')} 12)){Record $name 'endpoint' 'UNKNOWN' 'No synthetic endpoint file; no flag fallback';continue}
  Record $name 'endpoint' 'PASS' 'Endpoint exists in initially absent default-root for this recorded test-owned normal session'
  $manifest=@();for($i=0;$i -lt 3;$i++){$letter='ABC'[$i].ToString();$manifest+=[pscustomobject]@{label=$letter;title="StartAppFixture$letter";url=$urls[$i]}}
  $manifest | ConvertTo-Json -Compress | Set-Content -Encoding utf8 (Join-Path $OutDir "$name-manifest.json")
  $inspectHandle=[IntPtr]$inspect.Current.NativeWindowHandle
  if(-not [ObserveNative]::SetForegroundWindow($inspectHandle) -or [ObserveNative]::GetForegroundWindow() -ne $inspectHandle){throw 'Inspect foreground not proven before synthetic connection'}
  # Selection snapshots tightly bracket ONLY the connection/consent/enumeration attempt.
  $before=@(Selection $profile $exeName)
  $attempt=ObserveSocketAttempt $name 'first' $profile $exeName $inspect
  Record $name 'socket-observation' 'INFO' ($attempt | ConvertTo-Json -Depth 12 -Compress)
  Record $name 'prompt-observation' $(if($attempt.onsetEvidence -and $attempt.endEvidence){'INFO'}else{'UNKNOWN'}) 'Both frames pre-consent; bounded attribution refusal stops this browser without retry or relaxation'
  $after=@(Selection $profile $exeName)
  Record $name 'first-consent' $(if($attempt.promptSeen -and $attempt.decisionApplied){'PASS'}else{'UNKNOWN'}) 'One attributed Invoke returned only; not proof that transport opened or personal user consent'
  Record $name 'enumeration' $(if($attempt.decisionApplied -and $attempt.result.state -ceq 'metadata_collected' -and $attempt.helperExit -eq 0){'PASS'}else{'UNKNOWN'}) ($attempt | ConvertTo-Json -Depth 8 -Compress)
  $stable=(ValidSelection $before) -and (ValidSelection $after) -and ((CanonSelection $before) -ceq (CanonSelection $after))
  Record $name 'fixture-selection-unchanged' $(if($stable){'PASS'}elseif((ValidSelection $before) -and (ValidSelection $after)){'FAIL'}else{'UNKNOWN'}) (@{before=$before;after=$after} | ConvertTo-Json -Depth 7 -Compress)
  [void](FixturePictures $name 'after' $profile $exeName $after)

 }catch{Record $name 'probe' 'UNKNOWN' $_.Exception.Message}
 finally {
  if($null -ne $toggle){
   try{
    if($inspectHandle -eq [IntPtr]::Zero){$inspectHandle=[IntPtr]$inspect.Current.NativeWindowHandle}
    $toggle=FreshInspectToggle $profile $exeName $inspectHandle $browser.uri
    $preDisable=$toggle.Current.ToggleState.ToString()
    Diagnostic $name 'disable-prestate' @{state=$preDisable;target=$inspectHandle.ToInt64()}
    if($preDisable -ceq 'On'){
     if(-not(ActivateSynthetic $name 'disable-activation' $profile $exeName $inspectHandle)){throw 'Cleanup foreground unavailable; no disable input'}
     $toggle=FreshInspectToggle $profile $exeName $inspectHandle $browser.uri
     $freshDisable=$toggle.Current.ToggleState.ToString()
     Diagnostic $name 'disable-freshstate' @{state=$freshDisable}
     if($freshDisable -ceq 'On'){
      if([ObserveNative]::GetForegroundWindow() -ne $inspectHandle){throw 'Cleanup foreground changed; no disable input'}
      Diagnostic $name 'disable-call' @{attempted=$true;returned=$false}
      try{$toggle.Toggle();Diagnostic $name 'disable-return' @{returned=$true}}
      catch{Diagnostic $name 'disable-exception' @{returned=$false;exceptionType=$_.Exception.GetType().FullName};throw}
     }elseif($freshDisable -cne 'Off'){throw 'Cleanup fresh state ambiguous; no toggle input'}
    }elseif($preDisable -cne 'Off'){throw 'Cleanup prestate ambiguous; no toggle input'}
    $off=WaitToggleState $name 'disable-state' $profile $exeName $inspectHandle $browser.uri 'Off'
    Record $name 'disable' $(if($off){'PASS'}else{'UNKNOWN'}) 'Fresh bounded Off readback only, not transport enforcement proof'
   }catch{Record $name 'disable' 'UNKNOWN' $_.Exception.Message}
  }
  # Deliberately no claim that a generic post-disable error proves revocation.
  Record $name 'post-disable-unavailable' 'UNKNOWN' 'Not an enforcement proof; scoped diagnostic does not establish causal revocation'
  try {
   if($null -eq $script:ownership){throw 'No ownership context; no process or default-directory deletion attempted'}
   @(Processes $profile $exeName) | ForEach-Object {StopOwnedProcess $_}
   if(-not(WaitUntil {@(Processes $profile $exeName).Count -eq 0} 5)){throw 'Synthetic browser processes remain'}
   # Deliberately never recursively delete a default-root, even when initially absent.
   # Browser-created data belongs to disposable runner lifetime; preserving it avoids destructive default-profile cleanup.
   if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force}
   Record $name 'cleanup' 'PASS' 'Birth-bound test processes stopped and GUID fixture directory removed; default root left for ephemeral runner disposal, never deleted'
  }catch{Record $name 'cleanup' 'UNKNOWN' $_.Exception.Message}
  foreach($phase in @('version','navigation','inspect-before','fixture','before-window1','before-window2','inspect-toggle','endpoint','first-consent','enumeration','fixture-selection-unchanged','prompt-observation','socket-observation','after-window1','after-window2','reconnect-denied','disable','cleanup')){if(@($records | Where-Object {$_.browser -eq $name -and $_.step -eq $phase}).Count -eq 0){Record $name $phase 'UNKNOWN' 'Not reached or evidence unavailable'}}
  Record $name 'complete' 'INFO' 'C1 default-session diagnostic finished; lifecycle/reconnect/causalOff/consumerWin11 untested'
  $script:ownership=$null
 }
 if($script:helperCleanupUnconfirmed){break} # No second browser while helper closure is unproven.
}
Record 'all' 'complete' 'INFO' $(if($script:helperCleanupUnconfirmed){'Stopped remaining browser loop because helper cleanup unconfirmed; synthetic only'}else{'Both bounded loops finished; synthetic results only, no product implementation'})
if(@($records | Where-Object {$_.status -in @('UNKNOWN','UNAVAILABLE','FAIL')}).Count){exit 2}
exit 0
