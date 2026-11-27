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
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
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
$catalogue = @{ 'demo-link' = @{ id = 'demo-link'; type = 'link'; name = 'אתר לדוגמה'; target = 'https://example.com/' } }
$catalogue['icon-demo-github'] = @{ id='icon-demo-github'; type='link'; name='GitHub'; target='https://github.com/' }
$workspaces[4].items += @{ itemId='icon-demo-github'; id='icon-demo-github-member' }
$detailItems = @(
  @{ id='detail-docs'; type='link'; name='תיעוד הפרויקט'; target='https://example.com/docs' },
  @{ id='detail-code'; type='link'; name='מאגר קוד לדוגמה'; target='https://example.com/repository' },
  @{ id='detail-board'; type='link'; name='לוח משימות'; target='https://example.com/board' },
  @{ id='detail-editor'; type='program'; name='עורך טקסט'; target='C:\Windows\notepad.exe' },
  @{ id='detail-terminal'; type='program'; name='Terminal'; target='C:\Windows\System32\cmd.exe' },
  @{ id='detail-file'; type='file'; name='מצגת לדוגמה'; target='C:\Projects\Sample\demo.pptx' },
  @{ id='detail-folder'; type='folder'; name='תיקיית הפרויקט'; target='C:\Projects\Sample' }
)
$detailMembers = @()
foreach ($item in $detailItems) { $catalogue[$item.id] = $item; $detailMembers += @{ itemId=$item.id; id=('member-'+$item.id) } }
$workspaces[0].items = @($detailMembers)
$workspaces[0].category = 'תכנות'
$workspaces[0].items += @{ itemId='detail-editor'; id='duplicate-editor-dev'; name='עורך טקסט נוסף' }
$workspaces[1].items += @{ itemId='detail-editor'; id='shared-editor-home' }
$globalItems = @(
  @{ id='global-demo-docs'; type='link'; name='תיעוד'; target='https://example.com/docs'; addedAt='2026-11-20T12:00:00Z' },
  @{ id='global-demo-editor'; type='program'; name='עורך טקסט'; target='C:\Windows\notepad.exe'; addedAt='2026-11-20T12:00:00Z' }
)
$whiteItems = @(
  @{ id='white-demo-folder'; type='folder'; name='תיקיית מסמכים'; target='C:\Users\Demo\Documents'; addedAt='2026-11-20T12:00:00Z' },
  @{ id='white-demo-board'; type='link'; name='לוח משימות'; target='https://example.com/board'; addedAt='2026-11-21T12:00:00Z' }
)
@{ schemaVersion = 5; items = $catalogue; workspaces = @($workspaces); customCategories = @('תכנות'); defaultWorkspaceId = 'demo-0'; globalItems=$globalItems; captureWhitelist=$whiteItems } | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $storeDir 'store.json') -Encoding utf8
$exe = Get-ChildItem src/StartApp.App/bin/Release -Recurse -Filter StartApp.App.exe | Select-Object -First 1
$p = Start-Process $exe.FullName -PassThru
try {
 $a=[System.Windows.Automation.AutomationElement]; $ts=[System.Windows.Automation.TreeScope]::Descendants
 $pc=[System.Windows.Automation.PropertyCondition]
 $clock=[Diagnostics.Stopwatch]::StartNew()
 do { Start-Sleep -Milliseconds 250; $p.Refresh() } while (!$p.HasExited -and $p.MainWindowHandle -eq 0 -and $clock.Elapsed.TotalSeconds -lt 45)
 if ($p.HasExited -or $p.MainWindowHandle -eq 0) { throw 'Real application failed to open' }
 [void][DesignWindow]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,50,50,1100,720,0x40)
 [void][DesignWindow]::SetForegroundWindow($p.MainWindowHandle)
 Start-Sleep -Seconds 2
 function FindVisible($name) {
  $timer=[Diagnostics.Stopwatch]::StartNew()
  do {
   $cond=New-Object System.Windows.Automation.AndCondition((New-Object $pc($a::NameProperty,$name)),(New-Object $pc($a::IsOffscreenProperty,$false)))
   $el=$a::RootElement.FindFirst($ts,$cond)
   if($el){return $el}; Start-Sleep -Milliseconds 200
  } while($timer.Elapsed.TotalSeconds -lt 12)
  throw "Visible control not found: $name"
 }
 function Mouse($el,$click) {
  $r=$el.Current.BoundingRectangle
  [void][DesignWindow]::SetCursorPos([int]($r.X+$r.Width/2),[int]($r.Y+$r.Height/2))
  if($click){[DesignWindow]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[DesignWindow]::mouse_event(4,0,0,0,[UIntPtr]::Zero)}
  Start-Sleep -Milliseconds 700
 }
 Mouse (FindVisible 'View menu') $true
 foreach($name in @('סינון','לפי סוג תוכן','תוכנות')) {
  $el=FindVisible $name
  Mouse $el $false
  $pattern=$null
  if($el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern,[ref]$pattern)){$pattern.Expand()}
  Start-Sleep -Milliseconds 600
 }
 $notepad=FindVisible 'עורך טקסט'
 $terminal=FindVisible 'Terminal'
 $bounds=New-Object DesignWindow+Rect
 [void][DesignWindow]::GetWindowRect($p.MainWindowHandle,[ref]$bounds)
 $desktop=[System.Windows.Forms.SystemInformation]::VirtualScreen
 $bitmap=New-Object Drawing.Bitmap $desktop.Width,$desktop.Height
 $g=[Drawing.Graphics]::FromImage($bitmap)
 try{$g.CopyFromScreen($desktop.Left,$desktop.Top,0,0,$bitmap.Size);$bitmap.Save((Join-Path $PWD 'design-captures/startapp-real-software-filter-20261127-0947.png'))}finally{$g.Dispose();$bitmap.Dispose()}
 @{'process'=$p.ProcessName;'windowTitle'=$p.MainWindowTitle;'route'=@('תצוגה','סינון','לפי סוג תוכן','תוכנות');'visibleSoftware'=@($notepad.Current.Name,$terminal.Current.Name);'capture'='Actual desktop pixels via CopyFromScreen; unmodified StartApp.App process; synthetic demo store only'} | ConvertTo-Json -Depth 5 | Set-Content design-captures/evidence.json -Encoding utf8
} finally { if(!$p.HasExited){Stop-Process -Id $p.Id -Force} }
