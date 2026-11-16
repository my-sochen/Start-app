# Proposal upload gate, never uploads by itself. Caller must use immutable reviewed upload action.
param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$allowed=@{'observation.json'=64KB;'supervisor.json'=8KB;'compile.txt'=1MB;'inventory.json'=2MB;'build-completion.json'=16KB;'completion.json'=8KB;'launcher-lifecycle.json'=8KB}
$files=@()
foreach($name in $allowed.Keys){$path=Join-Path $OutputDirectory $name;if(Test-Path $path -PathType Leaf){if((Get-Item $path).Length -gt $allowed[$name]){throw 'ARTIFACT_CAP_STOP'};$files+=$path}}
if($files.Count -eq 0){throw 'NO_EVIDENCE'}
$files # caller must pass only these returned paths, never wildcard upload.
