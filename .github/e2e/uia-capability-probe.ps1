$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows') { throw 'Disposable Windows GitHub runner required' }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
$profile = Join-Path $env:RUNNER_TEMP ('uia-probe-profile-' + [Guid]::NewGuid().ToString('N'))
$fixture = Join-Path $env:RUNNER_TEMP ('uia-probe-fixture-' + [Guid]::NewGuid().ToString('N'))
$helper = $null; $window = $null
function ProfilePids { @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($profile) } | ForEach-Object { $_.ProcessId }) }
function Screenshot($name) {
  if ($null -eq $window) { return }
  $r = $window.Current.BoundingRectangle
  if ($r.Width -le 0 -or $r.Height -le 0) { throw 'Fixture has no visible rectangle' }
  $bmp = New-Object System.Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  try { $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size); $bmp.Save((Join-Path $PWD "diag/$name.png")) } finally { $g.Dispose(); $bmp.Dispose() }
}
try {
  New-Item -ItemType Directory -Path $fixture | Out-Null
  '<!doctype html><title>Synthetic same</title><h1>Synthetic same</h1>' | Set-Content (Join-Path $fixture 'same.html')
  '<!doctype html><title>Synthetic other</title><h1>Synthetic other</h1>' | Set-Content (Join-Path $fixture 'other.html')
  # Generate a minimal local PDF with exact byte offsets, no external fetch or private data.
  $pdf = "%PDF-1.4`n"; $offsets = @(0)
  $objects = @('<< /Type /Catalog /Pages 2 0 R >>', '<< /Type /Pages /Kids [3 0 R] /Count 1 >>', '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] >>')
  for ($i = 0; $i -lt $objects.Count; $i++) { $offsets += $pdf.Length; $pdf += "$($i+1) 0 obj`n$($objects[$i])`nendobj`n" }
  $xref = $pdf.Length; $pdf += "xref`n0 4`n0000000000 65535 f `n"
  for ($i = 1; $i -lt 4; $i++) { $pdf += ('{0:0000000000} 00000 n ' -f $offsets[$i]) + "`n" }
  $pdf += "trailer`n<< /Size 4 /Root 1 0 R >>`nstartxref`n$xref`n%%EOF`n"
  [IO.File]::WriteAllBytes((Join-Path $fixture 'fixture.pdf'), [Text.Encoding]::ASCII.GetBytes($pdf))
  $same = ([Uri](Join-Path $fixture 'same.html')).AbsoluteUri
  $other = ([Uri](Join-Path $fixture 'other.html')).AbsoluteUri
  $pdfUrl = ([Uri](Join-Path $fixture 'fixture.pdf')).AbsoluteUri
  Start-Process msedge -ArgumentList @('--new-window', '--no-first-run', '--no-default-browser-check', "--user-data-dir=$profile", $same, $same, 'about:blank', 'about:blank', $other, $pdfUrl) | Out-Null
  $deadline = [DateTime]::UtcNow.AddSeconds(30)
  do {
    $pids = @(ProfilePids)
    $windows = @([System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $pids -contains $_.Current.ProcessId -and -not $_.Current.IsOffscreen -and $_.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem))).Count -eq 6 })
    if ($windows.Count -eq 1) { $window = $windows[0]; break }
    Start-Sleep -Milliseconds 250
  } while ([DateTime]::UtcNow -lt $deadline)
  if ($null -eq $window) { throw 'No uniquely profile-bound six-tab fixture' }
  Screenshot 'probe-before'
  $exe = Join-Path $PWD 'tests/StartApp.UiaCapabilityProbe/bin/Release/net10.0-windows/StartApp.UiaCapabilityProbe.exe'
  $helper = Start-Process $exe -ArgumentList @("$($window.Current.NativeWindowHandle)", ('"' + $fixture + '"'), ('"' + (Join-Path $PWD 'diag') + '"')) -PassThru
  if (-not $helper.WaitForExit(120000)) { throw 'Probe exceeded 120 seconds' }
  Screenshot 'probe-after'
  if ($helper.ExitCode -ne 0) { throw 'Native probe failed; inspect classified sample evidence' }
} finally {
  if ($helper -and -not $helper.HasExited) { Stop-Process -Id $helper.Id -Force -ErrorAction Continue }
  @(ProfilePids) | ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction Continue }
  if (Test-Path $profile) { Remove-Item $profile -Recurse -Force -ErrorAction Continue }
  if (Test-Path $fixture) { Remove-Item $fixture -Recurse -Force -ErrorAction Continue }
}
