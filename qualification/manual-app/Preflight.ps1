# One actual-app manual-only observation. No permission to run is created here.
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_OS -cne 'Windows' -or $env:RUNNER_ARCH -cne 'X64' -or $env:ImageOS -cne 'win25-vs2026' -or $env:ImageVersion -cne '20261103.298.1'){throw 'HOST_STOP_NO_FALLBACK'}
if($env:GITHUB_RUN_ATTEMPT -cne '1'){throw 'NO_RERUN'}
$sdk=[string](& dotnet --version)
if($LASTEXITCODE -ne 0 -or $sdk.Trim() -cne '10.0.403'){throw 'SDK_STOP_NO_INSTALL'}
$store=Join-Path $env:LOCALAPPDATA 'StartApp'
if(Test-Path -LiteralPath $store){throw 'EXISTING_APP_DATA_STOP_NO_DELETE'}
$root=Get-Location
$evidence=Join-Path $root 'manual-app-evidence'
if(Test-Path -LiteralPath $evidence){throw 'OUTPUT_COLLISION'}
# Package restore is from the explicit public NuGet feed. No SDK/browser install or account terms.
& dotnet restore StartApp.slnx --source https://api.nuget.org/v3/index.json 2>&1 | Out-File manual-app-restore.txt
if($LASTEXITCODE -ne 0){throw 'SOLUTION_RESTORE_STOP'}
& dotnet build StartApp.slnx --no-restore --disable-build-servers -c Release 2>&1 | Out-File manual-app-build.txt
if($LASTEXITCODE -ne 0){throw 'FULL_BUILD_STOP'}
& dotnet test StartApp.slnx --no-build --no-restore --disable-build-servers -c Release --logger 'console;verbosity=minimal' 2>&1 | Out-File manual-app-tests.txt
if($LASTEXITCODE -ne 0){throw 'TEST_GATE_STOP'}
& dotnet restore qualification/manual-app/ManualApp.csproj --source https://api.nuget.org/v3/index.json 2>&1 | Out-File manual-harness-build.txt
if($LASTEXITCODE -ne 0){throw 'HARNESS_RESTORE_STOP'}
& dotnet build qualification/manual-app/ManualApp.csproj --no-restore --disable-build-servers -c Release 2>&1 | Out-File -Append manual-harness-build.txt
if($LASTEXITCODE -ne 0){throw 'HARNESS_BUILD_STOP'}
$target=Join-Path $root 'src/StartApp.App/bin/Release/net10.0-windows/StartApp.App.exe'
$helper=Join-Path $root 'qualification/manual-app/bin/Release/net10.0-windows/ManualApp.exe'
if(-not (Test-Path -LiteralPath $target -PathType Leaf) -or -not(Test-Path -LiteralPath $helper -PathType Leaf)){throw 'EXACT_OUTPUT_MISSING'}
# Exact reviewed app source pre-gated by workflow hashes; no package/published binary substitution.
$start=[Diagnostics.ProcessStartInfo]::new()
$start.FileName=$helper;$start.UseShellExecute=$false
foreach($arg in @($target,$evidence)){[void]$start.ArgumentList.Add($arg)}
$p=[Diagnostics.Process]::Start($start)
try {
 if(-not $p.WaitForExit(60000)){
  # Exact helper only; unknown app cleanup remains INCOMPLETE, never pass. Runner timeout is containment only.
  $p.Kill($false);[void]$p.WaitForExit(3000);throw 'HARNESS_TIMEOUT_OWNED_APP_CLEANUP_UNVERIFIED'
 }
 if($p.ExitCode -ne 0){throw 'MANUAL_APP_CASE_INCOMPLETE'}
 $result=Get-Content -LiteralPath (Join-Path $evidence 'manual-result.json') -Raw | ConvertFrom-Json
 if($result.result -cne 'UI_FACTS_PASSED_IMAGES_NEED_PIXEL_REVIEW' -or $result.ownedAppTerminated -ne $true){throw 'MANUAL_RESULT_NOT_PROVEN'}
} finally {$p.Dispose()}
