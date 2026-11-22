$ErrorActionPreference = 'Stop'
if (Get-Command Set-DisplayResolution -ErrorAction SilentlyContinue) {
  try { Set-DisplayResolution -Width 1920 -Height 1080 -Force -ErrorAction Stop }
  catch { Write-Warning 'Desktop resolution change was unavailable; actual capture dimensions are recorded.' }
}
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DesignWindow {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
}
'@
New-Item -ItemType Directory -Force design-captures | Out-Null
# This fresh hosted runner contains synthetic demo data only. No user account or store is used.
$storeDir = Join-Path $env:LOCALAPPDATA 'StartApp'
New-Item -ItemType Directory -Force $storeDir | Out-Null
$names = @('פיתוח', 'אולפן ביתי', 'לימודים', 'הכנת שיעור', 'מחקר ולמידה', 'פרויקט חדש')
$colors = @('#5B79BE', '#A183BD', '#629A8B', '#C59A52', '#7B93AC', '#A7AEB9')
$workspaces = @()
for ($i = 0; $i -lt 6; $i++) {
  $members = if ($i -eq 5) { @() } else { @(@{ itemId = 'demo-link'; id = "demo-member-$i" }) }
  $workspaces += @{ id = "demo-$i"; name = $names[$i]; category = 'כללי'; color = $colors[$i]; createdAt = '2026-11-17T12:00:00Z'; items = @($members) }
}
@{ schemaVersion = 5; items = @{ 'demo-link' = @{ id = 'demo-link'; type = 'link'; name = 'אתר לדוגמה'; target = 'https://example.com/' } }; workspaces = @($workspaces); defaultWorkspaceId = 'demo-0' } | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $storeDir 'store.json') -Encoding utf8
$exe = Get-ChildItem src/StartApp.App/bin/Release -Recurse -Filter StartApp.App.exe | Select-Object -First 1
$p = Start-Process $exe.FullName -PassThru
try {
  $clock = [Diagnostics.Stopwatch]::StartNew()
  do { Start-Sleep -Milliseconds 250; $p.Refresh() } while (!$p.HasExited -and $p.MainWindowHandle -eq 0 -and $clock.Elapsed.TotalSeconds -lt 45)
  if ($p.HasExited -or $p.MainWindowHandle -eq 0) { throw 'App did not open' }
  $a = [System.Windows.Automation.AutomationElement]
  $ts = [System.Windows.Automation.TreeScope]::Descendants
  $pc = [System.Windows.Automation.PropertyCondition]
  $win = $a::FromHandle($p.MainWindowHandle)
  function Capture($name, $width, $height) {
    [void][DesignWindow]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, 0, 0, $width, $height, 0x40)
    [void][DesignWindow]::SetForegroundWindow($p.MainWindowHandle)
    [void][DesignWindow]::SetCursorPos(1, 1)
    Start-Sleep -Milliseconds 900
    $r = New-Object DesignWindow+Rect
    [void][DesignWindow]::GetWindowRect($p.MainWindowHandle, [ref]$r)
    "$name actual=$($r.Right-$r.Left)x$($r.Bottom-$r.Top) requested=${width}x${height}" | Add-Content design-captures/dimensions.txt
    $image = New-Object Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [Drawing.Graphics]::FromImage($image)
    try { $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $image.Size); $image.Save((Join-Path $PWD "design-captures/$name.png")) }
    finally { $g.Dispose(); $image.Dispose() }
  }
  function InvokeNamed($name) {
    $button = $win.FindFirst($ts, (New-Object $pc($a::NameProperty, $name)))
    if (!$button) { throw "Missing app control: $name" }
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 350
  }
  Start-Sleep -Seconds 2
  Capture 'home-mock-size' 1224 786
  InvokeNamed 'Options menu'
  Capture 'options-mock-size' 1224 786
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
  Capture 'home-900x560' 900 560
  InvokeNamed 'View menu'
  Capture 'view-menu-900x560' 900 560
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
  InvokeNamed 'New workspace button'
  Capture 'create-empty-mock-size' 1224 786
  $input = $win.FindFirst($ts, (New-Object $pc($a::NameProperty, 'Workspace name')))
  $input.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('פרויקט חדש')
  Capture 'create-live-preview-blue' 1224 786
  InvokeNamed 'Create color green'
  Capture 'create-live-preview-green' 1224 786
  Capture 'create-live-preview-900x560' 900 560
  InvokeNamed 'Close window'
  Capture 'create-unsaved-close-guard' 900 560
  "Source: $env:GITHUB_SHA`nSynthetic demo data. Real app window, no test UI.`nHome opened; options and view buttons clicked. No deploy, account, or purchase actions." | Set-Content design-captures/actions.txt
} finally {
  if (!$p.HasExited) { Stop-Process -Id $p.Id -Force }
}
