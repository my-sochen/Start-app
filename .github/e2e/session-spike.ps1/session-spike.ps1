# Session-file feasibility spike (bounded experiment, disposable CI profile).
# Question: can an app read a LIVE Edge profile's Sessions files and reconstruct
# open/closed tabs with correct URLs, and how fresh is that picture?
param([string]$OutDir = "spike-out")
$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$log = "$OutDir/spike-log.txt"
function L($m) { $l = "[{0:HH:mm:ss.fff}] {1}" -f (Get-Date), $m; $l; $l | Out-File -Append $log }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms

$edgeExe = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
if (-not (Test-Path $edgeExe)) { $edgeExe = 'C:\Program Files\Microsoft\Edge\Application\msedge.exe' }
$prof = Join-Path $env:TEMP 'spike-profile'
Remove-Item -Recurse -Force $prof -ErrorAction SilentlyContinue

$urls = @('https://example.com/', 'https://www.iana.org/help/example-domains', 'https://www.rfc-editor.org/rfc/rfc2606')
$hosts = @('example.com', 'iana.org', 'rfc-editor.org')

# ---------- SNSS parser ----------
function Parse-SnssFile([string]$path) {
  $r = [ordered]@{ path=$path; ok=$false; version=$null; error=$null; tabs=@{}; closedTabs=@(); closedWins=@(); selNav=@{}; rawIds=@{} }
  try { $b = [System.IO.File]::ReadAllBytes($path) } catch { $r.error = "READ-FAILED: $($_.Exception.Message)"; return $r }
  if ($b.Length -lt 8 -or $b[0] -ne 0x53 -or $b[1] -ne 0x4E -or $b[2] -ne 0x53 -or $b[3] -ne 0x53) { $r.error = 'bad magic'; return $r }
  $r.version = [BitConverter]::ToInt32($b, 4)
  $off = 8; $seq = 0
  while ($off + 3 -le $b.Length) {
    $size = [BitConverter]::ToInt16($b, $off)
    if ($size -lt 1 -or $off + 2 + $size -gt $b.Length) { $r.error = "framing broke at $off size=$size len=$($b.Length)"; break }
    $id = $b[$off + 2]
    $pl = $off + 3; $plen = $size - 1
    $r.rawIds[$id] = 1 + [int]($r.rawIds[$id] ?? 0)
    if ($plen -ge 4) {
      $tabId = [BitConverter]::ToInt32($b, $pl)
      switch ($id) {
        0 { # SetTabWindow: tabId, windowId
          if (-not $r.tabs.Contains($tabId)) { $r.tabs[$tabId] = @{ urls = New-Object System.Collections.ArrayList; window = [BitConverter]::ToInt32($b, $pl+4); firstSeq = $seq } }
        }
        6 { # UpdateTabNavigation: tabId + SerializedNavigationEntry pickle; first http(s) string = virtual_url
          $m = [regex]::Match([System.Text.Encoding]::ASCII.GetString($b, $pl+4, $plen-4), 'https?://[ -~]{2,300}')
          if ($m.Success) {
            $u = $m.Value
            if (-not $r.tabs.Contains($tabId)) { $r.tabs[$tabId] = @{ urls = New-Object System.Collections.ArrayList; window = -1; firstSeq = $seq } }
            [void]$r.tabs[$tabId].urls.Add(@{ seq=$seq; url=$u })
          }
        }
        7 { $r.selNav[$tabId] = [BitConverter]::ToInt32($b, $pl+4) }
        16 { $r.closedTabs += @{ tabId=$tabId; closeTime=[BitConverter]::ToInt64($b, $pl+4); seq=$seq }; $r.tabs.Remove($tabId) }
        17 { $r.closedWins += @{ winId=$tabId; closeTime=[BitConverter]::ToInt64($b, $pl+4); seq=$seq } }
      }
    }
    $off += 2 + $size; $seq++
  }
  $r.ok = -not $r.error
  return $r
}

function Snapshot([string]$label) {
  $sessDir = Join-Path $prof 'Default\Sessions'
  $snap = [ordered]@{ label=$label; time=(Get-Date).ToString('o'); sessionFiles=@(); result=$null }
  if (Test-Path $sessDir) {
    $snap.sessionFiles = @(Get-ChildItem $sessDir -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | ForEach-Object { @{ name=$_.Name; len=$_.Length; mtime=$_.LastWriteTime.ToString('o') } })
    $live = Get-ChildItem $sessDir -Filter 'Session_*' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($live) {
      # live readability tests
      $direct = $true; try { $null = [System.IO.File]::ReadAllBytes($live.FullName) } catch { $direct = $false }
      $copyOk = $true; try { Copy-Item $live.FullName "$OutDir\copy-$label.snss" -Force -ErrorAction Stop } catch { $copyOk = $false }
      L "$label : live file=$($live.Name) directRead=$direct copy=$copyOk"
      $snap.directRead = $direct; $snap.copyOk = $copyOk
      $snap.result = Parse-SnssFile $live.FullName
    } else { L "$label : no Session_* file yet" }
  } else { L "$label : no Sessions dir yet" }
  return $snap
}

function Open-TabUrls($snap) {
  if (-not $snap.result -or -not $snap.result.ok) { return @() }
  $out = @()
  foreach ($k in $snap.result.tabs.Keys) {
    $u = $snap.result.tabs[$k].urls
    if ($u.Count -gt 0) { $out += [string]$u[$u.Count-1].url }
  }
  return $out
}

# ---------- Phase A: launch with 3 known tabs ----------
L "launching edge: $edgeExe profile=$prof"
$edge = Start-Process -FilePath $edgeExe -ArgumentList (@("--user-data-dir=$prof",'--no-first-run','--no-default-browser-check','--disable-session-crashed-bubble','--disable-features=EdgeWelcomePage') + $urls) -PassThru
Start-Sleep -Seconds 15
$A = Snapshot 'phaseA'
$A | ConvertTo-Json -Depth 6 | Out-File "$OutDir/phaseA.json"
$openA = Open-TabUrls $A
L "phaseA open urls: $($openA -join ' || ')"
$phaseA = @{}
foreach ($h in $hosts) { $phaseA[$h] = [bool]($openA | Where-Object { $_ -like "*$h*" }) }
L "phaseA coverage: $($phaseA.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" } | Out-String)"

# ---------- Phase B: close the iana tab, measure freshness ----------
$closedAt = $null; $closeMethod = 'none'
try {
  $root = [System.Windows.Automation.AutomationElement]::RootElement
  $pc = [System.Windows.Automation.PropertyCondition]
  $ct = [System.Windows.Automation.ControlType]
  $edgeWin = $null
  foreach ($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children, (New-Object $pc([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'Chrome_WidgetWin_1')))) {
    if ($w.Current.ProcessId -eq $edge.Id) { $edgeWin = $w; break }
  }
  if (-not $edgeWin) { $edgeWin = $root.FindAll([System.Windows.Automation.TreeScope]::Children, (New-Object $pc([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'Chrome_WidgetWin_1')))[0] }
  $tabCond = New-Object $pc([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ct::TabItem)
  $tabs = $edgeWin.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tabCond)
  L "edge tabs found via UIA: $($tabs.Count) names: $(($tabs | ForEach-Object { $_.Current.Name }) -join ' | ')"
  $target = $tabs | Where-Object { $_.Current.Name -like '*Example Domains*' } | Select-Object -First 1
  if ($target) {
    $target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 800
    [System.Windows.Forms.SendKeys]::SendWait('^w')
    $closeMethod = 'UIA-select + Ctrl+W'
    $closedAt = Get-Date
    L "closed iana tab at $($closedAt.ToString('HH:mm:ss.fff')) via $closeMethod"
  } else { L 'iana tab not found via UIA' }
} catch { L "close failed: $($_.Exception.Message)" }

# poll for the close to land in the live file
$freshness = [ordered]@{ closeAt = if ($closedAt) { $closedAt.ToString('o') } else { $null }; mtimeFlipSec = $null; contentFlipSec = $null; firstFiles = @() }
if ($closedAt) {
  $sessDir = Join-Path $prof 'Default\Sessions'
  $startFiles = @{}
  Get-ChildItem $sessDir -ErrorAction SilentlyContinue | ForEach-Object { $startFiles[$_.Name] = $_.LastWriteTime }
  for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 1
    $cur = Get-ChildItem $sessDir -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
    $newest = $cur | Select-Object -First 1
    $flip = ($cur | Where-Object { -not $startFiles.ContainsKey($_.Name) -or $startFiles[$_.Name] -ne $_.LastWriteTime })
    if ($flip -and -not $freshness.mtimeFlipSec) { $freshness.mtimeFlipSec = [math]::Round(((Get-Date) - $closedAt).TotalSeconds, 1); L "mtime flip after $($freshness.mtimeFlipSec)s -> $($newest.Name)" }
    if ($newest -and $newest.Name -like 'Session_*') {
      $pr = Parse-SnssFile $newest.FullName
      if ($pr.ok) {
        $openNow = @()
        foreach ($k in $pr.tabs.Keys) { $u = $pr.tabs[$k].urls; if ($u.Count -gt 0) { $openNow += [string]$u[$u.Count-1].url } }
        if ($openNow.Count -gt 0 -and -not ($openNow | Where-Object { $_ -like '*iana.org*' }) -and ($openNow | Where-Object { $_ -like '*example.com*' })) {
          $freshness.contentFlipSec = [math]::Round(((Get-Date) - $closedAt).TotalSeconds, 1)
          L "content flip after $($freshness.contentFlipSec)s: iana gone, open=$($openNow -join ' || ')"
          break
        }
      }
    }
  }
  if (-not $freshness.contentFlipSec) { L 'content never flipped within 60s' }
}

# ---------- Phase C: final snapshot incl. Tabs_ (closed tabs) ----------
$C = Snapshot 'phaseC'
$sessDir = Join-Path $prof 'Default\Sessions'
$tabsFile = Get-ChildItem $sessDir -Filter 'Tabs_*' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$closedParse = $null
if ($tabsFile) {
  $closedParse = Parse-SnssFile $tabsFile.FullName
  L "Tabs_ file=$($tabsFile.Name) parsed ok=$($closedParse.ok) tabs-in-file=$(($closedParse.tabs.Values | Where-Object { $_.urls.Count -gt 0 }).Count) closedRecords=$($closedParse.closedTabs.Count)"
  $closedParse | ConvertTo-Json -Depth 6 | Out-File "$OutDir/tabs-file.json"
}
# encrypted variant?
$encDir = Join-Path $prof 'Default\Sessions_Encrypted'
L "Sessions_Encrypted exists: $(Test-Path $encDir)"

$openC = Open-TabUrls $C
$summary = [ordered]@{
  phaseA_coverage = $phaseA
  phaseA_version = $A.result.version
  phaseA_parseOk = $A.result.ok
  phaseA_rawIds = ($A.result.rawIds.Keys | Sort-Object | ForEach-Object { [string]$_ }) -join ','
  directReadLive = $A.directRead
  copyOkLive = $A.copyOk
  closeMethod = $closeMethod
  freshness = $freshness
  phaseC_open = $openC
  ianaGoneFromOpen = -not [bool]($openC | Where-Object { $_ -like '*iana.org*' })
  othersStillOpen = ([bool]($openC | Where-Object { $_ -like '*example.com*' }) -and [bool]($openC | Where-Object { $_ -like '*rfc-editor.org*' }))
  tabsFile = if ($tabsFile) { $tabsFile.Name } else { $null }
  tabsFileHasIana = if ($closedParse) { $j = $closedParse | ConvertTo-Json -Depth 6; $j -match 'iana\.org' } else { $null }
  encryptedSessionsFolder = (Test-Path $encDir)
}
$summary | ConvertTo-Json -Depth 6 | Out-File "$OutDir/summary.json"
L "SUMMARY: $($summary | ConvertTo-Json -Depth 6 -Compress)"
Stop-Process -Id $edge.Id -Force -ErrorAction SilentlyContinue
L 'done'
