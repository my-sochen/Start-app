$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$out = Join-Path $PSScriptRoot 'evidence'
New-Item -ItemType Directory -Path $out -ErrorAction Stop | Out-Null
$fixturePath = (Resolve-Path (Join-Path $PSScriptRoot 'fixture/bin/Release/net10.0-windows/RawObserverFixture.exe')).Path
$observerPath = (Resolve-Path (Join-Path $PSScriptRoot 'observer/bin/Release/net10.0-windows/RawObserver.exe')).Path
$ready = Join-Path $out 'fixture.json'
$result = Join-Path $out 'observation.json'
# Exact binary digest is evidence, not identity authorization. These are our just-built fixed fixture binaries.
Get-FileHash -Algorithm SHA256 $fixturePath,$observerPath | ConvertTo-Json | Set-Content (Join-Path $out 'binary-hashes.json')
function Start-ExactProcess([string] $Exe, [string[]] $ExactArgs) {
    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $Exe; $info.UseShellExecute = $false
    foreach ($arg in $ExactArgs) { $info.ArgumentList.Add($arg) }
    return [System.Diagnostics.Process]::Start($info)
}
$fixture = Start-ExactProcess $fixturePath @($ready)
try {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path $ready)) {
        if ($fixture.HasExited -or $watch.ElapsedMilliseconds -gt 2500) { throw 'Fixture did not become ready in time' }
        Start-Sleep -Milliseconds 25
    }
    # Open the readiness JSON once the writer has closed it. Parse errors fail the one test, no rerun.
    $f = Get-Content -Raw $ready | ConvertFrom-Json
    if ($f.pid -ne $fixture.Id) { throw 'Fixture launch PID mismatch' }
    $observer = Start-ExactProcess $observerPath @($fixturePath, $result)
    try {
        # Hard external limit applies ONLY to this Process object created above. Never taskkill/name/PID fallback.
        if (-not $observer.WaitForExit(12000)) { $observer.Kill($false); $observer.WaitForExit(2000) | Out-Null; throw 'Collector timeout, fixture result INCOMPLETE' }
        if ($observer.ExitCode -ne 0) { throw "Collector incomplete/failed: $($observer.ExitCode)" }
    } finally { $observer.Dispose() }
    $records = @(Get-Content -Raw $result | ConvertFrom-Json)
    $p = @($records | Where-Object kind -eq 'process')
    $w = @($records | Where-Object kind -eq 'window-endpoint')
    foreach ($sample in 0,1) {
        if (@($p | Where-Object { $_.data.sample -eq $sample -and $_.data.pid -eq $fixture.Id -and $_.data.expectedPath }).Count -ne 1) { throw 'Missing root process endpoint' }
        foreach ($hwnd in $f.hwnds) {
            if (@($w | Where-Object { $_.data.sample -eq $sample -and $_.data.hwnd -eq $hwnd -and $_.data.pid -eq $fixture.Id -and $_.data.continuity -eq 'UNPROVEN' }).Count -ne 1) { throw 'Missing raw window endpoint' }
        }
    }
    # Synthetic clean hosted desktop only. No personal desktop/profile/browser exists in this job.
    # Capture after observation, outside the measured endpoint samples. Failure fails the fixture.
    Add-Type -AssemblyName System.Drawing.Common
    Add-Type -AssemblyName System.Windows.Forms
    $desktop = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = [System.Drawing.Bitmap]::new($desktop.Width, $desktop.Height)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try { $graphics.CopyFromScreen($desktop.Left, $desktop.Top, 0, 0, $bitmap.Size) }
        finally { $graphics.Dispose() }
        $bitmap.Save((Join-Path $out 'synthetic-fixture.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
    # Child may still be alive after sample 2. Its exit state is not required or treated as proof of churn.
    if (@($records | Where-Object { $_.kind -eq 'scope' -and $_.data.authorization -eq 'NONE' -and $_.data.uiaDependencies -eq 'NOT_COLLECTED' }).Count -ne 1) { throw 'Missing refusal labels' }
    # Output overwrite must refuse, and preserve exact bytes. No browser operation involved.
    $before = (Get-FileHash -Algorithm SHA256 $result).Hash
    $refusal = Start-ExactProcess $observerPath @($fixturePath, $result)
    try {
        if (-not $refusal.WaitForExit(3000)) { $refusal.Kill($false); throw 'Overwrite-refusal timeout' }
        if ($refusal.ExitCode -ne 2 -or (Get-FileHash -Algorithm SHA256 $result).Hash -ne $before) { throw 'Overwrite refusal failed' }
    } finally { $refusal.Dispose() }
    @{ fixtureOnly = $true; compileAndFixture = 'PASS'; browserCapture = 'NOT_TESTED'; identityAuthorization = 'NONE'; continuity = 'UNPROVEN' } | ConvertTo-Json | Set-Content (Join-Path $out 'result.json')
} finally {
    # Fixture exits itself after 15 seconds. We do not terminate it or a browser by PID/name.
    if (-not $fixture.WaitForExit(20000)) { throw 'Fixture failed self-exit; no kill fallback' }
    $fixture.Dispose()
}
