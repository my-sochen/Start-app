try {
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class W { [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; } [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags); }'
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
  [void][W]::SetCursorPos(5, 5); if ($p.MainWindowHandle -ne 0) { [void][W]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, 0, 0, 0, 0, 0x27) }; Start-Sleep -Milliseconds 400
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
  $edge = $null; try { $edge = Start-Process msedge -ArgumentList '--new-window', '--no-first-run', '--no-default-browser-check', 'https://example.com/' -PassThru } catch { Info 'edge start' "$_" }
  Start-Sleep -Seconds 8
  $exe = Get-ChildItem -Recurse src/StartApp.App/bin/Release -Filter StartApp.App.exe | Select-Object -First 1
  $p = Start-Process -FilePath $exe.FullName -PassThru
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.Elapsed.TotalSeconds -lt 60) { $p.Refresh(); if ($p.HasExited -or $p.MainWindowHandle -ne 0) { break }; Start-Sleep -Milliseconds 500 }
  Start-Sleep -Seconds 2
  Assert 'app window opened' (-not $p.HasExited -and $p.MainWindowHandle -ne 0) ''
  $win = $A::RootElement.FindFirst($TS::Children, (New-Object $PC($A::ProcessIdProperty, $p.Id)))
  Assert 'automation window found' ($null -ne $win) ''
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
  Assert 'folder address shown under its name' (WaitText $capdir 5) ((Texts) -join ' | ')
  Assert 'new-workspace mode: every row ticked by default' (@(Checks | Where-Object { -not (IsOn $_) }).Count -eq 0 -and (Checks).Count -gt 0) ($names -join ' | ')
  Assert 'detection notes are shown (partial coverage is stated)' (WaitText 'Files: documents open inside programs are not detected' 3) ((Texts) -join ' | ')
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

  # 5. X resets; a new scan reflects what is open at that moment (Notepad closed first)
  $c = ClickBtn 'Close capture' 900; Assert 'X clicked' ($c -eq 'ok') $c
  Assert 'capture screen closed' (-not (WaitText 'Open now' 2)) ''
  Assert 'capture list was cleared on close' ((Checks).Count -eq 0) ''
  Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 2
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
  $edgeRows = @($names2 | Where-Object { $_ -like '*example*' })
  Info 'PARTIAL site detection (active tab of Edge/Chrome via address bar)' $(if ($edgeRows.Count -gt 0) { "detected: $($edgeRows -join ', ')" } else { 'no site row detected in this run' })
  Shot 'add-from-open-details-entry'
  $c = ClickBtn 'Capture back' 900; Assert 'back from details entry goes to the workspace search' ($c -eq 'ok' -and $null -ne (NamedEdit 'Capture workspace search')) $c
  $c = ClickBtn 'Close capture' 900; Assert 'closed' ($c -eq 'ok') $c
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
