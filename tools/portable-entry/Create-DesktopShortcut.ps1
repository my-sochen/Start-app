# User-invoked portable helper. Never run automatically by the app or package builder.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = $PSScriptRoot
$target = Join-Path $root 'Start-app.exe'
$payload = Join-Path $root 'App/StartApp.App.exe'
if (-not (Test-Path -LiteralPath $target -PathType Leaf) -or -not (Test-Path -LiteralPath $payload -PathType Leaf)) {
    throw 'Extract the entire portable ZIP first. Keep this script beside Start-app.exe and its App folder.'
}
$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
if ([string]::IsNullOrWhiteSpace($desktop) -or -not (Test-Path -LiteralPath $desktop -PathType Container)) { throw 'Current-user Desktop folder is unavailable.' }
$link = Join-Path $desktop 'Start-app.lnk'
if (Test-Path -LiteralPath $link) { throw 'Start-app.lnk already exists. It was not changed. Rename or remove it yourself before trying again.' }
$shell = $null
$shortcut = $null
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('Start-app-shortcut-' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $tempRoot)
$tempLink = Join-Path $tempRoot 'Start-app.lnk'
try {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($tempLink)
    $shortcut.TargetPath = $target
    $shortcut.WorkingDirectory = $root
    $shortcut.IconLocation = $target + ',0'
    $shortcut.Description = 'Start-app'
    $shortcut.Save()
    # File.Move's two-argument overload refuses an existing destination, including a race after our initial check.
    [IO.File]::Move($tempLink, $link)
    if (-not (Test-Path -LiteralPath $link -PathType Leaf)) { throw 'Shortcut creation did not produce a file.' }
    Write-Host 'Created Start-app.lnk on your Desktop. Keep the extracted portable folder in place.'
} finally {
    if ($null -ne $shortcut) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) }
    if ($null -ne $shell) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
    if (Test-Path -LiteralPath $tempLink -PathType Leaf) { [IO.File]::Delete($tempLink) }
    if (Test-Path -LiteralPath $tempRoot -PathType Container) { [IO.Directory]::Delete($tempRoot, $false) }
}
