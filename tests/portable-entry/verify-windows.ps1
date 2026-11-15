$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
New-Item -ItemType Directory -Force diag, out-images | Out-Null
$repo = $PWD.Path
$root = Join-Path $env:RUNNER_TEMP ('Start-app portable ' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
Add-Type -Path "$repo/tests/portable-entry/OwnedTree.cs"
$owned = [Collections.Generic.List[OwnedTree]]::new()
$checks = [Collections.Generic.List[string]]::new()
# This dedicated hosted-runner user must have no prior StartApp data. Refuse to
# photograph any existing data, even if a runner image unexpectedly contains it.
$dataRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'StartApp'
if(Test-Path -LiteralPath $dataRoot){throw 'Existing app data found; refusing synthetic screenshot run'}
$ownedData = $false
$child=$null
function Assert-True([bool]$condition, [string]$name) {
    if (-not $condition) { throw "FAIL: $name" }
    $checks.Add($name); "PASS: $name"
}
function Wait-Exit($process, [int]$milliseconds = 15000) {
    if (-not $process.WaitForExit($milliseconds)) { throw "Owned process $($process.Id) did not exit" }
    $process.Refresh()
    return $process.ExitCode
}
function Start-Owned([string]$path,[string]$cwd) {
    $tree=[OwnedTree]::Start($path,$cwd)
    $owned.Add($tree)
    return $tree.Process
}
Add-Type -AssemblyName System.Drawing, System.Windows.Forms, UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class PortableWindow {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int L,T,R,B; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int z,uint f);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
}
'@
[void][PortableWindow]::SetProcessDPIAware()
function Save-Window($process, [string]$name) {
    $process.Refresh()
    Assert-True ([PortableWindow]::IsWindowVisible($process.MainWindowHandle) -and -not [PortableWindow]::IsIconic($process.MainWindowHandle)) "visible nonminimized window $name"
    Assert-True ([PortableWindow]::SetWindowPos($process.MainWindowHandle,[IntPtr]::Zero,40,40,0,0,0x41)) "move and raise $name"
    [void][PortableWindow]::SetCursorPos(5,5)
    [void][PortableWindow]::SetForegroundWindow($process.MainWindowHandle)
    Start-Sleep -Milliseconds 400
    Assert-True ([PortableWindow]::GetForegroundWindow() -eq $process.MainWindowHandle) "foreground owned window $name"
    $r = [PortableWindow+Rect]::new()
    Assert-True ([PortableWindow]::GetWindowRect($process.MainWindowHandle,[ref]$r)) "window rect $name"
    $screen=[System.Windows.Forms.SystemInformation]::VirtualScreen
    Assert-True ($r.L -ge $screen.Left -and $r.T -ge $screen.Top -and $r.R -le $screen.Right -and $r.B -le $screen.Bottom) "on-screen bounds $name"
    $w = $r.R-$r.L; $h=$r.B-$r.T
    Assert-True ($w -gt 100 -and $h -gt 80) "usable pixels $name"
    $bitmap = [Drawing.Bitmap]::new($w,$h)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.CopyFromScreen($r.L,$r.T,0,0,$bitmap.Size); $colors=[Collections.Generic.HashSet[int]]::new()
        for($y=0;$y -lt $h;$y+=16){for($x=0;$x -lt $w;$x+=16){[void]$colors.Add($bitmap.GetPixel($x,$y).ToArgb())}}
        Assert-True ($colors.Count -gt 8) "nonblank pixel diversity $name"
        $bitmap.Save((Join-Path $repo "out-images/$name.png")) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}
function Wait-Window($process) {
    for($i=0;$i -lt 60;$i++) {
        $process.Refresh()
        if($process.HasExited){throw "Early exit $($process.ExitCode)"}
        if($process.MainWindowHandle -ne [IntPtr]::Zero){return}
        Start-Sleep -Milliseconds 250
    }
    throw 'No window within15s'
}
try {
    # The probe proves target location, independent CWD, Unicode/space paths and
    # exact child exit propagation. Nothing is launched through a shell.
    $original = Join-Path $root 'original folder עברית'
    & "$repo/tools/portable-entry/package.ps1" -Payload "$repo/probe-payload" -Launcher "$repo/portable-build/Start-app.exe" -Destination $original
    $moved = Join-Path $root 'relocated folder בדיקה'
    Move-Item -LiteralPath $original -Destination $moved
    $decoy = New-Item -ItemType Directory -Path (Join-Path $root 'unrelated CWD')
    New-Item -ItemType Directory -Path (Join-Path $decoy 'App') | Out-Null
    Copy-Item -LiteralPath "$repo/portable-build/probe.exe" -Destination (Join-Path $decoy 'App/StartApp.App.exe')
    $p = Start-Owned (Join-Path $moved 'Start-app.exe') $decoy.FullName

    Assert-True ((Wait-Exit $p) -eq 23) 'child exit23 propagated'
    $probeFile = Join-Path $moved 'App/probe.txt'
    Assert-True (Test-Path -LiteralPath $probeFile) 'fixed sibling target launched after move'
    $cwd = [Text.Encoding]::Unicode.GetString([IO.File]::ReadAllBytes($probeFile))
    Assert-True ($cwd -eq (Join-Path $moved 'App')) 'child working directory is payload, including Hebrew/spaces'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $decoy 'App/probe.txt'))) 'CWD decoy never executed'
    # Every staged payload byte must equal the generated input; this is not a
    # claim that a new build equals the already published release.
    function Payload-Hashes($path) {
        $base=(Resolve-Path -LiteralPath $path).Path
        @(Get-ChildItem -LiteralPath $base -Force -Recurse -File | ForEach-Object {
            $_.FullName.Substring($base.Length)+':'+(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        } | Sort-Object)
    }
    # Simulate discovery failure/timeout: no child PID is registered with the
    # harness, yet terminating its creation-time job must reap every descendant.
    $failureDir=Join-Path $root 'failure containment'
    & "$repo/tools/portable-entry/package.ps1" -Payload "$repo/probe-payload" -Launcher "$repo/portable-build/Start-app.exe" -Destination $failureDir
    $env:STARTAPP_PROBE_WAIT='1'
    try {
        $failure=Start-Owned (Join-Path $failureDir 'Start-app.exe') $decoy.FullName
        for($i=0;$i -lt 40 -and -not (Test-Path -LiteralPath (Join-Path $failureDir 'App/probe.txt'));$i++){Start-Sleep -Milliseconds 50}
        Assert-True (Test-Path -LiteralPath (Join-Path $failureDir 'App/probe.txt')) 'failure-test descendant started'
        $tree=$owned[$owned.Count-1]
        Assert-True ($tree.ActiveCount -ge 2) 'failure test job contains launcher and descendant'
        # Deliberately skip normal actual-app discovery; retain the specific probe
        # only as an independent post-job exit witness, never as a kill target.
        $probeInfo=@(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($failure.Id)" | Where-Object {$_.ExecutablePath -eq (Join-Path (Join-Path $failureDir 'App') 'StartApp.App.exe')})
        Assert-True ($probeInfo.Count -eq 1) 'one exact failure probe descendant'
        $probeWitness=Get-Process -Id $probeInfo[0].ProcessId
        try {
            [void]$probeWitness.Handle
            Assert-True ($probeWitness.Path -eq $probeInfo[0].ExecutablePath -and [Math]::Abs(($probeWitness.StartTime.ToUniversalTime()-$probeInfo[0].CreationDate.ToUniversalTime()).TotalSeconds) -lt 1) 'retained probe identity'
            $tree.StopAndDispose()
            Assert-True ($probeWitness.WaitForExit(5000)) 'owned failure probe reaped after job cleanup'
        } finally {$probeWitness.Dispose()}
    } finally {Remove-Item Env:STARTAPP_PROBE_WAIT -ErrorAction SilentlyContinue}
    # Package helper must refuse overwrite instead of deleting/replacing anything.
    $refused=$false
    try { & "$repo/tools/portable-entry/package.ps1" -Payload "$repo/probe-payload" -Launcher "$repo/portable-build/Start-app.exe" -Destination $moved }
    catch { $refused=$true }
    Assert-True $refused 'existing staging destination refused'
    # Missing payload produces an actionable, non-elevating launcher error.
    $missing = New-Item -ItemType Directory -Path (Join-Path $root 'missing payload')
    Copy-Item -LiteralPath "$repo/portable-build/Start-app.exe" -Destination $missing.FullName
    $p=Start-Owned (Join-Path $missing 'Start-app.exe') $decoy.FullName;
    Wait-Window $p
    $element = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    $texts=$element.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
    $names= @($texts | ForEach-Object {$_.Current.Name}) -join ' '
    Assert-True ($names.Contains('App folder')) 'missing-payload message instructs keep App folder'
    Save-Window $p 'portable-entry-missing-folder'
    [void]$p.CloseMainWindow()
    Assert-True ((Wait-Exit $p) -eq 2) 'missing-payload failure exit2'
    # Actual application, not probe: starts ONLY through the top-level launcher.
    $actual = Join-Path $root 'real app עברית'
    & "$repo/tools/portable-entry/package.ps1" -Payload "$repo/app-payload" -Launcher "$repo/portable-build/Start-app.exe" -Destination $actual
    Assert-True (-not (Compare-Object (Payload-Hashes "$repo/app-payload") (Payload-Hashes (Join-Path $actual 'App')))) 'intact candidate payload hash and file list'
    New-Item -ItemType Directory -Path $dataRoot | Out-Null
    $ownedData=$true
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $dataRoot 'store.json'))) 'synthetic empty app data before launch'
    $launcher=Start-Owned (Join-Path $actual 'Start-app.exe') $decoy.FullName
    $child=$null
    for($i=0;$i -lt 60;$i++) {
        $processes=@(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($launcher.Id)")
        foreach($candidate in $processes) {
            if($candidate.ExecutablePath -eq (Join-Path (Join-Path $actual 'App') 'StartApp.App.exe')) { $found=Get-Process -Id $candidate.ProcessId
                # Force an OS process handle, then validate snapshot creation and exact path.
                [void]$found.Handle
                if($found.Path -ne $candidate.ExecutablePath -or [Math]::Abs(($found.StartTime.ToUniversalTime()-$candidate.CreationDate.ToUniversalTime()).TotalSeconds) -gt 1){$found.Dispose();throw 'Child identity changed during discovery'}
                $child=$found;break }
        }
        if($null -ne $child){break}
        $launcher.Refresh();if($launcher.HasExited){throw 'Actual launcher exited before owned child was found'}
        Start-Sleep -Milliseconds 250
    }
    Assert-True ($null -ne $child) 'exact owned actual app child found'
    Wait-Window $child
    $appElement=[Windows.Automation.AutomationElement]::FromHandle($child.MainWindowHandle)
    Assert-True ($appElement.Current.Name -match 'Start') 'actual WPF main window title'
    Save-Window $child 'portable-entry-actual-app'
    [void]$child.CloseMainWindow()
    Assert-True ((Wait-Exit $child) -eq 0) 'actual app clean exit0'
    Assert-True ((Wait-Exit $launcher) -eq 0) 'launcher clean exit0 after actual app'
    @{status='pass';checks=$checks;scope='Hosted Windows runner only. Not a consumer-PC or SmartScreen result.';sha=$env:GITHUB_SHA} | ConvertTo-Json -Depth 5 | Set-Content diag/portable-entry-result.json
}
catch {
    @{status='fail';checks=$checks;error=$_.Exception.Message;sha=$env:GITHUB_SHA} | ConvertTo-Json -Depth 5 | Set-Content diag/portable-entry-result.json
    throw
}
finally {
    # Job handles are retained from suspended creation, before any child exists.
    # Covers failures before discovery, not just successfully discovered PIDs.
    $cleanupErrors=@()
    try {
        foreach($tree in $owned){try{$tree.StopAndDispose()}catch{$cleanupErrors+=$_.Exception.Message}}
        if($null -ne $child){$child.Dispose()}
    }
    finally {
        if($ownedData -and (Test-Path -LiteralPath $dataRoot)){
            try{Remove-Item -LiteralPath $dataRoot -Recurse -Force}catch{$cleanupErrors+=$_.Exception.Message}
        }
    }
    if($cleanupErrors.Count){$cleanupErrors | Set-Content diag/portable-cleanup-errors.txt;throw ($cleanupErrors -join '; ')}
}
