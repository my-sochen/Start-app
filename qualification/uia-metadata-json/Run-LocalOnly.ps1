# PROPOSED reviewed runner entry point only. NOT authorized to run by its existence.
# It never references or launches any historical helper/browser/UIA assembly as code.
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
$names = @('System.Private.CoreLib.dll','System.Runtime.dll','System.Reflection.Metadata.dll','System.Text.Json.dll')
$paths = @($names | ForEach-Object { Join-Path $coreDir $_ }) + @((Join-Path $desktopDir 'UIAutomationClient.dll'),(Join-Path $desktopDir 'UIAutomationTypes.dll'))
$assemblies = @($paths | ForEach-Object {
    if (-not (Test-Path $_ -PathType Leaf) -or (Get-Item $_).Length -gt 256MB) { throw 'ASSEMBLY_FILE_STOP' }
    @{ path = $_; directory = (Split-Path $_); assemblyName = [IO.Path]::GetFileNameWithoutExtension($_); sha256 = (Get-FileHash -Algorithm SHA256 $_).Hash; bytes = (Get-Item $_).Length }
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
$compileFiles = @(@($dotnet,$hostfxr) + @($compilerPayload.FullName) + @($satellites.FullName) + @($corePayload.FullName) + @($refs.FullName) | Sort-Object -Unique)
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
# Freeze measured version/path/hash manifest before compiler or qualification starts.
Check-Time
$manifest = Join-Path $out 'measured-assemblies.json'
@{assemblies=$assemblies} | ConvertTo-Json -Depth 5 | Set-Content $manifest -Encoding utf8NoBOM
$manifestHash = (Get-FileHash $manifest -Algorithm SHA256).Hash
$app = Join-Path $out 'Qualify.dll'
$rsp = Join-Path $out 'compile.rsp'
$lines = @('-nologo','-target:exe','-langversion:latest','-nullable:enable','-warnaserror+','-nostdlib+','-deterministic+')
$lines += '-out:' + '"' + $app + '"'
$lines += @($refs | ForEach-Object { '-r:' + '"' + $_.FullName + '"' })
$lines += '"' + (Join-Path $root 'Qualify.cs') + '"'
$lines | Set-Content $rsp -Encoding utf8NoBOM
# Direct compiler, NOT dotnet build/restore/MSBuild/SDK targets. No package resolution.
Confirm-Inputs
Check-Time
& $dotnet exec --fx-version $core $csc -noconfig ('@' + $rsp) 2>&1 | Set-Content (Join-Path $out 'compile.txt') -Encoding utf8NoBOM
if ($LASTEXITCODE -ne 0) { throw 'COMPILE_STOP' }
Confirm-Inputs
$appHash = (Get-FileHash $app -Algorithm SHA256).Hash
@{runtimeOptions=@{tfm='net10.0';framework=@{name='Microsoft.NETCore.App';version=$core};rollForward='Disable'}} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $out 'Qualify.runtimeconfig.json') -Encoding utf8NoBOM
Check-Time
# Recheck frozen sources/host/reference files and generated output immediately before execution.
Confirm-Inputs
if ((Get-FileHash $manifest -Algorithm SHA256).Hash -cne $manifestHash) { throw 'MANIFEST_DRIFT_STOP' }
if ((Get-FileHash $app -Algorithm SHA256).Hash -cne $appHash) { throw 'OUTPUT_DRIFT_STOP' }
# Target UIA DLLs only opened as bytes by PEReader. No WindowsDesktop runtime reference.
& $dotnet $app $manifest (Join-Path $root 'cases.json') (Join-Path $root 'golden-net10.0.15.jsonl') $manifestHash 2>&1 | Set-Content (Join-Path $out 'result.json') -Encoding utf8NoBOM
$code = $LASTEXITCODE
Confirm-Inputs
if ((Get-FileHash $app -Algorithm SHA256).Hash -cne $appHash) { throw 'OUTPUT_POST_DRIFT_STOP' }
if ((Get-FileHash $manifest -Algorithm SHA256).Hash -cne $manifestHash) { throw 'MANIFEST_POST_DRIFT_STOP' }
Check-Time
@{generatedAppSha256=$appHash;measuredManifestSha256=$manifestHash;exitCode=$code;elapsedSeconds=$watch.Elapsed.TotalSeconds;meaning='METADATA_AND_SYNTHETIC_BYTES_ONLY'} | ConvertTo-Json | Set-Content (Join-Path $out 'completion.json') -Encoding utf8NoBOM
if ($code -ne 0) { throw 'QUALIFICATION_STOP_NO_RETRY' }

} catch {
    $reason = 'UNEXPECTED_SCRIPT_EXCEPTION'
    if ($_.Exception.Message -cmatch '^[A-Z][A-Z0-9_]{0,95}$') { $reason = $_.Exception.Message }
    @{outcome='STOP';code=$reason;elapsedSeconds=$watch.Elapsed.TotalSeconds;partial=$true} | ConvertTo-Json | Set-Content (Join-Path $out 'completion.json') -Encoding utf8NoBOM
    throw $reason
}
