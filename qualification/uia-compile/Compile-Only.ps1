# PROPOSAL ONLY. No push/run is authorized by this source.
# UIA DLLs are compiler references only, never loaded as executable code by this script.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = $PSScriptRoot
$out = Join-Path $root 'qualification-output'
if (Test-Path $out) { throw 'OUTPUT_ALREADY_EXISTS_STOP' }
New-Item -ItemType Directory -Path $out | Out-Null
$watch = [Diagnostics.Stopwatch]::StartNew()
function Check-Time { if ($watch.Elapsed.TotalSeconds -gt 60) { throw 'BUDGET_STOP' } }
try {
# Fixed approved source hashes. No remote source/config/package dependency.
$approved = Get-Content (Join-Path $root 'inputs-sha256.json') -Raw | ConvertFrom-Json
foreach ($p in $approved.PSObject.Properties) {
    $path = Join-Path $root $p.Name
    if ((Get-FileHash -Algorithm SHA256 $path).Hash -cne $p.Value) { throw 'SOURCE_HASH_STOP' }
}
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess -or [Environment]::OSVersion.Platform -ne 'Win32NT') { throw 'PLATFORM_STOP' }
if (-not (Test-Path $dotnet -PathType Leaf)) { throw 'DOTNET_MISSING_STOP' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
# Explicit limited injection checks, NOT an exhaustive loader/network sandbox.
foreach ($name in @('DOTNET_STARTUP_HOOKS','DOTNET_ADDITIONAL_DEPS','DOTNET_SHARED_STORE','DOTNET_ROLL_FORWARD','CORECLR_ENABLE_PROFILING','CORECLR_PROFILER','CORECLR_PROFILER_PATH','COR_ENABLE_PROFILING','COR_PROFILER','COR_PROFILER_PATH','COMPlus_ReadyToRun','DOTNET_ENABLE_PROFILING')) {
    if ([Environment]::GetEnvironmentVariable($name)) { throw 'UNEXPECTED_RUNTIME_ENV_STOP' }
}
foreach ($name in @('DOTNET_ROOT','DOTNET_ROOT_X64')) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ($value -and $value.TrimEnd('\') -ine 'C:\Program Files\dotnet') { throw 'DOTNET_ROOT_BINDING_STOP' }
    [Environment]::SetEnvironmentVariable($name,'C:\Program Files\dotnet')
}
# Current disposable environment, measured prospectively. Never claims E12 equivalence.
$sdks = @(& $dotnet --list-sdks)
if ($LASTEXITCODE -ne 0) { throw 'SDK_LIST_STOP' }
$runtimes = @(& $dotnet --list-runtimes)
if ($LASTEXITCODE -ne 0) { throw 'RUNTIME_LIST_STOP' }
function Latest-10([string[]]$lines,[string]$prefix) {
    $matches = @($lines | Where-Object { $_ -match $prefix } | ForEach-Object { [version]([regex]::Match($_,'10\.0\.\d+').Value) } | Sort-Object -Descending)
    if ($matches.Count -eq 0) { throw 'PREINSTALLED_10_ASSET_MISSING_STOP' }
    return $matches[0].ToString()
}
$sdk = Latest-10 $sdks '^10\.0\.\d+ '
$core = Latest-10 $runtimes '^Microsoft\.NETCore\.App 10\.0\.\d+ '
$desktop = Latest-10 $runtimes '^Microsoft\.WindowsDesktop\.App 10\.0\.\d+ '
$base = 'C:\Program Files\dotnet'
if ($sdks.Count -gt 128 -or $runtimes.Count -gt 128) { throw 'INVENTORY_CAP_STOP' }
$coreDir = Join-Path $base ('shared\Microsoft.NETCore.App\' + $core)
$desktopDir = Join-Path $base ('shared\Microsoft.WindowsDesktop.App\' + $desktop)
$csc = Join-Path $base ('sdk\' + $sdk + '\Roslyn\bincore\csc.dll')
$packs = @(Get-ChildItem (Join-Path $base 'packs\Microsoft.NETCore.App.Ref') -Directory | Where-Object { $_.Name -match '^10\.0\.\d+$' } | Sort-Object { [version]$_.Name } -Descending)
if ($packs.Count -eq 0) { throw 'REFERENCE_PACK_MISSING_STOP' }
$refDir = Join-Path $packs[0].FullName 'ref\net10.0'
$refs = @(Get-ChildItem $refDir -Filter '*.dll' -File)
if ($refs.Count -eq 0 -or $refs.Count -gt 256 -or -not (Test-Path $csc -PathType Leaf)) { throw 'OFFLINE_COMPILER_ASSET_STOP' }
# UIA/Desktop references are existing installed runtime assemblies, not downloaded packs.
$paths = @('UIAutomationClient.dll','UIAutomationTypes.dll','WindowsBase.dll') | ForEach-Object { Join-Path $desktopDir $_ }
$assemblies = @($paths | ForEach-Object {
 if (-not (Test-Path $_ -PathType Leaf) -or (Get-Item $_).Length -gt 256MB) { throw 'DESKTOP_REFERENCE_MISSING_STOP' }
 @{path=$_;sha256=(Get-FileHash $_ -Algorithm SHA256).Hash;bytes=(Get-Item $_).Length}
})
# Capture full preinstalled compiler payload identities; no dependency fetch allowed.
$compilerPayload = @(Get-ChildItem (Split-Path $csc) -File)
if ($compilerPayload.Count -gt 128) { throw 'COMPILER_INPUT_CAP_STOP' }
$satellites = @(Get-ChildItem (Split-Path $csc) -Directory | ForEach-Object { Get-ChildItem $_.FullName -Filter '*.resources.dll' -File })
$corePayload = @(Get-ChildItem $coreDir -File)
if ($satellites.Count -gt 128 -or $corePayload.Count -gt 512) { throw 'HOST_INPUT_CAP_STOP' }
$hostDirs = @(Get-ChildItem (Join-Path $base 'host\fxr') -Directory | Where-Object { $_.Name -match '^10\.0\.\d+$' } | Sort-Object { [version]$_.Name } -Descending)
if ($hostDirs.Count -eq 0) { throw 'HOST_FXR_MISSING_STOP' }
$hostfxr = Join-Path $hostDirs[0].FullName 'hostfxr.dll'
$compileFiles = @(@($dotnet,$hostfxr) + @($paths) + @($compilerPayload.FullName) + @($satellites.FullName) + @($corePayload.FullName) + @($refs.FullName) | Sort-Object -Unique)
foreach ($f in $compileFiles) { if ((Get-Item $f).Length -gt 256MB) { throw 'DEPENDENCY_FILE_CAP_STOP' } }
$compileIdentities = @($compileFiles | ForEach-Object { @{path=$_;sha256=(Get-FileHash -Algorithm SHA256 $_).Hash} })
function Confirm-Inputs {
    foreach ($entry in $compileIdentities) {
        if ((Get-FileHash -LiteralPath $entry.path -Algorithm SHA256).Hash -cne $entry.sha256) { throw 'COMPILER_HOST_REF_DRIFT_STOP' }
    }
    foreach ($p in $approved.PSObject.Properties) {
        if ((Get-FileHash (Join-Path $root $p.Name) -Algorithm SHA256).Hash -cne $p.Value) { throw 'SOURCE_DRIFT_STOP' }
    }
}
$inventory = @{compilerHostRequestedCore=$core;compilerHostPolicy='dotnet exec --fx-version exact; installed platform dependencies trusted, not hermetically proven';purpose='PROSPECTIVE_NOT_E12_EQUIVALENCE';imageOS=$env:ImageOS;imageVersion=$env:ImageVersion;runnerOS=$env:RUNNER_OS;runnerArch=$env:RUNNER_ARCH;osVersion=[Environment]::OSVersion.VersionString;process64=[Environment]::Is64BitProcess;dotnet=$dotnet;dotnetSha256=(Get-FileHash -Algorithm SHA256 $dotnet).Hash;sdk=$sdk;coreRuntime=$core;desktopRuntime=$desktop;referencePack=$packs[0].Name;assemblies=$assemblies;compilerInputs=$compileIdentities}
$inventory | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $out 'inventory.json') -Encoding utf8NoBOM
# Freeze exact measured inputs before invoking the installed compiler.
Check-Time
$app = Join-Path $out 'ObservationCompileOnly.dll'
$rsp = Join-Path $out 'compile.rsp'
$lines = @('-nologo','-target:library','-langversion:14','-nullable:enable','-warnaserror+','-nostdlib+','-deterministic+')
# No analyzer/source generator/package/config resolution.
$lines += '-out:' + '"' + $app + '"'
# Avoid duplicate WindowsBase identity from Core ref pack; use the measured Desktop DLL only.
$effectiveRefs = @($refs | Where-Object { $_.Name -cne 'WindowsBase.dll' })
$lines += @($effectiveRefs | ForEach-Object { '-r:' + '"' + $_.FullName + '"' })
$lines += @($paths | ForEach-Object { '-r:' + '"' + $_ + '"' })
$sourceNames = @($approved.PSObject.Properties.Name | Where-Object { $_ -cmatch '^[A-Za-z0-9_.-]+\.cs$' } | Sort-Object)
if ($sourceNames.Count -ne 7) { throw 'SOURCE_COUNT_STOP' }
$lines += @($sourceNames | ForEach-Object { '"' + (Join-Path $root $_) + '"' })
$lines | Set-Content $rsp -Encoding utf8NoBOM
$rspHash=(Get-FileHash $rsp -Algorithm SHA256).Hash
Confirm-Inputs
Check-Time
# Compiler ONLY. There is deliberately no runtimeconfig, reflection load or generated DLL invocation.
$stdout=Join-Path $out 'compiler-stdout.tmp'
$stderr=Join-Path $out 'compiler-stderr.tmp'
$arguments='exec --fx-version ' + $core + ' "' + $csc + '" -noconfig "@' + $rsp + '"'
$process=Start-Process -FilePath $dotnet -ArgumentList $arguments -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -NoNewWindow
$compilerWatch=[Diagnostics.Stopwatch]::StartNew()
$stopCode=$null
try {
 while (-not $process.WaitForExit(50)) {
  if ($compilerWatch.Elapsed.TotalSeconds -gt 30) { $stopCode='COMPILER_TIMEOUT_STOP';break }
  if ($watch.Elapsed.TotalSeconds -gt 60) { $stopCode='BUDGET_STOP';break }
  foreach($file in @($stdout,$stderr)) { if ((Test-Path $file) -and (Get-Item $file).Length -gt 512KB) { $stopCode='DIAGNOSTICS_CAP_STOP';break } }
  if($stopCode){break}
 }
 if($stopCode){if(-not $process.HasExited){$process.Kill($true);[void]$process.WaitForExit(5000)};throw $stopCode}
 $code=$process.ExitCode
 foreach($file in @($stdout,$stderr)) {if ((Get-Item $file).Length -gt 512KB){throw 'DIAGNOSTICS_CAP_STOP'}}
 @('STDOUT',([IO.File]::ReadAllText($stdout)),'STDERR',([IO.File]::ReadAllText($stderr))) | Set-Content (Join-Path $out 'compile.txt') -Encoding utf8NoBOM
} finally {
 if(-not $process.HasExited){$process.Kill($true);[void]$process.WaitForExit(5000)}
 $process.Dispose()
}
Confirm-Inputs
if ((Get-FileHash $rsp -Algorithm SHA256).Hash -cne $rspHash) { throw 'RESPONSE_DRIFT_STOP' }
Check-Time
if ($code -ne 0) { throw 'COMPILE_STOP_NO_RETRY' }
if (-not (Test-Path $app -PathType Leaf) -or (Get-Item $app).Length -gt 8MB) { throw 'OUTPUT_SIZE_STOP' }
$outputHash=(Get-FileHash $app -Algorithm SHA256).Hash
@{outcome='COMPILE_DECLARATION_NULLABILITY_CLOSURE_ONLY';generatedDllSha256=$outputHash;generatedDllBytes=(Get-Item $app).Length;responseSha256=$rspHash;exitCode=$code;elapsedSeconds=$watch.Elapsed.TotalSeconds;activated=$false} | ConvertTo-Json | Set-Content (Join-Path $out 'completion.json') -Encoding utf8NoBOM
} catch {
 $reason='UNEXPECTED_SCRIPT_EXCEPTION'
 if($_.Exception.Message -cmatch '^[A-Z][A-Z0-9_]{0,95}$'){$reason=$_.Exception.Message}
 @{outcome='STOP';code=$reason;elapsedSeconds=$watch.Elapsed.TotalSeconds;partial=$true;activated=$false} | ConvertTo-Json | Set-Content (Join-Path $out 'completion.json') -Encoding utf8NoBOM
 throw $reason
}
