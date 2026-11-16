param([switch]$ActivateOwnedFixture)
# PROPOSAL ONLY. No push/run is authorized by this source.
# Default is compile only. Explicit switch proposes synthetic-owned UIA activation after separate authorization.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = $PSScriptRoot
$out = Join-Path $root 'experiment-output'
if (Test-Path $out) { throw 'OUTPUT_ALREADY_EXISTS_STOP' }
New-Item -ItemType Directory -Path $out | Out-Null
$watch = [Diagnostics.Stopwatch]::StartNew()
$launcherLifecycle=[System.Collections.Generic.List[object]]::new()
$phaseClocks=[System.Collections.Generic.List[object]]::new()
$primaryFailure='NONE'
function Phase-Clock([string]$name){if($phaseClocks.Count -ge 32){throw 'PHASE_CLOCK_CAP_STOP'};[void]$phaseClocks.Add(@{phase=$name;scriptElapsedSeconds=$watch.Elapsed.TotalSeconds})}
Phase-Clock 'SCRIPT_BEGIN'
function Close-OwnedProcessTree($process,[string]$role){
 $attempted=$false;$confirmed=$false
 try{if(-not $process.HasExited){$attempted=$true;$process.Kill($true);$confirmed=$process.WaitForExit(5000)}else{$confirmed=$true}}
 catch{$confirmed=$false}
 finally{[void]$launcherLifecycle.Add(@{role=$role;treeTerminationAttempted=$attempted;rootExitConfirmed=$confirmed});$process.Dispose()}
}

function Check-Time { if ($watch.Elapsed.TotalSeconds -gt 150) { throw 'BUDGET_STOP' } }
try {
# Fixed approved source hashes (manifest itself must be pinned by caller preflight). No remote source/config/package dependency.
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
$paths = @('UIAutomationClient.dll','UIAutomationTypes.dll','WindowsBase.dll','PresentationFramework.dll','PresentationCore.dll','System.Xaml.dll') | ForEach-Object { Join-Path $desktopDir $_ }
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
$desktopPayload=@(Get-ChildItem $desktopDir -File -Recurse)
if($desktopPayload.Count -gt 2048){throw 'DESKTOP_PAYLOAD_CAP_STOP'}
$compileFiles = @(@($desktopPayload.FullName) + @($dotnet,$hostfxr) + @($paths) + @($compilerPayload.FullName) + @($satellites.FullName) + @($corePayload.FullName) + @($refs.FullName) | Sort-Object -Unique)
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
$inventory = @{compilerHostRequestedCore=$core;compilerHostPolicy='dotnet exec --fx-version exact; installed platform dependencies trusted, not hermetically proven';purpose='SYNTHETIC_OWNED_FIXTURE_PROPOSAL_NOT_BROWSER_PROOF';imageOS=$env:ImageOS;imageVersion=$env:ImageVersion;runnerOS=$env:RUNNER_OS;runnerArch=$env:RUNNER_ARCH;osVersion=[Environment]::OSVersion.VersionString;process64=[Environment]::Is64BitProcess;dotnet=$dotnet;dotnetSha256=(Get-FileHash -Algorithm SHA256 $dotnet).Hash;sdk=$sdk;coreRuntime=$core;desktopRuntime=$desktop;referencePack=$packs[0].Name;assemblies=$assemblies;compilerInputs=$compileIdentities}
Phase-Clock 'INVENTORY_COMPLETE'
$inventory | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $out 'inventory.json') -Encoding utf8NoBOM
# Compile all six assemblies separately. No merged-source shortcut for this gate.
$global=Join-Path $root 'GlobalUsings.cs'
$effectiveRefs=@($refs | Where-Object { $_.Name -cne 'WindowsBase.dll' } | ForEach-Object {$_.FullName})
$desktopRefs=@($paths)
$builds=@(
 @{name='Observation.Contracts';target='library';files=@('adapter-source/Contracts.cs','adapter-source/Acquisition.cs','adapter-source/CanonicalShape.cs');extra=@()},
 @{name='Observation.Windows';target='library';files=@('adapter-source/Transport.cs','adapter-source/WindowsObservationSource.cs');extra=@('Observation.Contracts')},
 @{name='Observation.Uia';target='library';files=@('adapter-source/UiaTransport.cs');extra=@('Observation.Contracts','Observation.Windows')},
 @{name='Fixture';target='exe';files=@('source/Fixture.cs');extra=@()},
 @{name='Observer';target='exe';files=@('source/Observer.cs','source/DiagnosticJournal.cs');extra=@('Observation.Contracts','Observation.Windows','Observation.Uia')},
 @{name='Supervisor';target='exe';files=@('source/Supervisor.cs','source/ExactOutputBudget.cs','source/DiagnosticJournal.cs');extra=@()}
)
$compiled=@()
foreach($build in $builds){
 Confirm-Inputs;Check-Time
 $app=Join-Path $out ($build.name+'.dll');$rsp=Join-Path $out ($build.name+'.rsp')
 $lines=@('-nologo',('-target:'+$build.target),'-langversion:14','-nullable:enable','-warnaserror+','-nostdlib+','-deterministic+',('-out:"'+$app+'"'))
 $references=@($effectiveRefs)
 if($build.name -in @('Observation.Uia','Fixture','Observer')){$references+=@($desktopRefs)}
 foreach($dependency in $build.extra){$references+=Join-Path $out ($dependency+'.dll')}
 $lines+=@($references|ForEach-Object {'-r:"'+$_+'"'})
 $lines+='"'+$global+'"';$lines+=@($build.files|ForEach-Object {'"'+(Join-Path $root $_)+'"'})
 $lines|Set-Content $rsp -Encoding utf8NoBOM
 $stdout=Join-Path $out ($build.name+'-stdout.tmp');$stderr=Join-Path $out ($build.name+'-stderr.tmp')
 $arguments='exec --fx-version '+$core+' "'+$csc+'" -noconfig "@'+$rsp+'"'
 Phase-Clock ('compiler-'+$build.name+'-before-start')
 $process=Start-Process $dotnet -ArgumentList $arguments -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -NoNewWindow
 try{
  Phase-Clock ('compiler-'+$build.name+'-wait-begin')
  $cw=[Diagnostics.Stopwatch]::StartNew()
  while(-not $process.WaitForExit(50)){
   if($cw.Elapsed.TotalSeconds -gt 20){throw 'COMPILER_TIMEOUT_STOP'}
   Check-Time
   foreach($f in @($stdout,$stderr)){if((Test-Path $f) -and (Get-Item $f).Length -gt 256KB){throw 'COMPILER_OUTPUT_STOP'}}
  }
  Phase-Clock ('compiler-'+$build.name+'-wait-end')
  $code=$process.ExitCode
  foreach($f in @($stdout,$stderr)){if((Get-Item $f).Length -gt 256KB){throw 'COMPILER_OUTPUT_STOP'}}
  @($build.name,'STDOUT',([IO.File]::ReadAllText($stdout)),'STDERR',([IO.File]::ReadAllText($stderr)))|Add-Content (Join-Path $out 'compile.txt') -Encoding utf8NoBOM
  if((Get-Item (Join-Path $out 'compile.txt')).Length -gt 1MB){throw 'COMBINED_DIAGNOSTICS_STOP'}
  if($code -ne 0){throw 'SEPARATE_PROJECT_COMPILE_STOP'}
  if((Get-Item $app).Length -gt 8MB){throw 'GENERATED_FILE_CAP_STOP'}
  $compiled+=@{name=$build.name;sha256=(Get-FileHash $app -Algorithm SHA256).Hash;bytes=(Get-Item $app).Length;responseSha256=(Get-FileHash $rsp -Algorithm SHA256).Hash}
 }finally{Close-OwnedProcessTree $process ('compiler-'+$build.name)}
}
Confirm-Inputs;Check-Time
@{outcome='SEPARATE_ASSEMBLIES_COMPILED_ONLY';assemblies=$compiled;activated=$false}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $out 'build-completion.json') -Encoding utf8NoBOM
Phase-Clock 'COMPILE_GATE_COMPLETE'
if(-not $ActivateOwnedFixture){return}
if($core -cne $desktop){throw 'CORE_DESKTOP_VERSION_MISMATCH_STOP'}
# ACTIVATION IS SEPARATE AUTHORITY. No default invocation activates provider/UIA.
foreach($name in @('Fixture','Observer','Supervisor')){
 $framework=if($name -eq 'Supervisor'){@{name='Microsoft.NETCore.App';version=$core}}else{@{name='Microsoft.WindowsDesktop.App';version=$desktop}}
 @{runtimeOptions=@{tfm='net10.0';framework=$framework;rollForward='Disable'}}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $out ($name+'.runtimeconfig.json')) -Encoding utf8NoBOM
}
foreach($entry in $compiled){if((Get-FileHash (Join-Path $out ($entry.name+'.dll')) -Algorithm SHA256).Hash -cne $entry.sha256){throw 'GENERATED_DRIFT_STOP'}}
Confirm-Inputs;Check-Time
$supervisor=$null
try{
 Phase-Clock 'SUPERVISOR_BEFORE_START'
 $supervisor=Start-Process $dotnet -ArgumentList ('"'+(Join-Path $out 'Supervisor.dll')+'" "'+$dotnet+'" "'+$out+'"') -RedirectStandardOutput (Join-Path $out 'supervisor-stdout.tmp') -RedirectStandardError (Join-Path $out 'supervisor-stderr.tmp') -PassThru -NoNewWindow
 Phase-Clock 'SUPERVISOR_WAIT_BEGIN'
 $sw=[Diagnostics.Stopwatch]::StartNew()
 while(-not $supervisor.WaitForExit(50)){
  if($sw.Elapsed.TotalSeconds -gt 40){throw 'SUPERVISOR_TIMEOUT_STOP'}
  foreach($name in @('supervisor-stdout.tmp','supervisor-stderr.tmp')){if((Get-Item (Join-Path $out $name)).Length -gt 64KB){throw 'SUPERVISOR_OUTPUT_CAP_STOP'}}
 }
 Phase-Clock 'SUPERVISOR_WAIT_END'
 foreach($name in @('supervisor-stdout.tmp','supervisor-stderr.tmp')){if((Get-Item (Join-Path $out $name)).Length -gt 64KB){throw 'SUPERVISOR_OUTPUT_CAP_STOP'}}
 if($supervisor.ExitCode -ne 0){throw 'SYNTHETIC_OBSERVATION_STOP'}
 $summary=Get-Content (Join-Path $out 'supervisor.json') -Raw|ConvertFrom-Json
 if(-not $summary.pass -or -not $summary.cleanupConfirmed -or -not $summary.allChildPipesAtEof -or $summary.outputOverflow -or $summary.childRawPipeBytesObserved -gt 65536 -or $summary.forcedOwnedChildTerminations -ne 0){throw 'CLEAN_EXIT_REFUSED_STOP'}
 Confirm-Inputs
 foreach($entry in $compiled){if((Get-FileHash (Join-Path $out ($entry.name+'.dll')) -Algorithm SHA256).Hash -cne $entry.sha256){throw 'GENERATED_POST_DRIFT_STOP'}}
 @{outcome='OWNED_TOPOLOGY_EXPERIMENT_PASS_CANONICAL_SEPARATE';activated=$true;elapsedSeconds=$watch.Elapsed.TotalSeconds}|ConvertTo-Json|Set-Content (Join-Path $out 'completion.json') -Encoding utf8NoBOM
}finally{if($supervisor){Close-OwnedProcessTree $supervisor 'supervisor-owned-experiment-tree'}}
} catch {
 $reason='UNEXPECTED_SCRIPT_EXCEPTION'
 if($_.Exception.Message -cmatch '^[A-Z][A-Z0-9_]{0,95}$'){$reason=$_.Exception.Message}
 $primaryFailure=$reason
 @{outcome='STOP';code=$reason;elapsedSeconds=$watch.Elapsed.TotalSeconds;partial=$true;activationRequested=[bool]$ActivateOwnedFixture}|ConvertTo-Json|Set-Content (Join-Path $out 'completion.json') -Encoding utf8NoBOM
 throw $reason
} finally {
 if($phaseClocks.Count -lt 32){[void]$phaseClocks.Add(@{phase='FINAL_HANDLING';scriptElapsedSeconds=$watch.Elapsed.TotalSeconds})}
 $forced=@($launcherLifecycle | Where-Object {$_.treeTerminationAttempted}).Count
 $unconfirmed=@($launcherLifecycle | Where-Object {-not $_.rootExitConfirmed}).Count
 @{phaseClocks=@($phaseClocks.ToArray());primaryFailure=$primaryFailure;scriptElapsedSeconds=$watch.Elapsed.TotalSeconds;entries=@($launcherLifecycle.ToArray());treeTerminationAttempts=$forced;unconfirmedRootExits=$unconfirmed;meaning='retained compiler roots and supervisor-root descendants only; root exit is not descendant-quiescence proof'}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $out 'launcher-lifecycle.json') -Encoding utf8NoBOM
 if($forced -ne 0 -or $unconfirmed -ne 0){@{outcome='STOP';code='LAUNCHER_CLEANUP_REFUSAL';primaryFailure=$primaryFailure;activationRequested=[bool]$ActivateOwnedFixture}|ConvertTo-Json|Set-Content (Join-Path $out 'completion.json') -Encoding utf8NoBOM;throw 'LAUNCHER_CLEANUP_REFUSAL'}
}
