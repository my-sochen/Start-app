# Session-file feasibility spike v2 (bounded experiment, disposable CI profile).
# v1 found: plain Session_ file is 0 bytes + locked; Sessions_Encrypted exists (format v5 era).
# v3: fixed error capture, alternate readers, Defender status, magic bytes, graceful-close re-read.
# BOUNDARY: no decryption. Encrypted content is reported as metadata only.
param([string]$OutDir = "spike-out")
$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$log = "$OutDir/spike-log.txt"
function L($m) { $l = "[{0:HH:mm:ss.fff}] {1}" -f (Get-Date), $m; $l; $l | Out-File -Append $log }

$edgeExe = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
if (-not (Test-Path $edgeExe)) { $edgeExe = 'C:\Program Files\Microsoft\Edge\Application\msedge.exe' }
$prof = Join-Path $env:TEMP 'spike-profile2'
Remove-Item -Recurse -Force $prof -ErrorAction SilentlyContinue

$urls = @('https://example.com/', 'https://www.iana.org/help/example-domains', 'https://www.rfc-editor.org/rfc/rfc2606')

function Try-ReadLocked([string]$path) {
  # ReadAllBytes/Copy-Item use FileShare.Read and fail on Chromium's locks; a forensic reader
  # opens with FileShare ReadWrite+Delete. Returns @{ ok; len; magic; error }.
  $r = @{ ok=$false; len=$null; magic=$null; error=$null }
  foreach ($share in @([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete, [System.IO.FileShare]::Read)) {
    try {
      $fs = New-Object System.IO.FileStream($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, $share)
      try {
        $buf = New-Object byte[] 8
        $n = $fs.Read($buf, 0, 8)
        $r.len = $fs.Length
        $r.magic = (($buf[0..([math]::Max(0,$n-1))] | ForEach-Object { $_.ToString('x2') }) -join ' ')
        $r.ok = $true
        $r.error = $null
        return $r
      } finally { $fs.Dispose() }
    } catch { $r.error = "share=$([int]$share): $($_.Exception.Message)" }
  }
  try { $dst = Join-Path $env:TEMP ('readtest-' + [IO.Path]::GetFileName($path)); Copy-Item $path $dst -Force -ErrorAction Stop; $alt = (Get-Item $dst).Length; $r.error += " | CopyItem OK len=$alt"; if (-not $r.ok) { $b = [IO.File]::ReadAllBytes($dst); $r.ok=$true; $r.len=$b.Length; $r.magic=(($b[0..([math]::Min(7,$b.Length-1))] | ForEach-Object { $_.ToString('x2') }) -join ' '); $r.error += ' (parsed from copy)' }; Remove-Item $dst -Force -ErrorAction SilentlyContinue } catch { $r.error += " | CopyItem: $($_.Exception.Message)" }
  return $r
}

function Inventory([string]$label) {
  $inv = @()
  foreach ($sub in @('Default\Sessions', 'Default\Sessions_Encrypted')) {
    $dir = Join-Path $prof $sub
    if (Test-Path $dir) {
      foreach ($f in (Get-ChildItem $dir -ErrorAction SilentlyContinue)) {
        $rd = Try-ReadLocked $f.FullName
        $inv += [ordered]@{ folder=$sub; name=$f.Name; sizeOnDisk=$f.Length; readOk=$rd.ok; readLen=$rd.len; magic=$rd.magic; readError=$rd.error; mtime=$f.LastWriteTime.ToString('o') }
        L "$label : $sub\$($f.Name) diskLen=$($f.Length) readOk=$($rd.ok) readLen=$($rd.len) magic=$($rd.magic) err=$($rd.error)"
      }
    } else { L "$label : $sub missing" }
  }
  return $inv
}

# Plaintext SNSS parser (for at-rest files only; encrypted files reported as metadata)
function Parse-SnssPlain([string]$path) {
  $r = [ordered]@{ ok=$false; version=$null; tabs=@{}; closedTabs=@(); error=$null }
  try { $b = [System.IO.File]::ReadAllBytes($path) } catch { $r.error = "READ-FAILED"; return $r }
  if ($b.Length -lt 8 -or $b[0] -ne 0x53 -or $b[1] -ne 0x4E -or $b[2] -ne 0x53 -or $b[3] -ne 0x53) { $r.error = 'bad magic'; return $r }
  $r.version = [BitConverter]::ToInt32($b, 4)
  $off = 8
  while ($off + 3 -le $b.Length) {
    $size = [BitConverter]::ToInt16($b, $off)
    if ($size -lt 1 -or $off + 2 + $size -gt $b.Length) { break }
    $id = $b[$off + 2]; $pl = $off + 3; $plen = $size - 1
    if ($plen -ge 4) {
      $tabId = [BitConverter]::ToInt32($b, $pl)
      if ($id -eq 6) {
        $m = [regex]::Match([System.Text.Encoding]::ASCII.GetString($b, $pl+4, $plen-4), 'https?://[ -~]{2,300}')
        if ($m.Success) {
          if (-not $r.tabs.Contains($tabId)) { $r.tabs[$tabId] = New-Object System.Collections.ArrayList }
          [void]$r.tabs[$tabId].Add($m.Value)
        }
      } elseif ($id -eq 16) { $r.closedTabs += $tabId; $r.tabs.Remove($tabId) }
    }
    $off += 2 + $size
  }
  $r.ok = $true
  return $r
}

try {
  $mp = Get-MpComputerStatus
  L "defender: RTP=$($mp.RealTimeProtectionEnabled) ASRcount=$(@($mp.AttackSurfaceReductionRules_Ids).Count) CFA=$($mp.EnableControlledFolderAccess)"
} catch { L "defender status read failed: $($_.Exception.Message)" }
L "launching edge: $edgeExe profile=$prof"
$edge = Start-Process -FilePath $edgeExe -ArgumentList (@("--user-data-dir=$prof",'--no-first-run','--no-default-browser-check','--disable-session-crashed-bubble') + $urls) -PassThru
Start-Sleep -Seconds 15

# --- Phase A: live inventory ---
$invA = Inventory 'live'
$invA | ConvertTo-Json -Depth 4 | Out-File "$OutDir/inventory-live.json"

# --- Phase B: close one tab, mtime freshness (lock-independent) ---
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
$closedAt = $null
try {
  $root = [System.Windows.Automation.AutomationElement]::RootElement
  $pc = [System.Windows.Automation.PropertyCondition]
  $ct = [System.Windows.Automation.ControlType]
  $wins = $root.FindAll([System.Windows.Automation.TreeScope]::Children, (New-Object $pc([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'Chrome_WidgetWin_1')))
  $edgeWin = $null
  foreach ($w in $wins) { if ($w.Current.ProcessId -eq $edge.Id) { $edgeWin = $w; break } }
  if (-not $edgeWin -and $wins.Count -gt 0) { $edgeWin = $wins[0] }
  $tabs = $edgeWin.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object $pc([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ct::TabItem)))
  L "edge tabs via UIA: $($tabs.Count): $(($tabs | ForEach-Object { $_.Current.Name }) -join ' | ')"
  $target = $tabs | Where-Object { $_.Current.Name -like '*Example Domains*' } | Select-Object -First 1
  if ($target) {
    $target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 800
    [System.Windows.Forms.SendKeys]::SendWait('^w')
    $closedAt = Get-Date
    L "closed iana tab at $($closedAt.ToString('HH:mm:ss.fff'))"
  }
} catch { L "close failed: $($_.Exception.Message)" }

$fresh = @{ closeAt = if ($closedAt) { $closedAt.ToString('o') } else { $null }; mtimeFlipSec = $null }
if ($closedAt) {
  $dirs = @((Join-Path $prof 'Default\Sessions'), (Join-Path $prof 'Default\Sessions_Encrypted'))
  $start = @{}
  foreach ($d in $dirs) { Get-ChildItem $d -ErrorAction SilentlyContinue | ForEach-Object { $start["$($_.FullName)"] = $_.LastWriteTime } }
  for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep -Seconds 1
    foreach ($d in $dirs) {
      foreach ($f in (Get-ChildItem $d -ErrorAction SilentlyContinue)) {
        if (-not $start.ContainsKey($f.FullName) -or $start[$f.FullName] -ne $f.LastWriteTime) {
          $fresh.mtimeFlipSec = [math]::Round(((Get-Date) - $closedAt).TotalSeconds, 1)
          $fresh.flipFile = "$($f.Directory.Name)\$($f.Name)"
          L "mtime flip after $($fresh.mtimeFlipSec)s -> $($fresh.flipFile)"
          break
        }
      }
      if ($fresh.mtimeFlipSec) { break }
    }
    if ($fresh.mtimeFlipSec) { break }
  }
  if (-not $fresh.mtimeFlipSec) { L 'no mtime flip within 20s' }
}

# --- Phase C: graceful close, then at-rest inventory + plaintext parse ---
L 'closing edge gracefully'
$edge.CloseMainWindow() | Out-Null
$closed = $edge.WaitForExit(15000)
if (-not $closed) { L 'edge did not exit in 15s; killing'; Stop-Process -Id $edge.Id -Force -ErrorAction SilentlyContinue; Start-Sleep 3 } else { L 'edge exited gracefully' }
Start-Sleep -Seconds 3
$invB = Inventory 'at-rest'
$invB | ConvertTo-Json -Depth 4 | Out-File "$OutDir/inventory-atrest.json"

$parseResults = @()
foreach ($e in $invB) {
  if ($e.readOk -and $e.readLen -gt 8) {
    $pr = Parse-SnssPlain (Join-Path $prof ($e.folder + '\' + $e.name))
    $parseResults += [ordered]@{ file = "$($e.folder)\$($e.name)"; ok = $pr.ok; version = $pr.version; error = $pr.error; openTabs = ($pr.tabs.Values | ForEach-Object { $_[-1] }) }
    L "at-rest parse $($e.name): ok=$($pr.ok) version=$($pr.version) err=$($pr.error)"
  }
}
$parseResults | ConvertTo-Json -Depth 4 | Out-File "$OutDir/parse-atrest.json"

$summary = [ordered]@{
  liveInventory = $invA
  freshness = $fresh
  atRestInventory = $invB
  atRestParse = $parseResults
  edgeExitedGracefully = $closed
}
$summary | ConvertTo-Json -Depth 6 | Out-File "$OutDir/summary.json"
L 'done'
