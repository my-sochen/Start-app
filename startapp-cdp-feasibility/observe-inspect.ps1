# Observation-only synthetic inspect-page diagnostic. Local candidate, no run authority.
param([string]$OutDir='cdp-inspect-observe-out')
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
function Capture($browser,$window) {
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
  [IO.File]::WriteAllBytes((Join-Path $OutDir "$browser-inspect.png"),$bytes)
  Write-Host "OBS_PNG_BEGIN $browser $($bytes.Length) $hash $width $height"
  $base64=[Convert]::ToBase64String($bytes)
  for($i=0;$i -lt $base64.Length;$i+=3000){Write-Host ("OBS_PNG_CHUNK $browser "+$base64.Substring($i,[Math]::Min(3000,$base64.Length-$i)))}
  Write-Host "OBS_PNG_END $browser"
  [pscustomobject]@{bytes=$bytes.Length;sha256=$hash;width=$width;height=$height;note='Screen crop of attributed window, not proof against occlusion; actual pixels must be reviewed'}
 }finally{$stream.Dispose();$graphics.Dispose();$bitmap.Dispose()}
}
try {
 Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing,System.Windows.Forms
 Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class ObserveNative { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); }'
}catch{Record 'all' 'preflight' 'UNKNOWN' $_.Exception.Message;Record 'all' 'complete' 'INFO' 'Preflight incomplete';exit 2}
Record 'all' 'scope' 'INFO' 'Synthetic navigation+observation only; attributed omnibox ValuePattern/foreground/focus/one Enter, no consent/toggle/setting change/debugging connection or real-user proof. UIA and PNG repository-audience console evidence.'
$browsers=@(
 @{name='chrome';exe="${env:ProgramFiles}\Google\Chrome\Application\chrome.exe";uri='chrome://inspect/#remote-debugging'},
 @{name='edge';exe="${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe";uri='edge://inspect/#remote-debugging'}
)
foreach($browser in $browsers) {
 $name=$browser.name;$exe=$browser.exe;$exeName=[IO.Path]::GetFileName($exe)
 $root=Join-Path $env:TEMP ('startapp-inspect-observe-'+$name+'-'+[Guid]::NewGuid().ToString('N'))
 $profile=Join-Path $root 'profile';New-Item -ItemType Directory -Force $profile | Out-Null
 try {
  if(-not(Test-Path $exe)){Record $name 'version' 'UNAVAILABLE' 'Browser binary not found';continue}
  Record $name 'version' 'INFO' (Get-Item $exe).VersionInfo.FileVersion
  Start-Process $exe -ArgumentList @('--no-first-run','--no-default-browser-check',"--user-data-dir=`"$profile`"",'--window-position=0,0','--window-size=1280,900','--new-window','about:blank') | Out-Null
  if(-not(WaitUntil {@(Windows $profile $exeName).Count -eq 1})){Record $name 'window' 'UNKNOWN' 'Exactly one attributed window not established';continue}
  Record $name 'window' 'PASS' 'Exactly one top-level Window attributed to the unique disposable profile'
  try {
   $navigation=NavigateInspect $profile $exeName $browser.uri
   Record $name 'navigation' $(if($navigation.confirmed){'PASS'}else{'UNKNOWN'}) ($navigation | ConvertTo-Json -Compress)
  }catch{Record $name 'navigation' 'UNKNOWN' $_.Exception.Message}
  # Capture even on unknown navigation, so actual pixels diagnose the setup. No permission clicks.
  $documentReady=WaitUntil {
   $windows=@(Windows $profile $exeName);if($windows.Count -ne 1){return $false}
   $documents=$windows[0].FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Document))
   return $documents.Count -gt 0
  } 15
  Record $name 'document' $(if($documentReady){'PASS'}else{'UNKNOWN'}) 'UIA Document existence only, not inspect-page readiness or content proof'
  $windows=@(Windows $profile $exeName);if($windows.Count -ne 1){throw 'Attributed window count changed'}
  $tree=Structure $windows[0]
  $tree | ConvertTo-Json -Depth 10 | Set-Content -Encoding utf8 (Join-Path $OutDir "$name-uia.json")
  foreach($node in $tree.nodes){Write-Host ('OBS_UIA '+$name+' '+($node | ConvertTo-Json -Depth 7 -Compress))}
  $treeErrors=@($tree.nodes | Where-Object {$null -ne $_.PSObject.Properties['error']}).Count
  Record $name 'uia' $(if($tree.truncated -or $treeErrors){'UNKNOWN'}else{'PASS'}) (@{count=$tree.nodes.Count;truncated=$tree.truncated;errors=$treeErrors;note='Captured structure only; human review required'} | ConvertTo-Json -Compress)
  try {Record $name 'pixels' 'PASS' ((Capture $name $windows[0]) | ConvertTo-Json -Compress)}catch{Record $name 'pixels' 'UNKNOWN' $_.Exception.Message}
 }catch {Record $name 'observation' 'UNKNOWN' $_.Exception.Message}
 finally {
  try {
   @(Processes $profile $exeName) | ForEach-Object {$processId=[int]$_.ProcessId;try{Stop-Process -Id $processId -Force -ErrorAction Stop}catch{if(Get-Process -Id $processId -ErrorAction SilentlyContinue){throw}}}
   if(-not(WaitUntil {@(Processes $profile $exeName).Count -eq 0} 5)){throw 'Synthetic browser processes remain'}
   Remove-Item -LiteralPath $root -Recurse -Force
   Record $name 'cleanup' 'PASS' 'Unique synthetic browser processes stopped and temp directory deleted'
  }catch {Record $name 'cleanup' 'UNKNOWN' $_.Exception.Message}
  foreach($phase in @('version','window','navigation','document','uia','pixels','cleanup')){if(@($records | Where-Object {$_.browser -eq $name -and $_.step -eq $phase}).Count -eq 0){Record $name $phase 'UNKNOWN' 'Not reached'}}
  Record $name 'complete' 'INFO' 'Observation ended; evidence collection status is not browser feasibility proof'
 }
}
Record 'all' 'complete' 'INFO' 'Both bounded observation loops finished; actual pixels/UIA require inspection'
if(@($records | Where-Object {$_.status -in @('UNKNOWN','UNAVAILABLE','FAIL')}).Count){exit 2}
exit 0
