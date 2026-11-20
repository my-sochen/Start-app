# Capture network diagnostic, test infrastructure only. Windows disposable runner required.
# Offline below means APP+BROWSER EGRESS BLOCKED, not whole-machine/adapter offline.
# The runner/control channel remains online. Never run on a user's desktop.
param([Parameter(Mandatory=$true)][string]$PackageDir,[Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows' -or $env:GITHUB_REPOSITORY -ne 'my-sochen/Start-app') { throw 'Disposable owned Windows CI only' }
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Windows.Forms,System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class CaptureNetNative {
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int n);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
'@
$A=[System.Windows.Automation.AutomationElement];$TS=[System.Windows.Automation.TreeScope];$CT=[System.Windows.Automation.ControlType];$PC=[System.Windows.Automation.PropertyCondition]
$Output=[IO.Path]::GetFullPath($Output);New-Item -ItemType Directory -Force $Output | Out-Null
$script:records=New-Object System.Collections.ArrayList
$script:rules=New-Object System.Collections.ArrayList
$script:app=$null;$script:browser=$null;$script:profile='';$script:exe='';$script:case='';$script:shot=0
$script:appExe=Join-Path ([IO.Path]::GetFullPath($PackageDir)) 'StartApp.App.exe'
if(-not(Test-Path $script:appExe)){throw 'Pinned packaged executable missing'}
$token=[Guid]::NewGuid().ToString('N');$started=[DateTime]::UtcNow
$fixtureBase='42d33fd341951225e22dbbdb009049cf2da28b88'
$script:fixtures=@(foreach($relative in @('README.md','.github/CODEOWNERS','StartApp.slnx')){
 $content=@(git show ($fixtureBase+':'+$relative)) -join "`n"
 if($LASTEXITCODE -ne 0 -or -not $content.Trim()){throw 'Pinned fixture source unavailable'}
 [pscustomobject]@{path=$relative;url=('https://raw.githubusercontent.com/my-sochen/Start-app/'+$fixtureBase+'/'+$relative);expected=(($content -replace '\s+',' ').Trim())}
})

function Record($name,$status,$details){
 [void]$script:records.Add([ordered]@{utc=[DateTime]::UtcNow.ToString('o');case=$script:case;name=$name;status=$status;details=$details})
 $script:records | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $Output 'results.json') -Encoding UTF8
 Write-Host ($status+' '+$script:case+' '+$name)
}
function Require($name,[bool]$ok,$details){Record $name $(if($ok){'PASS'}else{'BLOCKED'}) $details;if(-not $ok){throw ('FIXTURE_BLOCKED '+$name)}}
function Desc($root,$type){@($root.FindAll($TS::Descendants,(New-Object $PC($A::ControlTypeProperty,$type))))}
function Named($root,$type,$name){$root.FindFirst($TS::Descendants,(New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty,$type)),(New-Object $PC($A::NameProperty,$name)))))}
function Invoke($el){if($null -eq $el){throw 'UI control missing'};([System.Windows.Automation.InvokePattern]$el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()}
function AppWin {
 if($null -eq $script:app -or $script:app.HasExited){throw 'App exited'}
 $wins=@($A::RootElement.FindAll($TS::Children,(New-Object $PC($A::ProcessIdProperty,$script:app.Id))))
 if($wins.Count -eq 0){return $null};$wins | Select-Object -First 1
}
function OwnedBrowserProcesses {
 if(-not $script:profile){return @()}
 @(Get-CimInstance Win32_Process -Filter "Name='$([IO.Path]::GetFileName($script:exe))'" | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($script:profile)})
}
function BrowserWins {
 $ids=@(OwnedBrowserProcesses | ForEach-Object {[int]$_.ProcessId})
 if($ids.Count -eq 0){return @()}
 @($A::RootElement.FindAll($TS::Children,[System.Windows.Automation.Condition]::TrueCondition) | Where-Object {
  try{$ids -contains $_.Current.ProcessId -and $_.Current.ClassName -like 'Chrome_WidgetWin*' -and [CaptureNetNative]::IsWindowVisible([IntPtr]$_.Current.NativeWindowHandle)}catch{$false}
 })
}
function WaitUntil($name,[scriptblock]$check,[int]$seconds=20){
 $until=[DateTime]::UtcNow.AddSeconds($seconds)
 do{try{if(& $check){return $true}}catch{};Start-Sleep -Milliseconds 200}while([DateTime]::UtcNow -lt $until)
 return $false
}
function Shot($label){
 $script:shot++;$screen=[Windows.Forms.SystemInformation]::VirtualScreen
 $bmp=New-Object Drawing.Bitmap $screen.Width,$screen.Height;$g=[Drawing.Graphics]::FromImage($bmp)
 try{$g.CopyFromScreen($screen.Left,$screen.Top,0,0,$bmp.Size);$bmp.Save((Join-Path $Output ('{0:00}-{1}.png' -f $script:shot,$label)))}finally{$g.Dispose();$bmp.Dispose()}
}
function CleanupRules {
 foreach($n in @($script:rules)){Get-NetFirewallRule -Name $n -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction Stop}
 $script:rules.Clear()
}
function Offline {
 CleanupRules
 foreach($path in @($script:appExe,$script:exe)){
  $n='StartAppCaptureDiag-'+$token+'-'+$script:rules.Count
  New-NetFirewallRule -Name $n -DisplayName $n -Direction Outbound -Program $path -Action Block -Profile Any -Enabled True | Out-Null
  [void]$script:rules.Add($n)
 }
 $rules=@($script:rules | ForEach-Object {Get-NetFirewallRule -Name $_})
 Require 'app/browser-only firewall installed' ($rules.Count -eq 2 -and @($rules | Where-Object {$_.Enabled -ne 'True' -or $_.Action -ne 'Block'}).Count -eq 0) @{isolation='per-executable outbound block';wholeMachineOffline=$false;rules=$script:rules.ToArray();adapterUntouched=$true}
}
function Online {
 CleanupRules
 Require 'owned firewall rules removed' (@(Get-NetFirewallRule -Name ('StartAppCaptureDiag-'+$token+'-*') -ErrorAction SilentlyContinue).Count -eq 0) @{adapterUntouched=$true}
}
function StartBrowser {
 $script:profile=Join-Path $env:RUNNER_TEMP ('startapp-net-'+[Guid]::NewGuid().ToString('N'))
 $args=@('--new-window','--no-first-run','--no-default-browser-check','--disable-background-mode','--force-renderer-accessibility',('--user-data-dir="'+$script:profile+'"'))+@($script:fixtures | ForEach-Object {$_.url})
 $script:browser=Start-Process -FilePath $script:exe -ArgumentList $args -PassThru
 Require 'one owned browser window' (WaitUntil 'browser' {@(BrowserWins).Count -eq 1} 25) @{exe=$script:exe;profile=$script:profile;version=(Get-Item $script:exe).VersionInfo.FileVersion}
 $script:browserWindow=@(BrowserWins)[0]
 Require 'three native tabs' (WaitUntil 'tabs' {@(Desc $script:browserWindow $CT::TabItem).Count -eq 3} 15) @{}
}
function AddressControl {
 $edits=@(Desc $script:browserWindow $CT::Edit | Where-Object {$_.Current.ClassName.EndsWith('OmniboxViewViews',[StringComparison]::Ordinal)})
 if($edits.Count -ne 1){throw 'Missing/ambiguous owned omnibox'};$edits[0]
}
function SameProofUrl([string]$actual,[string]$requested) {
 try{
  $a=[uri]$actual;$r=[uri]$requested
  return $a.IsAbsoluteUri -and $a.Scheme -ceq 'https' -and $a.Host -ieq $r.Host -and $a.AbsolutePath -ceq $r.AbsolutePath -and $a.Query -ceq $r.Query
 }catch{return $false}
}
function ReadBrowserProof([string]$requested) {
 $address=([System.Windows.Automation.ValuePattern](AddressControl).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
 $selected=@(Desc $script:browserWindow $CT::TabItem | Where-Object {
  try{([System.Windows.Automation.SelectionItemPattern]$_.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Current.IsSelected}catch{$false}
 })
 if($selected.Count -ne 1){throw 'Selected-tab attribution ambiguous'}
 $windowBounds=$script:browserWindow.Current.BoundingRectangle
 $visible=@(Desc $script:browserWindow $CT::Document | Where-Object {
  $b=$_.Current.BoundingRectangle
  -not $_.Current.IsOffscreen -and -not $b.IsEmpty -and $b.Width -gt 0 -and $b.Height -gt 0 -and $b.IntersectsWith($windowBounds)
 })
 if($visible.Count -ne 1){throw ('Visible-document attribution ambiguous: '+$visible.Count)}
 $doc=$visible[0]
 # Chromium's document ValuePattern exposes that document's URL, independently of omnibox.
 # If unavailable (including on an error page), this proof stays BLOCKED, never title-derived.
 $documentUrl=([System.Windows.Automation.ValuePattern]$doc.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
 $body='';$problem=''
 try{$body=([System.Windows.Automation.TextPattern]$doc.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)).DocumentRange.GetText(20000)}catch{$problem=$_.Exception.Message}
 $names=@(Desc $doc $CT::Text | ForEach-Object {$_.Current.Name}) -join ' '
 return @{address=$address;documentUrl=$documentUrl;attributed=((SameProofUrl $address $requested) -and (SameProofUrl $documentUrl $requested));selectedTab=$selected[0].Current.Name;document=@{name=$doc.Current.Name;class=$doc.Current.ClassName;offscreen=$doc.Current.IsOffscreen;bounds=$doc.Current.BoundingRectangle.ToString();textPatternError=$problem;body=$body;textNames=$names};body=(($body+' '+$names) -replace '\s+',' ').Trim()}
}
function NavigateProbe([bool]$offline) {
 # Native browser network requests to pinned owner-repository files, never synthetic content.
 $tabs=@(Desc $script:browserWindow $CT::TabItem);$all=$true
 for($i=0;$i -lt 3;$i++){
  $fixture=$script:fixtures[$i]
  ([System.Windows.Automation.SelectionItemPattern]$tabs[$i].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
  [void][CaptureNetNative]::ShowWindow([IntPtr]$script:browserWindow.Current.NativeWindowHandle,9)
  [void][CaptureNetNative]::SetForegroundWindow([IntPtr]$script:browserWindow.Current.NativeWindowHandle)
  $edit=AddressControl;$edit.SetFocus()
  $url=$fixture.url+'?startapp_capture_diag='+$token+'-'+[Guid]::NewGuid().ToString('N')
  $script:lastProof=$null;$script:proofError=''
  [Windows.Forms.SendKeys]::SendWait('^l');[Windows.Forms.SendKeys]::SendWait($url);[Windows.Forms.SendKeys]::SendWait('{ENTER}')
  $ok=WaitUntil 'rendered browser network state' {
   try{
    $script:lastProof=ReadBrowserProof $url
    $addressMatches=$script:lastProof.attributed
    $hasNetworkError=$script:lastProof.body -match 'ERR_NETWORK_ACCESS_DENIED|ERR_INTERNET_DISCONNECTED|ERR_NETWORK_CHANGED|ERR_CONNECTION_[A-Z_]+'
    if($offline){$addressMatches -and $hasNetworkError}
    else{$addressMatches -and -not $hasNetworkError -and $script:lastProof.body.Contains($fixture.expected)}
   }catch{$script:proofError=$_.Exception.Message;return $false}
  } 15
  Record ('browser '+$(if($offline){'blocked'}else{'live response'})+' tab '+$i) $(if($ok){'PASS'}else{'BLOCKED'}) @{requestedUrl=$url;fixturePath=$fixture.path;expectedNormalizedBody=$fixture.expected;expectedOffline=$offline;proof=$script:lastProof;proofError=$script:proofError;test='unique-query native navigation, document-only actual selected visible document URL+UIA body, exact pinned source content'}
  Shot ('network-proof-'+$script:case+'-'+$(if($offline){'offline'}else{'online'})+'-tab'+$i)
  if(-not $ok){$all=$false}
 }
 Require 'browser network transition fixture proven' $all @{offline=$offline}
}
function StartAppOffline {
 $script:app=Start-Process -FilePath $script:appExe -WorkingDirectory (Split-Path $script:appExe) -PassThru
 Require 'published app starts under egress block' (WaitUntil 'app' {$null -ne (AppWin)} 20) @{pid=$script:app.Id;startedWithAppAndBrowserEgressBlocked=$true;wholeMachineOffline=$false}
}
function Scan($state,$network) {
 $before=@(BrowserWins | ForEach-Object {[ordered]@{hwnd=$_.Current.NativeWindowHandle;minimized=[CaptureNetNative]::IsIconic([IntPtr]$_.Current.NativeWindowHandle)}})
 $w=AppWin;[void][CaptureNetNative]::SetForegroundWindow([IntPtr]$w.Current.NativeWindowHandle)
 Invoke (Named $w $CT::Button 'Capture')
 $menu=$null
 $ready=WaitUntil 'capture menu' {
  $script:menu=$A::RootElement.FindFirst($TS::Descendants,(New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty,$CT::MenuItem)),(New-Object $PC($A::NameProperty,'סביבה חדשה')))))
  $null -ne $script:menu
 } 5
 Require 'new capture menu available' $ready @{}
 Invoke $script:menu
 $startedScan=WaitUntil 'visible scan start' {
  @(Desc (AppWin) $CT::Text | Where-Object {$_.Current.Name -eq 'סורק מה פתוח עכשיו...' -and -not $_.Current.IsOffscreen}).Count -gt 0
 } 3
 Require 'capture visibly started before completion polling' $startedScan @{}
 $ready=WaitUntil 'capture finished' {
  $aw=AppWin;$list=Named $aw $CT::List 'Open now list'
  $scanTexts=@(Desc $aw $CT::Text | Where-Object {$_.Current.Name -eq 'סורק מה פתוח עכשיו...' -and -not $_.Current.IsOffscreen})
  $null -ne $list -and $scanTexts.Count -eq 0 -and $null -ne (Named $aw $CT::Button 'Close capture')
 } 20
 Require 'capture settled' $ready @{}
 $w=AppWin;$list=Named $w $CT::List 'Open now list'
 $rows=@(Desc $list $CT::CheckBox | ForEach-Object {[ordered]@{label=$_.Current.Name;texts=@(Desc $_ $CT::Text | ForEach-Object {$_.Current.Name})}})
 $allText=@($rows | ForEach-Object {$_.texts -join ' '}) -join ' | '
 $fixturePaths=@($script:fixtures | ForEach-Object {([uri]$_.url).AbsolutePath});$found=@($fixturePaths | Where-Object {$allText.Contains($_)})
 $closed=$state -eq 'x-closed'
 $ok=if($closed){$found.Count -eq 0}else{$found.Count -eq 3}
 $after=@(BrowserWins | ForEach-Object {[ordered]@{hwnd=$_.Current.NativeWindowHandle;minimized=[CaptureNetNative]::IsIconic([IntPtr]$_.Current.NativeWindowHandle)}})
 $messages=@(Desc $w $CT::Text | ForEach-Object {$_.Current.Name} | Where-Object {$_ -match 'incomplete|failed|could not|too long|still active|scan stopped'})
 Record 'actual packaged UI capture' $(if($ok){'PASS'}else{'FAIL'}) @{state=$state;network=$network;expectedPaths=$(if($closed){@()}else{$fixturePaths});foundPaths=$found;rows=$rows;browserWindowsBefore=$before;browserWindowsAfter=$after;messages=$messages;foregroundAfter=[CaptureNetNative]::GetForegroundWindow().ToInt64()}
 Record 'browser executable shown as Program (observation, no fix)' 'OBSERVED' @{matches=@($rows | Where-Object {($_.texts -join ' ') -match '(?i)(msedge|chrome)\.exe'})}
 Record 'StartApp executable excluded (requirement check)' $(if($allText -match '(?i)StartApp\.App\.exe'){'FAIL'}else{'PASS'}) @{}
 if($state -eq 'minimized'){
  $stayed=@($after | Where-Object {-not $_.minimized}).Count -eq 0 -and $after.Count -eq 1
  Record 'browser remains minimized (desired behavior)' $(if($stayed){'PASS'}else{'FAIL'}) @{before=$before;after=$after}
 }
 Shot ('capture-'+$script:case)
 Invoke (Named $w $CT::Button 'Close capture')
 Require 'capture closes without saving' (WaitUntil 'overview' {$null -ne (Named (AppWin) $CT::Button 'Capture') -and (Named (AppWin) $CT::Button 'Capture').Current.IsEnabled} 5) @{}
}
function CloseBrowserByX {
 foreach($w in @(BrowserWins)){
  # WindowPattern.Close is the native normal close operation, not process kill.
  ([System.Windows.Automation.WindowPattern]$w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).Close()
 }
 Require 'normal X-equivalent close leaves no browser window' (WaitUntil 'browser closed' {@(BrowserWins).Count -eq 0} 10) @{method='UI Automation WindowPattern.Close; no force terminate';remainingOwnedProcesses=@(OwnedBrowserProcesses | Select-Object ProcessId,Name)}
}
function StopOwned {
 if($null -ne $script:app){try{if(-not $script:app.HasExited){[void]$script:app.CloseMainWindow();if(-not $script:app.WaitForExit(3000)){$script:app.Kill()}}}catch{}; $script:app=$null}
 foreach($p in @(OwnedBrowserProcesses)){Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue}
}
try {
 $identity=[ordered]@{run=$env:GITHUB_RUN_ID;workflowSha=$env:GITHUB_SHA;base='42d33fd341951225e22dbbdb009049cf2da28b88';publishedCommit='8e55048afaef806a3d971469ab4de844cd45198e';zipSha256='f5084c0898bccb9e2e69943cdbf40fb41087c86c656b0a71c34896e2ce766a57';exeSha256=(Get-FileHash $script:appExe -Algorithm SHA256).Hash;appDllSha256=(Get-FileHash (Join-Path $PackageDir 'StartApp.App.dll') -Algorithm SHA256).Hash;isolation='app and browser per-executable outbound firewall, runner network online';wholeMachineOfflineTested=$false;privateUserProfilesUsed=$false}
 $identity | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Output 'identity.json') -Encoding UTF8
 $initialChromium=@($A::RootElement.FindAll($TS::Children,[System.Windows.Automation.Condition]::TrueCondition) | Where-Object {try{$_.Current.ClassName -like 'Chrome_WidgetWin*' -and [CaptureNetNative]::IsWindowVisible([IntPtr]$_.Current.NativeWindowHandle)}catch{$false}})
 Require 'clean disposable desktop has no preexisting Chromium window' ($initialChromium.Count -eq 0) @{count=$initialChromium.Count}
 $edge=Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'
 $chrome=Join-Path $env:ProgramFiles 'Google\Chrome\Application\chrome.exe'
 foreach($b in @(@{name='Edge';exe=$edge},@{name='Chrome';exe=$chrome})){
  if(-not(Test-Path $b.exe)){Record ($b.name+' installed') 'BLOCKED' @{};continue}
  $script:exe=$b.exe
  foreach($state in @('open','x-closed','minimized')){
   $script:case=$b.name+'-'+$state
   try{
    Online;StartBrowser;NavigateProbe $false
    if($state -eq 'x-closed'){CloseBrowserByX}
    Offline
    if($state -ne 'x-closed'){NavigateProbe $true}
    if($state -eq 'minimized'){[void][CaptureNetNative]::ShowWindow([IntPtr]$script:browserWindow.Current.NativeWindowHandle,6)}
    StartAppOffline
    Scan $state 'app-browser-offline'
    Online
    if($state -ne 'x-closed'){
     NavigateProbe $false
     if($state -eq 'minimized'){[void][CaptureNetNative]::ShowWindow([IntPtr]$script:browserWindow.Current.NativeWindowHandle,6)}
    }
    # Same app process as offline startup, no app restart, online scan directly follows connectivity verification.
    Scan $state 'restored-online-same-app-process'
   }catch{Record 'scenario could not finish' 'BLOCKED' @{error=$_.Exception.Message};try{Shot ('blocked-'+$script:case)}catch{}}
   finally{CleanupRules;StopOwned}
  }
 }
}finally{
 try{CleanupRules;Record 'all owned firewall rules removed in finally' 'PASS' @{remaining=@(Get-NetFirewallRule -Name ('StartAppCaptureDiag-'+$token+'-*') -ErrorAction SilentlyContinue).Count}}catch{Record 'firewall cleanup' 'FAIL' @{error=$_.Exception.Message}}
 StopOwned
 Record 'scope limitations' 'UNTESTED' @{wholeMachineOrAdapterOffline=$true;userMachine=$true;loginProfiles=$true;productionSourceModified=$false;startedUtc=$started.ToString('o');finishedUtc=[DateTime]::UtcNow.ToString('o')}
}
# Test failures are retained as real results. Distinguish failed product expectations from fixture blockers.
if(@($script:records | Where-Object {$_.status -eq 'FAIL' -or $_.status -eq 'BLOCKED'}).Count -gt 0){exit 1}
