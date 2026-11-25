# Shared picker consumer contract. All color editing goes through the real advanced dialog.
function ColorWindow {
  $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$p.Id)))
  foreach ($w in $windows) {
    $hex = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Color hex')))
    if ($hex) {
      $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($hex)
      while ($parent -and $parent.Current.ControlType -ne [System.Windows.Automation.ControlType]::Window) { $parent = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent) }
      if ($parent) { return $parent }
    }
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
  $dump=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { "$($_.Current.ProcessId): $($_.Current.Name)" }
  throw "Advanced-color dialog did not open. Windows: $($dump -join ' | ')"
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

# Read the caller-owned selection, never the advanced editor's fallback preview.
# The shared picker hides this label while selection is empty.
function TestWorkspaceColorSelected {
  $scope = if (Get-Command ActiveAppWindow -ErrorAction SilentlyContinue) { ActiveAppWindow } else { CurWin }
  $labels = $scope.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Selected workspace color')))
  $visible = @($labels | Where-Object { -not $_.Current.IsOffscreen })
  if ($visible.Count -gt 1) { throw 'Ambiguous visible selected-color labels' }
  # WPF TextBlock exposes visibility/name but no TextPattern. This checks selection
  # presence only; exact selected values are checked through editor and saved JSON.
  return $visible.Count -eq 1
}
