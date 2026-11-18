$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows') { throw 'Disposable Windows GitHub runner required' }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
$profile = Join-Path $env:RUNNER_TEMP ('uia-probe-profile-' + [Guid]::NewGuid().ToString('N'))
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
  Start-Process msedge -ArgumentList @('--new-window', '--no-first-run', '--no-default-browser-check', "--user-data-dir=$profile", 'https://www.python.org/', 'https://www.python.org/', 'https://www.mozilla.org/', 'https://www.wikipedia.org/', 'about:blank', 'about:blank') | Out-Null
  $deadline = [DateTime]::UtcNow.AddSeconds(30)
  do {
    $pids = @(ProfilePids)
    $windows = @([System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $pids -contains $_.Current.ProcessId -and -not $_.Current.IsOffscreen -and $_.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem))).Count -eq 6 })
    if ($windows.Count -eq 1) { $window = $windows[0]; break }
    Start-Sleep -Milliseconds 250
  } while ([DateTime]::UtcNow -lt $deadline)
  if ($null -eq $window) { throw 'No uniquely profile-bound six-tab fixture' }
  Screenshot 'probe-before'
  $exe = Join-Path $PWD 'tests/StartApp.NativeCapturePolicy/bin/Release/net10.0-windows/StartApp.NativeCapturePolicy.exe'
  $env:STARTAPP_CAPTURE_TRACE_PATH = Join-Path $PWD 'diag/native-policy-trace.txt'
  $helper = Start-Process $exe -ArgumentList @("$($window.Current.NativeWindowHandle)", ('"' + (Join-Path $PWD 'diag') + '"')) -PassThru
  if (-not $helper.WaitForExit(180000)) { throw 'Native fixture exceeded 180 seconds' }
  Screenshot 'probe-after'
  if ($helper.ExitCode -ne 0) { throw 'Native policy cases failed; inspect actual assertions' }
} finally {
  if ($helper -and -not $helper.HasExited) { Stop-Process -Id $helper.Id -Force -ErrorAction Continue }
  @(ProfilePids) | ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction Continue }
  if (Test-Path $profile) { Remove-Item $profile -Recurse -Force -ErrorAction Continue }
}
