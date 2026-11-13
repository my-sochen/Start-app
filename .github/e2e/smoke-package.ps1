param(
  [string]$ExePath = 'verify-ship\StartApp.App.exe',
  [string]$ShotPath = 'out-images\shipped-smoke-main-window.png'
)
New-Item -ItemType Directory -Force diag, out-images | Out-Null
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
# Capture actual window pixels of the shipped package for visual review.
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class W { [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; } [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags); }'
[void][W]::SetCursorPos(5, 5)
[void][W]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, 0, 0, 0, 0, 0x27)
Start-Sleep -Milliseconds 400
$r = New-Object W+R
[void][W]::GetWindowRect($p.MainWindowHandle, [ref]$r)
$w = $r.Rt - $r.L; $h = $r.B - $r.T
if ($w -le 0 -or $h -le 0) {
  "SMOKE FAIL: no usable window rect for screenshot ($w x $h)" | Tee-Object -Append diag/errors.txt
  $p.Kill(); exit 1
}
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$bmp.Save("$PWD\$ShotPath")
$g.Dispose(); $bmp.Dispose()
if (-not (Test-Path $ShotPath) -or (Get-Item $ShotPath).Length -eq 0) {
  "SMOKE FAIL: screenshot was not produced at $ShotPath" | Tee-Object -Append diag/errors.txt
  $p.Kill(); exit 1
}
"SMOKE SHOT: $ShotPath ${w}x${h}"
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
