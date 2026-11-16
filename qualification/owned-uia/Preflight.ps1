param([switch]$ActivateOwnedFixture)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=$PSScriptRoot
if($env:RUNNER_OS -cne 'Windows' -or $env:RUNNER_ARCH -cne 'X64' -or -not $env:ImageOS -or -not $env:ImageVersion){throw 'RUNNER_IDENTITY_STOP'}
$manifest=Join-Path $root 'FULL-MANIFEST.json'
if((Get-Item $manifest).Length -gt 64KB){throw 'MANIFEST_SIZE_STOP'}
if((Get-FileHash $manifest -Algorithm SHA256).Hash -cne '3FD55993C371BC17FE3F1365042313D0851AD4DF76C7321B27B66FEF80787DD3'){throw 'MANIFEST_HASH_STOP'}
$entries=Get-Content $manifest -Raw|ConvertFrom-Json
if(@($entries.PSObject.Properties).Count -ne 15){throw 'MANIFEST_COUNT_STOP'}
foreach($entry in $entries.PSObject.Properties){
 if($entry.Name -cnotmatch '^(?:[A-Za-z0-9_.-]+|(?:adapter-source|source)/[A-Za-z0-9_.-]+\.cs)$'){throw 'MANIFEST_PATH_STOP'}
 $file=Join-Path $root $entry.Name
 if((Get-Item $file).Length -gt 256KB -or (Get-FileHash $file -Algorithm SHA256).Hash -cne $entry.Value){throw 'PAYLOAD_HASH_STOP'}
}
foreach($name in @('Build-And-Optional-Observe.ps1','ARTIFACT-POLICY.ps1')){
 $tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile((Join-Path $root $name),[ref]$tokens,[ref]$errors)
 if($errors.Count){throw 'SCRIPT_PARSE_STOP'}
}
& (Join-Path $root 'Build-And-Optional-Observe.ps1') -ActivateOwnedFixture:$ActivateOwnedFixture
