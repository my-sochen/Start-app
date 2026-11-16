# Dedicated hosted runner only. No personal Desktop, browser, or capture work.
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=$PWD.Path
@{status='running';stage='INHERITED_PORTABLE';sha=$env:GITHUB_SHA}|ConvertTo-Json|Set-Content diag/icon-result.json
& "$repo/tests/portable-entry/verify-windows.ps1"
# Existing verifier proves owned app startup/close, launcher paths/refusal/cleanup and actual frame PNG.
$root=Join-Path $env:RUNNER_TEMP ('icon-only-'+[guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $root)
$desktop=Join-Path $root 'disposable Desktop'
[void](New-Item -ItemType Directory -Path $desktop)
$package=Join-Path $root 'portable folder'
$checks=[Collections.Generic.List[string]]::new()
$com=$null;$lnk=$null
function Check([bool]$ok,[string]$label){if(-not $ok){throw $label};$checks.Add($label)}
try {
 & "$repo/tools/portable-entry/package.ps1" -Payload "$repo/app-payload" -Launcher "$repo/portable-build/Start-app.exe" -Destination $package
 $helper=Join-Path $package 'Create-DesktopShortcut.ps1'
 $original=[IO.File]::ReadAllText($helper)
 $needle='$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)'
 Check (($original.Split(@($needle),[StringSplitOptions]::None).Length-1) -eq 1) 'Exactly one Desktop lookup seam'
 $replacement='$desktop = '+"'"+$desktop.Replace("'","''")+"'"
 $shim=$original.Replace($needle,$replacement)
 Check ($shim.Replace($replacement,$needle) -ceq $original) 'Only test Desktop lookup changed; rest exact original helper'
 [IO.File]::WriteAllText($helper,$shim)
 & $helper
 $path=Join-Path $desktop 'Start-app.lnk'
 Check (Test-Path -LiteralPath $path -PathType Leaf) 'Disposable shortcut exists'
 $com=New-Object -ComObject WScript.Shell
 $lnk=$com.CreateShortcut($path)
 Check ($lnk.TargetPath -ceq (Join-Path $package 'Start-app.exe')) 'Shortcut targets root launcher'
 Check ($lnk.WorkingDirectory -ceq $package) 'Shortcut working directory preserved'
 Check ($lnk.IconLocation -ceq ((Join-Path $package 'Start-app.exe')+',0')) 'Shortcut icon points launcher resource0'
 Check ([string]::IsNullOrEmpty($lnk.Arguments)) 'No shortcut arguments'
 $before=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
 $refused=$false;try{& $helper}catch{$refused=$true}
 Check $refused 'Existing shortcut refused'
 Check ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ceq $before) 'Existing shortcut bytes unchanged'
 [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($lnk);$lnk=$null
 [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($com);$com=$null
 # Render icons resolved by Windows shell, not the source PNG.
 Add-Type -AssemblyName System.Drawing
 Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class ShellIcon {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] public struct Info { public IntPtr hIcon;public int iIcon;public uint attr;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string display;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)]public string type; }
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)]public static extern IntPtr SHGetFileInfo(string p,uint a,out Info i,uint cb,uint f);
 [DllImport("user32.dll")]public static extern bool DestroyIcon(IntPtr h);
}
'@
 $bitmap=[Drawing.Bitmap]::new(760,170);$g=[Drawing.Graphics]::FromImage($bitmap)
 try{
 $g.Clear([Drawing.Color]::White);$font=[Drawing.Font]::new('Segoe UI',10)
 try{
 $x=10
 foreach($entry in @(@('App host',(Join-Path $package 'App/StartApp.App.exe')),@('Root launcher',(Join-Path $package 'Start-app.exe')),@('Disposable shortcut',$path))){
  $info=[ShellIcon+Info]::new();$result=[ShellIcon]::SHGetFileInfo($entry[1],0,[ref]$info,[Runtime.InteropServices.Marshal]::SizeOf($info),0x100)
  Check ($result -ne [IntPtr]::Zero -and $info.hIcon -ne [IntPtr]::Zero) ('Shell resolves '+$entry[0])
  try{$icon=[Drawing.Icon]::FromHandle($info.hIcon);$g.DrawIcon($icon,[Drawing.Rectangle]::new($x,45,64,64));$g.DrawString($entry[0],$font,[Drawing.Brushes]::Black,$x,10)}finally{[void][ShellIcon]::DestroyIcon($info.hIcon)}
  $x+=250
 }
 $bitmap.Save((Join-Path $repo 'out-images/icon-shell-contact-sheet.png'))
 }finally{$font.Dispose()}
 }finally{$g.Dispose();$bitmap.Dispose()}
 @{status='pass';checks=$checks;sha=$env:GITHUB_SHA;scope='Hosted runner only. Helper Desktop lookup substituted to disposable folder; no personal Desktop or link activation; no icon cache reset.'}|ConvertTo-Json -Depth 5|Set-Content diag/icon-result.json
} catch {
 @{status='fail';checks=$checks;error=$_.Exception.Message;sha=$env:GITHUB_SHA}|ConvertTo-Json -Depth 5|Set-Content diag/icon-result.json
 throw
} finally {
 if($null -ne $lnk){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($lnk)}
 if($null -ne $com){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($com)}
 if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force}
}
