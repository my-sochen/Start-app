$ErrorActionPreference = 'Stop'
if (Get-Command Set-DisplayResolution -ErrorAction SilentlyContinue) {
  try { Set-DisplayResolution -Width 1920 -Height 1080 -Force -ErrorAction Stop }
  catch { Write-Warning 'Desktop resolution change was unavailable; actual capture dimensions are recorded.' }
}
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DesignWindow {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
'@
New-Item -ItemType Directory -Force design-captures | Out-Null
# This fresh hosted runner contains synthetic demo data only. No user account or store is used.
$storeDir = Join-Path $env:LOCALAPPDATA 'StartApp'
New-Item -ItemType Directory -Force $storeDir | Out-Null
$names = @('פיתוח', 'אולפן ביתי', 'לימודים', 'הכנת שיעור', 'מחקר ולמידה', 'פרויקט חדש')
$colors = @('#5B79BE', '#A183BD', '#629A8B', '#C59A52', '#7B93AC', '#A7AEB9')
$workspaces = @()
for ($i = 0; $i -lt 6; $i++) {
  $members = if ($i -eq 5) { @() } else { @(@{ itemId = 'demo-link'; id = "demo-member-$i" }) }
  $workspaces += @{ id = "demo-$i"; name = $names[$i]; category = 'כללי'; color = $colors[$i]; createdAt = '2026-11-17T12:00:00Z'; items = @($members) }
}
$catalogue = @{ 'demo-link' = @{ id = 'demo-link'; type = 'link'; name = 'אתר לדוגמה'; target = 'https://example.com/' } }
$detailItems = @(
  @{ id='detail-docs'; type='link'; name='תיעוד הפרויקט'; target='https://example.com/docs' },
  @{ id='detail-code'; type='link'; name='מאגר קוד לדוגמה'; target='https://example.com/repository' },
  @{ id='detail-board'; type='link'; name='לוח משימות'; target='https://example.com/board' },
  @{ id='detail-editor'; type='program'; name='עורך טקסט'; target='C:\Windows\notepad.exe' },
  @{ id='detail-terminal'; type='program'; name='Terminal'; target='C:\Windows\System32\cmd.exe' },
  @{ id='detail-file'; type='file'; name='מצגת לדוגמה'; target='C:\Projects\Sample\demo.pptx' },
  @{ id='detail-folder'; type='folder'; name='תיקיית הפרויקט'; target='C:\Projects\Sample' }
)
$detailMembers = @()
foreach ($item in $detailItems) { $catalogue[$item.id] = $item; $detailMembers += @{ itemId=$item.id; id=('member-'+$item.id) } }
$workspaces[0].items = @($detailMembers)
$workspaces[0].category = 'תכנות'
$workspaces[0].items += @{ itemId='detail-editor'; id='duplicate-editor-dev'; name='עורך טקסט נוסף' }
$workspaces[1].items += @{ itemId='detail-editor'; id='shared-editor-home' }
$globalItems = @(
  @{ id='global-demo-docs'; type='link'; name='תיעוד'; target='https://example.com/docs'; addedAt='2026-11-20T12:00:00Z' },
  @{ id='global-demo-editor'; type='program'; name='עורך טקסט'; target='C:\Windows\notepad.exe'; addedAt='2026-11-20T12:00:00Z' }
)
$whiteItems = @(
  @{ id='white-demo-folder'; type='folder'; name='תיקיית מסמכים'; target='C:\Users\Demo\Documents'; addedAt='2026-11-20T12:00:00Z' },
  @{ id='white-demo-board'; type='link'; name='לוח משימות'; target='https://example.com/board'; addedAt='2026-11-21T12:00:00Z' }
)
@{ schemaVersion = 5; items = $catalogue; workspaces = @($workspaces); customCategories = @('תכנות'); defaultWorkspaceId = 'demo-0'; globalItems=$globalItems; captureWhitelist=$whiteItems } | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $storeDir 'store.json') -Encoding utf8
$exe = Get-ChildItem src/StartApp.App/bin/Release -Recurse -Filter StartApp.App.exe | Select-Object -First 1
$env:STARTAPP_CRASHLOG = '1'
$p = Start-Process $exe.FullName -PassThru
try {
  $clock = [Diagnostics.Stopwatch]::StartNew()
  do { Start-Sleep -Milliseconds 250; $p.Refresh() } while (!$p.HasExited -and $p.MainWindowHandle -eq 0 -and $clock.Elapsed.TotalSeconds -lt 45)
  if ($p.HasExited -or $p.MainWindowHandle -eq 0) {
    $crash = Join-Path $storeDir 'crash.log'
    if (Test-Path $crash) { Copy-Item $crash design-captures/crash.log; Get-Content $crash }
    throw 'App did not open'
  }
  $a = [System.Windows.Automation.AutomationElement]
  $ts = [System.Windows.Automation.TreeScope]::Descendants
  $pc = [System.Windows.Automation.PropertyCondition]
  $win = $a::FromHandle($p.MainWindowHandle)
  function Capture($name, $width, $height, $preserveFocus = $false, $preserveCursor = $false) {
    if (!$preserveFocus) {
      [void][DesignWindow]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, 0, 0, $width, $height, 0x40)
      [void][DesignWindow]::SetForegroundWindow($p.MainWindowHandle)
    }
    if (!$preserveCursor) { [void][DesignWindow]::SetCursorPos(1, 1) }
    Start-Sleep -Milliseconds 900
    $r = New-Object DesignWindow+Rect
    [void][DesignWindow]::GetWindowRect($p.MainWindowHandle, [ref]$r)
    "$name actual=$($r.Right-$r.Left)x$($r.Bottom-$r.Top) requested=${width}x${height}" | Add-Content design-captures/dimensions.txt
    $image = New-Object Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [Drawing.Graphics]::FromImage($image)
    try { $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $image.Size); $image.Save((Join-Path $PWD "design-captures/$name.png")) }
    finally { $g.Dispose(); $image.Dispose() }
  }
  function InvokeNamed($name) {
    $visible = New-Object System.Windows.Automation.AndCondition((New-Object $pc($a::NameProperty, $name)), (New-Object $pc($a::IsOffscreenProperty, $false)))
    if ($name -eq 'Toggle default workspace') {
      $visible = New-Object System.Windows.Automation.AndCondition($visible, (New-Object $pc($a::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    }
    $button = $win.FindFirst($ts, $visible)
    if (!$button -and $name -in @('Save item edit', 'Cancel item edit')) { $button = RootNamed $name }
    if (!$button) { throw "Missing visible app control: $name" }
    if ($name -in @('Options menu', 'Toggle default workspace')) {
      $point = [System.Windows.Point]::new(0.0, 0.0)
      $clickable = $false
      for ($attempt = 0; $attempt -lt 20 -and !$clickable; $attempt++) {
        $clickable = $button.TryGetClickablePoint([ref]$point)
        if (!$clickable) { Start-Sleep -Milliseconds 100 }
      }
      if (!$clickable) {
        $bounds = $button.Current.BoundingRectangle
        if ($bounds.Width -le 0 -or $bounds.Height -le 0) { throw "Visible app control has empty bounds: $name" }
        $point = [System.Windows.Point]::new($bounds.X + $bounds.Width / 2, $bounds.Y + $bounds.Height / 2)
        Write-Host "Using visible control bounds for mouse click: $name ($bounds)"
      }
      [void][DesignWindow]::SetCursorPos([int]$point.X, [int]$point.Y)
      [DesignWindow]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
      [DesignWindow]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
    } else { $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
    Start-Sleep -Milliseconds 350
  }
  function RootNamed($name) {
    $element = $a::RootElement.FindFirst($ts, (New-Object $pc($a::NameProperty, $name)))
    if (!$element) { throw "Missing app popup control: $name" }
    return $element
  }
  function ExpandNamed($name) {
    $element = RootNamed $name
    $element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 250
    return $element
  }
  Start-Sleep -Seconds 2
  Capture 'home-mock-size' 1280 880
  InvokeNamed 'Maximize window'
  InvokeNamed 'Restore window'
  Capture 'home-caption-restored' 1280 880
  $homeTitle = $win.FindFirst($ts, (New-Object $pc($a::NameProperty, 'פיתוח')))
  $homeTitle.SetFocus()
  [System.Windows.Forms.SendKeys]::SendWait('+{F10}')
  Capture 'home-right-click-default' 1280 880
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
  InvokeNamed 'Options menu'
  Capture 'options-mock-size' 1280 880
  $captureMenu = ExpandNamed 'לכידה'
  Capture 'home-menu-capture-flyout' 1280 880
  $captureMenu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
  (RootNamed 'אייטמים').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 350
  Capture 'all-items-top' 1280 880
  $allSearch = $win.FindFirst($ts, (New-Object $pc($a::NameProperty, 'Search all items')))
  $allSearch.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('עורך')
  Capture 'all-items-search' 1280 880
  $sharedName = $win.FindFirst($ts, (New-Object System.Windows.Automation.AndCondition((New-Object $pc($a::NameProperty, 'עורך טקסט')), (New-Object $pc($a::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))))
  $sharedName.SetFocus()
  [System.Windows.Forms.SendKeys]::SendWait('+{F10}')
  Capture 'all-items-actions' 1280 880
  (RootNamed 'ערוך').SetFocus()
  [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
  Capture 'all-items-shared-env-picker' 1280 880
  foreach ($envName in @('פיתוח','אולפן ביתי')) {
    $choice = $a::RootElement.FindFirst($ts, (New-Object System.Windows.Automation.AndCondition((New-Object $pc($a::NameProperty, $envName)), (New-Object $pc($a::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))))
    $choice.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).AddToSelection()
  }
  Capture 'all-items-multi-env-selected' 1280 880
  (RootNamed 'Confirm item workspace choice').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Capture 'all-items-multi-env-edit' 1280 880
  $batchName = (RootNamed 'Selected item name').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
  $originalBatchName = $batchName.Current.Value
  $batchName.SetValue($originalBatchName + ' updated')
  (RootNamed 'Cancel selected item edit').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Capture 'all-items-unsaved-dialog' 1280 880
  (RootNamed 'Keep selected item editing').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  $batchName.SetValue($originalBatchName)
  (RootNamed 'Cancel selected item edit').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  $allSearch.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('אתר לדוגמה')
  $linkName = $win.FindFirst($ts, (New-Object System.Windows.Automation.AndCondition((New-Object $pc($a::NameProperty, 'אתר לדוגמה')), (New-Object $pc($a::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))))
  $linkName.SetFocus(); [System.Windows.Forms.SendKeys]::SendWait('+{F10}')
  (RootNamed 'ערוך').SetFocus(); [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
  $choice = $a::RootElement.FindFirst($ts, (New-Object System.Windows.Automation.AndCondition((New-Object $pc($a::NameProperty, 'אולפן ביתי')), (New-Object $pc($a::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))))
  $choice.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).AddToSelection()
  (RootNamed 'Confirm item workspace choice').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Capture 'all-items-link-full-editor' 1280 880
  $launchChoices = RootNamed 'Selected item open with'
  $launchChoices.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
  Capture 'all-items-open-with-options' 1280 880 $true
  $launchChoices.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
  (RootNamed 'Cancel selected item edit').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  $allSearch.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('תיקיית הפרויקט')
  $folderName = $win.FindFirst($ts, (New-Object System.Windows.Automation.AndCondition((New-Object $pc($a::NameProperty, 'תיקיית הפרויקט')), (New-Object $pc($a::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))))
  $folderName.SetFocus(); [System.Windows.Forms.SendKeys]::SendWait('+{F10}')
  (RootNamed 'ערוך').SetFocus(); [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
  Capture 'all-items-folder-full-editor' 1280 880
  (RootNamed 'Cancel selected item edit').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  $allSearch.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('עורך')
  $sharedName = $win.FindFirst($ts, (New-Object System.Windows.Automation.AndCondition((New-Object $pc($a::NameProperty, 'עורך טקסט')), (New-Object $pc($a::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))))
  foreach ($action in @(@('מחק','all-items-delete-env-picker'), @('הוסף לסביבה','all-items-add-env-picker'))) {
    $sharedName.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('+{F10}')
    (RootNamed $action[0]).SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Capture $action[1] 1280 880
    (RootNamed 'Cancel item workspace choice').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  }
  $allSearch.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('')
  $allViewport = $win.FindFirst($ts, (New-Object $pc($a::NameProperty, 'All items viewport')))
  $allScroll = $allViewport.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
  if ($allScroll.Current.VerticallyScrollable) { $allScroll.SetScrollPercent(-1,100) }
  Capture 'all-items-bottom' 1280 880
  InvokeNamed 'Back to workspaces'
  InvokeNamed 'Options menu'
  $globalMenu = $a::RootElement.FindFirst($ts, (New-Object $pc($a::NameProperty, 'סביבה גלובלית')))
  $globalMenu.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 350
  Capture 'global-env-menu-entry' 1280 880
  InvokeNamed 'Add global item'
  Capture 'global-env-add-dialog' 1280 880
  InvokeNamed 'Cancel global add'
  InvokeNamed 'Back to workspaces'
  InvokeNamed 'Help menu'
  Capture 'home-menu-help' 1280 880
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
  InvokeNamed 'Settings'
  Capture 'settings' 1280 880
  InvokeNamed 'Back to workspaces'
  InvokeNamed 'פרויקט חדש'
  Capture 'empty-workspace' 1280 880
  InvokeNamed 'Back to workspaces'
  Capture 'home-900x560' 900 560
  Capture 'home-before-menus' 1280 880
  InvokeNamed 'View menu'
  Capture 'view-menu-900x560' 1280 880
  foreach ($entry in @(@('גודל כרטיסים','view-size-flyout'),@('מיון','view-sort-flyout'),@('תבנית תצוגה','view-template-flyout'))) {
    $sub = ExpandNamed $entry[0]
    Capture $entry[1] 1280 880
    $sub.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
  }
  $filter = $a::RootElement.FindFirst($ts, (New-Object $pc($a::NameProperty, 'סינון')))
  $filter.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
  Start-Sleep -Milliseconds 350
  $colorMenu = $a::RootElement.FindFirst($ts, (New-Object $pc($a::NameProperty, 'לפי צבע')))
  $colorMenu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
  Start-Sleep -Milliseconds 350
  Capture 'view-color-flyouts-900x560' 1280 880
  $colorMenu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
  $categoryMenu = ExpandNamed 'לפי קטגוריה'
  Capture 'filters-category' 1280 880
  $categoryMenu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
  $typeMenu = ExpandNamed 'לפי סוג תוכן'
  Capture 'filters-type' 1280 880
  $programMenu = ExpandNamed 'תוכנות'
  Capture 'filters-program-items' 1280 880
  $programMenu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
  $typeMenu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
  $filter.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
  Start-Sleep -Milliseconds 500
  $win = $a::FromHandle($p.MainWindowHandle)
  InvokeNamed 'פיתוח'
  Capture 'details-default-mock-size' 1280 880
  $detailName = $win.FindFirst($ts, (New-Object System.Windows.Automation.AndCondition((New-Object $pc($a::NameProperty, 'תיעוד הפרויקט')), (New-Object $pc($a::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))))
  $detailName.SetFocus()
  [System.Windows.Forms.SendKeys]::SendWait('+{F10}')
  Capture 'details-item-actions' 1280 880
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}')

  InvokeNamed 'Toggle default workspace'
  Capture 'details-remove-default-confirmation' 1280 880
  InvokeNamed 'Confirm default-env replace'
  InvokeNamed 'Dismiss notification'
  Capture 'details-nondefault-mock-size' 1280 880
  InvokeNamed 'Toggle default workspace'
  InvokeNamed 'Dismiss notification'
  $nameInput = $win.FindFirst($ts, (New-Object $pc($a::NameProperty, 'Workspace name')))
  $nameInput.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('פיתוח לפרויקט')
  $descriptionInput = $win.FindFirst($ts, (New-Object $pc($a::NameProperty, 'Workspace description')))
  $descriptionInput.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('כל מה שצריך כדי לחזור לפרויקט')
  Capture 'details-dirty-mock-size' 1280 880
  InvokeNamed 'Back to workspaces'
  Capture 'details-unsaved-dialog' 1280 880
  InvokeNamed 'Back to editing'
  InvokeNamed 'Save workspace details'
  InvokeNamed 'Edit item'
  Capture 'details-item-editor' 1280 880
  $editInput = RootNamed 'Edit item name'
  $editInput.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('תיעוד הפרויקט מעודכן')
  Capture 'details-item-editor-dirty' 1280 880
  InvokeNamed 'Cancel item edit'
  Capture 'details-item-unsaved-dialog' 1280 880
  (RootNamed 'Keep item editing').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  InvokeNamed 'Save item edit'
  InvokeNamed 'Edit item'
  InvokeNamed 'Cancel item edit'
  InvokeNamed 'שגר'
  Capture 'launch-selection' 1280 880
  InvokeNamed 'Close opening selection'
  InvokeNamed 'Back to workspaces'
  InvokeNamed 'New workspace button'
  Capture 'create-empty-mock-size' 1280 880
  $input = $win.FindFirst($ts, (New-Object $pc($a::NameProperty, 'Workspace name')))
  $input.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('פרויקט חדש')
  Capture 'create-live-preview-blue' 1280 880
  InvokeNamed 'Create color green'
  Capture 'create-live-preview-green' 1280 880
  Capture 'create-live-preview-900x560' 900 560
  InvokeNamed 'Close window'
  Capture 'create-unsaved-close-guard' 1280 880
  InvokeNamed 'Back to editing'
  InvokeNamed 'ביטול וחזרה'
  InvokeNamed 'צא בלי לשמור'
  InvokeNamed 'Options menu'
  $pickerMenu = RootNamed 'הגדר סביבה כברירת מחדל'
  $pickerMenu.SetFocus()
  [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
  Start-Sleep -Milliseconds 500
  Capture 'default-workspace-picker' 1280 880
  $choice = $a::RootElement.FindFirst($ts, (New-Object System.Windows.Automation.AndCondition((New-Object $pc($a::NameProperty, 'אולפן ביתי')), (New-Object $pc($a::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))))
  $point = $choice.GetClickablePoint()
  [void][DesignWindow]::SetCursorPos([int]$point.X, [int]$point.Y)
  Capture 'default-workspace-picker-hover' 1280 880 $true $true
  [DesignWindow]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
  [DesignWindow]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
  Start-Sleep -Milliseconds 350
  Capture 'default-picker-replace-confirmation' 1280 880
  InvokeNamed 'Cancel default-env replace'
  InvokeNamed 'Options menu'
  (RootNamed 'הגדר סביבה כברירת מחדל').SetFocus()
  [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
  Start-Sleep -Milliseconds 350
  $none = $a::RootElement.FindFirst($ts, (New-Object System.Windows.Automation.AndCondition((New-Object $pc($a::NameProperty, 'ללא ברירת מחדל')), (New-Object $pc($a::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)))))
  Capture 'default-picker-before-removal' 1280 880
  $none.SetFocus()
  [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
  Start-Sleep -Milliseconds 350
  Capture 'default-picker-remove-confirmation' 1280 880
  InvokeNamed 'Confirm default-env replace'
  Capture 'home-no-default' 1280 880
  InvokeNamed 'Options menu'
  (RootNamed 'הגדר סביבה כברירת מחדל').SetFocus()
  [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
  Start-Sleep -Milliseconds 350
  Capture 'default-picker-no-current-default' 1280 880
  (RootNamed 'Close default workspace picker').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  # A disposable CI-only desktop window supplies a real capture candidate; nothing is launched by the app.
  $fixture = New-Object System.Windows.Forms.Form
  try {
    $fixture.Text = 'StartApp design capture fixture'
    $fixture.Show()
    [System.Windows.Forms.Application]::DoEvents()
    InvokeNamed 'אולפן ביתי'
    InvokeNamed 'Add from open items'
    $candidate = $null
    for ($i = 0; $i -lt 40 -and !$candidate; $i++) {
      [System.Windows.Forms.Application]::DoEvents()
      Start-Sleep -Milliseconds 250
      $openItems = RootNamed 'Open now list'
      $candidate = $openItems.FindAll($ts, (New-Object $pc($a::ControlTypeProperty, [System.Windows.Automation.ControlType]::CheckBox))) | Where-Object { $_.Current.Name -like '*לוח משימות*' } | Select-Object -First 1
    }
    if (!$candidate) { throw 'No desktop fixture candidate detected' }
    $candidate.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    $second = $openItems.FindAll($ts, (New-Object $pc($a::ControlTypeProperty, [System.Windows.Automation.ControlType]::CheckBox))) | Where-Object { $_.Current.Name -like '*תיקיית מסמכים*' } | Select-Object -First 1
    if (!$second) { throw 'No second capture fixture candidate detected' }
    $second.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Capture 'capture-two-items-selected' 1280 880
    InvokeNamed 'Add selected capture items'
    $added = RootNamed 'Added items not saved yet'
    $edit = $added.FindAll($ts, (New-Object $pc($a::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button))) | Where-Object { $_.Current.Name -like 'Toggle added *לוח משימות*' } | Select-Object -First 1
    if (!$edit) { throw 'No staged fixture editor available' }
    $edit.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Capture 'capture-embedded-full-editor' 1280 880
    $captureScroll = (RootNamed 'Capture target viewport').GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if ($captureScroll.Current.VerticallyScrollable) { $captureScroll.SetScrollPercent(-1,100) }
    $visibleEditorName = $added.FindAll($ts, (New-Object $pc($a::NameProperty, 'Added item name'))) | Where-Object { !$_.Current.IsOffscreen } | Select-Object -First 1
    if (!$visibleEditorName) { throw 'Expanded capture name field is not visible' }
    $visibleEditorName.SetFocus()
    Capture 'capture-website-expanded-hint' 1280 880
    $other = $added.FindAll($ts, (New-Object $pc($a::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button))) | Where-Object { $_.Current.Name -like 'Toggle added *תיקיית מסמכים*' } | Select-Object -First 1
    if (!$other) { throw 'No second staged accordion arrow detected' }
    $other.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Capture 'capture-switched-item-editor' 1280 880
  } catch { $_.Exception.Message | Set-Content design-captures/capture-fixture-gap.txt }
  finally { $fixture.Close(); $fixture.Dispose() }
  "Source: $env:GITHUB_SHA`nSynthetic demo data. Real app window, no test UI.`nHome opened; options and view buttons clicked. No deploy, account, or purchase actions." | Set-Content design-captures/actions.txt
} finally {
  if (!$p.HasExited) { Stop-Process -Id $p.Id -Force }
}
