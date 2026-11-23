$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class GuideWin {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int L,T,R,B; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int ht,uint f);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h,int a,out Rect r,int size);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb,IntPtr l);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 delegate bool EnumProc(IntPtr h,IntPtr l);
 public static IntPtr[] Windows(uint pid) {var hs=new System.Collections.Generic.List<IntPtr>();EnumWindows((h,l)=>{uint p;GetWindowThreadProcessId(h,out p);if(p==pid&&IsWindowVisible(h))hs.Add(h);return true;},IntPtr.Zero);return hs.ToArray();}
}
'@
[void][GuideWin]::SetThreadDpiAwarenessContext([IntPtr](-4))
$out = Join-Path $PWD 'proto2-captures'
New-Item -ItemType Directory -Force $out | Out-Null
$tag = 'v0.1.0-proto.2'
$expected = 'f5084c0898bccb9e2e69943cdbf40fb41087c86c656b0a71c34896e2ce766a57'
$releaseCommit = '8e55048afaef806a3d971469ab4de844cd45198e'
$url = 'https://github.com/my-sochen/Start-app/releases/download/v0.1.0-proto.2/StartApp-win-x64.zip'
$zip = Join-Path $env:RUNNER_TEMP 'proto2-published.zip'
$ship = Join-Path $env:RUNNER_TEMP 'proto2-unmodified'
$shots = [System.Collections.Generic.List[object]]::new()
$p = $null
$provenance = [ordered]@{ tag=$tag; releaseCommit=$releaseCommit; assetUrl=$url; expectedSha256=$expected; harnessCommit=$env:GITHUB_SHA; baseDev='a69364d160366f485232ef99a9a474b7d6638f77'; runnerImage=$env:ImageVersion; runId=$env:GITHUB_RUN_ID; sampleDataOnly=$true; rebuilt=$false; instrumented=$false; screenshots=$shots; status='started' }
function SaveProvenance { $provenance | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $out 'provenance.json') -Encoding utf8 }
function TreeHashes {
 @(Get-ChildItem $ship -Recurse -File | Sort-Object FullName | ForEach-Object { [ordered]@{ path=[IO.Path]::GetRelativePath($ship,$_.FullName); sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } })
}
try {
 Invoke-WebRequest $url -OutFile $zip
 $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
 $provenance.actualSha256 = $actual
 if ($actual -cne $expected) { throw 'Published ZIP hash mismatch. Refusing extraction and execution.' }
 $checksum = (Invoke-WebRequest 'https://github.com/my-sochen/Start-app/releases/download/v0.1.0-proto.2/StartApp-win-x64.zip.sha256').Content
 if ($checksum -is [byte[]]) { $checksum=[Text.Encoding]::UTF8.GetString($checksum) }
 if ("$checksum" -notmatch "(?i)^\s*$expected\s+StartApp-win-x64\.zip\s*$") { throw 'Published checksum file does not match the pinned ZIP.' }
 $provenance.publishedChecksumVerified=$true
 Expand-Archive $zip -DestinationPath $ship
 $before = TreeHashes
 $before | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $out 'extracted-file-hashes.json')
 $exe = Join-Path $ship 'App/StartApp.App.exe'
 if (-not (Test-Path $exe)) { throw 'Expected published app executable missing.' }
 $provenance.executable='App/StartApp.App.exe'
 $provenance.executableSha256=(Get-FileHash $exe).Hash.ToLowerInvariant()
 $provenance.fileVersion=(Get-Item $exe).VersionInfo.FileVersion
 $data = Join-Path $env:LOCALAPPDATA 'StartApp'
 if (Test-Path $data) { throw 'Runner store already exists. Refusing to reuse non-sample data.' }
 New-Item -ItemType Directory -Force $data | Out-Null
 $sample = Join-Path $env:RUNNER_TEMP 'StartApp-guide-sample'
 New-Item -ItemType Directory -Force $sample | Out-Null
 Set-Content (Join-Path $sample 'meeting-notes.txt') 'Synthetic guide sample. No personal information.'
 $items = @{
  'sample-site'=@{id='sample-site';type='link';name='Example website';target='https://example.com/';description='Sample website only'}
  'sample-file'=@{id='sample-file';type='file';name='Meeting notes';target=(Join-Path $sample 'meeting-notes.txt');description='Synthetic sample notes'}
  'sample-folder'=@{id='sample-folder';type='folder';name='Project folder';target=$sample}
 }
 $store=@{schemaVersion=4;items=$items;workspaces=@(
  @{id='guide-work';name='Sample project';description='Website, notes and project folder';createdAt='2026-11-19T10:00:00Z';color='#DBEAFE';items=@(@{id='guide-m1';itemId='sample-site'},@{id='guide-m2';itemId='sample-file'},@{id='guide-m3';itemId='sample-folder'})},
  @{id='guide-empty';name='New workspace';description='An empty sample workspace';createdAt='2026-11-19T10:00:00Z';color='#DCFCE7';items=@()}
 );customCategories=@()}
 $store | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $data 'store.json') -Encoding utf8
 $A=[System.Windows.Automation.AutomationElement]; $TS=[System.Windows.Automation.TreeScope]; $PC=[System.Windows.Automation.PropertyCondition]; $CT=[System.Windows.Automation.ControlType]
 $p=Start-Process $exe -WorkingDirectory (Split-Path $exe) -PassThru
 $deadline=[Diagnostics.Stopwatch]::StartNew()
 while ($deadline.Elapsed.TotalSeconds -lt 30) { $p.Refresh(); if ($p.MainWindowHandle -ne 0) { break }; if($p.HasExited){throw 'Published app exited before showing a window.'};Start-Sleep -Milliseconds 150 }
 if ($p.MainWindowHandle -eq 0) { throw 'Published app window was not ready.' }
 $win=$A::FromHandle($p.MainWindowHandle)
 [void][GuideWin]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,20,20,940,650,0x40)
 function Named($name,$scope=$win) { $scope.FindFirst($TS::Descendants,(New-Object $PC($A::NameProperty,$name))) }
 function Button($name,$scope=$win) { $scope.FindFirst($TS::Descendants,(New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::NameProperty,$name)),(New-Object $PC($A::ControlTypeProperty,$CT::Button))))) }
 function WaitNamed($name,$scope=$win) { $sw=[Diagnostics.Stopwatch]::StartNew(); do { $e=Named $name $scope; if($e){return $e};Start-Sleep -Milliseconds 100 } while($sw.Elapsed.TotalSeconds -lt 15);throw "Control did not appear: $name" }
 function Click($name) { $b=Button $name; if(-not $b -or -not $b.Current.IsEnabled){throw "Button unavailable: $name"};$b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke();Start-Sleep -Milliseconds 250 }
 function Shot($file,$explanation,$handle=$p.MainWindowHandle) {
  [void][GuideWin]::SetForegroundWindow($handle);[void][GuideWin]::SetCursorPos(0,0);Start-Sleep -Milliseconds 500
  $r=New-Object GuideWin+Rect;if([GuideWin]::DwmGetWindowAttribute($handle,9,[ref]$r,16) -ne 0){throw 'Cannot determine visible app frame.'}
  $screen=[System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
  if($r.L -lt $screen.Left -or $r.T -lt $screen.Top -or $r.R -gt $screen.Right -or $r.B -gt $screen.Bottom){throw 'App frame extends outside usable desktop. Refusing clipped screenshot.'}
  if($r.R-$r.L -le 0 -or $r.B-$r.T -le 0){throw 'No valid app window rectangle.'}
  $bmp=New-Object Drawing.Bitmap ($r.R-$r.L),($r.B-$r.T);$g=[Drawing.Graphics]::FromImage($bmp)
  try{$g.CopyFromScreen($r.L,$r.T,0,0,$bmp.Size);$bmp.Save((Join-Path $out $file),[Drawing.Imaging.ImageFormat]::Png)}finally{$g.Dispose();$bmp.Dispose()}
  $shots.Add([ordered]@{file=$file;explanation=$explanation;width=$r.R-$r.L;height=$r.B-$r.T;appProcessId=$p.Id})
  SaveProvenance; Write-Output "Captured $file"
 }
 [void](WaitNamed 'Sample project')
 Shot '01-home-create.png' 'Published proto.2 home: existing sample workspaces and its original inline create form. No beta design.'
 Click 'Sample project';[void](WaitNamed 'Item target')
 Shot '02-workspace-details.png' 'Published proto.2 workspace details: sample name, description, category, manual item form and saved sample items.'
 Click 'Edit item';[void](WaitNamed 'Edit item name')
 Shot '03-item-editor.png' 'Published proto.2 inline saved-item editor, opened without altering the sample item.'
 Click 'Cancel item edit'
 Click 'Pick from existing resources';[void](WaitNamed 'Search resources')
 Shot '04-existing-resource-picker.png' 'Published proto.2 existing-resource picker with the sample catalog; nothing is added.'
 Click 'Close resource picker'
 Click 'שגר';[void](WaitNamed 'Confirm opening selection')
 Shot '05-opening-selection.png' 'Published proto.2 pre-launch selection. No item is launched; the next action cancels it.'
 Click 'Close opening selection';Click 'Back'
 Click 'Settings'
 $settings=$null;$wait=[Diagnostics.Stopwatch]::StartNew()
 while($wait.Elapsed.TotalSeconds -lt 15 -and -not $settings){
  foreach($h in [GuideWin]::Windows([uint32]$p.Id)){
   if($h -ne $p.MainWindowHandle){$candidate=$A::FromHandle($h);if($candidate.Current.Name -eq 'Settings window' -or $candidate.Current.Name -eq 'הגדרות'){$settings=$candidate;break}}
  }
  if(-not $settings){Start-Sleep -Milliseconds 100}
 }
 if(-not $settings){throw 'Published Settings window did not open.'}
 Shot '06-update-settings.png' 'Published proto.2 Settings window as shipped. No update check, download or preference change was requested.' ([IntPtr]$settings.Current.NativeWindowHandle)
 $settings.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
 $null=$p.CloseMainWindow();$p.WaitForExit(5000)|Out-Null;if(-not $p.HasExited){$p.Kill();$p.WaitForExit()}
 $after=TreeHashes
 if(($before|ConvertTo-Json -Depth 4 -Compress) -cne ($after|ConvertTo-Json -Depth 4 -Compress)){throw 'Extracted release files changed during the capture run.'}
 $provenance.extractedFilesUnchanged=$true
 $provenance.status='pass'
 'PASS: exact published ZIP, hash verified, six app-only screenshots, sample data only, no binary changes.' | Set-Content (Join-Path $out 'RESULT.txt')
} catch {
 $provenance.status='fail';$provenance.error="$($_.Exception.Message)"
 $_ | Out-String | Set-Content (Join-Path $out 'ERROR.txt')
 throw
} finally {
 if($p -and -not $p.HasExited){$p.Kill();$p.WaitForExit()}
 SaveProvenance
}
