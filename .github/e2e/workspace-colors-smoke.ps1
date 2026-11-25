$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
. "$PSScriptRoot/workspace-colors.ps1"
$output=Join-Path $PWD 'color-uia-evidence'
New-Item $output -ItemType Directory -Force | Out-Null
$binary=Join-Path $PWD 'tests/StartApp.ColorCapture/bin/Release/net10.0-windows/StartApp.ColorCapture.exe'
$p=Start-Process $binary -ArgumentList @(('"'+$output+'"'),'--uia-smoke') -PassThru
function CurWin {
  return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,[int]$p.Id)))
}
function SetText($edit,$text) { $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text) }
try {
  $timer=[Diagnostics.Stopwatch]::StartNew()
  do { $window=CurWin; if($window) { $picker=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'Advanced color selection'))) }; if($picker) {break}; Start-Sleep -Milliseconds 100 } while($timer.Elapsed.TotalSeconds -lt 15)
  if(-not $picker) {throw 'Shared picker missing at startup'}
  if ((ReadSelectedWorkspaceColor) -ne '') {throw 'Empty picker falsely reports a selected color'}
  SetWorkspaceColor '#12ab34'
  if ((ReadSelectedWorkspaceColor) -ne '#12AB34') {throw 'Selected-color label did not expose chosen color'}
  $actual=ReadWorkspaceColor
  if($actual -ne '#12AB34') {throw "Shared consumer readback failed: $actual"}
  $palette=$window.FindAll([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'Create palette')))
  if($palette.Count -ne 1) {throw 'Unique create palette anchor missing'}
  $buttons=$palette[0].FindAll([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button)))
  if($buttons.Count -ne 9) {throw 'Expected nine anchored preset buttons'}
  @{ok=$true;color=$actual;presetCount=$buttons.Count} | ConvertTo-Json | Set-Content (Join-Path $output 'checks.json')
} finally {
  New-Item (Join-Path $output 'stop') -ItemType File -Force | Out-Null
  if(-not $p.WaitForExit(10000)) {$p.Kill($true)}
}
