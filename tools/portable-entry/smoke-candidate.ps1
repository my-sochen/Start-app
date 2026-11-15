param(
 [Parameter(Mandatory)][string]$Extracted,
 [Parameter(Mandatory)][string]$EvidenceDirectory,
 [Parameter(Mandatory)][string]$OwnershipHelper
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
# This is only a smoke run of the extracted ARCHIVE, not a copy staged elsewhere.
Add-Type -Path $OwnershipHelper
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes, System.Windows.Forms
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class CandidateWindow {
 [StructLayout(LayoutKind.Sequential)]public struct R{public int L,T,Rt,B;}
 [DllImport("user32.dll")]public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int z,uint f);
 [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")]public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out R r);
}
'@
[void][CandidateWindow]::SetProcessDPIAware()
$root=(Resolve-Path -LiteralPath $Extracted).Path
$entry=Join-Path $root 'Start-app.exe'
$target=Join-Path (Join-Path $root 'App') 'StartApp.App.exe'
if(-not (Test-Path -LiteralPath $entry -PathType Leaf)){throw 'Missing top-level launcher'}
if(-not (Test-Path -LiteralPath $target -PathType Leaf)){throw 'Missing payload app'}
$data=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'StartApp'
if(Test-Path -LiteralPath $data){throw 'Refusing smoke over existing app data'}
New-Item -ItemType Directory -Path $data | Out-Null
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
$tree=$null;$child=$null;$cleanup=@()
try {
 $tree=[OwnedTree]::Start($entry,$root)
 for($i=0;$i -lt 100;$i++){
  $snapshot=@(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($tree.Process.Id)" | Where-Object {$_.ExecutablePath -eq $target})
  if($snapshot.Count -eq 1){
   $child=Get-Process -Id $snapshot[0].ProcessId;[void]$child.Handle
   if($child.Path -ne $target -or [Math]::Abs(($child.StartTime.ToUniversalTime()-$snapshot[0].CreationDate.ToUniversalTime()).TotalSeconds) -ge 1){throw 'Child identity mismatch'}
   break
  }
  if($tree.Process.HasExited){throw 'Launcher exited before owned app was found'}
  Start-Sleep -Milliseconds 200
 }
 if($null -eq $child){throw 'No exact app child in20seconds'}
 for($i=0;$i -lt 100;$i++){
  $child.Refresh();if($child.HasExited){throw 'App exited before UI'}
  if($child.MainWindowHandle -ne [IntPtr]::Zero){break};Start-Sleep -Milliseconds 200
 }
 $h=$child.MainWindowHandle
 if($h -eq [IntPtr]::Zero -or -not [CandidateWindow]::IsWindowVisible($h) -or [CandidateWindow]::IsIconic($h)){throw 'No visible main window'}
 if(-not [CandidateWindow]::SetWindowPos($h,[IntPtr]::Zero,40,40,0,0,0x41)){throw 'Cannot position app'}
 [void][CandidateWindow]::SetForegroundWindow($h);Start-Sleep -Milliseconds 400
 if([CandidateWindow]::GetForegroundWindow() -ne $h){throw 'App not foreground'}
 $r=[CandidateWindow+R]::new()
 if(-not [CandidateWindow]::GetWindowRect($h,[ref]$r)){throw 'No window bounds'}
 $screen=[Windows.Forms.SystemInformation]::VirtualScreen
 if($r.L -lt $screen.Left -or $r.T -lt $screen.Top -or $r.Rt -gt $screen.Right -or $r.B -gt $screen.Bottom){throw 'App outside screen'}
 $w=$r.Rt-$r.L;$height=$r.B-$r.T
 if($w -lt 100 -or $height -lt 80){throw 'Unusable app bounds'}
 $bmp=[Drawing.Bitmap]::new($w,$height);$g=[Drawing.Graphics]::FromImage($bmp)
 try{
  $g.CopyFromScreen($r.L,$r.T,0,0,$bmp.Size)
  $bmp.Save((Join-Path (Resolve-Path $EvidenceDirectory).Path 'portable-candidate-actual-app.png'))
 }finally{$g.Dispose();$bmp.Dispose()}
 [void]$child.CloseMainWindow()
 if(-not $child.WaitForExit(10000) -or $child.ExitCode -ne 0){throw 'App did not exit cleanly'}
 if(-not $tree.Process.WaitForExit(10000) -or $tree.Process.ExitCode -ne 0){throw 'Launcher did not exit cleanly'}
 @{status='pass';scope='Exact extracted candidate archive only; human pixels pending';sha=$env:GITHUB_SHA} | ConvertTo-Json | Set-Content (Join-Path $EvidenceDirectory 'portable-candidate-smoke.json')
}finally{
 try{if($null -ne $tree){try{$tree.StopAndDispose()}catch{$cleanup+=$_.Exception.Message}};if($null -ne $child){$child.Dispose()}}
 finally{try{Remove-Item -LiteralPath $data -Recurse -Force}catch{$cleanup+=$_.Exception.Message}}
 if($cleanup.Count){throw ($cleanup -join '; ')}
}
