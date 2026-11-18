param([string]$Output, [switch]$ContractTest)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows') { throw 'Disposable hosted Windows runner required' }
function Require-CleanResult([bool]$Enumerated, [int]$ClassReadFailures, [int]$VisibleChromiumCount) {
  if (-not $Enumerated -or $ClassReadFailures -ne 0 -or $VisibleChromiumCount -ne 0) { throw 'Clean Chromium precondition not established' }
}
if ($ContractTest) {
  Require-CleanResult $true 0 0
  foreach ($case in @(@($false,0,0),@($true,1,0),@($true,0,1),@($false,1,1))) {
    $rejected=$false
    try { Require-CleanResult $case[0] $case[1] $case[2] } catch { $rejected=$true }
    if (-not $rejected) { throw 'Guard negative contract allowed unsafe result' }
  }
  'Clean guard contracts: one accepted and four unsafe combinations rejected'
  exit 0
}
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class CleanChromium {
 public delegate bool Callback(IntPtr w, IntPtr p);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Callback cb, IntPtr p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr w);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr w, StringBuilder b, int n);
}
'@
$visible = [Collections.Generic.List[string]]::new()
$errors = [Collections.Generic.List[string]]::new()
$enumerated = [CleanChromium]::EnumWindows({param($w,$unused)
  if ([CleanChromium]::IsWindowVisible($w)) {
    $b=[Text.StringBuilder]::new(256)
    $length=[CleanChromium]::GetClassName($w,$b,256)
    if ($length -le 0 -or $length -ge 255) { $errors.Add('Visible window class unreadable or truncated'); return $false }
    if ($b.ToString().StartsWith('Chrome_WidgetWin_', [StringComparison]::Ordinal)) { $visible.Add($w.ToString()) }
  }
  return $true
},[IntPtr]::Zero)
@{utc=[DateTime]::UtcNow; enumerationSucceeded=$enumerated; classReadErrors=@($errors.ToArray()); visibleChromiumHandles=@($visible.ToArray()); clean=($enumerated -and $errors.Count -eq 0 -and $visible.Count -eq 0)} | ConvertTo-Json -Depth 5 | Set-Content $Output
Require-CleanResult $enumerated $errors.Count $visible.Count
