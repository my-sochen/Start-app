param([Parameter(Mandatory)][string]$Source,[Parameter(Mandatory)][string]$Revision,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or -not $IsWindows){throw 'Disposable hosted Windows runner required'}
$Source=(Resolve-Path $Source).Path
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$Output=(Resolve-Path $Output).Path
Add-Type -AssemblyName System.Drawing,System.Windows.Forms
$started=Get-Date
$results=[Collections.Generic.List[object]]::new()
function Probe([string]$name,[string]$exe,[string]$cwd,[bool]$portable){
 $dir=Join-Path $Output $name;New-Item -ItemType Directory $dir | Out-Null
 $data=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'StartApp'
 # Each mode is a new process on this job's disposable runner, no other product launch allowed.
 if(Get-Process StartApp.App -ErrorAction SilentlyContinue){throw 'Unexpected existing product process; refusing interference'}
 if(Test-Path $data){Move-Item $data ($data+'-diagnostic-'+[guid]::NewGuid().ToString('N'))}
 New-Item -ItemType Directory $data | Out-Null
 $env:STARTAPP_CRASHLOG='1'
 $before=Get-Date
 $p=Start-Process -FilePath $exe -WorkingDirectory $cwd -PassThru -RedirectStandardOutput "$dir/stdout.txt" -RedirectStandardError "$dir/stderr.txt"
 $child=$null;$visible=$false;$window=0
 try{
  for($i=0;$i -lt 100;$i++){
   $p.Refresh()
   if($portable -and $null -eq $child){
    $row=@(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.Id)" | Where-Object {$_.Name -eq 'StartApp.App.exe'})
    if($row.Count -eq 1){try{$child=Get-Process -Id $row[0].ProcessId;[void]$child.Handle}catch{}}
   }
   $app=if($portable){$child}else{$p}
   if($null -ne $app){$app.Refresh();if($app.HasExited){break};if($app.MainWindowHandle -ne [IntPtr]::Zero){$visible=$true;$window=$app.MainWindowHandle.ToInt64();break}}
   if($p.HasExited -and $null -eq $child){break}
   Start-Sleep -Milliseconds 200
  }
  $app=if($portable){$child}else{$p}
  $appExit=$null;$appExited=$null
  if($null -ne $app){$app.Refresh();$appExited=$app.HasExited;if($appExited){$appExit=$app.ExitCode}}
  $snapshot=@(Get-CimInstance Win32_Process | Where-Object {$_.ProcessId -eq $p.Id -or $_.ParentProcessId -eq $p.Id} | Select-Object ProcessId,ParentProcessId,Name,ExecutablePath,CommandLine)
  $snapshot | ConvertTo-Json -Depth 5 | Set-Content "$dir/processes.json"
  if($visible){
   $bmp=[Drawing.Bitmap]::new([Windows.Forms.Screen]::PrimaryScreen.Bounds.Width,[Windows.Forms.Screen]::PrimaryScreen.Bounds.Height)
   $g=[Drawing.Graphics]::FromImage($bmp);try{$g.CopyFromScreen(0,0,0,0,$bmp.Size);$bmp.Save("$dir/desktop.png")}finally{$g.Dispose();$bmp.Dispose()}
  }
  $p.Refresh();$entryExited=$p.HasExited;$entryExit=if($entryExited){$p.ExitCode}else{$null}
  $result=[ordered]@{mode=$name;revision=$Revision;binary=$exe;entryPid=$p.Id;entryExited=$entryExited;entryExitCode=$entryExit;childPid=if($child){$child.Id}else{$null};appExited=$appExited;appExitCode=$appExit;hasWindow=$visible;windowHandle=$window;started=$before.ToUniversalTime().ToString('O');elapsedSeconds=((Get-Date)-$before).TotalSeconds}
  $results.Add($result);$result|ConvertTo-Json -Depth 5|Set-Content "$dir/result.json"
  if(Test-Path "$data/crash.log"){Copy-Item "$data/crash.log" "$dir/crash.log"}
  Start-Sleep -Seconds 2
  Get-WinEvent -FilterHashtable @{LogName='Application';StartTime=$before.AddSeconds(-1)} -ErrorAction SilentlyContinue | Where-Object {$_.ProviderName -match '\.NET Runtime|Application Error|Windows Error Reporting|SideBySide'} | Select-Object TimeCreated,Id,ProviderName,Message | ConvertTo-Json -Depth 5 | Set-Content "$dir/windows-errors.json"
 }finally{
  foreach($q in @($child,$p)){if($null -ne $q){try{if(-not $q.HasExited){$null=$q.CloseMainWindow();if(-not $q.WaitForExit(3000)){$q.Kill($true);$q.WaitForExit()}}}finally{$q.Dispose()}}}
 }
}
Push-Location $Source
try{
 if((git rev-parse HEAD) -cne $Revision){throw 'Source revision mismatch'}
 git status --porcelain | Set-Content "$Output/source-status.txt"
 dotnet --info | Set-Content "$Output/dotnet-info.txt"
 dotnet build src/StartApp.App/StartApp.App.csproj -c Release -p:StartAppReleaseVersion=0.1.0-proto.2 -p:StartAppSourceCommit=$Revision -nr:false *> "$Output/build.txt"
 if($LASTEXITCODE -ne 0){throw 'Build failed'}
 $bin=Join-Path $Source 'src/StartApp.App/bin/Release/net10.0-windows/StartApp.App.exe'
 Get-Content ([IO.Path]::ChangeExtension($bin,'runtimeconfig.json')) | Set-Content "$Output/direct-runtimeconfig.json"
 Probe 'direct-exact-source' $bin (Split-Path $bin) $false
 dotnet publish src/StartApp.App/StartApp.App.csproj -c Release -r win-x64 --self-contained true -p:ContinuousIntegrationBuild=true -p:StartAppReleaseVersion=0.1.0-proto.1 -p:StartAppSourceCommit=$Revision --artifacts-path diagnostic-artifacts -nr:false -o diagnostic-payload *> "$Output/publish.txt"
 if($LASTEXITCODE -ne 0){throw 'Publish failed'}
 cmd /c tools\portable-entry\build.cmd *> "$Output/build-entry.txt"
 if($LASTEXITCODE -ne 0){throw 'Native entry build failed'}
 & ./tools/portable-entry/package.ps1 -Payload diagnostic-payload -Launcher portable-build/Start-app.exe -Destination diagnostic-portable
 Compress-Archive -Path diagnostic-portable/* -DestinationPath diagnostic.zip
 Expand-Archive diagnostic.zip diagnostic-extracted
 $entry=Join-Path $Source 'diagnostic-extracted/Start-app.exe'
 Get-Content "$Source/diagnostic-extracted/App/StartApp.App.runtimeconfig.json" | Set-Content "$Output/package-runtimeconfig.json"
 Probe 'extracted-zip-exact-source' $entry (Split-Path $entry) $true
}finally{
 $results | ConvertTo-Json -Depth 6 | Set-Content "$Output/results.json"
 Pop-Location
}
