# LOCAL ONLY. No workflow, push or runtime authorization supplied by this file.
param([ValidateSet('edge')][string]$Browser)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_OS -cne 'Windows') { throw 'Disposable hosted Windows CI only' }
# These markers alone are NOT environment authentication; workflow provisioning must establish it.
if ($env:ImageVersion -cne '20261103.298.1') { throw 'UNSUPPORTED allocated image; no pin loosening/retry/install' }
$exe = Join-Path ${env:ProgramFiles(x86)} 'Microsoft/Edge/Application/msedge.exe'
$expectedVersion = '156.0.4314.58'
$expectedSubject = 'CN=Microsoft Corporation'
$launcher = (Resolve-Path (Join-Path $PSScriptRoot 'bin/Release/net10.0-windows/SyntheticLaunch.exe')).Path
$out = Join-Path $PSScriptRoot ('synthetic-' + $Browser)
if (-not (Test-Path -LiteralPath $out -PathType Container)) { throw 'Reviewed preflight evidence directory missing' }
# Hold exact image against write/delete across signature/hash/version checks and child launch.
$image = [IO.FileStream]::new($exe, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try {
    $signature = Get-AuthenticodeSignature -LiteralPath $exe
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
    $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($image)).ToLowerInvariant()
    @{ image = $exe; version = $version; sha256 = $digest; signer = $(if ($null -ne $signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { $null }); thumbprint = $(if ($null -ne $signature.SignerCertificate) { $signature.SignerCertificate.Thumbprint } else { $null }); signatureStatus = [string]$signature.Status; digestMeaning = 'prelaunch measurement of fixed-path version, not independently known-good digest' } | ConvertTo-Json | Set-Content (Join-Path $out 'prelaunch-inventory.json')
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or -not $signature.SignerCertificate.Subject.StartsWith($expectedSubject + ',', [StringComparison]::Ordinal)) { throw 'Expected valid publisher signature missing; no launch' }
    if ($version -cne $expectedVersion) { throw 'UNSUPPORTED exact preinstalled version, no install/downgrade' }
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $launcher; $start.UseShellExecute = $false
    $start.ArgumentList.Add($Browser); $start.ArgumentList.Add($digest); $start.ArgumentList.Add((Join-Path $out 'launch.json')); $start.ArgumentList.Add('--edge-scoped-shape-observation-only')
    $process = [Diagnostics.Process]::Start($start)
    try {
        # If UIA/native Invoke blocks, stop ONLY the launched helper. Its job closes/kills members.
        if (-not $process.WaitForExit(60000)) { $process.Kill($false); $process.WaitForExit(3000) | Out-Null; throw 'Synthetic operator timeout; no retry, cleanup verification required' }
        if ($process.ExitCode -ne 0) { throw "Synthetic candidate incomplete, exit $($process.ExitCode); no retry" }
    } finally { $process.Dispose() }
} finally { $image.Dispose() }
