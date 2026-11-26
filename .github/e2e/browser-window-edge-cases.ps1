$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows'){throw 'Disposable Windows runner required'}
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
$out=Join-Path $PWD 'browser-edge-cases';New-Item $out -ItemType Directory -Force|Out-Null
$checks=[Collections.Generic.List[object]]::new()
function Check($name,$ok){$checks.Add(@{name=$name;ok=[bool]$ok});if(-not $ok){throw $name}}
function Tabs($w){@($w.FindAll([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::TabItem)))|Where-Object {$_.Current.Name -match '^Probe-'})}
function Windows($profile){$ids=@(Get-CimInstance Win32_Process|Where-Object {$_.CommandLine -and $_.CommandLine.Contains($profile)}|ForEach-Object {$_.ProcessId});@([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.Condition]::TrueCondition)|Where-Object {$ids -contains $_.Current.ProcessId -and -not $_.Current.IsOffscreen -and (Tabs $_).Count -gt 0})}
function WaitWindows($profile,$count){$until=[DateTime]::UtcNow.AddSeconds(30);do{$ws=@(Windows $profile);if($ws.Count -eq $count){return $ws};Start-Sleep -Milliseconds 150}while([DateTime]::UtcNow -lt $until);throw "Expected $count windows, got $($ws.Count)"}
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
$pages=Join-Path $out 'pages';New-Item $pages -ItemType Directory -Force|Out-Null
foreach($name in @('Unrelated','C','A','B')){[IO.File]::WriteAllText((Join-Path $pages ($name+'.html')),("<html><head><title>Loading</title></head><body><h1>$name</h1><script>document.title='Probe-$name-'+Math.random().toString(36).slice(2)</script><input value='preserve unsaved state'></body></html>"))}
$server=Start-Process python -ArgumentList @('-m','http.server',"$port",'--bind','127.0.0.1','--directory',('"'+$pages+'"')) -PassThru -RedirectStandardOutput (Join-Path $out 'http-out.txt') -RedirectStandardError (Join-Path $out 'http-error.txt')
$until=[DateTime]::UtcNow.AddSeconds(10)
do{try{Invoke-WebRequest "http://127.0.0.1:$port/Unrelated.html" -UseBasicParsing|Out-Null;break}catch{if([DateTime]::UtcNow -gt $until){throw};Start-Sleep -Milliseconds 100}}while($true)

try{
 foreach($browser in @(@{Name='Chrome';Exe='C:\Program Files\Google\Chrome\Application\chrome.exe'},@{Name='Edge';Exe='C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'})){
  $profile=Join-Path $env:RUNNER_TEMP ('browser-edge-'+[guid]::NewGuid().ToString('N'))
  try{
   $helper=Join-Path $PWD 'tests/StartApp.BrowserWindowProbe/bin/Release/net10.0-windows/StartApp.BrowserWindowProbe.exe'
   & $helper $browser.Exe $profile "http://127.0.0.1:$port/Unrelated.html"
   Check "$($browser.Name) unrelated fixture started" ($LASTEXITCODE -eq 0)
   $first=@(WaitWindows $profile 1);$original=$first[0];$originalToken=(Tabs $original)[0].Current.Name
   foreach($iteration in 1..2){
    & $helper $browser.Exe $profile "http://127.0.0.1:$port/C.html?x=1&y=2" "http://127.0.0.1:$port/A.html#fragment" "http://127.0.0.1:$port/B.html"
    Check "$($browser.Name) product batch $iteration submitted" ($LASTEXITCODE -eq 0)
    $ws=@(WaitWindows $profile ($iteration+1))
    Check "$($browser.Name) unrelated tab state preserved $iteration" ((Tabs $original)[0].Current.Name -eq $originalToken)
    $new=@($ws|Where-Object {(Tabs $_).Count -eq 3})
    Check "$($browser.Name) dedicated windows remain separate $iteration" ($new.Count -eq $iteration)
    foreach($w in $new){$names=@((Tabs $w)|ForEach-Object {$_.Current.Name});Check "$($browser.Name) C A B order $iteration" ($names[0] -like 'Probe-C-*' -and $names[1] -like 'Probe-A-*' -and $names[2] -like 'Probe-B-*')}
   }
   $w=$new[-1];$r=$w.Current.BoundingRectangle;$b=New-Object Drawing.Bitmap([int]$r.Width,[int]$r.Height);$g=[Drawing.Graphics]::FromImage($b);$g.CopyFromScreen([int]$r.X,[int]$r.Y,0,0,$b.Size);$b.Save((Join-Path $out ($browser.Name+'.png')));$g.Dispose();$b.Dispose()
  }finally{@(Get-CimInstance Win32_Process|Where-Object {$_.Name -in @('chrome.exe','msedge.exe') -and $_.CommandLine -and $_.CommandLine.Contains($profile)})|ForEach-Object {Stop-Process -Id $_.ProcessId -Force -ErrorAction Continue}}
 }
}finally{if(-not $server.HasExited){$server|Stop-Process -Force};$checks|ConvertTo-Json -Depth 5|Set-Content (Join-Path $out 'checks.json')}
