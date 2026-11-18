$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../.github/e2e/capture-readiness.ps1')
$checks = 0
function Check([string]$name,[bool]$ok) { if (-not $ok) { throw $name }; $script:checks++; Write-Host "PASS $name" }
function Store([string]$ws='Cap B',[string]$target='D:\fixture\folder',[string]$name='my capture folder',[string]$desc='added from capture',[string]$opener='C:\Windows\notepad.exe') {
  @{ items=@{i=@{id='i';target=$target}};workspaces=@(@{name=$ws;items=@(@{itemId='i';name=$name;description=$desc;launch=@{openWith=$opener}})}) } | ConvertTo-Json -Depth 8 -Compress
}
function AcceptStore([string]$json) { Test-CaptureSavedInstance $json 'Cap B' 'D:\fixture\folder' 'my capture folder' 'added from capture' 'C:\Windows\notepad.exe' }
Check 'valid exact instance accepted' (AcceptStore (Store))
Check 'wrong workspace rejected' (-not (AcceptStore (Store -ws 'Cap A')))
Check 'wrong target rejected' (-not (AcceptStore (Store -target 'D:\other')))
Check 'wrong instance name rejected' (-not (AcceptStore (Store -name 'old')))
Check 'wrong description rejected' (-not (AcceptStore (Store -desc 'old')))
Check 'wrong opener rejected' (-not (AcceptStore (Store -opener 'C:\foreign.exe')))
Check 'malformed snapshot rejected' (-not (AcceptStore '{bad'))
Check 'empty snapshot rejected' (-not (AcceptStore ''))
$o = Store | ConvertFrom-Json
$o.workspaces += $o.workspaces[0]
Check 'ambiguous workspace rejected' (-not (AcceptStore ($o | ConvertTo-Json -Depth 8)))
$o = Store | ConvertFrom-Json
$o.workspaces[0].items += $o.workspaces[0].items[0]
Check 'duplicate membership rejected' (-not (AcceptStore ($o | ConvertTo-Json -Depth 8)))
$o = Store -ws 'Cap A' | ConvertFrom-Json
$o.workspaces += @{name='Cap B';items=@()}
Check 'correct strings elsewhere do not satisfy save' (-not (AcceptStore ($o | ConvertTo-Json -Depth 8)))
$script:reads=0
$r=Wait-CaptureTextObservation -Read { $script:reads++; if($script:reads -eq 1){ Store -name 'stale' }else{ Store } } -Accept {param($v) AcceptStore $v} -TimeoutMilliseconds 200 -PollMilliseconds 1
Check 'stale first store waits for exact accepted snapshot' ($r.Matched -and $r.Attempts -eq 2 -and (AcceptStore $r.Value) -and $script:reads -eq 2)
$script:reads=0
$r=Wait-CaptureTextObservation -Read { $script:reads++; if($script:reads -eq 1){''}else{'example.com'} } -Accept {param($v) $v -ceq 'example.com'} -TimeoutMilliseconds 200 -PollMilliseconds 1
Check 'transient empty UI value waits for expected value' ($r.Matched -and $r.Value -ceq 'example.com' -and $r.Attempts -eq 2 -and $script:reads -eq 2)
$script:reads=0
$r=Wait-CaptureTextObservation -Read { $script:reads++; 'foreign.example.com' } -Accept {param($v) $v -ceq 'example.com'} -TimeoutMilliseconds 15 -PollMilliseconds 1
Check 'stable wrong UI value still fails' (-not $r.Matched -and $r.Value -ceq 'foreign.example.com' -and $script:reads -ge 1)
Check 'no diagnostic re-read after timeout' ($r.Attempts -eq $script:reads)
$script:reads=0
$r=Wait-CaptureTextObservation -Read { $script:reads++; if($script:reads -eq 1){'example.com'}else{'wrong'} } -Accept {param($v) $v -ceq 'example.com'} -TimeoutMilliseconds 100 -PollMilliseconds 1
Check 'assertion and detail retain one accepted sample' ($r.Matched -and $r.Value -ceq 'example.com' -and $script:reads -eq 1)
$r=Wait-CaptureTextObservation -Read { throw 'synthetic unavailable reader' } -Accept {param($v) $true} -TimeoutMilliseconds 10 -PollMilliseconds 1
Check 'read errors never accepted' (-not $r.Matched -and $r.ReadErrorType.Length -gt 0)
$script:reads=0
$r=Wait-CaptureTextObservation -Read { $script:reads++;if($script:reads -eq 1){throw 'transient'}else{'example.com'} } -Accept {param($v) $v -ceq 'example.com'} -TimeoutMilliseconds 200 -PollMilliseconds 1
Check 'transient read error can recover within budget' ($r.Matched -and $r.ReadErrorType -ceq '' -and $r.Attempts -eq 2)
$r=Wait-CaptureTextObservation -Read { Store -name 'stale' } -Accept {param($v) AcceptStore $v} -TimeoutMilliseconds 10 -PollMilliseconds 1
Check 'never-persisted state still fails' (-not $r.Matched -and -not (AcceptStore $r.Value))
Write-Host "$checks readiness checks passed"
$r=Wait-CaptureTextObservation -Read { Start-Sleep -Milliseconds 30; 'example.com' } -Accept {param($v) $v -ceq 'example.com'} -TimeoutMilliseconds 5 -PollMilliseconds 1
Check 'matching read returned after deadline is not accepted' (-not $r.Matched -and $r.Attempts -eq 1)
$r=Wait-CaptureTextObservation -Read {'example.com'} -Accept {param($v) Start-Sleep -Milliseconds 30;$true} -TimeoutMilliseconds 5 -PollMilliseconds 1
Check 'predicate returned after deadline is not accepted' (-not $r.Matched)
. (Join-Path $PSScriptRoot '../../.github/e2e/chrome-fixture-observation.ps1')
Check 'owned HWND and PID pair accepted' (Test-CaptureFixtureWindowIdentity 100 22 @(22,23) 100 22)
Check 'foreign PID never accepted' (-not (Test-CaptureFixtureWindowIdentity 100 22 @(23) 100 22))
Check 'recycled HWND different PID rejected' (-not (Test-CaptureFixtureWindowIdentity 100 22 @(22,23) 100 23))
Check 'different HWND rejected' (-not (Test-CaptureFixtureWindowIdentity 100 22 @(22) 101 22))
Check 'zero HWND rejected' (-not (Test-CaptureFixtureWindowIdentity 0 22 @(22) 0 22))
Check 'missing PID set rejected' (-not (Test-CaptureFixtureWindowIdentity 100 22 @() 100 22))
Check 'empty PID set does not enumerate native windows' ((Get-CaptureFixtureNativeSnapshot -OwnedPids @()).Windows.Count -eq 0)
Write-Host "$checks TOTAL readiness checks passed"
