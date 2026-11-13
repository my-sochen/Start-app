# Extension + native-host feasibility spike (bounded, disposable CI profile, no store/account/install on user systems).
# Proves: sideloaded MV3 extension can enumerate ALL tabs and deliver them to a native host process
# via nativeMessaging on Edge. Ground truth = 3 known tabs. Uncovered: discarded tabs, post-startup
# tab changes, other browsers, store/packaged install.
param([string]$OutDir = "ext-spike-out")
$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$log = "$OutDir/ext-spike-log.txt"
function L($m) { $l = "[{0:HH:mm:ss.fff}] {1}" -f (Get-Date), $m; $l; $l | Out-File -Append $log }

$work = Join-Path $env:TEMP 'ext-spike'
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$work/ext" | Out-Null

# ---- 1. deterministic extension key + id ----
$rsa = [System.Security.Cryptography.RSA]::Create(2048)
$der = $rsa.ExportSubjectPublicKeyInfo()
$keyB64 = [Convert]::ToBase64String($der)
$sha = [System.Security.Cryptography.SHA256]::Create().ComputeHash($der)
$id = -join ($sha[0..15] | ForEach-Object { '{0:x2}' -f $_ } | ForEach-Object { $_ }) 
# map hex nibbles 0-f -> a-p
$chars = 'abcdefghijklmnop'
$extId = -join (($id.ToCharArray()) | ForEach-Object { $chars[[Convert]::ToInt32([string]$_, 16)] })
L "extension id derived: $extId"

# ---- 2. extension files ----
$manifest = @"
{
  "manifest_version": 3,
  "name": "Start-app Tab Bridge SPIKE",
  "version": "0.1.0",
  "description": "Spike: list tabs to native host. Metadata only.",
  "key": "$keyB64",
  "permissions": ["tabs", "nativeMessaging"],
  "background": { "service_worker": "background.js" }
}
"@
$manifest | Out-File -Encoding utf8 "$work/ext/manifest.json"
$bg = @'
const HOST = 'com.startapp.tabbridge';
function answer(port, req) {
  if (!req || req.cmd !== 'list-tabs') return;
  chrome.tabs.query({}).then((tabs) => {
    port.postMessage({ id: req.id, tabs: tabs.map((t) => ({ windowId: t.windowId, index: t.index, active: t.active, discarded: !!t.discarded, title: t.title || '', url: t.url || '' })) });
  });
}
function open() {
  try {
    const port = chrome.runtime.connectNative(HOST);
    port.onMessage.addListener((m) => answer(port, m));
    port.onDisconnect.addListener(() => {});
  } catch (e) {}
}
chrome.runtime.onStartup.addListener(open);
chrome.runtime.onInstalled.addListener(open);
open();
'@
$bg | Out-File -Encoding utf8 "$work/ext/background.js"

# ---- 3. native host (C# stdio) ----
$cs = @'
using System;
using System.IO;
using System.Text;
class H {
  static void Main(string[] args) {
    string log = Path.Combine(Path.GetTempPath(), "tabbridge-host.log");
    try {
      File.AppendAllText(log, "host started " + DateTime.UtcNow.ToString("o") + "\n");
      Send("{\"cmd\":\"list-tabs\",\"id\":1}");
      string resp = Read();
      File.AppendAllText(log, "RESP " + resp + "\n");
    } catch (Exception e) { File.AppendAllText(log, "ERR " + e.ToString() + "\n"); }
  }
  static void Send(string m) {
    byte[] b = Encoding.UTF8.GetBytes(m);
    Stream so = Console.OpenStandardOutput();
    so.Write(BitConverter.GetBytes(b.Length), 0, 4);
    so.Write(b, 0, b.Length);
    so.Flush();
  }
  static string Read() {
    Stream si = Console.OpenStandardInput();
    byte[] lb = new byte[4]; ReadFull(si, lb, 4);
    int n = BitConverter.ToInt32(lb, 0);
    if (n <= 0 || n > 1048576) throw new Exception("bad frame len " + n);
    byte[] buf = new byte[n]; ReadFull(si, buf, n);
    return Encoding.UTF8.GetString(buf);
  }
  static void ReadFull(Stream s, byte[] buf, int n) { int o = 0; while (o < n) { int r = s.Read(buf, o, n - o); if (r <= 0) throw new EndOfStreamException(); o += r; } }
}
'@
$cs | Out-File -Encoding utf8 "$work/Host.cs"
$csc = @('C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe', 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe') | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { $csc = (Get-ChildItem 'C:\Windows\Microsoft.NET' -Recurse -Filter csc.exe -ErrorAction SilentlyContinue | Select-Object -First 1).FullName }
L "csc found: $csc"
if ($csc) { $cout = & $csc /nologo /out:"$work/host.exe" "$work/Host.cs" 2>&1; L "csc output: $($cout -join ' | ')" }
L "host compiled: $(Test-Path "$work/host.exe")"

$hostManifestPath = "$work/host-manifest.json"
$hostManifest = @"
{
  "name": "com.startapp.tabbridge",
  "description": "Start-app Tab Bridge native host SPIKE",
  "path": "$($work -replace '\\','\\')\\host.exe",
  "type": "stdio",
  "allowed_origins": ["chrome-extension://$extId/"]
}
"@
$hostManifest | Out-File -Encoding ascii $hostManifestPath
reg add "HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.startapp.tabbridge" /ve /d "$hostManifestPath" /f | Out-Null
L "registered host manifest at HKCU"

# ---- 4. launch Edge sideloaded with 3 known tabs ----
$prof = Join-Path $env:TEMP 'ext-spike-profile'
Remove-Item -Recurse -Force $prof -ErrorAction SilentlyContinue
$urls = @('https://example.com/', 'https://www.iana.org/help/example-domains', 'https://www.rfc-editor.org/rfc/rfc2606')
$edgeExe = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
$edge = Start-Process -FilePath $edgeExe -ArgumentList (@("--user-data-dir=$prof", "--load-extension=$work\ext", '--no-first-run', '--no-default-browser-check') + $urls) -PassThru
L "edge launched pid=$($edge.Id)"

# ---- 5. poll for host log response ----
$hostLog = Join-Path $env:TEMP 'tabbridge-host.log'
$resp = $null
for ($i = 0; $i -lt 45; $i++) {
  Start-Sleep -Seconds 1
  if (Test-Path $hostLog) {
    $content = Get-Content $hostLog -Raw
    if ($content -match 'RESP ') { $resp = $content; break }
    if ($content -match 'ERR ') { $resp = $content; L 'host errored'; break }
  }
}
if ($resp) { L "host log received after ~$($i)s"; Copy-Item $hostLog "$OutDir/tabbridge-host.log" -Force } else { L 'NO host log within 45s' }

# ---- 6. ground truth compare ----
$summary = [ordered]@{ hostResponded = [bool]$resp; groundTruth = @{}; allPresent = $false; tabsCount = 0; discardedFlagsPresent = $false }
if ($resp -and $resp -match 'RESP (\{.*\})') {
  try {
    $obj = $Matches[1] | ConvertFrom-Json
    $summary.tabsCount = @($obj.tabs).Count
    $summary.discardedFlagsPresent = ($null -ne $obj.tabs[0].PSObject.Properties['discarded'])
    foreach ($h in @('example.com','iana.org','rfc-editor.org')) {
      $summary.groundTruth[$h] = [bool](@($obj.tabs) | Where-Object { $_.url -like "*$h*" })
    }
    $summary.allPresent = -not ($summary.groundTruth.Values -contains $false)
    @($obj.tabs) | ForEach-Object { L "tab: [$($_.windowId)/$($_.index)] active=$($_.active) discarded=$($_.discarded) url=$($_.url)" }
  } catch { L "resp parse failed: $($_.Exception.Message)" }
}
$summary | ConvertTo-Json -Depth 5 | Out-File "$OutDir/summary.json"
L "SUMMARY: $($summary | ConvertTo-Json -Compress)"
Stop-Process -Id $edge.Id -Force -ErrorAction SilentlyContinue
L 'done'
