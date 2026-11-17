# Focused diagnostic reproduction only. Shared helper definitions copied from exact5ebf capture harness; broad flow is not executed.
$results = New-Object System.Collections.ArrayList   # initialized BEFORE the outer try: the outer catch appends to this in-memory CURRENT-RUN collection and never loads a disk file that could hold a stale prior run's PASS/FLOW evidence
try {
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class W { [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; } [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, System.UIntPtr e); [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags); [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid); }'
$A = [System.Windows.Automation.AutomationElement]; $CT = [System.Windows.Automation.ControlType]; $PC = [System.Windows.Automation.PropertyCondition]; $TS = [System.Windows.Automation.TreeScope]
$script:shot = 0; $runId = $env:GITHUB_RUN_ID
# Controlled-runner scalar timeline only. Bounded append attempts, no names/URLs/input content.
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class InputTraceNative { [StructLayout(LayoutKind.Sequential)] public struct LastInput { public uint Size; public uint Tick; } [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LastInput input); }'
$script:phaseTraceCount = 0
function TracePhase($stage) {
  if ($script:phaseTraceCount++ -ge 512) { return }
  try {
    $li = New-Object InputTraceNative+LastInput
    $li.Size = [uint32][System.Runtime.InteropServices.Marshal]::SizeOf($li)
    $ok = [InputTraceNative]::GetLastInputInfo([ref]$li)
    "$([Environment]::TickCount64) stage=$stage inputTick=$($li.Tick) inputRead=$ok" | Out-File -Append -Encoding utf8 diag/harness-phase-trace.txt
  } catch { }
}
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
  TracePhase 'shot-before-cursor-and-window'
  [void][W]::SetCursorPos(5, 5); if ($p.MainWindowHandle -ne 0) { [void][W]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0010) }; Start-Sleep -Milliseconds 400
  $f = "out-images/cap-$state.png"
  $w0 = CurWin; if ($null -eq $w0) { "no app window for $state" | Tee-Object -Append diag/errors.txt; return }
  $br = $w0.Current.BoundingRectangle; $w = [int]$br.Width; $h = [int]$br.Height
  if ($w -le 0 -or $h -le 0) { "no window rect for $state" | Tee-Object -Append diag/errors.txt; return }
  $bmp = New-Object System.Drawing.Bitmap $w, $h; $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen([int]$br.X, [int]$br.Y, 0, 0, $bmp.Size); $bmp.Save("$PWD/$f"); "shot $f ${w}x${h}"
  TracePhase 'shot-after-capture'
}
function ShotRaw($state) {
  TracePhase 'shot-raw-before-cursor'
  # Evidence shot with NO window-raise: captures whatever is actually on screen over the app region.
  [void][W]::SetCursorPos(5, 5)
  $f = "out-images/cap-$state.png"
  $w0 = CurWin; if ($null -eq $w0) { "no app window for $state" | Tee-Object -Append diag/errors.txt; return }
  $br = $w0.Current.BoundingRectangle; $w = [int]$br.Width; $h = [int]$br.Height
  if ($w -le 0 -or $h -le 0) { "no window rect for $state" | Tee-Object -Append diag/errors.txt; return }
  $bmp = New-Object System.Drawing.Bitmap $w, $h; $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen([int]$br.X, [int]$br.Y, 0, 0, $bmp.Size); $bmp.Save("$PWD/$f"); "shot $f ${w}x${h}"
  TracePhase 'shot-after-capture'
}
function UiaAll($ct) { for ($t = 0; $t -lt 3; $t++) { $w0 = CurWin; if ($null -eq $w0) { if ($t -eq 2) { "UIA window gone for pid $($p.Id): appHasExited=$($p.HasExited) mainWindowHandle=$($p.MainWindowHandle)" | Out-File -Append diag/uia-liveness.txt; return @() }; Start-Sleep -Milliseconds 400; continue }; try { return $w0.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $ct))) } catch { if ($t -eq 2) { "UIA read failed after retries: $($_.Exception.Message) | appHasExited=$(if ($p) { $p.HasExited } else { "no-process" }) mainWindowHandle=$(if ($p) { $p.MainWindowHandle } else { "-" })" | Out-File -Append diag/uia-liveness.txt; throw }; Start-Sleep -Milliseconds 400 } } }
function Texts() { @(UiaAll $CT::Text | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) }
function ListLines() { @(UiaAll $CT::ListItem | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) }
function Btn($name) { (CurWin).FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::Button)), (New-Object $PC($A::NameProperty, $name))))) }
# The capture screen is an opaque overlay over the overview, which stays Visible behind it, so both forms'
# palette swatches sit in the window-wide UIA control view under the same hex automation names (FindFirst
# picked the OVERVIEW palette in run 43577316808 - a false pass). The capture palette ItemsControl carries
# the unique automation name 'Capture palette'; EVERY capture palette lookup must go through this anchor
# (exactly one match) and search only inside its subtree - never the window-wide tree.
function ClickBtn($name, $settle = 900) {
  $b = $null; for ($i = 0; $i -lt 40; $i++) { $b = Btn $name; if ($b -and $b.Current.IsEnabled) { break }; Start-Sleep -Milliseconds 250 }
  if (-not $b) { return "button '$name' not found" }; if (-not $b.Current.IsEnabled) { return "button '$name' stayed disabled" }
  $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds $settle; return 'ok'
}
function WaitText($needle, $secs) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.Elapsed.TotalSeconds -lt $secs) { if (@(Texts) + @(ListLines) | Where-Object { $_ -like "*$needle*" }) { return $true }; Start-Sleep -Milliseconds 250 }; return $false }
function WaitGone($needle, $secs) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.Elapsed.TotalSeconds -lt $secs) { if (-not (@(Texts) + @(ListLines) | Where-Object { $_ -like "*$needle*" })) { return $true }; Start-Sleep -Milliseconds 250 }; return $false }
function NamedEdit($name) { (CurWin).FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::Edit)), (New-Object $PC($A::NameProperty, $name))))) }
function ValOf($edit) { if ($null -eq $edit) { return '' }; try { return $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { return '' } }
function BrowserWinUrl($w) { $e = @($w.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Edit))) | Where-Object { $_.Current.ClassName -like '*OmniboxViewViews' } | Select-Object -First 1); if ($e) { ValOf $e } else { '' } }
function OmniboxHost($raw) { $v = ($raw -replace '^\s+', '') -replace '\s+$', ''; if ($v -match '^(?:[a-zA-Z][a-zA-Z0-9+.\-]*://)?([^/?#]+)') { return $Matches[1].ToLowerInvariant() }; return '' }
function InvokeEl($el, $what) { if ($null -eq $el) { Assert "$what found" $false 'element not found'; return $false }; try { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); return $true } catch { Assert "$what invokable" $false "$_"; return $false } }
function MenuItemByName($name) { $A::RootElement.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::MenuItem)), (New-Object $PC($A::NameProperty, $name))))) }
function MenuInsideWindow($label) { $m = $A::RootElement.FindFirst($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Menu))); if ($null -eq $m) { Assert "$label menu element found" $false 'no open menu'; return }; $mr = $m.Current.BoundingRectangle; $wr = (CurWin).Current.BoundingRectangle; Assert "$label fully inside the app window" ($mr.Left -ge $wr.Left -and $mr.Right -le $wr.Right -and $mr.Top -ge $wr.Top -and $mr.Bottom -le $wr.Bottom) ("menu=$($mr.Left),$($mr.Top)-$($mr.Right),$($mr.Bottom) window=$($wr.Left),$($wr.Top)-$($wr.Right),$($wr.Bottom)") }
function EdgeFixturePids() { if (-not $edgeProfile) { return @() }; @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($edgeProfile) } | ForEach-Object { $_.ProcessId }) }   # ForEach-Object emission: zero matches yield a truly EMPTY array - @((...).ProcessId) emitted @($null) on no match, a null that hit the finally Stop-Process binder and failed run 43496001745 after every assertion had passed
$script:edgeHwnd = 0   # the ONE verified fixture browser window, captured once at readiness
function EdgeWindow() {
  # The ONE browser window this test drives. Before readiness: the tab-richest real window among THIS
  # fixture profile's own msedge processes (profile-command-line identity survives a possible
  # launcher->browser PID handoff; run 43490152034 bound only the launcher PID and read before=-1 while
  # the fixture tabs existed - handoff vs welcome-surface overlay not conclusively distinguished).
  # After readiness: re-binds ONLY the captured HWND, revalidated to still exist and still belong to a
  # fixture-profile process; if the verified window is gone or replaced this returns null and the caller
  # fails - setup, middle selection and the before/after restore can never silently drift to another
  # window. No class-name fallback - a fallback can bind a different browser window and make the
  # setup/before/after reads disagree.
  $fp = @(EdgeFixturePids)
  if ($fp.Count -eq 0) { return $null }
  $cand = @($A::RootElement.FindAll($TS::Children, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { try { $fp -contains $_.Current.ProcessId -and $_.Current.ControlType -eq $CT::Window -and -not $_.Current.IsOffscreen -and $_.Current.BoundingRectangle.Width -gt 0 -and $_.Current.BoundingRectangle.Height -gt 0 } catch { $false } })
  if ($script:edgeHwnd -ne 0) {
    $bound = @($cand | Where-Object { try { $_.Current.NativeWindowHandle -eq $script:edgeHwnd } catch { $false } })
    if ($bound.Count -gt 0) { return $bound[0] }
    return $null
  }
  if ($cand.Count -gt 0) { return ($cand | Sort-Object { try { $_.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::TabItem))).Count } catch { 0 } } -Descending | Select-Object -First 1) }
  return $null
}
function EdgeTabExpectHost($w, $t, $expectedHost, $sliceDeadline) {
  # Expected-host proof for THIS tab: it must be selected and the omnibox authority must read as two
  # consecutive EQUAL occurrences of the expected host while it stays selected, re-confirming the
  # selection AFTER the second read (same discipline as ChromeTabExpectHost). A stable WRONG host keeps
  # waiting - the expected host may still settle within the slice - and a lost selection resets the proof.
  try { $t.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch { return $false }
  $stable = ''
  while ((Get-Date) -lt $sliceDeadline) {
    $sel = $false; try { $sel = $t.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } catch { }
    if (-not $sel) { $stable = ''; Start-Sleep -Milliseconds 150; continue }
    $h = OmniboxHost (BrowserWinUrl $w)
    if ($h -ne $expectedHost) { $stable = ''; Start-Sleep -Milliseconds 200; continue }
    if ($stable -eq $h) {
      $stillSel = $false; try { $stillSel = $t.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } catch { }
      if ($stillSel) { return $true }
      $stable = ''
    } else { $stable = $h; Start-Sleep -Milliseconds 200 }
  }
  return $false
}
function EdgeTabUrlRaw($w, $t) {
  # Diagnostic-only single read. UNPROVEN by design (no stable re-read, no post-read selection check):
  # used only for failure logging, never as readiness or restoration evidence.
  try { $t.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 400; return BrowserWinUrl $w } catch { return '' }
}
function EdgeSetupShot($state) {
  # Setup-time evidence shot of the EDGE window region (the app process does not exist yet, so Shot/CurWin are unusable here).
  try {
    $wE = EdgeWindow; if ($null -eq $wE) { "no edge window for $state" | Tee-Object -Append diag/errors.txt; return }
    $br = $wE.Current.BoundingRectangle; $w = [int]$br.Width; $h = [int]$br.Height
    if ($w -le 0 -or $h -le 0) { "no edge rect for $state" | Tee-Object -Append diag/errors.txt; return }
    $bmp = New-Object System.Drawing.Bitmap $w, $h; $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen([int]$br.X, [int]$br.Y, 0, 0, $bmp.Size)
    $f = "out-images/cap-$state.png"; $bmp.Save("$PWD/$f"); "shot $f ${w}x${h}"
  } catch { "edge setup shot failed: $_" | Tee-Object -Append diag/errors.txt }
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
function SetText($edit, $text) { if ($null -eq $edit) { Assert 'SetText target present' $false 'edit element not found'; return }; try { $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text) } catch { Assert 'SetText succeeded' $false "$_" }; Start-Sleep -Milliseconds 400 }
function AnyByName($name) { $e = (CurWin).FindFirst($TS::Descendants, (New-Object $PC($A::NameProperty, $name))); if (-not $e) { $e = $A::RootElement.FindFirst($TS::Descendants, (New-Object $PC($A::NameProperty, $name))) }; $e }
function Checks() { @((CurWin).FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::CheckBox)))) }
function CheckNames() { @(Checks | ForEach-Object { $_.Current.Name }) }
function CheckByLike($needle) { Checks | Where-Object { $_.Current.Name -like $needle } | Select-Object -First 1 }
function OpenCaptureMenu($item) {
  $c = ClickBtn 'Capture' 900; if ($c -ne 'ok') { return $c }
  $mi = $null; for ($i = 0; $i -lt 20; $i++) { $mi = MenuItemByName $item; if ($mi) { break }; Start-Sleep -Milliseconds 250 }
  if (-not $mi) { return "menu item '$item' not found" }
  MenuInsideWindow "capture dropdown ($item)"
  TracePhase 'capture-menu-before-invoke'
  if (-not (InvokeEl $mi "capture menu item $item")) { return 'invoke failed' }
  TracePhase 'capture-menu-after-invoke'
  $script:sawScanning = $false; $swS = [Diagnostics.Stopwatch]::StartNew()  # the scan now runs off the UI thread with a visible scanning state
  while ($swS.Elapsed.TotalMilliseconds -lt 1500) { if (@(Texts) | Where-Object { $_ -like '*סורק*' }) { $script:sawScanning = $true; break }; Start-Sleep -Milliseconds 200 }
  TracePhase 'capture-menu-after-status-poll'
  return 'ok'
}
try {
  # This entrypoint is for a fresh disposable Windows GitHub runner only; never clear existing user data.
  if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows') { throw 'Dedicated Windows GitHub runner required' }
  $storeDir = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'StartApp'
  $store = Join-Path $storeDir 'store.json'
  if (Test-Path $storeDir) { throw 'Expected clean runner app data; refusing to delete existing data' }
  $edgeProfile = Join-Path $env:RUNNER_TEMP ('edge-fixture-' + [Guid]::NewGuid().ToString('N'))
  $edge = $null; try { $edge = Start-Process msedge -ArgumentList '--new-window', '--no-first-run', '--no-default-browser-check', "--user-data-dir=$edgeProfile", 'https://example.com/', 'https://example.org/', 'https://example.net/' -PassThru } catch { Info 'edge start' "$_" }
  # Fixture readiness under ONE shared 25s setup deadline: exactly the three fixture tabs, each proven by a
  # selected+stable omnibox read at its launched host IN LAUNCH ORDER, then the verified window's HWND is
  # captured once. Nothing is dismissed or closed: an unexpected surface simply keeps readiness unmet and
  # the setup fails with precise diagnostics (v10 review: no guessed Close* on an unidentified first-run UI).
  $edgeHosts = @('example.com', 'example.org', 'example.net')
  $edgeReady = $false; $edgeDiag = ''; $edgeInitial = ''
  $deadlineE = (Get-Date).AddSeconds(25)
  while ((Get-Date) -lt $deadlineE -and -not $edgeReady) {
    $wE = EdgeWindow
    if ($null -eq $wE) { Start-Sleep -Milliseconds 400; continue }
    if (-not $edgeInitial) {
      # Non-mutating snapshot of the first candidate window BEFORE the first readiness Select (names and
      # selected flags exactly as found), so setup evidence predates every mutation, success or failure.
      $tabsI = @($wE.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::TabItem))))
      $edgeInitial = "hwnd=0x$('{0:X}' -f $wE.Current.NativeWindowHandle) title='$($wE.Current.Name)' tabs=$($tabsI.Count) [" + ((@(for ($i = 0; $i -lt $tabsI.Count; $i++) { $t = $tabsI[$i]; $sel = $false; try { $sel = $t.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } catch { }; $nm = ''; try { $nm = $t.Current.Name } catch { }; "t$i sel=$sel name='$nm'" })) -join ' | ') + ']'
      Info 'edge fixture initial state (before any selection)' $edgeInitial
    }
    $tabsE = @($wE.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::TabItem))))
    if ($tabsE.Count -ne 3) { Start-Sleep -Milliseconds 400; continue }
    $hosts = @('', '', '')
    for ($i = 0; $i -lt 3; $i++) {
      if ((Get-Date) -ge $deadlineE) { break }
      $sliceE = (Get-Date).AddSeconds(4); if ($sliceE -gt $deadlineE) { $sliceE = $deadlineE }
      if (EdgeTabExpectHost $wE $tabsE[$i] $edgeHosts[$i] $sliceE) { $hosts[$i] = $edgeHosts[$i] }
    }
    if ($hosts[0] -eq $edgeHosts[0] -and $hosts[1] -eq $edgeHosts[1] -and $hosts[2] -eq $edgeHosts[2]) {
      $script:edgeHwnd = $wE.Current.NativeWindowHandle
      $edgeReady = $true
      Info 'edge fixture ready: three tabs at their launched hosts, verified window captured' "hwnd=0x$('{0:X}' -f $script:edgeHwnd) title='$($wE.Current.Name)' hosts='$($hosts -join ',')'"
      break
    }
    Start-Sleep -Milliseconds 400
  }
  if (-not $edgeReady) {
    # Non-mutating as-found snapshot FIRST (tab count/names/selection exactly as found); only then bounded
    # selected+stable URL reads as diagnostics - the log order is the mutation order.
    $wE = EdgeWindow
    if ($null -ne $wE) {
      $tabsE = @($wE.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::TabItem))))
      $asFound = (@(for ($i = 0; $i -lt $tabsE.Count; $i++) { $t = $tabsE[$i]; $sel = $false; try { $sel = $t.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } catch { }; $nm = ''; try { $nm = $t.Current.Name } catch { }; "t$i sel=$sel name='$nm'" }) -join ' | ')
      $diagDeadline = (Get-Date).AddSeconds(12)
      $reads = (@(for ($i = 0; $i -lt $tabsE.Count; $i++) { if ((Get-Date) -ge $diagDeadline) { break }; $sliceD = (Get-Date).AddSeconds(3); if ($sliceD -gt $diagDeadline) { $sliceD = $diagDeadline }; "t$i rawUnproven='$(EdgeTabUrlRaw $wE $tabsE[$i])'" }) -join ' | ')
      $edgeDiag = "launchPid=$(if ($edge) { $edge.Id } else { 'none' }) launchExited=$(if ($edge) { $edge.HasExited } else { '-' }) fixturePids=[$(@(EdgeFixturePids | ForEach-Object { "pid=$_" }) -join ',')] hwnd=0x$('{0:X}' -f $wE.Current.NativeWindowHandle) title='$($wE.Current.Name)' tabs=$($tabsE.Count) asFound=[$asFound] reads=[$reads] initial=[$edgeInitial]"
    } else {
      $edgeDiag = "launchPid=$(if ($edge) { $edge.Id } else { 'none' }) launchExited=$(if ($edge) { $edge.HasExited } else { '-' }) fixturePids=[$(@(EdgeFixturePids | ForEach-Object { "pid=$_" }) -join ',')] hwnd=none (no profile-bound window)"
    }
    EdgeSetupShot 'edge-setup-timeout'
  }
  $edgeTabBefore = -1; try { $edgeTabBefore = EdgeSelectedTabIndex } catch { }; Info 'edge active tab index before scans' "$edgeTabBefore"
  # Fixed experiment, not retry-until-green: three idle controls followed by three fixture-only Selects.
  # No product is running yet. Only scalar tick/success/stage data in TracePhase; this cannot identify an actor.
  if (-not $edgeReady) { throw 'Three-tab fixture readiness required before focused probes' }
  foreach ($probeIndex in 0..2) { TracePhase 'probe-idle-before'; Start-Sleep -Milliseconds 150; TracePhase 'probe-idle-after' }
  $probeWindow = EdgeWindow
  $probeTabs = @($probeWindow.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::TabItem))))
  if ($probeTabs.Count -ne 3) { throw 'Probe requires exact three fixture tabs' }
  foreach ($probeIndex in 0..2) {
    TracePhase 'probe-select-before'
    $probeTabs[$probeIndex].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    TracePhase 'probe-select-return'
    Start-Sleep -Milliseconds 150
    TracePhase 'probe-select-after-settle'
  }
  # Regression (PR19 review): restore must also work when the user's tab is NOT first in UIA enumeration -
  # the old code discovered the original tab inside the mutating loop and lost it. Move the ONE verified
  # fixture window (same captured HWND) to its middle tab (index 1 of the three example.* tabs) before the
  # app starts; setup, before and after all read THAT window, and the post-scan assert below proves
  # restore for the active-middle case.
  $moved = $false
  $wE = EdgeWindow
  if ($edgeReady -and $null -ne $wE) {
    try {
      $tabsE = @($wE.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::TabItem))))
      if ($tabsE.Count -eq 3) { $tabsE[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $moved = $true }
    } catch { }
  }
  Start-Sleep -Milliseconds 800
  $edgeTabBefore = -1; try { $edgeTabBefore = EdgeSelectedTabIndex } catch { }
  Assert 'regression setup: Edge active tab moved to the middle (index 1) before the scans' ($edgeReady -and $moved -and $edgeTabBefore -eq 1) ("ready=$edgeReady moved=$moved before=$edgeTabBefore hwnd=0x$('{0:X}' -f $script:edgeHwnd) $edgeDiag")
  $exe = Get-ChildItem -Recurse src/StartApp.App/bin/Release -Filter StartApp.App.exe | Select-Object -First 1
  $env:STARTAPP_CAPTURE_TRACE_PATH = Join-Path $PWD 'diag/browser-capture-trace.txt'
  $env:STARTAPP_CRASHLOG = '1' # opt the app into crash.log evidence for this harness run
  $p = Start-Process -FilePath $exe.FullName -PassThru
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.Elapsed.TotalSeconds -lt 60) { $p.Refresh(); if ($p.HasExited -or $p.MainWindowHandle -ne 0) { break }; Start-Sleep -Milliseconds 500 }
  Start-Sleep -Seconds 2
  Assert 'app window opened' (-not $p.HasExited -and $p.MainWindowHandle -ne 0) ''
  $win = $A::RootElement.FindFirst($TS::Children, (New-Object $PC($A::ProcessIdProperty, $p.Id)))
  Assert 'automation window found' ($null -ne $win) ''
  $script:mainHwnd = $p.MainWindowHandle
  Info 'main window handle captured for owner-aware dialog discovery' ("mainHwnd=0x{0:X}" -f $script:mainHwnd.ToInt64())
  $vsB = [System.Windows.Forms.SystemInformation]::VirtualScreen
  Info 'virtual screen bounds (measured)' "left=$($vsB.Left) top=$($vsB.Top) right=$($vsB.Right) bottom=$($vsB.Bottom) ($($vsB.Width)x$($vsB.Height))"
  [void][W]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, $vsB.Left, $vsB.Top, 0, 0, 0x0001)
  Start-Sleep -Milliseconds 600
  $wr0 = (CurWin).Current.BoundingRectangle
  Assert 'app window fully inside the virtual screen' ($wr0.Left -ge $vsB.Left -and $wr0.Top -ge $vsB.Top -and $wr0.Right -le $vsB.Right -and $wr0.Bottom -le $vsB.Bottom) ("window=$([int]$wr0.Left),$([int]$wr0.Top)-$([int]$wr0.Right),$([int]$wr0.Bottom) screen=$($vsB.Left),$($vsB.Top)-$($vsB.Right),$($vsB.Bottom)")
  Assert 'overview shown' (WaitText 'Create workspace' 10) ((Texts) -join ' | ')
  Assert 'capture button is on the overview page' ($null -ne (Btn 'Capture')) ''
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
  Assert 'ALL TABS: the active tab site example.com is listed' ($null -ne (CheckByLike '*example.com*')) ($names -join ' | ')
  Assert 'ALL TABS: background tab example.org is listed' ($null -ne (CheckByLike '*example.org*')) ($names -join ' | ')
  Assert 'ALL TABS: background tab example.net is listed' ($null -ne (CheckByLike '*example.net*')) ($names -join ' | ')
  $edgeTabAfter = EdgeSelectedTabIndex
  $sameWin = ($null -ne (EdgeWindow))
  Assert 'ALL TABS: the tab the user was on stays selected after the scan' ($edgeTabBefore -ge 0 -and $sameWin -and $edgeTabAfter -eq $edgeTabBefore) ("before=$edgeTabBefore after=$edgeTabAfter hwnd=0x$('{0:X}' -f $script:edgeHwnd) sameWindow=$sameWin")
  $fgPid = [uint32]0; [void][W]::GetWindowThreadProcessId([W]::GetForegroundWindow(), [ref]$fgPid)
  Info 'foreground window after the all-tabs scan' $(if ($fgPid -eq $p.Id) { 'app (scan did not raise the browser)' } elseif (@(EdgeFixturePids) -contains $fgPid) { 'edge - the scan raises the scanned browser window' } else { "another window (pid $fgPid)" })
  ShotRaw 'post-scan-before-raise'
  # design-only dirty guard on X: typed design without a name is unsaved; discarding closes the capture screen and
  # resets the shared create form; reopening scans fresh with the form at its defaults
  SetText (NamedEdit 'Capture card color') '#E9D5FF'
  Shot 'new-form-design-filled'
  $c = ClickBtn 'Close capture' 900; Assert 'capture X clicked with design-only input' ($c -eq 'ok') $c
  Assert 'unsaved guard shown for capture-new design-only input' (WaitText 'You have changes that were not saved' 5) ((Texts) -join ' | ')
  Shot 'capture-new-design-unsaved-guard'
  $c = ClickBtn 'צא בלי לשמור' 900; Assert 'discard the design-only capture-new form' ($c -eq 'ok') $c
  Assert 'capture screen closed after discard' (-not (WaitText 'Open now' 2)) ''
  $c = OpenCaptureMenu 'סביבה חדשה'; Assert 'capture dropdown -> new (after the design discard)' ($c -eq 'ok') $c
  Assert 'capture screen shown after the design discard' (WaitText 'Open now' 10) ((Texts) -join ' | ')
  Assert 'rescan after discard finished' (WaitGone 'סורק' 30) ((Texts) -join ' | ')
  Assert 'reopened capture-new form has empty design fields' ((ValOf (NamedEdit 'Capture card color')) -eq '' -and (ValOf (NamedEdit 'Capture card image path')) -eq '') ''
  $names = CheckNames
  $rescanComplete = (($null -ne (CheckByLike '*example.com*')) -and ($null -ne (CheckByLike '*example.org*')) -and ($null -ne (CheckByLike '*example.net*')))
  Assert 'REAL rescan after discard: the three Edge tabs are listed again' $rescanComplete ($names -join ' | ')
  if (-not $rescanComplete) {
    # Read-only post-failure fixture evidence, not a retry or a relaxed completeness assertion.
    $fw = EdgeWindow
    if ($fw) {
      $ft = @($fw.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::TabItem))))
      Info 'failed rescan fixture state (no tab switching)' ("tabs=$($ft.Count) selected=$(EdgeSelectedTabIndex) activeAddress=$(BrowserWinUrl $fw)")
    }
    ShotRaw 'rescan-missing-tab-before-diagnostics'
    $notice = AnyByName 'Notification'
    if ($notice) {
      foreach ($group in @($notice.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Group))))) {
        try { $group.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand() } catch { }
      }
      Info 'failed rescan notification details' ((@($notice.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Edit)))) | ForEach-Object { ValOf $_ }) -join ' | ')
    }
  }
  Assert 'TABS-FOCUSED-FLOW-COMPLETE' $true $env:GITHUB_SHA
}
catch { [void]$results.Add([ordered]@{ name = 'tab reproduction error'; ok = $false; detail = $_.Exception.GetType().Name }) }
finally {
  $results | ConvertTo-Json -Depth 4 | Out-File diag/capture-assertions.json
  try { if ($p -and -not $p.HasExited) { $p.Kill(); $p.WaitForExit(5000) | Out-Null } }
  finally {
  foreach ($pidF in @(EdgeFixturePids | Where-Object { $_ -ne $null })) { Stop-Process -Id $pidF -Force -ErrorAction SilentlyContinue }
  if ($edgeProfile) { Remove-Item $edgeProfile -Recurse -Force -ErrorAction SilentlyContinue }
  }
}
if (@($results | Where-Object { $_.ok -eq $false }).Count -gt 0) { exit 1 }
}
catch { [void]$results.Add([ordered]@{ name = 'tab harness setup error'; ok = $false; detail = $_.Exception.GetType().Name }); $results | ConvertTo-Json -Depth 4 | Out-File diag/capture-assertions.json; exit 1 }
