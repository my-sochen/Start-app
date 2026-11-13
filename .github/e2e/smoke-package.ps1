param([string]$ExePath = 'verify-ship\StartApp.App.exe')
New-Item -ItemType Directory -Force diag | Out-Null
if (-not (Test-Path $ExePath)) { "SMOKE FAIL: $ExePath not found" | Out-File -Append diag/errors.txt; exit 1 }
$exe = (Resolve-Path $ExePath).Path
$p = Start-Process -FilePath $exe -PassThru
$ok = $false
for ($i = 0; $i -lt 60; $i++) {
  Start-Sleep -Milliseconds 500
  $p.Refresh()
  if ($p.HasExited) { break }
  if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $ok = $true; break }
}
if (-not $ok) {
  $state = if ($p.HasExited) { "exited early, code $($p.ExitCode)" } else { 'still running, no window' }
  "SMOKE FAIL: no main window within 30s ($state)" | Tee-Object -Append diag/errors.txt
  if (-not $p.HasExited) { $p.Kill() }
  exit 1
}
"SMOKE PASS: main window handle $($p.MainWindowHandle)"
[void]$p.CloseMainWindow()
if (-not $p.WaitForExit(10000)) {
  'SMOKE FAIL: app did not exit within 10s after CloseMainWindow' | Tee-Object -Append diag/errors.txt
  $p.Kill()
  exit 1
}
if ($p.ExitCode -ne 0) {
  "SMOKE FAIL: exit code $($p.ExitCode), expected 0" | Tee-Object -Append diag/errors.txt
  exit 1
}
'SMOKE PASS: clean exit, code 0'
