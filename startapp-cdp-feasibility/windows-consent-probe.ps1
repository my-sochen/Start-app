# Architecture diagnostic. Use only on a fresh hosted Windows runner.
# No policy, registry, default-profile, app-code, account, release or store writes.
param([string]$OutDir='cdp-feasibility-out')
$ErrorActionPreference='Stop'
$OutDir=[IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force $OutDir | Out-Null
$records=[Collections.Generic.List[object]]::new()
function Record($browser,$step,$status,$detail) {
 $records.Add([pscustomobject]@{browser=$browser;step=$step;status=$status;detail=$detail;utc=[DateTime]::UtcNow.ToString('o')})
 $records | ConvertTo-Json -Depth 12 | Set-Content -Encoding utf8 (Join-Path $OutDir 'assertions.json')
 Write-Host "$browser | $step | $status | $detail"
}
function Shot($name) {
 $r=[Windows.Forms.SystemInformation]::VirtualScreen
 $b=[Drawing.Bitmap]::new($r.Width,$r.Height);$g=[Drawing.Graphics]::FromImage($b)
 try {$g.CopyFromScreen($r.Left,$r.Top,0,0,$b.Size);$b.Save((Join-Path $OutDir "$name.png"),[Drawing.Imaging.ImageFormat]::Png)} finally {$g.Dispose();$b.Dispose()}
}
function TestProcesses($profile,$exeName) {
 @(Get-CimInstance Win32_Process -Filter "Name='$exeName'" | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($profile)})
}
function TestWindows($profile,$exeName) {
 $ids=@(TestProcesses $profile $exeName | ForEach-Object {[int]$_.ProcessId})
 @([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.Condition]::TrueCondition) | Where-Object {$ids -contains $_.Current.ProcessId})
}
function Descendants($w) { @($w.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)) }
function SelectedFixtures($profile,$exeName) {
 $r=@()
 foreach($w in (TestWindows $profile $exeName)) {
  $tabs=@(Descendants $w | Where-Object {$_.Current.ControlType -eq [Windows.Automation.ControlType]::TabItem})
  foreach($t in $tabs) {
   if($t.Current.Name -notmatch 'StartAppFixture[ABC]'){continue}
   try {$p=$t.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern);$r+= [pscustomobject]@{hwnd=$w.Current.NativeWindowHandle;title=$t.Current.Name;selected=$p.Current.IsSelected}} catch {throw 'Fixture tab selection state unavailable'}
  }
 }
 @($r | Sort-Object hwnd,title)
}
function Wait-Until([scriptblock]$condition,[int]$seconds=15) {
 $until=[DateTime]::UtcNow.AddSeconds($seconds)
 do {if (& $condition){return $true};Start-Sleep -Milliseconds 250}while([DateTime]::UtcNow -lt $until)
 return $false
}
function ProbeSocket($name,$phase,$profile,$exeName,$decision) {
 $result=Join-Path $OutDir "$name-$phase-result.json"
 $stdout=Join-Path $OutDir "$name-$phase-stdout.txt";$stderr=Join-Path $OutDir "$name-$phase-stderr.txt"
 $helper=Join-Path $PSScriptRoot 'read-targets.mjs';$endpoint=Join-Path $profile 'DevToolsActivePort'
 $p=Start-Process -FilePath 'node.exe' -ArgumentList @('"'+$helper+'"','"'+$endpoint+'"','"'+$result+'"') -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
 $prompt=$false;$acted=$false;$until=[DateTime]::UtcNow.AddSeconds(38)
 try {
  do {
   # Never search the inspect/config page for action buttons. A prompt must
   # be a separately attributed modal Window with exact remote-debugging title.
   $dialogs=@()
   foreach($w in (TestWindows $profile $exeName)) {
    foreach($d in @($w)+@(Descendants $w)) {
     if($d.Current.ControlType -ne [Windows.Automation.ControlType]::Window){continue}
     if($d.Current.Name -notmatch '(?i)^Allow remote debugging(?:\?| connection\?| session\?)?$'){continue}
     try {$wp=$d.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern);if(-not $wp.Current.IsModal){continue}}catch{continue}
     $els=@(Descendants $d)
     if(@($els | Where-Object {$_.Current.ControlType -eq [Windows.Automation.ControlType]::CheckBox}).Count){continue}
     if(@($els | Where-Object {$_.Current.ControlType -eq [Windows.Automation.ControlType]::Document -or $_.Current.Name -match '(?i)Address and search bar|allow remote debugging for this browser instance'}).Count){continue}
     $choices=if($decision -eq 'allow'){@('Allow')}else{@('Cancel','Deny','Block',"Don't allow")}
     $buttons=@($els | Where-Object {$_.Current.ControlType -eq [Windows.Automation.ControlType]::Button -and $choices -contains $_.Current.Name})
     if($buttons.Count -eq 1){$dialogs+= [pscustomobject]@{dialog=$d;button=$buttons[0]}}
    }
   }
   if($dialogs.Count -gt 1){Shot "$name-$phase-ambiguous-prompt";break}
   if($dialogs.Count -eq 1 -and -not $acted) {
    $prompt=$true;Shot "$name-$phase-consent-before"
    $invoke=$dialogs[0].button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern);$invoke.Invoke();$acted=$true
   }
   if($p.HasExited){break};Start-Sleep -Milliseconds 250
  }while([DateTime]::UtcNow -lt $until)
  if(-not $p.HasExited){try {Stop-Process -Id $p.Id -ErrorAction Stop}catch{if(-not $p.HasExited){throw}};Record $name $phase 'UNKNOWN' 'Local diagnostic helper timed out'}
  if(Test-Path $result){$r=Get-Content -Raw $result | ConvertFrom-Json}else{$r=[pscustomobject]@{state='no_result'}}
  $p.Refresh();$exitCode=if($p.HasExited){$p.ExitCode}else{-1}
  [pscustomobject]@{result=$r;helperExit=$exitCode;promptSeen=$prompt;decisionApplied=$acted}
 } finally {try{if(-not $p.HasExited){Stop-Process -Id $p.Id -ErrorAction Stop}}catch{if(-not $p.HasExited){Write-Warning 'Synthetic helper cleanup not confirmed'}}}
}
try {
 Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing,System.Windows.Forms
 if(-not(Get-Command node.exe -ErrorAction SilentlyContinue)){throw 'Node executable unavailable'}
}catch {Record 'all' 'preflight' 'UNKNOWN' $_.Exception.Message;Record 'all' 'complete' 'INFO' 'Preflight incomplete';exit 2}
$environment=[pscustomobject]@{os=[Environment]::OSVersion.VersionString;ps=$PSVersionTable.PSVersion.ToString();node=(& node.exe --version);uiCulture=[Globalization.CultureInfo]::CurrentUICulture.Name;session=$env:SESSIONNAME}
$environment | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $OutDir 'environment.json')
$browsers=@(
 @{name='chrome';exe="${env:ProgramFiles}\Google\Chrome\Application\chrome.exe";inspect='chrome://inspect/#remote-debugging'},
 @{name='edge';exe="${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe";inspect='edge://inspect/#remote-debugging'}
)
foreach($browser in $browsers) {
 $name=$browser.name;$exe=$browser.exe;$exeName=[IO.Path]::GetFileName($exe)
 $root=Join-Path $env:TEMP ('startapp-cdp-isolated-'+$name+'-'+[Guid]::NewGuid().ToString('N'))
 $profile=Join-Path $root 'profile';New-Item -ItemType Directory -Force $profile | Out-Null
 try {
  if(-not(Test-Path $exe)){Record $name 'binary' 'UNAVAILABLE' $exe;continue}
  $version=(Get-Item $exe).VersionInfo.FileVersion
  Record $name 'version' 'INFO' $version
  if([int]($version.Split('.')[0]) -lt 144){Record $name 'version-floor' 'UNAVAILABLE' 'Conservative consent-probe floor 144, no install/downgrade';continue}
  $urls=@()
  foreach($letter in @('A','B','C')) {
   $f=Join-Path $root "fixture-$letter.html";Set-Content -Encoding utf8 $f "<!doctype html><title>StartAppFixture$letter</title><h1>Fixture $letter</h1>"
   $urls+=([Uri]$f).AbsoluteUri
  }
  # Explicitly NO --remote-debugging-port/pipe. Profile is temporary, not the user's.
  Start-Process $exe -ArgumentList @('--no-first-run','--no-default-browser-check',"--user-data-dir=`"$profile`"",'--new-window',$urls[0],$urls[1]) | Out-Null
  if(-not(Wait-Until { @(SelectedFixtures $profile $exeName).Count -ge 2 } 20)){Shot "$name-setup-unavailable";Record $name 'fixture' 'UNKNOWN' 'Two fixture tabs not accessible in isolated-profile UI';continue}
  Start-Process $exe -ArgumentList @("--user-data-dir=`"$profile`"",'--new-window',$urls[2]) | Out-Null
  if(-not(Wait-Until { @(SelectedFixtures $profile $exeName).Count -eq 3 } 15)){Shot "$name-setup-unavailable";Record $name 'fixture' 'UNKNOWN' 'Three fixture tabs not accessible';continue}
  $pre=@(SelectedFixtures $profile $exeName)
  if(@($pre.hwnd | Select-Object -Unique).Count -ne 2){Record $name 'fixture' 'UNKNOWN' 'Expected two distinct fixture windows not proven';continue}
  Record $name 'fixture' 'PASS' 'Three synthetic fixture tabs across two attributed windows'
  Shot "$name-fixtures-before"
  # Keep inspect UI in its own extra window; do not change fixture tab selection.
  Start-Process $exe -ArgumentList @("--user-data-dir=`"$profile`"",'--new-window',$browser.inspect) | Out-Null
  $script:toggle=$null
  $got=Wait-Until {
   foreach($w in (TestWindows $profile $exeName)) {
    foreach($e in (Descendants $w)) {
     if($e.Current.ControlType -eq [Windows.Automation.ControlType]::CheckBox -and $e.Current.Name -match '(?i)allow remote debugging for this browser instance'){$script:toggle=$e;return $true}
    }
   };return $false
  } 15
  Shot "$name-inspect-before"
  if(-not $got){Record $name 'inspect-toggle' 'UNKNOWN' 'Documented checkbox unavailable in isolated hosted UI; no flag fallback';continue}
  $tp=$script:toggle.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
  if($tp.Current.ToggleState -ne [Windows.Automation.ToggleState]::Off){Record $name 'inspect-toggle' 'UNKNOWN' 'Fresh isolated profile unexpectedly enabled already';continue}
  $tp.Toggle();Shot "$name-inspect-enabled"
  if($tp.Current.ToggleState -ne [Windows.Automation.ToggleState]::On){Record $name 'inspect-toggle' 'UNKNOWN' 'Toggle On not verified';continue}
  Record $name 'inspect-toggle' 'PASS' 'Synthetic inspect setting On verified'
  if(-not(Wait-Until {Test-Path (Join-Path $profile 'DevToolsActivePort')} 12)){Record $name 'endpoint' 'UNKNOWN' 'UI enabled but endpoint file not found; no debug flags substituted';continue}
  Record $name 'endpoint' 'PASS' 'Endpoint file found only in synthetic profile'
  $before=@(SelectedFixtures $profile $exeName)
  $r=ProbeSocket $name 'first' $profile $exeName 'allow'
  Record $name 'first-consent' $(if($r.promptSeen -and $r.decisionApplied){'PASS'}else{'UNKNOWN'}) 'Synthetic modal-only prompt attribution/action; not real-user consent'
  $after=@(SelectedFixtures $profile $exeName);Shot "$name-fixtures-after"
  $selectedStable=$before.Count -eq 3 -and $after.Count -eq 3 -and @($before.hwnd | Select-Object -Unique).Count -eq 2 -and (($before | ConvertTo-Json -Compress) -eq ($after | ConvertTo-Json -Compress))
  # Exact URI matches only. Case/8.3 canonicalization differences are UNKNOWN, not assumed coverage.
  $actual=@($r.result.targets | Where-Object {$_.url -in $urls})
  $match=$r.helperExit -eq 0 -and $r.result.state -eq 'enumerated' -and $actual.Count -eq 3 -and @($actual.url | Select-Object -Unique).Count -eq 3 -and @($actual.title | Where-Object {$_ -in @('StartAppFixtureA','StartAppFixtureB','StartAppFixtureC')} | Select-Object -Unique).Count -eq 3
  Record $name 'enumeration' $(if($match){'PASS'}else{'UNKNOWN'}) ($r | ConvertTo-Json -Depth 8 -Compress)
  Record $name 'fixture-selection-unchanged' $(if($selectedStable){'PASS'}else{'FAIL'}) (@{before=$before;after=$after} | ConvertTo-Json -Depth 6 -Compress)
  if($r.result.state -ne 'enumerated'){continue}
  $deny=ProbeSocket $name 'reconnect-deny' $profile $exeName 'deny'
  Record $name 'reconnect-denied' $(if($deny.promptSeen -and $deny.decisionApplied -and $deny.helperExit -eq 7 -and $deny.result.state -eq 'closed' -and $deny.result.clean -eq $true){'PASS'}else{'UNKNOWN'}) ($deny | ConvertTo-Json -Depth 8 -Compress)
  # Disable evidence must be current ToggleState Off; transport errors are
  # observations, NOT proof that the setting caused enforcement.
  try {
   if($tp.Current.ToggleState -eq [Windows.Automation.ToggleState]::On){$tp.Toggle()}
   $isOff=$tp.Current.ToggleState -eq [Windows.Automation.ToggleState]::Off
   Record $name 'disable' $(if($isOff){'PASS'}else{'UNKNOWN'}) 'Fresh isolated inspect ToggleState Off readback'
   if($isOff){
    $off=ProbeSocket $name 'after-disable' $profile $exeName 'deny'
    Record $name 'post-disable-unavailable' $(if($off.result.state -eq 'enumerated' -and $off.helperExit -eq 0){'FAIL'}else{'UNKNOWN'}) ($off | ConvertTo-Json -Depth 8 -Compress)
   }
  }catch {Record $name 'disable-error' 'UNKNOWN' $_.Exception.Message}

 }catch{Record $name 'probe' 'UNKNOWN' $_.Exception.Message;try{Shot "$name-exception"}catch{Write-Host $_}}
 finally {
  # Kill ONLY browser processes whose command line identifies our unique temp profile.
  @(TestProcesses $profile $exeName) | ForEach-Object {Stop-Process -Id $_.ProcessId -Force -ErrorAction Continue}
  $clean=Wait-Until { @(TestProcesses $profile $exeName).Count -eq 0 } 5
  try {if(-not $clean){throw 'Test browser processes remain'};Remove-Item -LiteralPath $root -Recurse -Force;Record $name 'cleanup' 'PASS' 'Stopped unique test-profile processes and deleted only their synthetic temp directory'}catch{Record $name 'cleanup' 'UNKNOWN' $_.Exception.Message}
  $required=@('version','fixture','inspect-toggle','endpoint','first-consent','enumeration','fixture-selection-unchanged','reconnect-denied','disable','post-disable-unavailable','cleanup')
  foreach($phase in $required){if(@($records | Where-Object {$_.browser -eq $name -and $_.step -eq $phase}).Count -eq 0){Record $name $phase 'UNKNOWN' 'Not reached or no attributable evidence'}}
  Record $name 'complete' 'INFO' 'Bounded browser diagnostic finished; required phases recorded, UNKNOWN remains inconclusive'
 }
}
Record 'all' 'scope' 'INFO' 'Diagnostic only. No Windows11/default-profile/multi-profile/private/sleeping-tab or Start-app integration proof. UNKNOWN is not PASS.'

Record 'all' 'complete' 'INFO' 'Both bounded browser loops finished'
if(@($records | Where-Object {$_.status -in @('FAIL','UNKNOWN','UNAVAILABLE')}).Count){exit 2}
exit 0
