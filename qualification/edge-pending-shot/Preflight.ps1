# Screenshot-only source candidate. No push/run approval is created by this script.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$out = Join-Path $PSScriptRoot 'evidence'
New-Item -ItemType Directory -Path $out -ErrorAction Stop | Out-Null
$result = 'INCONCLUSIVE'
$stage = 'HOST_PREFLIGHT'
$held = @()
try {
    if ($env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_OS -cne 'Windows' -or $env:RUNNER_ARCH -cne 'X64' -or $env:ImageOS -cne 'win25-vs2026' -or $env:ImageVersion -cne '20261103.298.1') { throw 'HOST_STOP' }
    $dotnet = (Get-Command dotnet.exe -CommandType Application -ErrorAction Stop).Path
    $sdk = [string](& $dotnet --version)
    if ($LASTEXITCODE -ne 0 -or $sdk.Trim() -cne '10.0.403') { throw 'SDK_STOP_NO_INSTALL' }
    $specs = @(
        @{ browser='edge'; path=(Join-Path ${env:ProgramFiles(x86)} 'Microsoft/Edge/Application/msedge.exe'); version='156.0.4314.58'; publisher='CN=Microsoft Corporation,'; historicalDigest='d35d48b11f0439eeeddbbff0bf54856e922f5e1506a2f3098b5a808fcec4068b' }
    )
    $stage = 'EDGE_BINARY_PREFLIGHT'
    $inventory = @()
    # Gate the one Edge executable and hold against writes/deletion. Not whole-distribution attestation.
    foreach ($s in $specs) {
        $lock = [IO.FileStream]::new($s.path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $held += $lock
        $sig = Get-AuthenticodeSignature -LiteralPath $s.path
        $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($s.path).ProductVersion
        $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($lock)).ToLowerInvariant()
        $subject = if ($null -ne $sig.SignerCertificate) { $sig.SignerCertificate.Subject } else { '' }
        $inventory += @{browser=$s.browser;image=$s.path;version=$version;measuredDigest=$digest;historicalMeasuredDigest=$s.historicalDigest;signatureStatus=[string]$sig.Status;signer=$subject;digestMeaning='historical measurement equality plus current signature/version gate; NOT an independently known-good digest'}
        @{ imageOS=$env:ImageOS; imageVersion=$env:ImageVersion; sdk=$sdk.Trim(); binaries=$inventory } | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $out 'inventory.json')
        if ($sig.Status -ne 'Valid' -or -not $subject.StartsWith($s.publisher, [StringComparison]::Ordinal) -or $version -cne $s.version -or $digest -cne $s.historicalDigest) { throw 'BINARY_IDENTITY_STOP_NO_PIN_RELAXATION' }
    }
    $stage = 'OFFLINE_COMPILE'
    $feed = Join-Path $PSScriptRoot 'empty-feed'
    New-Item -ItemType Directory -Path $feed -ErrorAction Stop | Out-Null
    & $dotnet restore EdgePendingShot.csproj --source $feed --ignore-failed-sources 2>&1 | Out-File (Join-Path $out 'compile.txt')
    if ($LASTEXITCODE -ne 0) { throw 'RESTORE_STOP_NO_DOWNLOAD_FALLBACK' }
    & $dotnet build EdgePendingShot.csproj --no-restore --disable-build-servers -c Release 2>&1 | Out-File -Append (Join-Path $out 'compile.txt')
    if ($LASTEXITCODE -ne 0) { throw 'BUILD_STOP' }
    $helper = Join-Path $PSScriptRoot 'bin/Release/net10.0-windows/EdgePendingShot.exe'
    $caseResults = @()
    foreach ($s in $specs) {
        $stage = 'SCREENSHOT_CASE_' + $s.browser
        $target = Join-Path $out 'result.json'
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = $helper; $start.UseShellExecute = $false
        foreach ($arg in @($s.historicalDigest, $out)) { [void]$start.ArgumentList.Add($arg) }
        $p = [Diagnostics.Process]::Start($start)
        try {
            if (-not $p.WaitForExit(45000)) {
                # Kill exact helper only. Its own job handle closes. This is NOT cleanup proof or PASS.
                $p.Kill($false); [void]$p.WaitForExit(3000)
                throw 'HELPER_TIMEOUT_CLEANUP_UNVERIFIED_STOP'
            }
            if (-not (Test-Path -LiteralPath $target -PathType Leaf) -or (Get-Item -LiteralPath $target).Length -gt 32KB) { throw 'CASE_EVIDENCE_STOP' }
            $r = Get-Content -LiteralPath $target -Raw | ConvertFrom-Json
            $caseResults += @{browser=$s.browser;result=$r.result;exit=$p.ExitCode;cleanup=$r.cleanup}
            # A failed/inconclusive first case stops; do not widen scope/retry to get both green.
            if ($p.ExitCode -ne 0 -or $r.result -cne 'IMAGES_ACQUIRED_PENDING_PIXEL_INSPECTION' -or $r.cleanup -cne 'OWNED_JOB_EMPTY_ROOT_REMOVED') { throw 'CASE_INCOMPLETE_STOP' }
        } finally { $p.Dispose() }
    }
    $result = 'EDGE_PENDING_IMAGES_REQUIRE_PIXEL_INSPECTION'
} catch {
    # Never export full exceptions, raw response headers, endpoint GUID text or dummy profile files.
    $_.Exception.GetType().Name | Set-Content (Join-Path $out 'preflight-error-type.txt')
} finally {
    foreach ($lock in $held) { $lock.Dispose() }
    @{result=$result;lastStage=$stage;positiveCompatibility='NOT_TESTED';productionEligibility='UNCHANGED_REFUSED';utc=[DateTime]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content (Join-Path $out 'completion.json')
}
if ($result -cne 'EDGE_PENDING_IMAGES_REQUIRE_PIXEL_INSPECTION') { exit 3 }
