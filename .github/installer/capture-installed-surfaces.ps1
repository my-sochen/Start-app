# Run only after a real install, before reinstall/uninstall.
$desktop=[Environment]::GetFolderPath('Desktop')
$downloads=Join-Path $env:USERPROFILE 'Downloads';New-Item $downloads -ItemType Directory -Force|Out-Null
$downloaded=Join-Path $downloads ([IO.Path]::GetFileName($exe));Copy-Item $exe $downloaded -Force
$shell=New-Object -ComObject Shell.Application
function FindSurface($pattern){
 $until=[DateTime]::UtcNow.AddSeconds(15)
 do{$all=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.Condition]::TrueCondition);$found=$all|Where-Object {$_.Current.Name -match $pattern -and $_.Current.BoundingRectangle.Width -gt 150}|Select-Object -First 1;if($found){return $found};Start-Sleep -Milliseconds 200}while([DateTime]::UtcNow -lt $until)
 return $null
}
function FolderShot($path,$pattern,$name){
 Start-Process explorer.exe -ArgumentList $path
 $window=FindSurface $pattern
 if(-not $window){throw "Explorer surface missing: $name"}
 [MouseInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)|Out-Null
 Start-Sleep -Milliseconds 1500
 $window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|ForEach-Object {"$($_.Current.ControlType.ProgrammaticName)|$($_.Current.Name)"}|Set-Content (Join-Path $out "$name-ui.txt")
 Shot $name $window
 foreach($sw in @($shell.Windows())){try{if($sw.Document.Folder.Self.Path -eq $path){$sw.Quit()}}catch{}}
}
try{
 # The standard Windows desktop, not an illustration or icon extraction.
 $shell.MinimizeAll();Start-Sleep -Seconds 2
 if(-not(Get-Process explorer -ErrorAction SilentlyContinue)){Start-Process explorer.exe;Start-Sleep -Seconds 3}
 $shell.MinimizeAll();Start-Sleep -Seconds 1
 Shot '06-desktop-installed-shortcut'
 FolderShot $downloads 'Downloads' '07-setup-file-in-downloads'
 FolderShot $target '^StartApp$|StartApp' '08-installed-files-folder'
 $reg='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{B7E4A1C2-5F3D-4E8A-9C60-2D1F8A5B3E77}_is1'
 $entry=Get-ItemProperty $reg
 Check 'Windows uninstall registration exists' ($entry.DisplayName -eq 'Start App' -and $entry.UninstallString -match 'unins000.exe')
 $entry|Select-Object DisplayName,DisplayVersion,InstallLocation,UninstallString|ConvertTo-Json|Set-Content (Join-Path $out 'windows-uninstall-registration.json')
 try{
  Start-Process 'ms-settings:appsfeatures'
  $settings=FindSurface '^Settings$'
  if($settings){[MouseInput]::SetForegroundWindow([IntPtr]$settings.Current.NativeWindowHandle)|Out-Null;Start-Sleep -Seconds 3;Shot '09-windows-settings-installed-apps' $settings;$settings.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|ForEach-Object {"$($_.Current.ControlType.ProgrammaticName)|$($_.Current.Name)"}|Set-Content (Join-Path $out '09-settings-ui.txt')}
  else{'Windows Settings was not available on this hosted Windows image.'|Set-Content (Join-Path $out 'settings-unavailable.txt')}
 }catch{$_.ToString()|Set-Content (Join-Path $out 'settings-unavailable.txt')}
 Start-Process control.exe -ArgumentList 'appwiz.cpl'
 $programs=FindSurface 'Programs and Features'
 if($programs){[MouseInput]::SetForegroundWindow([IntPtr]$programs.Current.NativeWindowHandle)|Out-Null;Start-Sleep -Seconds 2;Shot '10-windows-programs-uninstall-list' $programs;$programs.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|ForEach-Object {"$($_.Current.ControlType.ProgrammaticName)|$($_.Current.Name)"}|Set-Content (Join-Path $out '10-programs-ui.txt')}
}catch{$_.ToString()|Set-Content (Join-Path $out 'surfaces-error.txt')}
