param(
    [Parameter(Mandatory)][string]$Payload,
    [Parameter(Mandatory)][string]$Launcher,
    [Parameter(Mandatory)][string]$Destination
)
$ErrorActionPreference = 'Stop'
# New destination only: never remove or overwrite existing user files.
if (Test-Path -LiteralPath $Destination) { throw 'Destination already exists. Choose a new directory.' }
$payloadRoot = (Resolve-Path -LiteralPath $Payload).Path
$launcherPath = (Resolve-Path -LiteralPath $Launcher).Path
if (-not (Test-Path -LiteralPath (Join-Path $payloadRoot 'StartApp.App.exe') -PathType Leaf)) { throw 'Payload lacks StartApp.App.exe' }
New-Item -ItemType Directory -Path $Destination | Out-Null
# Any failure leaves a clearly incomplete NEW directory, never a silently valid package.
# Its caller must not distribute it; no deletion of potentially user-touched files.
$app = New-Item -ItemType Directory -Path (Join-Path $Destination 'App')
Get-ChildItem -LiteralPath $payloadRoot -Force | Copy-Item -Destination $app.FullName -Recurse
Copy-Item -LiteralPath $launcherPath -Destination (Join-Path $Destination 'Start-app.exe')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Create-DesktopShortcut.ps1') -Destination (Join-Path $Destination 'Create-DesktopShortcut.ps1')
@'
Start-app - prototype / אב-טיפוס

Extract the entire ZIP first. Open Start-app.exe, beside the App folder.
Keep the App folder beside this EXE. Do not move either one alone.
חלצו את כל קובצי ה-ZIP ופתחו את Start-app.exe לצד תיקיית App.

Optional Desktop shortcut: right-click Create-DesktopShortcut.ps1 and choose Run with PowerShell.
It creates only a new current-user Start-app.lnk and refuses to replace an existing one.
Do not move the extracted portable folder after creating the shortcut. If PowerShell policy
blocks the script, do not weaken the policy: right-click Start-app.exe and create a shortcut manually.
A manually created shortcut uses the same embedded UFO icon. No administrator rights are needed.

This layout does not make the app code-signed or approved by Windows.
Do not disable Windows Security to run it. This is an evaluation prototype.
Your existing known limits and prototype notice are in the App folder.
'@ | Set-Content -LiteralPath (Join-Path $Destination 'START-HERE.txt') -Encoding utf8
