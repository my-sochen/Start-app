# Shared picker consumer contract. All color editing goes through the real advanced dialog.
function ColorWindow {
  $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$p.Id)))
  foreach ($w in $windows) {
    if ($w.Current.Name -eq 'בחירה מתקדמת') { return $w }
  }
  return $null
}
function OpenColorEditor {
  $scope = if (Get-Command ActiveAppWindow -ErrorAction SilentlyContinue) { ActiveAppWindow } else { CurWin }
  $buttons = $scope.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Advanced color selection')))
  $visible = @($buttons | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.IsEnabled })
  if ($visible.Count -ne 1) { throw "Expected one visible shared color picker, got $($visible.Count)" }
  $button = $visible[0]
  if ($null -eq $button) { throw 'Visible shared advanced-color button missing' }
  $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  $timer = [Diagnostics.Stopwatch]::StartNew()
  do { $editor = ColorWindow; if ($editor) { return $editor }; Start-Sleep -Milliseconds 100 } while ($timer.Elapsed.TotalSeconds -lt 10)
  throw 'Advanced-color dialog did not open'
}
function ColorHexBox($editor) {
  return $editor.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Color hex')))
}
function ReadWorkspaceColor {
  $editor = OpenColorEditor
  try { return (ColorHexBox $editor).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
  finally { $editor.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
}
function SetWorkspaceColor($hex) {
  $editor = OpenColorEditor
  SetText (ColorHexBox $editor) $hex
  $accept = $editor.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'בחר צבע')))
  if ($null -eq $accept -or -not $accept.Current.IsEnabled) { throw 'Advanced color choice not accepted' }
  $accept.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  $timer = [Diagnostics.Stopwatch]::StartNew()
  while ((ColorWindow) -and $timer.Elapsed.TotalSeconds -lt 10) { Start-Sleep -Milliseconds 100 }
  if (ColorWindow) { throw 'Advanced color dialog did not close after confirmation' }
}

function RevealColorSwatch($swatch) {
  $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  for ($attempt=0; $attempt -lt 20; $attempt++) {
    if (-not $swatch.Current.IsOffscreen) { return }
    $ancestor = $walker.GetParent($swatch); $scrolled = $false
    while ($ancestor) {
      $pattern = $null
      if ($ancestor.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref]$pattern) -and $pattern.Current.VerticallyScrollable) {
        $pattern.Scroll([System.Windows.Automation.ScrollAmount]::NoAmount, [System.Windows.Automation.ScrollAmount]::SmallIncrement)
        $scrolled=$true; break
      }
      $ancestor=$walker.GetParent($ancestor)
    }
    if (-not $scrolled) { throw 'Offscreen color swatch has no scrollable ancestor' }
    Start-Sleep -Milliseconds 100
  }
  throw 'Color swatch remained offscreen after scrolling'
}
