# Shared DEV subset and TEST full synthetic capture command. No receipt reuse.
$ErrorActionPreference = 'Stop'
if ($env:SHA -cnotmatch '^[0-9a-f]{40}$' -or (git rev-parse HEAD) -cne $env:SHA) {
  throw 'Synthetic capture requires the exact checked-out commit'
}
python tests/update-notifier/test_overlay_rows.py
if ($LASTEXITCODE -ne 0) { exit 1 }
dotnet build tests/StartApp.UpdateUiCapture/StartApp.UpdateUiCapture.csproj --configuration Release -p:StartAppReleaseVersion=0.1.0-proto.2 -p:StartAppSourceCommit=$env:SHA
if ($LASTEXITCODE -ne 0) { exit 1 }
$output = Join-Path $env:RUNNER_TEMP ("update-capture-" + [guid]::NewGuid().ToString('N'))
if (Test-Path $output) { throw 'capture output already exists; refusing reuse' }
$destination = Join-Path $PWD 'out-images/update-states'
if (Test-Path $destination) { throw 'capture destination already exists; refusing nested copy/reuse' }
$binary = Join-Path $PWD 'tests/StartApp.UpdateUiCapture/bin/Release/net10.0-windows/StartApp.UpdateUiCapture.exe'
# Bounded child lifetime; only this capture process tree is terminated. No
# machine-wide cleanup or real LOCALAPPDATA access by this test program.
$captureOut = Join-Path $env:RUNNER_TEMP ("update-capture-" + [guid]::NewGuid().ToString('N') + '.out.log')
$captureErr = Join-Path $env:RUNNER_TEMP ("update-capture-" + [guid]::NewGuid().ToString('N') + '.err.log')
$capture = Start-Process -FilePath $binary -ArgumentList ('"' + $output + '"') -PassThru -RedirectStandardOutput $captureOut -RedirectStandardError $captureErr
try {
  if (-not $capture.WaitForExit(60000)) {
    $capture.Kill($true)
    throw 'synthetic WPF capture timed out'
  }
  if ($capture.ExitCode -ne 0) {
    # Dump the child's own output before failing: an unhandled .NET exception (exit -532462766) lands here.
    if (Test-Path $captureOut) { '--- capture stdout ---'; Get-Content $captureOut }
    if (Test-Path $captureErr) { '--- capture stderr ---'; Get-Content $captureErr }
    throw "capture exited $($capture.ExitCode)"
  }
} finally {
  if (Test-Path $output) { Copy-Item -Path $output -Destination $destination -Recurse }
  $capture.Dispose()
}
$pngs = @(Get-ChildItem out-images/update-states -Filter '*.png')
if ($pngs.Count -ne 30) { throw "Expected thirty WPF state images (eighteen original states plus twelve beta/event/MAIN states), found $($pngs.Count)" }
if (-not (Test-Path out-images/update-states/capture-evidence.json)) { throw 'capture metadata missing' }
$evidence = @(Get-Content out-images/update-states/capture-evidence.json -Raw | ConvertFrom-Json)
$identity = @($evidence | Where-Object { $_.state -ceq 'loaded-App-identity' })
$selection = @($evidence | Where-Object { $_.state -ceq 'default-factory-source-selection' })
if ($identity.Count -ne 1 -or $identity[0].sourceCommit -cne $env:SHA -or $identity[0].informationalVersion -cne "0.1.0-proto.2+sha.$env:SHA") { throw 'Captured App identity does not match exact checkout' }
if ($selection.Count -ne 1 -or $selection[0].selectedSource -cne 'MainBetaNoticeSource') { throw 'Default MAIN source selection evidence missing' }
foreach ($width in 900,720) {
  $path = @($evidence | Where-Object { $_.state -ceq "$width-main-notice-event-path" })
  if ($path.Count -ne 1 -or $path[0].startupReads -ne 11 -or $path[0].clickReads -ne 11 -or $path[0].explicitBrowserSinkCalls -ne 1) { throw 'Injected MAIN notice event evidence incomplete' }
}
