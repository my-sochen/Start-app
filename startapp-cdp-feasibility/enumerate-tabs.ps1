# Synthetic navigation, consent and metadata diagnostic. Local candidate, no run authority.
param([string]$OutDir='cdp-enumerate-out')
$ErrorActionPreference='Stop'
$OutDir=[IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force $OutDir | Out-Null
$records=[Collections.Generic.List[object]]::new()
function Record($browser,$step,$status,$detail) {
 $r=[pscustomobject]@{browser=$browser;step=$step;status=$status;detail=$detail;utc=[DateTime]::UtcNow.ToString('o')}
 $records.Add($r)
 Write-Host ('OBS_RECORD '+($r | ConvertTo-Json -Depth 8 -Compress))
 $records | ConvertTo-Json -Depth 10 | Set-Content -Encoding utf8 (Join-Path $OutDir 'assertions.json')
}
function Processes($profile,$exeName) {
 @(Get-CimInstance Win32_Process -Filter "Name='$exeName'" | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($profile)})
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
   $state=@{}
   if($patterns -contains 'TogglePatternIdentifiers.Pattern') {try{$state.toggle=$e.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState.ToString()}catch{}}
   $nodes.Add([pscustomobject]@{depth=$depth;name=$c.Name;type=$c.ControlType.ProgrammaticName;automationId=$c.AutomationId;class=$c.ClassName;framework=$c.FrameworkId;enabled=$c.IsEnabled;offscreen=$c.IsOffscreen;bounds=@($rect.X,$rect.Y,$rect.Width,$rect.Height);patterns=$patterns;state=$state})
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
   $items+=[pscustomobject]@{hwnd=$w.Current.NativeWindowHandle;title=$tab.Current.Name;selected=$sp.Current.IsSelected}
  }
 }
 @($items | Sort-Object hwnd,title)
}
function ValidSelection($items) {
 if($items.Count -ne 3 -or @($items.hwnd | Select-Object -Unique).Count -ne 2 -or @($items.title | ForEach-Object {($_ -split '\s')[0]} | Select-Object -Unique).Count -ne 3){return $false}
 foreach($g in ($items | Group-Object hwnd)){if(@($g.Group | Where-Object {$_.selected -eq $true}).Count -ne 1){return $false}}
 return $true
}
function Picture($name,$phase,$window) {
 try {$d=Capture "$name-$phase" $window;Record $name $phase 'PASS' ($d | ConvertTo-Json -Compress);return $true}
 catch {Record $name $phase 'UNKNOWN' $_.Exception.Message;return $false}
}
function FixturePictures($name,$stage,$profile,$exeName,$expected) {
 $all=@(Windows $profile $exeName);$ok=$true
 $handles=@($expected.hwnd | Sort-Object -Unique)
 if($handles.Count -ne 2){return $false}
 for($i=0;$i -lt $handles.Count;$i++){
  $w=@($all | Where-Object {$_.Current.NativeWindowHandle -eq $handles[$i]})
  if($w.Count -ne 1){return $false}
  $h=[IntPtr]$handles[$i]
  if(-not [ObserveNative]::SetForegroundWindow($h) -or [ObserveNative]::GetForegroundWindow() -ne $h){Record $name "$stage-window$($i+1)" 'UNKNOWN' 'Synthetic fixture window foreground not verified';$ok=$false;continue}
  if(-not(Picture $name "$stage-window$($i+1)" $w[0])){$ok=$false}
 }
 return $ok
}
function SocketAttempt($name,$phase,$profile,$exeName,$inspectWindow,$decision) {
 # Both initial and reconnect attempts must expose the attributed inspect window.
 $inspectHandle=[IntPtr]$inspectWindow.Current.NativeWindowHandle
 $eligible=@(Windows $profile $exeName | Where-Object {$_.Current.NativeWindowHandle -eq $inspectHandle})
 if($eligible.Count -ne 1 -or -not [ObserveNative]::SetForegroundWindow($inspectHandle) -or [ObserveNative]::GetForegroundWindow() -ne $inspectHandle){throw 'Attributed inspect foreground not established before socket attempt'}
 $result=Join-Path $OutDir "$name-$phase-result.json"
 $p=Start-Process 'node.exe' -ArgumentList @('"'+(Join-Path $PSScriptRoot 'read-targets.mjs')+'"','"'+(Join-Path $profile 'DevToolsActivePort')+'"','"'+$result+'"') -PassThru -RedirectStandardOutput (Join-Path $OutDir "$name-$phase-stdout.txt") -RedirectStandardError (Join-Path $OutDir "$name-$phase-stderr.txt")
 $prompt=$false;$acted=$false;$until=[DateTime]::UtcNow.AddSeconds(38)
 try {
  do {
   $dialogs=@()
   foreach($w in (Windows $profile $exeName)) {
    foreach($d in @($w)+@(Descendants $w)) {
     if($d.Current.ControlType -ne [Windows.Automation.ControlType]::Window -or $d.Current.Name -notmatch '(?i)^Allow remote debugging(?:\?| connection\?| session\?)?$'){continue}
     try {if(-not $d.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Current.IsModal){continue}}catch{continue}
     $els=@(Descendants $d)
     if(@($els | Where-Object {$_.Current.ControlType -eq [Windows.Automation.ControlType]::CheckBox -or $_.Current.ControlType -eq [Windows.Automation.ControlType]::Document -or $_.Current.Name -match '(?i)Address and search bar|allow remote debugging for this browser instance'}).Count){continue}
     $choices=if($decision -eq 'allow'){@('Allow')}else{@('Cancel','Deny','Block',"Don't allow")}
     $buttons=@($els | Where-Object {$_.Current.ControlType -eq [Windows.Automation.ControlType]::Button -and $choices -contains $_.Current.Name})
     $dialogs+=[pscustomobject]@{dialog=$d;buttons=$buttons}
    }
   }
   if($dialogs.Count -gt 1){break}
   if($dialogs.Count -eq 1 -and -not $acted){
    if($dialogs[0].buttons.Count -ne 1){break}
    $prompt=$true
    if([ObserveNative]::GetForegroundWindow() -ne $inspectHandle){break}
    if(-not(Picture $name "$phase-prompt" $inspectWindow)){break}
    # Synthetic disposable-profile modal only, never inspect-page controls.
    if([ObserveNative]::GetForegroundWindow() -ne $inspectHandle){break}
    $dialogs[0].buttons[0].GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();$acted=$true
   }
   if($p.HasExited){break};Start-Sleep -Milliseconds 250
  }while([DateTime]::UtcNow -lt $until)
  if(-not $p.HasExited){try{Stop-Process -Id $p.Id -ErrorAction Stop}catch{if(-not $p.HasExited){throw}}}
  $p.Refresh();$exitCode=if($p.HasExited){$p.ExitCode}else{-1}
  $r=if(Test-Path $result){Get-Content -Raw $result | ConvertFrom-Json}else{[pscustomobject]@{state='no_result'}}
  if(-not $acted){
   if([ObserveNative]::GetForegroundWindow() -eq $inspectHandle){[void](Picture $name "$phase-unattributed" $inspectWindow)}else{Record $name "$phase-unattributed" 'UNKNOWN' 'Inspect is not foreground; no misleading screen crop emitted'}
   $tree=Structure $inspectWindow
   foreach($node in $tree.nodes){Write-Host ('OBS_UIA '+$name+'-'+$phase+' '+($node | ConvertTo-Json -Depth 7 -Compress))}
  }
  [pscustomobject]@{result=$r;helperExit=$exitCode;promptSeen=$prompt;decisionApplied=$acted;decision=$decision}
 }finally{try{if(-not $p.HasExited){Stop-Process -Id $p.Id -ErrorAction Stop}}catch{Write-Warning 'Synthetic helper cleanup not confirmed'}}
}
try {
 Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing,System.Windows.Forms
 Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class ObserveNative { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); }'
 if(-not(Get-Command node.exe -ErrorAction SilentlyContinue)){throw 'Node unavailable'}
}catch{Record 'all' 'preflight' 'UNKNOWN' $_.Exception.Message;Record 'all' 'complete' 'INFO' 'Preflight incomplete';exit 2}
Record 'all' 'scope' 'INFO' 'Fresh synthetic profiles only: bounded navigation input, exact inspect checkbox toggle, strict modal simulated allow/deny, helper Target.getTargets only. No personal profile/product/registry/policy/release. UNKNOWN is not PASS.'
$browsers=@(
 @{name='chrome';exe="${env:ProgramFiles}\Google\Chrome\Application\chrome.exe";uri='chrome://inspect/#remote-debugging'},
 @{name='edge';exe="${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe";uri='edge://inspect/#remote-debugging'}
)
foreach($browser in $browsers) {
 $name=$browser.name;$exe=$browser.exe;$exeName=[IO.Path]::GetFileName($exe)
 $root=Join-Path $env:TEMP ('startapp-enumerate-'+$name+'-'+[Guid]::NewGuid().ToString('N'));$profile=Join-Path $root 'profile'
 New-Item -ItemType Directory -Force $profile | Out-Null
 $toggle=$null
 try {
  if(-not(Test-Path $exe)){Record $name 'version' 'UNAVAILABLE' 'Browser binary missing';continue}
  $version=(Get-Item $exe).VersionInfo.FileVersion;Record $name 'version' 'INFO' $version
  if([int]($version.Split('.')[0]) -lt 144){Record $name 'version-floor' 'UNAVAILABLE' 'Consent route conservative floor144';continue}
  Start-Process $exe -ArgumentList @('--no-first-run','--no-default-browser-check',"--user-data-dir=`"$profile`"",'--window-position=0,0','--window-size=1000,680','--new-window','about:blank') | Out-Null
  if(-not(WaitUntil {@(Windows $profile $exeName).Count -eq 1})){Record $name 'navigation' 'UNKNOWN' 'Unique initial window not established';continue}
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
  Start-Process $exe -ArgumentList @("--user-data-dir=`"$profile`"",'--new-window',$urls[0],$urls[1]) | Out-Null
  if(-not(WaitUntil {@(Selection $profile $exeName).Count -eq 2})){Record $name 'fixture' 'UNKNOWN' 'Initial two fixture tabs unavailable';continue}
  Start-Process $exe -ArgumentList @("--user-data-dir=`"$profile`"",'--new-window',$urls[2]) | Out-Null
  if(-not(WaitUntil {ValidSelection @(Selection $profile $exeName)})){Record $name 'fixture' 'UNKNOWN' '3tabs/2windows/exactly one selected each not proven';continue}
  Record $name 'fixture' 'PASS' 'Three synthetic tabs across two fixture windows; one selected per window'
  $initial=@(Selection $profile $exeName)
  [void](FixturePictures $name 'before' $profile $exeName $initial)
  $toggle.Toggle()
  if($toggle.Current.ToggleState -ne [Windows.Automation.ToggleState]::On){Record $name 'inspect-toggle' 'UNKNOWN' 'On readback failed';continue}
  Record $name 'inspect-toggle' 'PASS' 'Exact verified inspect checkbox toggled On in disposable profile only'
  if(-not(WaitUntil {Test-Path (Join-Path $profile 'DevToolsActivePort')} 12)){Record $name 'endpoint' 'UNKNOWN' 'No synthetic endpoint file; no flag fallback';continue}
  Record $name 'endpoint' 'PASS' 'Endpoint exists in unique disposable profile'
  $inspectHandle=[IntPtr]$inspect.Current.NativeWindowHandle
  if(-not [ObserveNative]::SetForegroundWindow($inspectHandle) -or [ObserveNative]::GetForegroundWindow() -ne $inspectHandle){throw 'Inspect foreground not proven before synthetic connection'}
  # Selection snapshots tightly bracket ONLY the connection/consent/enumeration attempt.
  $before=@(Selection $profile $exeName)
  $attempt=SocketAttempt $name 'first' $profile $exeName $inspect 'allow'
  $after=@(Selection $profile $exeName)
  Record $name 'first-consent' $(if($attempt.promptSeen -and $attempt.decisionApplied){'PASS'}else{'UNKNOWN'}) 'Strict attributed synthetic modal action only; never real-user consent proof'
  $targets=@($attempt.result.targets | Where-Object {$_.url -cin $urls})
  $matched=$attempt.helperExit -eq 0 -and $attempt.result.state -eq 'enumerated' -and $targets.Count -eq 3 -and @($targets.url | Select-Object -Unique).Count -eq 3 -and @($targets.title | Select-Object -Unique).Count -eq 3
  Record $name 'enumeration' $(if($matched){'PASS'}else{'UNKNOWN'}) ($attempt | ConvertTo-Json -Depth 8 -Compress)
  $stable=(ValidSelection $before) -and (ValidSelection $after) -and (($before | ConvertTo-Json -Compress) -ceq ($after | ConvertTo-Json -Compress))
  Record $name 'fixture-selection-unchanged' $(if($stable){'PASS'}elseif((ValidSelection $before) -and (ValidSelection $after)){'FAIL'}else{'UNKNOWN'}) (@{before=$before;after=$after} | ConvertTo-Json -Depth 7 -Compress)
  [void](FixturePictures $name 'after' $profile $exeName $after)
  if($attempt.result.state -eq 'enumerated'){
   $deny=SocketAttempt $name 'reconnect-deny' $profile $exeName $inspect 'deny'
   Record $name 'reconnect-denied' $(if($deny.promptSeen -and $deny.decisionApplied -and $deny.helperExit -eq 7 -and $deny.result.state -eq 'closed' -and $deny.result.clean -eq $true){'PASS'}else{'UNKNOWN'}) ($deny | ConvertTo-Json -Depth 8 -Compress)
  }
 }catch{Record $name 'probe' 'UNKNOWN' $_.Exception.Message}
 finally {
  if($null -ne $toggle){try{if($toggle.Current.ToggleState -eq [Windows.Automation.ToggleState]::On){$toggle.Toggle()};Record $name 'disable' $(if($toggle.Current.ToggleState -eq [Windows.Automation.ToggleState]::Off){'PASS'}else{'UNKNOWN'}) 'Fresh Off readback, not transport enforcement proof'}catch{Record $name 'disable' 'UNKNOWN' $_.Exception.Message}}
  # Deliberately no claim that a generic post-disable error proves revocation.
  Record $name 'post-disable-unavailable' 'UNKNOWN' 'Not an enforcement proof; scoped diagnostic does not establish causal revocation'
  try {
   @(Processes $profile $exeName) | ForEach-Object {$processId=[int]$_.ProcessId;try{Stop-Process -Id $processId -Force -ErrorAction Stop}catch{if(Get-Process -Id $processId -ErrorAction SilentlyContinue){throw}}}
   if(-not(WaitUntil {@(Processes $profile $exeName).Count -eq 0} 5)){throw 'Synthetic browser processes remain'}
   Remove-Item -LiteralPath $root -Recurse -Force;Record $name 'cleanup' 'PASS' 'Only unique test-profile processes/temp directory removed'
  }catch{Record $name 'cleanup' 'UNKNOWN' $_.Exception.Message}
  foreach($phase in @('version','navigation','inspect-before','fixture','before-window1','before-window2','inspect-toggle','endpoint','first-consent','enumeration','fixture-selection-unchanged','after-window1','after-window2','reconnect-denied','disable','cleanup')){if(@($records | Where-Object {$_.browser -eq $name -and $_.step -eq $phase}).Count -eq 0){Record $name $phase 'UNKNOWN' 'Not reached or evidence unavailable'}}
  Record $name 'complete' 'INFO' 'Bounded browser diagnostic finished; unknown remains inconclusive'
 }
}
Record 'all' 'complete' 'INFO' 'Both bounded loops finished; synthetic results only, no product implementation'
if(@($records | Where-Object {$_.status -in @('UNKNOWN','UNAVAILABLE','FAIL')}).Count){exit 2}
exit 0
