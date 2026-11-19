$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PlanningWindow {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@
$out = Join-Path $PWD 'planning-images'
New-Item -ItemType Directory $out | Out-Null
# This runner is disposable. Refuse to replace any existing application data.
$storeDir = Join-Path $env:LOCALAPPDATA 'StartApp'
if (Test-Path $storeDir) { throw 'Existing app data found; refusing to replace it' }
New-Item -ItemType Directory $storeDir | Out-Null
$items = [ordered]@{
 'it_site' = @{id='it_site';type='link';name='תיעוד הפרויקט';target='https://example.invalid/project-docs'}
 'it_folder' = @{id='it_folder';type='folder';name='תיקיית הפרויקט';target='C:\PlanningSample\project'}
 'it_file' = @{id='it_file';type='file';name='מסמך עבודה';target='C:\PlanningSample\project\notes.txt'}
 'it_program' = @{id='it_program';type='program';name='עורך קוד לדוגמה';target='C:\PlanningSample\Editor.exe'}
}
$members = @(); $index = 0
foreach ($id in $items.Keys) { $index++; $members += @{id="mi_$index";itemId=$id;description='נתוני דוגמה לצילום הממשק בלבד'} }
$seed = @{schemaVersion=4;items=$items;customCategories=@();workspaces=@(
 @{id='ws_sample';name='סביבת פיתוח לדוגמה';description='צילום עדכני של ממשק הפיתוח עם נתונים מלאכותיים, ללא הרצת פריטים';category='כללי';createdAt='2026-11-19T00:00:00Z';items=$members},
 @{id='ws_second';name='סביבה נוספת לדוגמה';category='כללי';items=@()}
)}
$seed | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $storeDir 'store.json') -Encoding utf8
$exe = Get-Item 'src/StartApp.App/bin/Release/net10.0-windows/StartApp.App.exe'
$p = $null
$A = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
function ByName([string]$name) {
 $condition = [System.Windows.Automation.PropertyCondition]::new($A::NameProperty,$name)
 $found = $script:window.FindFirst($TS::Descendants,$condition)
 if ($null -ne $found) { return $found }
 # Native context menus can be separate top-level windows of this process.
 $scoped = [System.Windows.Automation.AndCondition]::new($condition,[System.Windows.Automation.PropertyCondition]::new($A::ProcessIdProperty,[int]$p.Id))
 return $A::RootElement.FindFirst($TS::Descendants,$scoped)
}
function ById([string]$id) {
 return $script:window.FindFirst($TS::Descendants, [System.Windows.Automation.PropertyCondition]::new($A::AutomationIdProperty,$id))
}
function AwaitElement([scriptblock]$query) {
 $clock = [Diagnostics.Stopwatch]::StartNew()
 while ($clock.Elapsed.TotalSeconds -lt 20) {
  if ($p.HasExited) { throw 'App exited before capture' }
  $el = & $query
  if ($null -ne $el -and -not $el.Current.IsOffscreen) { return $el }
  Start-Sleep -Milliseconds 200
 }
 throw 'Expected UI element did not appear'
}
function Screenshot([string]$name) {
 $r = [PlanningWindow+Rect]::new()
 if (-not [PlanningWindow]::GetWindowRect($p.MainWindowHandle,[ref]$r)) { throw 'Window bounds unavailable' }
 $w = $r.Right-$r.Left; $h = $r.Bottom-$r.Top
 $area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
 if ($r.Left -lt $area.Left -or $r.Top -lt $area.Top -or $r.Right -gt $area.Right -or $r.Bottom -gt $area.Bottom) { throw 'App window extends outside the usable desktop; refusing a clipped image' }
 if ($w -lt 600 -or $h -lt 400) { throw 'Window too small for useful screenshot' }
 $bitmap = [Drawing.Bitmap]::new($w,$h)
 $graphics = [Drawing.Graphics]::FromImage($bitmap)
 try { $graphics.CopyFromScreen($r.Left,$r.Top,0,0,$bitmap.Size); $bitmap.Save((Join-Path $out "$name.png")) }
 finally { $graphics.Dispose(); $bitmap.Dispose() }
}
try {
 $p = Start-Process $exe.FullName -PassThru
 $clock = [Diagnostics.Stopwatch]::StartNew()
 while ($clock.Elapsed.TotalSeconds -lt 60) { $p.Refresh(); if ($p.HasExited) { throw 'App exited at startup' }; if ($p.MainWindowHandle -ne 0) { break }; Start-Sleep -Milliseconds 200 }
 if ($p.MainWindowHandle -eq 0) { throw 'No actual app window' }
 $area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
 $width = [Math]::Min(1200, $area.Width - 16)
 $height = [Math]::Min(850, $area.Height - 16)
 if ($width -lt 720 -or $height -lt 400) { throw 'Desktop work area too small to capture the whole app' }
 if (-not [PlanningWindow]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,($area.Left+8),($area.Top+8),$width,$height,0x0040)) { throw 'Could not fit app inside desktop work area' }
 [void][PlanningWindow]::SetForegroundWindow($p.MainWindowHandle)
 $script:window = $A::FromHandle($p.MainWindowHandle)
 $card = AwaitElement { ById 'WorkspaceTitle' }
 Start-Sleep -Milliseconds 500
 Screenshot '01-current-workspaces'
 # Navigate to details using the real workspace card. Never invoke Launch.
 $card.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
 $edit = AwaitElement { ByName 'Edit item' }
 Start-Sleep -Milliseconds 500
 Screenshot '02-current-workspace-details'
 # Open only the existing item context menu. Never invoke any menu action.
 $edit.SetFocus()
 [System.Windows.Forms.SendKeys]::SendWait('+{F10}')
 $null = AwaitElement { ByName 'מחיקה' }
 Start-Sleep -Milliseconds 350
 Screenshot '03-current-item-menu'
 [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
 $binding = [ordered]@{
  sourceCommit=$env:APP_SOURCE;workflowCommit=$env:GITHUB_SHA;runId=$env:GITHUB_RUN_ID
  capturedUtc=[DateTime]::UtcNow.ToString('o');binarySha256=(Get-FileHash $exe.FullName -Algorithm SHA256).Hash
  purpose='Current development app UI for task breakdown only. Synthetic sample data. No launch, test suite, script feature, release or deployment.'
  images=@('01-current-workspaces.png','02-current-workspace-details.png','03-current-item-menu.png')
 }
 $binding | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $out 'source-identity.json') -Encoding utf8
}
finally { if ($null -ne $p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force } }
