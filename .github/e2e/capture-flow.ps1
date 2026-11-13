try {
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class W { [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; } [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, System.UIntPtr e); [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags); [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid); }'
$A = [System.Windows.Automation.AutomationElement]; $CT = [System.Windows.Automation.ControlType]; $PC = [System.Windows.Automation.PropertyCondition]; $TS = [System.Windows.Automation.TreeScope]
$results = New-Object System.Collections.ArrayList; $script:shot = 0; $runId = $env:GITHUB_RUN_ID
function Assert($name, $ok, $detail) { [void]$results.Add([ordered]@{ name = $name; ok = [bool]$ok; detail = "$detail" }); "ASSERT $(if ($ok) {'PASS'} else {'FAIL'}) $name :: $detail" }
function Info($name, $detail) { [void]$results.Add([ordered]@{ name = $name; ok = $true; info = $true; detail = "$detail" }); "INFO $name :: $detail" }
function CurWin() { $ws = @($A::RootElement.FindAll($TS::Children, (New-Object $PC($A::ProcessIdProperty, $p.Id)))); if ($ws.Count -eq 0) { return $null }; $named = @($ws | Where-Object { $_.Current.Name }); if ($named.Count -gt 0) { return $named[0] }; return $ws[0] }
$script:dumped = $false
function DumpState($why) {
  if ($script:dumped) { return }; $script:dumped = $true
  try {
    $p.Refresh()
    "DUMP($why): hasExited=$($p.HasExited) mainHwnd=$($p.MainWindowHandle) responding=$(if ($p.HasExited) { '-' } else { $p.Responding })" | Tee-Object -Append diag/errors.txt
    $ws = @($A::RootElement.FindAll($TS::Children, (New-Object $PC($A::ProcessIdProperty, $p.Id))))
    "DUMP($why): top-level windows for pid=$($p.Id): $($ws.Count)" | Tee-Object -Append diag/errors.txt
    foreach ($w0 in $ws) { try { $c = $w0.Current; "DUMP win: name='$($c.Name)' class='$($c.ClassName)' offscreen=$($c.IsOffscreen) rect=$($c.BoundingRectangle)" | Tee-Object -Append diag/errors.txt } catch { "DUMP win unreadable: $($_.Exception.GetType().Name)" | Tee-Object -Append diag/errors.txt } }
    try {
      $ev = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = @('Application Error', '.NET Runtime', 'Windows Error Reporting') } -MaxEvents 60 -ErrorAction Stop | Where-Object { $_.Message -like '*StartApp*' } | Select-Object -First 3
      foreach ($e in $ev) { $m = ($e.Message -replace '\s+', ' '); if ($m.Length -gt 1600) { $m = $m.Substring(0, 1600) }; "DUMP event $($e.ProviderName) $($e.TimeCreated.ToString('HH:mm:ss')): $m" | Out-File -Append diag/errors.txt }
      if (-not $ev) { "DUMP no StartApp crash events in Application log" | Out-File -Append diag/errors.txt }
    } catch { "DUMP event log read failed: $($_.Exception.Message)" | Out-File -Append diag/errors.txt }
    $vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bmp = New-Object System.Drawing.Bitmap $vs.Width, $vs.Height; $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($vs.Left, $vs.Top, 0, 0, $bmp.Size)
    $f = "out-images/cap-DESKTOP-$why.png"; $bmp.Save("$PWD/$f"); "DUMP desktop shot $f $($vs.Width)x$($vs.Height)"
  } catch { "DUMP failed: $_" | Tee-Object -Append diag/errors.txt }
}
function Shot($state) {
  [void][W]::SetCursorPos(5, 5); if ($p.MainWindowHandle -ne 0) { [void][W]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0010) }; Start-Sleep -Milliseconds 400
  $f = "out-images/cap-$state.png"
  $w0 = CurWin; if ($null -eq $w0) { "no app window for $state" | Tee-Object -Append diag/errors.txt; return }
  $br = $w0.Current.BoundingRectangle; $w = [int]$br.Width; $h = [int]$br.Height
  if ($w -le 0 -or $h -le 0) { "no window rect for $state" | Tee-Object -Append diag/errors.txt; return }
  $bmp = New-Object System.Drawing.Bitmap $w, $h; $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen([int]$br.X, [int]$br.Y, 0, 0, $bmp.Size); $bmp.Save("$PWD/$f"); "shot $f ${w}x${h}"
}
function ShotRaw($state) {
  # Evidence shot with NO window-raise: captures whatever is actually on screen over the app region.
  [void][W]::SetCursorPos(5, 5)
  $f = "out-images/cap-$state.png"
  $w0 = CurWin; if ($null -eq $w0) { "no app window for $state" | Tee-Object -Append diag/errors.txt; return }
  $br = $w0.Current.BoundingRectangle; $w = [int]$br.Width; $h = [int]$br.Height
  if ($w -le 0 -or $h -le 0) { "no window rect for $state" | Tee-Object -Append diag/errors.txt; return }
  $bmp = New-Object System.Drawing.Bitmap $w, $h; $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen([int]$br.X, [int]$br.Y, 0, 0, $bmp.Size); $bmp.Save("$PWD/$f"); "shot $f ${w}x${h}"
}
function UiaAll($ct) { for ($t = 0; $t -lt 3; $t++) { $w0 = CurWin; if ($null -eq $w0) { if ($t -eq 2) { "UIA window gone for pid $($p.Id): appHasExited=$($p.HasExited) mainWindowHandle=$($p.MainWindowHandle)" | Out-File -Append diag/uia-liveness.txt; return @() }; Start-Sleep -Milliseconds 400; continue }; try { return $w0.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $ct))) } catch { if ($t -eq 2) { "UIA read failed after retries: $($_.Exception.Message) | appHasExited=$(if ($p) { $p.HasExited } else { "no-process" }) mainWindowHandle=$(if ($p) { $p.MainWindowHandle } else { "-" })" | Out-File -Append diag/uia-liveness.txt; throw }; Start-Sleep -Milliseconds 400 } } }
function Texts() { @(UiaAll $CT::Text | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) }
function ListLines() { @(UiaAll $CT::ListItem | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) }
function Btn($name) { (CurWin).FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::Button)), (New-Object $PC($A::NameProperty, $name))))) }
function BtnLike($needle) { @((CurWin).FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Button)))) | Where-Object { $_.Current.Name -like "*$needle*" } | Select-Object -First 1 }
function ClickBtn($name, $settle = 900) {
  $b = $null; for ($i = 0; $i -lt 40; $i++) { $b = Btn $name; if ($b -and $b.Current.IsEnabled) { break }; Start-Sleep -Milliseconds 250 }
  if (-not $b) { return "button '$name' not found" }; if (-not $b.Current.IsEnabled) { return "button '$name' stayed disabled" }
  $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds $settle; return 'ok'
}
function WaitText($needle, $secs) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.Elapsed.TotalSeconds -lt $secs) { if (@(Texts) + @(ListLines) | Where-Object { $_ -like "*$needle*" }) { return $true }; Start-Sleep -Milliseconds 250 }; return $false }
function WaitGone($needle, $secs) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.Elapsed.TotalSeconds -lt $secs) { if (-not (@(Texts) + @(ListLines) | Where-Object { $_ -like "*$needle*" })) { return $true }; Start-Sleep -Milliseconds 250 }; return $false }
function NamedEdit($name) { (CurWin).FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::Edit)), (New-Object $PC($A::NameProperty, $name))))) }
function NamedCombo($name) { (CurWin).FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::ComboBox)), (New-Object $PC($A::NameProperty, $name))))) }
function ComboValue($combo) { if ($null -eq $combo) { return '' }; try { $sel = $combo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection(); if ($sel.Count -gt 0) { return $sel[0].Current.Name } } catch { }; return '' }
function ValOf($edit) { if ($null -eq $edit) { return '' }; try { return $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { return '' } }
function InvokeEl($el, $what) { if ($null -eq $el) { Assert "$what found" $false 'element not found'; return $false }; try { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); return $true } catch { Assert "$what invokable" $false "$_"; return $false } }
function ToggleEl($el, $what) { if ($null -eq $el) { Assert "$what found" $false 'element not found'; return $false }; try { $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); return $true } catch { Assert "$what toggleable" $false "$_"; return $false } }
function MenuItemByName($name) { $A::RootElement.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::MenuItem)), (New-Object $PC($A::NameProperty, $name))))) }
function MenuInsideWindow($label) { $m = $A::RootElement.FindFirst($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Menu))); if ($null -eq $m) { Assert "$label menu element found" $false 'no open menu'; return }; $mr = $m.Current.BoundingRectangle; $wr = (CurWin).Current.BoundingRectangle; Assert "$label fully inside the app window" ($mr.Left -ge $wr.Left -and $mr.Right -le $wr.Right -and $mr.Top -ge $wr.Top -and $mr.Bottom -le $wr.Bottom) ("menu=$($mr.Left),$($mr.Top)-$($mr.Right),$($mr.Bottom) window=$($wr.Left),$($wr.Top)-$($wr.Right),$($wr.Bottom)") }
function EdgeWindow() {
  # The ONE browser window this test drives: the launched Edge process's own UIA window. No class-name
  # fallback - a fallback can bind a different browser window and make the setup/before/after reads disagree.
  if ($edge -and -not $edge.HasExited) {
    $cand = @($A::RootElement.FindAll($TS::Children, (New-Object $PC($A::ProcessIdProperty, $edge.Id))) | Where-Object { try { $_.Current.ControlType -eq $CT::Window } catch { $false } })
    if ($cand.Count -gt 0) { return $cand[0] }
  }
  return $null
}
function EdgeSelectedTabIndex() {
  # Index of the selected tab in the one bound Edge window (all three test pages share the title 'Example Domain', so the index is the discriminator).
  $w0 = EdgeWindow
  if ($null -eq $w0) { return -1 }
  try {
    $tabs = $w0.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::TabItem)))
    for ($i = 0; $i -lt $tabs.Count; $i++) { try { $sip = $tabs[$i].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern); if ($sip.Current.IsSelected) { return $i } } catch { } }
  } catch { }
  return -1
}
function DragRowToStage($like, $label) {
  # Real pointer drag: press on the source row, move in steps over the drop zone, release. Same path a user's mouse takes.
  $row = CheckByLike $like
  if ($null -eq $row) { Assert "$label found for drag" $false 'row not found'; return }
  $rr = $row.Current.BoundingRectangle; $wr = (CurWin).Current.BoundingRectangle
  $sx = [int]($rr.X + $rr.Width * 0.65); $sy = [int]($rr.Y + $rr.Height / 2)
  $tx = [int]($wr.X + $wr.Width * 0.25); $ty = [int]($wr.Y + $wr.Height * 0.55)
  [void][W]::SetCursorPos($sx, $sy); Start-Sleep -Milliseconds 250
  [W]::mouse_event(0x02, 0, 0, 0, [System.UIntPtr]::Zero); Start-Sleep -Milliseconds 250
  for ($i = 1; $i -le 12; $i++) { [void][W]::SetCursorPos([int]($sx + ($tx - $sx) * $i / 12), [int]($sy + ($ty - $sy) * $i / 12)); Start-Sleep -Milliseconds 60 }
  Start-Sleep -Milliseconds 350
  [W]::mouse_event(0x04, 0, 0, 0, [System.UIntPtr]::Zero); Start-Sleep -Milliseconds 700
}
function StoreObj() { if (Test-Path $store) { Get-Content $store -Raw -Encoding UTF8 | ConvertFrom-Json } else { $null } }
function SetText($edit, $text) { if ($null -eq $edit) { Assert 'SetText target present' $false 'edit element not found'; return }; try { $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text) } catch { Assert 'SetText succeeded' $false "$_" }; Start-Sleep -Milliseconds 400 }
function SetType($typeName) {
  $combo = NamedCombo 'Item type'
  if (-not $combo) { return 'item type combo not found' }
  $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 400
  $it = $combo.FindFirst($TS::Descendants, (New-Object $PC($A::NameProperty, $typeName)))
  if (-not $it) { $it = $A::RootElement.FindFirst($TS::Descendants, (New-Object $PC($A::NameProperty, $typeName))) }
  if (-not $it) { return "type item '$typeName' not found" }
  $it.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 400; return 'ok'
}
function NotepadCount() { @(Get-Process notepad -ErrorAction SilentlyContinue).Count }
function AnyByName($name) { $e = (CurWin).FindFirst($TS::Descendants, (New-Object $PC($A::NameProperty, $name))); if (-not $e) { $e = $A::RootElement.FindFirst($TS::Descendants, (New-Object $PC($A::NameProperty, $name))) }; $e }
function StoreJson() { if (Test-Path $store) { (Get-Content $store -Raw -Encoding UTF8) -replace '\s+', ' ' } else { '' } }

function CaptureTargetText() { $tw = (CurWin).FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::Text)), (New-Object $PC($A::AutomationIdProperty, 'CaptureTargetWorkspace'))))); if ($null -eq $tw) { return '<no header element>' }; return $tw.Current.Name }
function Checks() { @((CurWin).FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::CheckBox)))) }
function CheckNames() { @(Checks | ForEach-Object { $_.Current.Name }) }
function CheckByLike($needle) { Checks | Where-Object { $_.Current.Name -like $needle } | Select-Object -First 1 }
function StagedNames() { @((CurWin).FindAll($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::Edit)), (New-Object $PC($A::NameProperty, 'Added item name'))))) | ForEach-Object { ValOf $_ }) }
function StagedAddrs() { @((CurWin).FindAll($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::Edit)), (New-Object $PC($A::NameProperty, 'Added item address'))))) | ForEach-Object { ValOf $_ }) }
function ScrollStaged([int]$delta) {
  # Real mouse wheel over the staged panel: scrolls the innermost scrollable under the cursor, like a user's wheel.
  $anchor = (CurWin).FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::Edit)), (New-Object $PC($A::NameProperty, 'Added item address')))))
  if ($null -eq $anchor) { return }
  $r = $anchor.Current.BoundingRectangle; if ($r.Width -le 0) { return }
  [void][W]::SetCursorPos([int]($r.X + [Math]::Min(200, $r.Width / 2)), [int]($r.Y + $r.Height / 2)); Start-Sleep -Milliseconds 150
  $d = if ($delta -lt 0) { [uint32](4294967296 + $delta) } else { [uint32]$delta }
  [W]::mouse_event(0x0800, 0, 0, $d, [System.UIntPtr]::Zero); Start-Sleep -Milliseconds 450
}
function StagedAddrsDeep() {
  # The staged list is virtualized: UIA only returns rendered rows. Wheel through the panel and union what renders.
  # Distinct addresses only (enough for the sections that stage each row once).
  $seen = @{}; $ordered = New-Object System.Collections.ArrayList
  foreach ($addr in (StagedAddrs)) { if ($addr -and -not $seen.ContainsKey($addr)) { $seen[$addr] = 1; [void]$ordered.Add($addr) } }
  for ($i = 0; $i -lt 20; $i++) { ScrollStaged -360; foreach ($addr in (StagedAddrs)) { if ($addr -and -not $seen.ContainsKey($addr)) { $seen[$addr] = 1; [void]$ordered.Add($addr) } } }
  for ($i = 0; $i -lt 20; $i++) { ScrollStaged 360 }
  return $ordered
}
function WsCountByTarget($ws, $o, $like) { $ids = @($o.items.PSObject.Properties.Value | Where-Object { $_.target -like $like } | ForEach-Object { $_.id }); return @($ws.items | Where-Object { $ids -contains $_.itemId }).Count }
function EffectiveNames($ws, $o) { @($ws.items | ForEach-Object { $m = $_; if (-not [string]::IsNullOrWhiteSpace($m.name)) { $m.name } else { $it = @($o.items.PSObject.Properties.Value | Where-Object { $_.id -eq $m.itemId })[0]; if ($it) { $it.name } else { '' } } }) }
function StagedActionBtns() { @((CurWin).FindAll($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::Button)), (New-Object $PC($A::NameProperty, 'Staged item actions')))))) }
function IsOn($el) { try { return $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq 'On' } catch { return $false } }
function OpenCaptureMenu($item) {
  $c = ClickBtn 'Capture' 900; if ($c -ne 'ok') { return $c }
  $mi = $null; for ($i = 0; $i -lt 20; $i++) { $mi = MenuItemByName $item; if ($mi) { break }; Start-Sleep -Milliseconds 250 }
  if (-not $mi) { return "menu item '$item' not found" }
  MenuInsideWindow "capture dropdown ($item)"
  if (-not (InvokeEl $mi "capture menu item $item")) { return 'invoke failed' }
  $script:sawScanning = $false; $swS = [Diagnostics.Stopwatch]::StartNew()  # the scan now runs off the UI thread with a visible scanning state
  while ($swS.Elapsed.TotalMilliseconds -lt 1500) { if (@(Texts) | Where-Object { $_ -like '*סורק*' }) { $script:sawScanning = $true; break }; Start-Sleep -Milliseconds 200 }
  return 'ok'
}
try {
  $root = Join-Path $env:RUNNER_TEMP 'cap-flow'; New-Item -ItemType Directory -Force $root | Out-Null
  $capdir = Join-Path $root 'capfolder'; New-Item -ItemType Directory -Force $capdir | Out-Null
  $storeDir = Join-Path $env:LOCALAPPDATA 'StartApp'; if (Test-Path $storeDir) { Remove-Item -Recurse -Force $storeDir }
  $store = Join-Path $storeDir 'store.json'
  # Real things to detect: a Notepad window, a File Explorer window on a known folder, and (best effort, reported as info only) an Edge window.
  $np = Start-Process notepad.exe -PassThru
  Start-Process explorer.exe -ArgumentList "`"$capdir`""
  $edge = $null; try { $edge = Start-Process msedge -ArgumentList '--new-window', '--no-first-run', '--no-default-browser-check', 'https://example.com/', 'https://example.org/', 'https://example.net/' -PassThru } catch { Info 'edge start' "$_" }
  Start-Sleep -Seconds 8
  $edgeTabBefore = -1; try { $edgeTabBefore = EdgeSelectedTabIndex } catch { }; Info 'edge active tab index before scans' "$edgeTabBefore"
  # Regression (PR19 review): restore must also work when the user's tab is NOT first in UIA enumeration -
  # the old code discovered the original tab inside the mutating loop and lost it. Move the one bound Edge
  # window to its middle tab (index 1 of the three example.* tabs) before the app starts; setup, before and
  # after all read the SAME window, and the post-scan assert below proves restore for the active-middle case.
  $moved = $false
  $wE = EdgeWindow
  if ($null -ne $wE) {
    try {
      $tabsE = $wE.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::TabItem)))
      if ($tabsE.Count -ge 3) { $tabsE[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $moved = $true }
    } catch { }
  }
  Start-Sleep -Milliseconds 800
  $edgeTabBefore = -1; try { $edgeTabBefore = EdgeSelectedTabIndex } catch { }
  Assert 'regression setup: Edge active tab moved to the middle (index 1) before the scans' ($moved -and $edgeTabBefore -eq 1) ("moved=$moved before=$edgeTabBefore")
  $exe = Get-ChildItem -Recurse src/StartApp.App/bin/Release -Filter StartApp.App.exe | Select-Object -First 1
  $p = Start-Process -FilePath $exe.FullName -PassThru
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.Elapsed.TotalSeconds -lt 60) { $p.Refresh(); if ($p.HasExited -or $p.MainWindowHandle -ne 0) { break }; Start-Sleep -Milliseconds 500 }
  Start-Sleep -Seconds 2
  Assert 'app window opened' (-not $p.HasExited -and $p.MainWindowHandle -ne 0) ''
  $win = $A::RootElement.FindFirst($TS::Children, (New-Object $PC($A::ProcessIdProperty, $p.Id)))
  Assert 'automation window found' ($null -ne $win) ''
  $vsB = [System.Windows.Forms.SystemInformation]::VirtualScreen
  Info 'virtual screen bounds (measured)' "left=$($vsB.Left) top=$($vsB.Top) right=$($vsB.Right) bottom=$($vsB.Bottom) ($($vsB.Width)x$($vsB.Height))"
  [void][W]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, $vsB.Left, $vsB.Top, 0, 0, 0x0001)
  Start-Sleep -Milliseconds 600
  $wr0 = (CurWin).Current.BoundingRectangle
  Assert 'app window fully inside the virtual screen' ($wr0.Left -ge $vsB.Left -and $wr0.Top -ge $vsB.Top -and $wr0.Right -le $vsB.Right -and $wr0.Bottom -le $vsB.Bottom) ("window=$([int]$wr0.Left),$([int]$wr0.Top)-$([int]$wr0.Right),$([int]$wr0.Bottom) screen=$($vsB.Left),$($vsB.Top)-$($vsB.Right),$($vsB.Bottom)")
  Assert 'overview shown' (WaitText 'Create workspace' 10) ((Texts) -join ' | ')
  Assert 'capture button is on the overview page' ($null -ne (Btn 'Capture')) ''
  foreach ($n in 'Cap A', 'Cap B') { SetText (NamedEdit 'Workspace name') $n; $c = ClickBtn 'Create workspace' 900; Assert "create $n" ($c -eq 'ok') $c; $c = ClickBtn 'Back' 900; Assert "back after $n" ($c -eq 'ok') $c }

  # 1. dropdown -> new workspace: real scan, all rows ticked, read-only list
  $c = OpenCaptureMenu 'סביבה חדשה'; Assert 'capture dropdown -> סביבה חדשה' ($c -eq 'ok') $c
  $shown = WaitText 'Open now' 10; Assert 'capture screen shown' $shown ((Texts) -join ' | ')
  if (-not $shown) {
    DumpState 'no-capture-screen'
    'capture screen never appeared; every later capture step depends on it, so the flow stops here (fail fast, no fake cascade)' | Out-File -Append diag/errors.txt
    $results | ConvertTo-Json -Depth 4 | Out-File diag/capture-assertions.json
    exit 1
  }
  Assert 'scanning state was visible while the scan ran' $script:sawScanning ''
  Assert 'scan finished before the rows are read' (WaitGone 'סורק' 30) ((Texts) -join ' | ')
  $names = CheckNames
  Info 'detected rows (new form)' ($names -join ' | ')
  $npRow = CheckByLike '*Notepad*'; $folderRow = CheckByLike '*capfolder*'
  Assert 'REAL scan: the open Notepad window is listed as a program' ($null -ne $npRow) ($names -join ' | ')
  Assert 'REAL scan: the open Explorer window on the test folder is listed as a folder' ($null -ne $folderRow) ($names -join ' | ')
  Assert 'ALL TABS: the active tab site example.com is listed' ($null -ne (CheckByLike '*example.com*')) ($names -join ' | ')
  Assert 'ALL TABS: background tab example.org is listed' ($null -ne (CheckByLike '*example.org*')) ($names -join ' | ')
  Assert 'ALL TABS: background tab example.net is listed' ($null -ne (CheckByLike '*example.net*')) ($names -join ' | ')
  $edgeTabAfter = EdgeSelectedTabIndex
  Assert 'ALL TABS: the tab the user was on stays selected after the scan' ($edgeTabAfter -eq $edgeTabBefore) ("before=$edgeTabBefore after=$edgeTabAfter")
  $fgPid = [uint32]0; [void][W]::GetWindowThreadProcessId([W]::GetForegroundWindow(), [ref]$fgPid)
  Info 'foreground window after the all-tabs scan' $(if ($fgPid -eq $p.Id) { 'app (scan did not raise the browser)' } elseif ($edge -and -not $edge.HasExited -and $fgPid -eq $edge.Id) { 'edge - the scan raises the scanned browser window' } else { "another window (pid $fgPid)" })
  ShotRaw 'post-scan-before-raise'
  Assert 'folder address shown under its name' (WaitText $capdir 5) ((Texts) -join ' | ')
  Assert 'new-workspace mode: every row ticked by default' (@(Checks | Where-Object { -not (IsOn $_) }).Count -eq 0 -and (Checks).Count -gt 0) ($names -join ' | ')
  Assert 'detection notes are shown (partial coverage is stated)' (WaitText 'Files: documents open inside programs are not detected' 3) ((Texts) -join ' | ')
  Assert 'sites note states the verified scope (hosted-runner: Edge+Chrome single window, Hebrew UI) and the remaining limits' ((WaitText 'verified in hosted-runner scenarios on Edge and Chrome with a single window' 3) -and (WaitText 'Brave, multiple windows and discarded tabs are not verified' 3)) ((Texts) -join ' | ')
  Assert 'capture rows have no action menu or edit button (read-only)' ($null -eq (BtnLike 'Item actions')) ''
  Shot 'new-form-with-scan'
  if ($npRow) { [void](ToggleEl $npRow 'notepad row'); Start-Sleep -Milliseconds 400 }
  Assert 'unticking works' ($npRow -and -not (IsOn $npRow)) ''
  Assert 'create button needs a name' (-not (Btn 'Create workspace from capture').Current.IsEnabled) ''
  SetText (NamedEdit 'Capture workspace name') 'From capture'
  $c = ClickBtn 'Create workspace from capture' 1200; Assert 'create from capture clicked' ($c -eq 'ok') $c
  Assert 'details opened after create (spec: after creation details open)' (WaitText 'Created:' 15) ((Texts) -join ' | ')
  Assert 'capture screen closed after create' (-not (WaitText 'Open now' 2)) ''
  $sj = StoreJson
  Assert 'store holds the captured folder' ($sj.Contains('capfolder')) $sj
  Assert 'store does NOT hold the unticked Notepad program' (-not $sj.ToLower().Contains('notepad.exe')) $sj
  Assert 'the captured folder is listed in the new workspace' (WaitText 'capfolder' 5) ((Texts) -join ' | ')
  Shot 'created-from-capture-details'
  $c = ClickBtn 'Back' 900; Assert 'back to overview' ($c -eq 'ok') $c

  # all-tabs evidence is captured; collapse Edge to the single-active-tab shape so the
  # existing-workspace flows below stage exactly the row set their exact-count asserts were built for
  Get-Process msedge -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 2
  $edge = $null; try { $edge = Start-Process msedge -ArgumentList '--new-window', '--no-first-run', '--no-default-browser-check', '--hide-crash-restore-bubble', 'https://example.com/' -PassThru } catch { Info 'edge relaunch (single tab)' "$_" }
  Start-Sleep -Seconds 4
  Assert 'edge relaunched with a single tab for the workspace flows' ($null -ne (Get-Process msedge -ErrorAction SilentlyContinue)) ''

  # 2. dropdown -> existing workspace: search, side by side, staged until Save
  $c = OpenCaptureMenu 'סביבה קיימת'; Assert 'capture dropdown -> סביבה קיימת' ($c -eq 'ok') $c
  Assert 'workspace search shown' ((WaitText 'Open now' 10) -and $null -ne (NamedEdit 'Capture workspace search')) ''
  Info 'scanning state observed (existing)' $(if ($script:sawScanning) { 'yes' } else { 'not seen - scan faster than the poll' })
  Assert 'existing-mode scan finished' (WaitGone 'סורק' 30) ((Texts) -join ' | ')
  Assert 'existing mode: rows start unticked' (@(Checks | Where-Object { IsOn $_ }).Count -eq 0 -and (Checks).Count -gt 0) ''
  SetText (NamedEdit 'Capture workspace search') 'Cap B'
  Start-Sleep -Milliseconds 600
  Shot 'pick-workspace-search'
  $c = ClickBtn 'Pick workspace Cap B' 1000; Assert 'picked Cap B' ($c -eq 'ok') $c
  Assert 'side by side: target workspace named' ((CaptureTargetText) -eq 'Cap B') (CaptureTargetText)
  Assert 'add button reads הוסף הכל while nothing is ticked' ($null -ne (Btn 'הוסף הכל')) ''
  $folderRow = CheckByLike '*capfolder*'; [void](ToggleEl $folderRow 'folder row'); Start-Sleep -Milliseconds 500
  Assert 'add button becomes הוסף נבחרים when a row is ticked' ($null -ne (Btn 'הוסף נבחרים')) ''
  $c = ClickBtn 'הוסף נבחרים' 900; Assert 'add selected clicked' ($c -eq 'ok') $c
  Assert 'staged row appears with editable name' ($null -ne (NamedEdit 'Added item name')) ''
  Assert 'staged item has the default name (the original)' ((ValOf (NamedEdit 'Added item name')) -eq 'capfolder') (ValOf (NamedEdit 'Added item name'))
  SetText (NamedEdit 'Added item name') 'my capture folder'; SetText (NamedEdit 'Added item description') 'added from capture'
  Assert 'NOT saved yet: store has no such instance name' (-not (StoreJson).Contains('my capture folder')) (StoreJson)
  Shot 'side-by-side-staged'

  # 2b. settled rule: a staged item offers the FULL item action set, staged until Save, the source list unchanged
  $rowCount = (Checks).Count
  $folderRow = CheckByLike '*capfolder*'; [void](ToggleEl $folderRow 'folder row untick'); Start-Sleep -Milliseconds 400
  $npRow = CheckByLike '*Notepad*'; Assert 'Notepad row present for a two-program staging' ($null -ne $npRow) ((CheckNames) -join ' | ')
  [void](ToggleEl $npRow 'notepad row'); Start-Sleep -Milliseconds 400
  $termRow = CheckByLike '*Terminal*'; Assert 'Windows Terminal Host row present' ($null -ne $termRow) ((CheckNames) -join ' | ')
  [void](ToggleEl $termRow 'terminal row'); Start-Sleep -Milliseconds 500
  $c = ClickBtn 'הוסף נבחרים' 900; Assert 'two programs staged beside the folder' ($c -eq 'ok' -and @(StagedNames).Count -eq 3) $c
  $c = ClickBtn 'Staged item actions' 900; Assert 'staged item actions menu opened' ($c -eq 'ok') $c
  $sfound = @('מחיקה','העתק כתובת','עריכת כתובת','פתח באמצעות','הזז למעלה','הזז למטה') | Where-Object { $null -ne (MenuItemByName $_) }
  Assert 'staged menu shows the six settled entries' (@($sfound).Count -eq 6) ("found: $($sfound -join ', ')")
  foreach ($h in @('מחיקה','העתק כתובת','עריכת כתובת','פתח באמצעות','הזז למעלה','הזז למטה')) { $e = MenuItemByName $h; Assert "staged menu entry actually visible: $h" ($null -ne $e -and -not $e.Current.IsOffscreen) '' }
  MenuInsideWindow 'staged item menu'
  $shareMi = MenuItemByName 'שיתוף לסביבה אחרת'
  Assert 'שיתוף לסביבה אחרת visible but disabled (action undefined) with another workspace present' ($null -ne $shareMi -and -not $shareMi.Current.IsOffscreen -and -not $shareMi.Current.IsEnabled) ("found=$($null -ne $shareMi) enabled=$(if ($shareMi) { $shareMi.Current.IsEnabled } else { 'n/a' })")
  Shot 'side-by-side-staged-menu'
  $mi = MenuItemByName 'הזז למטה'; [void](InvokeEl $mi 'folder at its group edge'); Start-Sleep -Milliseconds 600
  Assert 'הזז למטה at the group edge changes nothing (four-group rule)' (@(StagedNames)[0] -eq 'my capture folder') ((StagedNames) -join ' | ')
  $before = @(StagedNames)
  $btns = @(StagedActionBtns); Assert 'three staged action buttons' ($btns.Count -eq 3) "$($btns.Count)"
  [void](InvokeEl $btns[1] 'first program actions'); Start-Sleep -Milliseconds 800
  Assert 'פתח באמצעות hidden on a program item (it is the program)' ($null -eq (MenuItemByName 'פתח באמצעות')) ''
  $mi = MenuItemByName 'הזז למטה'; [void](InvokeEl $mi 'program move down'); Start-Sleep -Milliseconds 700
  $after = @(StagedNames)
  Assert 'הזז למטה swaps within the Programs group only' ($after.Count -eq 3 -and $after[0] -eq $before[0] -and $after[1] -eq $before[2] -and $after[2] -eq $before[1]) ($after -join ' | ')
  $btns = @(StagedActionBtns)
  [void](InvokeEl $btns[2] 'moved program actions'); Start-Sleep -Milliseconds 800
  $mi = MenuItemByName 'עריכת כתובת'; [void](InvokeEl $mi 'staged address edit entry'); Start-Sleep -Milliseconds 800
  $addr = NamedEdit 'Full address'; $rowAddr = @(StagedAddrs)[2]
  Assert 'address popup opened on the staged row with its full address' ($null -ne $addr -and (ValOf $addr) -eq $rowAddr) ("popup=$(ValOf $addr) row=$rowAddr")
  Assert 'copy button inside the popup' ($null -ne (AnyByName 'Copy full address')) ''
  SetText $addr 'C:\Windows\System32\notepad.exe'
  $c = ClickBtn 'שמור' 900; Assert 'address saved on the staged row' ($c -eq 'ok') $c
  Assert 'staged row took the new address, store unchanged' (@(StagedAddrs)[2] -eq 'C:\Windows\System32\notepad.exe' -and -not (StoreJson).Contains('System32')) ((StagedAddrs) -join ' | ')
  $btns = @(StagedActionBtns)
  [void](InvokeEl $btns[2] 'moved program actions for copy'); Start-Sleep -Milliseconds 800
  $mi = MenuItemByName 'העתק כתובת'; [void](InvokeEl $mi 'staged copy address'); Start-Sleep -Milliseconds 700
  Assert 'העתק כתובת on a staged row reports the copy' (WaitText 'Address copied' 5) ((Texts) -join ' | ')
  $btns = @(StagedActionBtns)
  [void](InvokeEl $btns[2] 'moved program actions for delete'); Start-Sleep -Milliseconds 800
  $mi = MenuItemByName 'מחיקה'; [void](InvokeEl $mi 'staged delete moved program'); Start-Sleep -Milliseconds 700
  Assert 'מחיקה removes that staged row only' (@(StagedNames).Count -eq 2) ((StagedNames) -join ' | ')
  $btns = @(StagedActionBtns)
  [void](InvokeEl $btns[1] 'other program actions for delete'); Start-Sleep -Milliseconds 800
  $mi = MenuItemByName 'מחיקה'; [void](InvokeEl $mi 'staged delete other program'); Start-Sleep -Milliseconds 700
  Assert 'second delete leaves the folder row only' (@(StagedNames).Count -eq 1 -and @(StagedNames)[0] -eq 'my capture folder') ((StagedNames) -join ' | ')
  $c = ClickBtn 'Staged item actions' 900; Assert 'staged menu reopened for open-with' ($c -eq 'ok') $c
  $mi = MenuItemByName 'פתח באמצעות'; [void](InvokeEl $mi 'staged open-with entry'); Start-Sleep -Milliseconds 800
  $prog = NamedEdit 'Program path'; Assert 'open-with popup opened on a staged row' ($null -ne $prog) ''
  SetText $prog 'C:\Windows\notepad.exe'
  $cb = AnyByName 'Set as default'; [void](ToggleEl $cb 'set-as-default checkbox'); Start-Sleep -Milliseconds 300
  $npBefore = @(Get-Process notepad -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
  $c = ClickBtn 'Open with chosen program' 900; Assert 'staged open-with go clicked' ($c -eq 'ok') $c
  Assert 'staged open-with sent the request' (WaitText 'open request sent' 15) ((Texts) -join ' | ')
  Assert 'the chosen default is staged on the row, NOT in the store' ((ValOf (NamedEdit 'Added item open with')) -like '*notepad.exe' -and -not (StoreJson).Contains('notepad.exe')) ("field=$(ValOf (NamedEdit 'Added item open with'))")
  $npNew = @(Get-Process notepad -ErrorAction SilentlyContinue | Where-Object { $npBefore -notcontains $_.Id })
  Assert 'staged open-with really started a new notepad process' ($npNew.Count -gt 0) ("before=$($npBefore.Count) now=$((@(Get-Process notepad -ErrorAction SilentlyContinue)).Count)")
  $npNew | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 1
  Assert 'the source capture list is unchanged by all staged actions' ((Checks).Count -eq $rowCount -and $null -ne (CheckByLike '*capfolder*')) ("rows=$((Checks).Count) before=$rowCount")
  # restore the tick state the duplicate flow below expects: only the saved folder ticked
  $npRow2 = CheckByLike '*Notepad*'; [void](ToggleEl $npRow2 'notepad row untick'); Start-Sleep -Milliseconds 300
  $termRow2 = CheckByLike '*Terminal*'; [void](ToggleEl $termRow2 'terminal row untick'); Start-Sleep -Milliseconds 300
  $folderRow = CheckByLike '*capfolder*'; [void](ToggleEl $folderRow 'folder row retick'); Start-Sleep -Milliseconds 400
  Assert 'tick state restored for the duplicate flow: only the saved folder ticked' (@(Checks | Where-Object { IsOn $_ }).Count -eq 1) ((CheckNames) -join ' | ')

  $c = ClickBtn 'Capture save' 1200; Assert 'save clicked' ($c -eq 'ok') $c
  $sj = StoreJson
  Assert 'after Save the instance name, description and staged open-with default are persisted' ($sj.Contains('my capture folder') -and $sj.Contains('added from capture') -and $sj.Contains('notepad.exe')) $sj
  Assert 'after Save the capture list stays open' ((Checks).Count -gt 0 -and $null -ne (Btn 'Capture save')) ''
  Assert 'staged area is empty again after Save' ($null -eq (NamedEdit 'Added item name')) ''
  Shot 'side-by-side-after-save'

  # 2c. REAL pointer drag/drop: stage by dragging from the capture list; the per-drag duplicate warning fires per drag
  $storeBeforeDrag = StoreJson
  DragRowToStage '*example.com*' 'site row'
  Assert 'real drag stages the site with its default name' ((ValOf (NamedEdit 'Added item name')) -eq 'example.com') (ValOf (NamedEdit 'Added item name'))
  Assert 'drag stages on screen only: store unchanged' ((StoreJson) -eq $storeBeforeDrag) ''
  $exRow = CheckByLike '*example.com*'; Assert 'drag does not tick or remove the source row' ($null -ne $exRow -and -not (IsOn $exRow)) ''
  Shot 'side-by-side-drag-staged'
  $c = ClickBtn 'Remove added example.com' 800; Assert 'staged drag row removed' ($c -eq 'ok' -and $null -eq (NamedEdit 'Added item name')) $c
  DragRowToStage '*capfolder*' 'saved folder row'
  Assert 'dragging an existing item shows the per-drag duplicate warning' (WaitText 'already in this workspace' 5) ((Texts) -join ' | ')
  Shot 'drag-duplicate-warning'
  $c = ClickBtn 'Capture duplicate cancel' 800; Assert 'cancel keeps the duplicate out' ($c -eq 'ok' -and $null -eq (NamedEdit 'Added item name')) $c
  DragRowToStage '*capfolder*' 'saved folder row again'
  Assert 'the per-drag warning fires for every drag, not once' (WaitText 'already in this workspace' 5) ((Texts) -join ' | ')
  $c = ClickBtn 'Capture duplicate add anyway' 900; Assert 'add anyway stages the duplicate as a second instance' ($c -eq 'ok' -and (ValOf (NamedEdit 'Added item name')) -eq 'capfolder') $c
  Assert 'the drag-confirmed duplicate is still unsaved' (([regex]::Matches((StoreJson), 'my capture folder')).Count -eq 1) (StoreJson)
  $c = ClickBtn 'Remove added capfolder' 800; Assert 'drag cleanup: nothing staged for the grouped flow below' ($c -eq 'ok' -and $null -eq (NamedEdit 'Added item name')) $c

  # 3. duplicates: add the ticked folder again -> one grouped dialog
  $c = ClickBtn 'הוסף נבחרים' 900; Assert 'add selected again' ($c -eq 'ok') $c
  Assert 'grouped duplicate dialog shown' (WaitText 'Already in this workspace' 5) ((Texts) -join ' | ')
  Assert 'dialog lists the duplicate with its own checkbox' ($null -ne (CheckByLike 'Add again: capfolder*')) ((CheckNames) -join ' | ')
  Shot 'duplicate-dialog'
  $c = ClickBtn 'Capture duplicates skip' 900; Assert 'skip duplicates' ($c -eq 'ok') $c
  Assert 'nothing staged after skipping' ($null -eq (NamedEdit 'Added item name')) ''
  $c = ClickBtn 'הוסף נבחרים' 900; Assert 'add selected a third time' ($c -eq 'ok') $c
  $dup = CheckByLike 'Add again: capfolder*'; [void](ToggleEl $dup 'dup checkbox'); Start-Sleep -Milliseconds 300
  $c = ClickBtn 'Capture duplicates add ticked' 900; Assert 'add ticked duplicate' ($c -eq 'ok') $c
  Assert 'the ticked duplicate is staged as a second instance' ($null -ne (NamedEdit 'Added item name')) ''

  # 4. back with unsaved staging -> guard; exit without saving keeps the snapshot; earlier save stays
  $c = ClickBtn 'Capture back' 1000; Assert 'back clicked' ($c -eq 'ok') $c
  Assert 'unsaved-changes dialog on back' (WaitText 'Unsaved changes' 5) ((Texts) -join ' | ')
  Shot 'back-unsaved-dialog'
  $c = ClickBtn 'צא בלי לשמור' 1000; Assert 'exit without saving' ($c -eq 'ok') $c
  Assert 'back at the workspace search with the capture list still there' ($null -ne (NamedEdit 'Capture workspace search') -and (Checks).Count -gt 0) ''
  Assert 'the discarded duplicate was not saved (only the first save exists)' (([regex]::Matches((StoreJson), 'my capture folder')).Count -eq 1) (StoreJson)

  # 4a. שמור וצא: the unsaved guard's save path persists the staged row and returns to the search
  SetText (NamedEdit 'Capture workspace search') 'Cap B'
  Start-Sleep -Milliseconds 600
  $c = ClickBtn 'Pick workspace Cap B' 1000; Assert 'picked Cap B for save-and-exit' ($c -eq 'ok') $c
  foreach ($cbx in @(Checks | Where-Object { IsOn $_ })) { [void](ToggleEl $cbx 'untick all before save-and-exit'); Start-Sleep -Milliseconds 200 }
  Assert 'nothing ticked before save-and-exit (tick state does not leak between sections)' (@(Checks | Where-Object { IsOn $_ }).Count -eq 0) ((CheckNames) -join ' | ')
  $npTick = CheckByLike '*Notepad*'; [void](ToggleEl $npTick 'notepad tick for save-and-exit'); Start-Sleep -Milliseconds 400
  $c = ClickBtn 'הוסף נבחרים' 900; Assert 'notepad staged for save-and-exit' ($c -eq 'ok' -and (@(StagedNames) -contains 'Notepad')) $c
  Assert 'no duplicate dialog for the single non-duplicate stage' ($null -eq (CheckByLike 'Add again: *')) ((CheckNames) -join ' | ')
  $c = ClickBtn 'Capture back' 1000; Assert 'back with an unsaved staged row' ($c -eq 'ok') $c
  Assert 'unsaved dialog offers the save-and-exit path' ((WaitText 'Unsaved changes' 5) -and $null -ne (Btn 'שמור וצא')) ((Texts) -join ' | ')
  $c = ClickBtn 'שמור וצא' 1500; Assert 'save-and-exit clicked' ($c -eq 'ok') $c
  Assert 'save-and-exit returns to the workspace search' ($null -ne (NamedEdit 'Capture workspace search')) ((Texts) -join ' | ')
  $o = StoreObj
  $npId = ($o.items.PSObject.Properties.Value | Where-Object { $_.target -like '*notepad.exe' } | Select-Object -First 1).id
  $wsB = @($o.workspaces | Where-Object { $_.name -eq 'Cap B' })[0]
  Assert 'save-and-exit persisted the staged row to Cap B' (@($wsB.items | Where-Object { $_.itemId -eq $npId }).Count -eq 1) (StoreJson)
  Shot 'save-and-exit-back-at-search'

  # 4b. הוסף הכל for real: non-duplicates stage at once, ALL duplicates gather in ONE grouped dialog, nothing saved before the choice
  SetText (NamedEdit 'Capture workspace search') 'Cap B'
  Start-Sleep -Milliseconds 600
  $c = ClickBtn 'Pick workspace Cap B' 1000; Assert 'picked Cap B for add-all' ($c -eq 'ok') $c
  foreach ($cbx in @(Checks | Where-Object { IsOn $_ })) { [void](ToggleEl $cbx 'untick before add-all'); Start-Sleep -Milliseconds 200 }
  Assert 'nothing ticked before add-all' (@(Checks | Where-Object { IsOn $_ }).Count -eq 0) ((CheckNames) -join ' | ')
  $storeBeforeAll = StoreJson
  $c = ClickBtn 'הוסף הכל' 1500; Assert 'add-all clicked' ($c -eq 'ok') $c
  $stagedAll = @(StagedNames)
  Assert 'add-all staged the non-duplicate terminal at once' ($stagedAll -contains 'Windows Terminal Host') ($stagedAll -join ' | ')
  Assert 'duplicates not staged before the dialog choice' (($stagedAll -notcontains 'capfolder') -and ($stagedAll -notcontains 'Notepad')) ($stagedAll -join ' | ')
  Assert 'ONE grouped dialog for BOTH duplicates' (WaitText 'Already in this workspace' 5) ((Texts) -join ' | ')
  Assert 'dialog lists each duplicate with its own checkbox' (($null -ne (CheckByLike 'Add again: capfolder*')) -and ($null -ne (CheckByLike 'Add again: Notepad*'))) ((CheckNames) -join ' | ')
  Assert 'dialog does NOT list the non-duplicates' ($null -eq (CheckByLike 'Add again: *Terminal*')) ((CheckNames) -join ' | ')
  Assert 'add-all changed nothing in the store' ((StoreJson) -eq $storeBeforeAll) ''
  Shot 'add-all-grouped-dialog'
  $c = ClickBtn 'Capture duplicates skip' 900; Assert 'skip both duplicates' ($c -eq 'ok') $c
  Assert 'non-duplicates stay staged after skipping' (@(StagedNames) -contains 'Windows Terminal Host') ((StagedNames) -join ' | ')
  foreach ($sn in @(StagedNames)) { $c = ClickBtn "Remove added $sn" 800; Assert "remove staged $sn before the selective pass" ($c -eq 'ok') $c }
  $c = ClickBtn 'הוסף הכל' 1500; Assert 'add-all again for the selective pass' ($c -eq 'ok') $c
  Assert 'grouped dialog again for the selective pass' (WaitText 'Already in this workspace' 5) ((Texts) -join ' | ')
  $dupF = CheckByLike 'Add again: capfolder*'; [void](ToggleEl $dupF 'tick only the folder duplicate'); Start-Sleep -Milliseconds 300
  $c = ClickBtn 'Capture duplicates add ticked' 900; Assert 'add only the ticked duplicate' ($c -eq 'ok') $c
  $nameEdits = @((CurWin).FindAll($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::Edit)), (New-Object $PC($A::NameProperty, 'Added item name'))))))
  $addrEdits = @((CurWin).FindAll($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::Edit)), (New-Object $PC($A::NameProperty, 'Added item address'))))))
  $fidx = -1; for ($i = 0; $i -lt $addrEdits.Count; $i++) { if ((ValOf $addrEdits[$i]) -like '*capfolder*') { $fidx = $i; break } }
  $stagedAddrsNow = @($addrEdits | ForEach-Object { ValOf $_ })
  Assert 'the ticked duplicate staged, the unticked duplicate did not' ($fidx -ge 0 -and -not @($stagedAddrsNow | Where-Object { $_ -like '*notepad.exe' })) ($stagedAddrsNow -join ' | ')
  Assert 'the approved duplicate staged with the capture default name, not a local one' ((ValOf $nameEdits[$fidx]) -eq 'capfolder') (ValOf $nameEdits[$fidx])
  foreach ($sn in @(StagedNames | Where-Object { $_ -ne 'capfolder' })) { $c = ClickBtn "Remove added $sn" 800; Assert "remove non-folder staged $sn before the selective save" ($c -eq 'ok') $c }
  Assert 'only the folder remains staged for the selective save' ((@(StagedNames) -join '|') -eq 'capfolder') ((StagedNames) -join ' | ')
  SetText (NamedEdit 'Added item name') 'capfolder second'
  $c = ClickBtn 'Capture save' 1500; Assert 'save the selective add' ($c -eq 'ok') $c
  Assert 'the selective save saved exactly one row' (WaitText 'Saved 1 item' 3) ((Texts) -join ' | ')
  $o = StoreObj
  $wsB = @($o.workspaces | Where-Object { $_.name -eq 'Cap B' })[0]
  $capItemId = ($o.items.PSObject.Properties.Value | Where-Object { $_.target -like '*capfolder*' } | Select-Object -First 1).id
  $bFolderNames = @($wsB.items | Where-Object { $_.itemId -eq $capItemId } | ForEach-Object { $_.name })
  Assert 'the approved duplicate saved as an independent second instance (own name, first untouched)' ($bFolderNames.Count -eq 2 -and $bFolderNames -contains 'my capture folder' -and $bFolderNames -contains 'capfolder second') ($bFolderNames -join ' | ')
  Assert 'Cap B holds exactly the folder twice plus Notepad (no extra rows saved)' (@($wsB.items).Count -eq 3) ((@($wsB.items | ForEach-Object { $_.name })) -join ' | ')

  # 4c. between workspaces: no false warning when the address lives only elsewhere, both keep it, list identical across switches, exit-without-save keeps every save, re-add gets capture defaults
  $c = ClickBtn 'Capture back' 1000; Assert 'back to search after the selective save' ($c -eq 'ok' -and $null -ne (NamedEdit 'Capture workspace search')) $c
  SetText (NamedEdit 'Capture workspace search') 'Cap A'
  Start-Sleep -Milliseconds 600
  $c = ClickBtn 'Pick workspace Cap A' 1000; Assert 'picked Cap A' ($c -eq 'ok') $c
  $namesBeforeSwitch = @(CheckNames)
  DragRowToStage '*capfolder*' 'folder row into Cap A'
  Assert 'no false duplicate warning when the address exists only in another workspace' (-not (WaitText 'already in this workspace' 2)) ((Texts) -join ' | ')
  Shot '4c-after-drag-cap-a'
  Assert 'the dragged folder staged in Cap A with the capture default name' ((ValOf (NamedEdit 'Added item name')) -eq 'capfolder') (ValOf (NamedEdit 'Added item name'))
  $c = ClickBtn 'Capture save' 1200; Assert 'saved into Cap A' ($c -eq 'ok') $c
  $o = StoreObj
  $capItemId = ($o.items.PSObject.Properties.Value | Where-Object { $_.target -like '*capfolder*' } | Select-Object -First 1).id
  $wsA = @($o.workspaces | Where-Object { $_.name -eq 'Cap A' })[0]
  $wsB = @($o.workspaces | Where-Object { $_.name -eq 'Cap B' })[0]
  Assert 'both workspaces now hold the captured folder as separate instances' ((@($wsA.items | Where-Object { $_.itemId -eq $capItemId })).Count -eq 1 -and (@($wsB.items | Where-Object { $_.itemId -eq $capItemId })).Count -eq 2) (StoreJson)
  $storeBeforeDiscard = StoreJson
  $npTick2 = CheckByLike '*Notepad*'; [void](ToggleEl $npTick2 'notepad tick for the discard test'); Start-Sleep -Milliseconds 400
  $c = ClickBtn 'הוסף נבחרים' 900; Assert 'notepad staged in Cap A for the discard test' ($c -eq 'ok' -and (@(StagedNames) -contains 'Notepad')) $c
  $c = ClickBtn 'Capture back' 1000; Assert 'back with the staged notepad' ($c -eq 'ok') $c
  $c = ClickBtn 'צא בלי לשמור' 1000; Assert 'exit without saving from Cap A' ($c -eq 'ok') $c
  Assert 'exit without saving changed nothing: every earlier save in both workspaces intact' ((StoreJson) -eq $storeBeforeDiscard) (StoreJson)
  $npUntick = CheckByLike '*Notepad*'; if ($npUntick -and (IsOn $npUntick)) { [void](ToggleEl $npUntick 'notepad untick after discard'); Start-Sleep -Milliseconds 300 }
  SetText (NamedEdit 'Capture workspace search') 'Cap B'
  Start-Sleep -Milliseconds 600
  $c = ClickBtn 'Pick workspace Cap B' 1000; Assert 'picked Cap B again' ($c -eq 'ok') $c
  Assert 'the capture list is identical after switching workspaces' ((@(CheckNames) -join '|') -eq ($namesBeforeSwitch -join '|')) ((CheckNames) -join ' | ')
  $folderTick = CheckByLike '*capfolder*'; [void](ToggleEl $folderTick 'folder tick for re-add'); Start-Sleep -Milliseconds 400
  $c = ClickBtn 'הוסף נבחרים' 900; Assert 're-add the folder to Cap B' ($c -eq 'ok') $c
  Assert 'duplicate dialog for the re-add' (WaitText 'Already in this workspace' 5) ((Texts) -join ' | ')
  $dupR = CheckByLike 'Add again: capfolder*'; [void](ToggleEl $dupR 'tick the re-add duplicate'); Start-Sleep -Milliseconds 300
  $c = ClickBtn 'Capture duplicates add ticked' 900; Assert 'add the re-add duplicate' ($c -eq 'ok') $c
  Assert 'the re-added instance gets the capture default name, not a locally renamed one' ((ValOf (NamedEdit 'Added item name')) -eq 'capfolder') (ValOf (NamedEdit 'Added item name'))
  $c = ClickBtn 'Remove added capfolder' 800; Assert 're-add cleanup' ($c -eq 'ok' -and $null -eq (NamedEdit 'Added item name')) $c
  $folderUntick = CheckByLike '*capfolder*'; if ($folderUntick -and (IsOn $folderUntick)) { [void](ToggleEl $folderUntick 'folder untick after re-add'); Start-Sleep -Milliseconds 300 }
  $c = ClickBtn 'Capture back' 1000
  if (WaitText 'Unsaved changes' 2) { $c2 = ClickBtn 'צא בלי לשמור' 1000; Assert 'discard after the re-add cleanup' ($c2 -eq 'ok') $c2 }
  Assert 'back at the search for the reset flow' ($null -ne (NamedEdit 'Capture workspace search')) ((Texts) -join ' | ')

  # 5. X resets; a new scan reflects what is open at that moment (Notepad closed first)
  $c = ClickBtn 'Close capture' 900; Assert 'X clicked' ($c -eq 'ok') $c
  Assert 'capture screen closed' (-not (WaitText 'Open now' 2)) ''
  Assert 'capture list was cleared on close' ((Checks).Count -eq 0) ''
  Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 2
  # back to three tabs so the details rescan re-proves background-tab detection
  Get-Process msedge -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 2
  $edge = $null; try { $edge = Start-Process msedge -ArgumentList '--new-window', '--no-first-run', '--no-default-browser-check', '--hide-crash-restore-bubble', 'https://example.com/', 'https://example.org/', 'https://example.net/' -PassThru } catch { Info 'edge relaunch (three tabs)' "$_" }
  Start-Sleep -Seconds 4
  Assert 'edge relaunched with three tabs for the details rescan' ($null -ne (Get-Process msedge -ErrorAction SilentlyContinue)) ''
  # entry from the workspace details screen
  $c = ClickBtn 'Cap A' 1000; Assert 'open Cap A details' ($c -eq 'ok') $c
  Assert 'details screen shown' (WaitText 'Created:' 10) ''
  $c = ClickBtn 'Add from open items' 1500; Assert 'add-from-open clicked' ($c -eq 'ok') $c
  Assert 'goes straight to side by side for the current workspace' ((WaitText 'Open now' 10) -and $null -ne (Btn 'Capture save')) ((Texts) -join ' | ')
  $script:sawScanning = $false; $swS = [Diagnostics.Stopwatch]::StartNew()
  while ($swS.Elapsed.TotalMilliseconds -lt 2000) { if (@(Texts) | Where-Object { $_ -like '*סורק*' }) { $script:sawScanning = $true; break }; Start-Sleep -Milliseconds 200 }
  Info 'scanning state observed (details entry)' $(if ($script:sawScanning) { 'yes' } else { 'not seen - scan faster than the poll' })
  Assert 'rescan finished' (WaitGone 'סורק' 30) ((Texts) -join ' | ')
  Assert 'target workspace is Cap A' ((CaptureTargetText) -eq 'Cap A') (CaptureTargetText)
  $names2 = CheckNames; Info 'detected rows (after closing Notepad)' ($names2 -join ' | ')
  Assert 'REAL rescan: the closed Notepad is gone from the new scan' ($null -eq (CheckByLike '*Notepad*')) ($names2 -join ' | ')
  Assert 'REAL rescan: the Explorer folder is still listed' ($null -ne (CheckByLike '*capfolder*')) ($names2 -join ' | ')
  Assert 'ALL TABS (details rescan): background tab example.org still listed' ($null -ne (CheckByLike '*example.org*')) ($names2 -join ' | ')
  $edgeRows = @($names2 | Where-Object { $_ -like '*example*' })
  Info 'ALL TABS site detection (every Edge tab via address bar)' $(if ($edgeRows.Count -gt 0) { "detected: $($edgeRows -join ', ')" } else { 'no site row detected in this run' })
  Shot 'add-from-open-details-entry'
  $c = ClickBtn 'Capture back' 900; Assert 'back from details entry goes to the workspace search' ($c -eq 'ok' -and $null -ne (NamedEdit 'Capture workspace search')) $c
  $c = ClickBtn 'Close capture' 900; Assert 'closed' ($c -eq 'ok') $c
  # 5b. out-of-viewport staging: multi-tab capture stages more rows than the panel renders.
  # Staged set, save and below-viewport removal are verified against the store, not the rendered rows.
  $c = ClickBtn 'Back' 900; Assert 'back to overview for deep staging' ($c -eq 'ok') $c
  $c = OpenCaptureMenu 'סביבה קיימת'; Assert 'capture dropdown -> existing (deep staging)' ($c -eq 'ok') $c
  Assert 'capture screen shown (deep staging)' ((WaitText 'Open now' 10) -and $null -ne (NamedEdit 'Capture workspace search')) ''
  Assert 'deep staging scan finished' (WaitGone 'סורק' 30) ((Texts) -join ' | ')
  SetText (NamedEdit 'Capture workspace search') 'Cap B'
  Start-Sleep -Milliseconds 600
  $c = ClickBtn 'Pick workspace Cap B' 1000; Assert 'picked Cap B for deep staging' ($c -eq 'ok') $c
  $storeBeforeDeep = StoreJson
  $c = ClickBtn 'הוסף הכל' 1500; Assert 'add-all clicked (deep staging)' ($c -eq 'ok') $c
  Assert 'deep staging: duplicate dialog lists only the folder' ((WaitText 'Already in this workspace' 5) -and $null -ne (CheckByLike 'Add again: capfolder*') -and $null -eq (CheckByLike 'Add again: *example*')) ((CheckNames) -join ' | ')
  $c = ClickBtn 'Capture duplicates skip' 900; Assert 'skip the folder duplicate (deep staging)' ($c -eq 'ok') $c
  $deepStaged = @(StagedAddrsDeep)
  $expectDeep = @('*WindowsTerminal.exe*', '*msedge.exe*', '*example.com*', '*example.org*', '*example.net*')
  $missingDeep = @(); foreach ($e in $expectDeep) { if (@($deepStaged | Where-Object { $_ -like $e }).Count -eq 0) { $missingDeep += $e } }
  Assert 'deep staging: exactly the 5 non-duplicate rows staged beyond the rendered viewport' (($deepStaged.Count -eq 5) -and $missingDeep.Count -eq 0) (($deepStaged -join ' | ') + ' || missing: ' + ($missingDeep -join ' | '))
  Assert 'deep staging: nothing saved before the choice' ((StoreJson) -eq $storeBeforeDeep) ''
  Shot 'deep-staging-staged'
  $c = ClickBtn 'Capture save' 1500; Assert 'deep staging save clicked' ($c -eq 'ok') $c
  Assert 'deep staging: app reports saving all 5 rows' (WaitText 'Saved 5 items' 3) ((Texts) -join ' | ')
  $o = StoreObj; $wsB = @($o.workspaces | Where-Object { $_.name -eq 'Cap B' })[0]
  Assert 'deep staging: store holds the previous 3 plus exactly the 5 new in Cap B' (@($wsB.items).Count -eq 8) ((@($wsB.items | ForEach-Object { $_.name })) -join ' | ')
  foreach ($e in $expectDeep) { Assert "deep staging: $e saved exactly once to Cap B" ((WsCountByTarget $wsB $o $e) -eq 1) "count=$(WsCountByTarget $wsB $o $e)" }
  Assert 'deep staging: every saved instance has a non-empty effective name (membership or catalog fallback)' (@(EffectiveNames $wsB $o | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -eq 0) ((EffectiveNames $wsB $o) -join ' | ')
  $c = ClickBtn 'Close capture' 900; Assert 'capture closed after deep save' ($c -eq 'ok') $c

  # 5c. below-viewport removal: restage five duplicate rows, wheel to a row the panel does not render, remove it, save the rest
  $c = OpenCaptureMenu 'סביבה קיימת'; Assert 'capture dropdown -> existing (deep removal)' ($c -eq 'ok') $c
  Assert 'capture screen shown (deep removal)' ((WaitText 'Open now' 10) -and $null -ne (NamedEdit 'Capture workspace search')) ''
  Assert 'deep removal scan finished' (WaitGone 'סורק' 30) ((Texts) -join ' | ')
  SetText (NamedEdit 'Capture workspace search') 'Cap B'
  Start-Sleep -Milliseconds 600
  $c = ClickBtn 'Pick workspace Cap B' 1000; Assert 'picked Cap B for deep removal' ($c -eq 'ok') $c
  $c = ClickBtn 'הוסף הכל' 1500; Assert 'add-all clicked (deep removal)' ($c -eq 'ok') $c
  Assert 'deep removal: grouped dialog for every row now that all six are duplicates' (WaitText 'Already in this workspace' 5) ((Texts) -join ' | ')
  foreach ($cbx in @(Checks | Where-Object { $_.Current.Name -like 'Add again:*' -and $_.Current.Name -notlike '*capfolder*' })) { if (-not (IsOn $cbx)) { [void](ToggleEl $cbx ('tick ' + $cbx.Current.Name)); Start-Sleep -Milliseconds 200 } }
  $c = ClickBtn 'Capture duplicates add ticked' 900; Assert 'add the five ticked duplicates (deep removal)' ($c -eq 'ok') $c
  $deep2 = @(StagedAddrsDeep)
  Assert 'deep removal: five duplicate rows staged beyond the viewport' (($deep2.Count -eq 5) -and @($deep2 | Where-Object { $_ -like '*example.net*' }).Count -eq 1) ($deep2 -join ' | ')
  $removedNet = $false
  for ($i = 0; $i -lt 20 -and -not $removedNet; $i++) {
    $rb = Btn 'Remove added example.net'
    if ($rb) { try { $rb.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); $removedNet = $true } catch { }; Start-Sleep -Milliseconds 600; break }
    ScrollStaged -360
  }
  Assert 'below-viewport staged row example.net realized and removed' $removedNet ''
  $deep3 = @(StagedAddrsDeep)
  Assert 'deep removal: staged set is the four kept rows' (($deep3.Count -eq 4) -and @($deep3 | Where-Object { $_ -like '*example.net*' }).Count -eq 0) ($deep3 -join ' | ')
  $c = ClickBtn 'Capture save' 1500; Assert 'deep removal save clicked' ($c -eq 'ok') $c
  Assert 'deep removal: app reports saving the 4 kept rows' (WaitText 'Saved 4 items' 3) ((Texts) -join ' | ')
  $o = StoreObj; $wsB = @($o.workspaces | Where-Object { $_.name -eq 'Cap B' })[0]
  Assert 'deep removal: Cap B holds exactly 12 rows after the kept four saved' (@($wsB.items).Count -eq 12) ((@($wsB.items | ForEach-Object { $_.name })) -join ' | ')
  Assert 'deep removal: example.net stayed saved exactly once (removal kept it out)' ((WsCountByTarget $wsB $o '*example.net*') -eq 1) "count=$(WsCountByTarget $wsB $o '*example.net*')"
  foreach ($e in @('*WindowsTerminal.exe*', '*msedge.exe*', '*example.com*', '*example.org*')) { Assert "deep removal: $e now twice in Cap B" ((WsCountByTarget $wsB $o $e) -eq 2) "count=$(WsCountByTarget $wsB $o $e)" }
  Assert 'deep removal: the folder stayed at its two instances' ((WsCountByTarget $wsB $o '*capfolder*') -eq 2) "count=$(WsCountByTarget $wsB $o '*capfolder*')"
  Assert 'deep removal: every instance effective name still non-empty (membership or catalog fallback)' (@(EffectiveNames $wsB $o | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -eq 0) ((EffectiveNames $wsB $o) -join ' | ')
  $c = ClickBtn 'Close capture' 900; Assert 'capture closed after deep removal' ($c -eq 'ok') $c

  # 6. instance independence across workspaces: renaming Cap A's instance never touches Cap B's
  if (-not (WaitText 'Created:' 2)) { $c = ClickBtn 'Cap A' 1000; Assert 'open Cap A details (independence)' ($c -eq 'ok') $c; Assert 'Cap A details shown' (WaitText 'Created:' 10) '' }
  $c = ClickBtn 'Edit item' 900; Assert 'edit Cap A folder instance' ($c -eq 'ok') $c
  SetText (NamedEdit 'Edit item name') 'folder in A'
  $c = ClickBtn 'Save item edit' 900; Assert 'Cap A instance rename saved' ($c -eq 'ok') $c
  Assert 'renamed instance visible in Cap A' (WaitText 'folder in A' 10) ((Texts) -join ' | ')
  $o = StoreObj
  $capItemId = ($o.items.PSObject.Properties.Value | Where-Object { $_.target -like '*capfolder*' } | Select-Object -First 1).id
  $wsA = @($o.workspaces | Where-Object { $_.name -eq 'Cap A' })[0]
  $wsB = @($o.workspaces | Where-Object { $_.name -eq 'Cap B' })[0]
  $aNames = @($wsA.items | Where-Object { $_.itemId -eq $capItemId } | ForEach-Object { $_.name })
  $bNames = @($wsB.items | Where-Object { $_.itemId -eq $capItemId } | ForEach-Object { $_.name })
  Assert 'editing the instance in Cap A changed only Cap A' (($aNames -contains 'folder in A') -and $bNames.Count -eq 2 -and ($bNames -contains 'my capture folder') -and ($bNames -contains 'capfolder second')) ("A: $($aNames -join '|') B: $($bNames -join '|')")
  $c = ClickBtn 'Back' 900; Assert 'back to overview from Cap A' ($c -eq 'ok') $c
  # 7. cross-browser engineering check: Chrome tabs detected by the same UIA scan (user asked for all browsers; free verification)
  # Hardened fixture (PR19 CI run 43472682415: after the old fixed 6s wait the scan listed no Chrome rows;
  # the preserved evidence does not say why): resolved executable + dedicated disposable profile; the window
  # we wait for is identity-bound to THIS launch through the unique profile path in the process command line,
  # which survives a launcher->browser process handoff. Readiness is polled and bounded, and requires BOTH
  # fixture tabs with their actual URLs (each tab selected and read, then the original tab restored), so a
  # failure here is capture evidence, not ambiguous fixture initialization. A missing executable or a timeout
  # is a hard failure with process/window diagnostics, never a skip and never an Edge substitute.
  $chromeExe = $null
  foreach ($cand in @("$env:ProgramFiles\Google\Chrome\Application\chrome.exe", "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe", "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe")) { if (-not $chromeExe -and (Test-Path $cand)) { $chromeExe = $cand } }
  if (-not $chromeExe) { $gcm = Get-Command chrome.exe -ErrorAction SilentlyContinue; if ($gcm) { $chromeExe = $gcm.Source } }
  if (-not $chromeExe) {
    Assert 'CROSS-BROWSER: chrome executable resolved' $false 'chrome.exe not found in the standard install paths or on PATH; this gate requires Chrome (no skip, no Edge substitute)'
  } else {
    $chromeProfile = Join-Path $env:RUNNER_TEMP ('chrome-fixture-' + [Guid]::NewGuid().ToString('N'))
    function ChromeFixturePids() { @((Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($chromeProfile) }).ProcessId) }
    function ChromeFixtureWin() { $fpids = @(ChromeFixturePids); @($A::RootElement.FindAll($TS::Children, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { try { $fpids -contains $_.Current.ProcessId -and $_.Current.ControlType -eq $CT::Window -and -not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.Width -gt 0 -and $_.Current.BoundingRectangle.Height -gt 0 } catch { $false } } | Select-Object -First 1) }
    function ChromeWinUrl($w) { $e = @($w.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Edit))) | Where-Object { $_.Current.ClassName -like '*OmniboxViewViews' } | Select-Object -First 1); if ($e) { ValOf $e } else { '' } }
    function OmniboxHost($raw) { $v = ($raw -replace '^\s+', '') -replace '\s+$', ''; if ($v -match '^(?:[a-zA-Z][a-zA-Z0-9+.\-]*://)?([^/?#]+)') { return $Matches[1].ToLowerInvariant() }; return '' }
    # Per-tab EXPECTED-host proof: the fixture is controlled - exactly two tabs in launched URL order, so tab0
    # must settle on host www.iana.org and tab1 on www.rfc-editor.org. A stale omnibox value from the other
    # tab can never match this tab's expected host. A host counts only after two consecutive matching reads
    # with the tab selected, re-confirming the selection at accept time.
    function ChromeTabExpectHost($w, $t, $expectedHost, $tabIndex, $deadline) {
      try { $t.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch { return $false }
      $stable = ''
      while ((Get-Date) -lt $deadline) {
        $sel = $false; try { $sel = $t.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } catch { }
        if (-not $sel) { $stable = ''; Start-Sleep -Milliseconds 150; continue }
        $h = OmniboxHost (ChromeWinUrl $w)
        $script:tabLastHosts[$tabIndex] = $h
        if ($h -ne $expectedHost) { $stable = ''; Start-Sleep -Milliseconds 200; continue }
        if ($stable -eq $expectedHost) {
          $stillSel = $false; try { $stillSel = $t.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } catch { }
          if ($stillSel) { return $true }
          $stable = ''
        } else { $stable = $h; Start-Sleep -Milliseconds 200 }
      }
      return $false
    }
    $chrome = $null; $chromeLaunchErr = ''
    try { $chrome = Start-Process $chromeExe -ArgumentList '--new-window', '--no-first-run', '--no-default-browser-check', "--user-data-dir=$chromeProfile", 'https://www.iana.org/help/example-domains', 'https://www.rfc-editor.org/' -PassThru } catch { $chromeLaunchErr = "$_" }
    if (-not $chrome) {
      Assert 'CROSS-BROWSER: chrome launch' $false "Start-Process $chromeExe failed: $chromeLaunchErr"
    } else {
      Info 'chrome fixture launched' "exe=$chromeExe pid=$($chrome.Id) profile=$chromeProfile"
      $expectedHosts = @('www.iana.org', 'www.rfc-editor.org')   # launched URL order => tab strip order
      $script:tabLastHosts = @('', '')
      $cw = $null; $curl = ''; $csawWin = $false; $csawTabs = 0; $tabTitles = ''; $tooMany = $false
      $deadlineC = (Get-Date).AddSeconds(20)   # one shared deadline covers the window wait AND every per-tab proof
      while ((Get-Date) -lt $deadlineC -and -not $cw -and -not $tooMany) {
        $w = ChromeFixtureWin
        if ($w) {
          $csawWin = $true
          $tabs = @($w.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::TabItem))))
          if ($tabs.Count -gt $csawTabs) { $csawTabs = $tabs.Count }
          $tabTitles = (@($tabs | ForEach-Object { try { $_.Current.Name } catch { '?' } }) -join ' | ')
          if ($tabs.Count -gt 2) { $tooMany = $true; break }   # controlled fixture: never broaden acceptance
          if ($tabs.Count -eq 2) {
            $origTab = $null
            foreach ($t in $tabs) { try { if ($t.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { $origTab = $t } } catch { } }
            try {
              $ok0 = ChromeTabExpectHost $w $tabs[0] $expectedHosts[0] 0 $deadlineC
              $ok1 = ChromeTabExpectHost $w $tabs[1] $expectedHosts[1] 1 $deadlineC
            } finally {
              try { if ($origTab) { $origTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() } } catch { }
            }
            $curl = "tab0=$($script:tabLastHosts[0]) tab1=$($script:tabLastHosts[1])"
            if ($ok0 -and $ok1) { $cw = $w; break }
          }
        }
        Start-Sleep -Milliseconds 400
      }
      if ($tooMany) {
        Assert 'CROSS-BROWSER: exactly the two launched fixture tabs in launch order' $false "found $csawTabs tabs: $tabTitles"
      } elseif (-not $cw) {
        $procDiag = (@(ChromeFixturePids | ForEach-Object { "pid=$_" }) -join ',')
        $launchState = ''; try { $launchState = "launchedHasExited=$($chrome.HasExited)"; if ($chrome.HasExited) { $launchState += " exitCode=$($chrome.ExitCode)" } } catch { $launchState = "launchedStateError=$_" }
        Assert 'CROSS-BROWSER: chrome window with both fixture tabs at their expected hosts within 20s shared deadline (fixture-profile identity)' $false "$launchState windowSeen=$csawWin maxTabs=$csawTabs tabTitles='$tabTitles' lastHosts='$curl' fixturePids=[$procDiag]"
      } else {
        Assert 'CROSS-BROWSER: chrome window with both fixture tabs at their expected hosts within 20s shared deadline (fixture-profile identity)' $true "$curl"
        $c = OpenCaptureMenu 'סביבה חדשה'; Assert 'capture dropdown -> new (chrome check)' ($c -eq 'ok') $c
        Assert 'capture screen shown (chrome check)' (WaitText 'Open now' 10) ((Texts) -join ' | ')
        Assert 'chrome-check scan finished' (WaitGone 'סורק' 30) ((Texts) -join ' | ')
        $cnames = CheckNames
        Info 'detected rows (chrome check)' ($cnames -join ' | ')
        Assert 'CROSS-BROWSER: Chrome window tab iana.org is listed' ($null -ne (CheckByLike '*iana.org*')) ($cnames -join ' | ')
        Assert 'CROSS-BROWSER: Chrome window tab rfc-editor.org is listed' ($null -ne (CheckByLike '*rfc-editor.org*')) ($cnames -join ' | ')
        Shot 'chrome-check-scan'
        $c = ClickBtn 'Close capture' 900; Assert 'capture closed after chrome check' ($c -eq 'ok') $c
        if (WaitText 'Unsaved changes' 2) { $c = ClickBtn 'צא בלי לשמור' 900; Assert 'discarded unsaved capture selections (chrome check)' ($c -eq 'ok') $c }
      }
      foreach ($pidF in @(ChromeFixturePids)) { Stop-Process -Id $pidF -Force -ErrorAction SilentlyContinue }
      Remove-Item $chromeProfile -Recurse -Force -ErrorAction SilentlyContinue
    }
  }

  # 8. localized browser UI: Edge with Hebrew UI must still be detected (locale-independent omnibox lookup; the accessible Name is localized, the class name is not)
  $edgeHe = $null; try { Get-Process msedge -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 2; $edgeHe = Start-Process msedge -ArgumentList '--new-window', '--no-first-run', '--no-default-browser-check', '--lang=he', "--user-data-dir=$env:RUNNER_TEMP\edge-he-profile", 'https://example.com/' -PassThru } catch { Info 'edge-he start' "$_" }
  if ($edgeHe) {
    Start-Sleep -Seconds 7
    try {
      $heWin = $A::RootElement.FindFirst($TS::Children, (New-Object $PC($A::ProcessIdProperty, $edgeHe.Id)))
      if ($heWin) { $heEdits = @($heWin.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Edit))) | ForEach-Object { $_.Current.Name + ' [' + $_.Current.ClassName + ']' }); Info 'edge-he edit controls (proves whether the UI localized)' ($heEdits -join ' | ') }
    } catch { Info 'edge-he UIA read' "$_" }
    $c = OpenCaptureMenu 'סביבה חדשה'; Assert 'capture dropdown -> new (locale check)' ($c -eq 'ok') $c
    Assert 'capture screen shown (locale check)' (WaitText 'Open now' 10) ((Texts) -join ' | ')
    Assert 'locale-check scan finished' (WaitGone 'סורק' 30) ((Texts) -join ' | ')
    $lnames = CheckNames
    Info 'detected rows (locale check)' ($lnames -join ' | ')
    Assert 'LOCALE: Hebrew-UI Edge tab example.com is listed' ($null -ne (CheckByLike '*example.com*')) ($lnames -join ' | ')
    $c = ClickBtn 'Close capture' 900; Assert 'capture closed after locale check' ($c -eq 'ok') $c
    if (WaitText 'Unsaved changes' 2) { $c = ClickBtn 'צא בלי לשמור' 900; Assert 'discarded unsaved capture selections (locale check)' ($c -eq 'ok') $c }
    Get-Process msedge -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  } else {
    Info 'LOCALE: edge-he could not start' 'skipped'
  }
  # 9. multi-window scan (settled spec: סריקה של מה שפתוח - אין הגבלה לחלון אחד; EnumWindows path in product code).
  # Sequential: window A fully ready (its own fixture URL visible in ITS omnibox) before B launches on the same profile (avoids the profile lock race).
  # Missing/duplicate window is a HARD failure, not a skip. Readiness via bounded condition loops only.
  try { Get-Process msedge -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 2 } catch { Info 'multi-window pre-clean' "$_" }
  function EdgeWins() { $pids = @((Get-Process msedge -ErrorAction SilentlyContinue).Id); @($A::RootElement.FindAll($TS::Children, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { try { $pids -contains $_.Current.ProcessId -and $_.Current.ClassName -like 'Chrome_WidgetWin*' -and -not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.Width -gt 0 } catch { $false } }) }
  function WinUrl($w) { $e = @($w.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Edit))) | Where-Object { $_.Current.ClassName -like '*OmniboxViewViews' } | Select-Object -First 1); if ($e) { ValOf $e } else { '' } }
  try { $null = Start-Process msedge -ArgumentList '--new-window', '--no-first-run', '--no-default-browser-check', "--user-data-dir=$env:RUNNER_TEMP\edge-mw-profile", 'https://example.com/' } catch { Info 'multi-window start A' "$_" }
  $wA = $null; $urlA = ''; $swA = [Diagnostics.Stopwatch]::StartNew()
  while ($swA.Elapsed.TotalSeconds -lt 20 -and $null -eq $wA) { foreach ($w in @(EdgeWins)) { $u = WinUrl $w; if ($u -like '*example.com*') { $wA = $w; $urlA = $u; break } }; if ($null -eq $wA) { Start-Sleep -Milliseconds 500 } }
  Assert 'MULTI-WINDOW: window A present showing its own fixture URL' ($null -ne $wA) ("urlA='$urlA' after $($swA.Elapsed.TotalSeconds.ToString('0.0'))s")
  $wB = $null; $urlB = ''; $hA = 0
  if ($wA) {
    $hA = $wA.Current.NativeWindowHandle
    try { $null = Start-Process msedge -ArgumentList '--new-window', '--no-first-run', '--no-default-browser-check', "--user-data-dir=$env:RUNNER_TEMP\edge-mw-profile", 'https://www.iana.org/help/example-domains' } catch { Info 'multi-window start B' "$_" }
    $swB = [Diagnostics.Stopwatch]::StartNew()
    while ($swB.Elapsed.TotalSeconds -lt 20 -and $null -eq $wB) { foreach ($w in @(EdgeWins)) { if ($w.Current.NativeWindowHandle -ne $hA) { $u = WinUrl $w; if ($u -like '*iana.org*') { $wB = $w; $urlB = $u; break } } }; if ($null -eq $wB) { Start-Sleep -Milliseconds 500 } }
    Assert 'MULTI-WINDOW: second DISTINCT window present showing its own fixture URL (hard gate)' ($null -ne $wB) ("urlB='$urlB' after $($swB.Elapsed.TotalSeconds.ToString('0.0'))s")
  }
  if ($wA -and $wB) {
    $hB = $wB.Current.NativeWindowHandle
    Assert 'MULTI-WINDOW: distinct window runtime handles' ($hA -ne 0 -and $hB -ne 0 -and $hA -ne $hB) ("hA=$hA hB=$hB")
    Info 'MULTI-WINDOW: per-window fixture URLs' ("hA=$hA url='$urlA' | hB=$hB url='$urlB'")
    $c = OpenCaptureMenu 'סביבה חדשה'; Assert 'capture dropdown -> new (multi-window)' ($c -eq 'ok') $c
    Assert 'capture screen shown (multi-window)' (WaitText 'Open now' 10) ((Texts) -join ' | ')
    Assert 'multi-window scan finished' (WaitGone 'סורק' 40) ((Texts) -join ' | ')
    $mnames = CheckNames
    Info 'detected rows (multi-window)' ($mnames -join ' | ')
    Assert 'MULTI-WINDOW: window-A tab example.com is listed' ($null -ne (CheckByLike '*example.com*')) ($mnames -join ' | ')
    Assert 'MULTI-WINDOW: window-B tab iana.org is listed' ($null -ne (CheckByLike '*iana.org*')) ($mnames -join ' | ')
    Shot 'multi-window-scan'
    $c = ClickBtn 'Close capture' 900; Assert 'capture closed after multi-window' ($c -eq 'ok') $c
    if (WaitText 'Unsaved changes' 2) { $c = ClickBtn 'צא בלי לשמור' 900; Assert 'discarded unsaved capture selections (multi-window)' ($c -eq 'ok') $c }
  }
  Get-Process msedge -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  Assert 'FLOW-COMPLETE' $true $env:GITHUB_SHA
}
catch { $il = $_.InvocationInfo; "script error: $_ at line $($il.ScriptLineNumber): $($il.Line.Trim())" | Tee-Object -Append diag/errors.txt; [void]$results.Add([ordered]@{ name = 'script error'; ok = $false; detail = "$_ at line $($il.ScriptLineNumber)" }) }
finally {
  $results | ConvertTo-Json -Depth 4 | Out-File diag/capture-assertions.json
  Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  if ($edge -and -not $edge.HasExited) { Get-Process msedge -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue }
  if ($p -and -not $p.HasExited) { $null = $p.CloseMainWindow(); Start-Sleep 3; if (-not $p.HasExited) { $p.Kill() } }
}
if (@($results | Where-Object { $_.ok -eq $false }).Count -gt 0) { 'CAPTURE UI ASSERTIONS FAILED' | Out-File -Append diag/errors.txt; exit 1 }
}
catch { $il = $_.InvocationInfo; "capture harness setup error: $_ at line $($il.ScriptLineNumber): $($il.Line.Trim())" | Out-File -Append diag/errors.txt; @([ordered]@{ name = 'capture harness setup error'; ok = $false; detail = "$_ at line $($il.ScriptLineNumber)" }) | ConvertTo-Json -Depth 4 | Out-File diag/capture-assertions.json; exit 1 }
