try {
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class W { [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; } [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags); }'
# Native top-level window toolkit for owner-aware dialog discovery. A modal common-item dialog is an OWNED
# top-level HWND. The first live run (43479971420) left the visibly open picker undetected by a
# desktop-children UIA search; the exact topology cause is unproven, so discovery here does not depend on
# the UIA tree at all: it enumerates real native windows of the app process and binds them to the main
# window through the Win32 owner chain.
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class WinN {
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lparam);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int count);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr hWnd, StringBuilder text, int count);
  [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
  delegate bool EnumProc(IntPtr hWnd, IntPtr lparam);
  public const uint WM_CLOSE = 0x0010;
  public static string Esc(string s) {
    var b = new StringBuilder();
    foreach (var ch in s) {
      int c = ch;
      if (c < 0x20 || c == 0x7F || char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.Format) b.Append(string.Format("\\u{0:X4}", c));
      else b.Append(ch);
    }
    return b.ToString();
  }
  public static uint PidOf(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return p; }
  public static IntPtr OwnerOf(IntPtr h) { return GetWindow(h, 4); } // GW_OWNER
  public static string TitleOf(IntPtr h) { var sb = new StringBuilder(512); GetWindowTextW(h, sb, sb.Capacity); return sb.ToString(); }
  public static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassNameW(h, sb, sb.Capacity); return sb.ToString(); }
  public static List<IntPtr> TopLevelOfPid(uint pid) {
    var found = new List<IntPtr>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid) found.Add(h); return true; }, IntPtr.Zero);
    return found;
  }
}
'@
$A = [System.Windows.Automation.AutomationElement]; $CT = [System.Windows.Automation.ControlType]; $PC = [System.Windows.Automation.PropertyCondition]; $TS = [System.Windows.Automation.TreeScope]
$results = New-Object System.Collections.ArrayList; $script:shot = 0; $runId = $env:GITHUB_RUN_ID
function Assert($name, $ok, $detail) { [void]$results.Add([ordered]@{ name = $name; ok = [bool]$ok; detail = "$detail" }); "ASSERT $(if ($ok) {'PASS'} else {'FAIL'}) $name :: $detail" }
function Info($name, $detail) { [void]$results.Add([ordered]@{ name = $name; ok = $true; info = $true; detail = "$detail" }); "INFO $name :: $detail" }
function Shot($state) {
  [void][W]::SetCursorPos(5, 5); [void][W]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, 0, 0, 0, 0, 0x27); Start-Sleep -Milliseconds 400
  $script:shot++; $f = "out-images/feat-{0:00}-$state.png" -f $script:shot
  $r = New-Object W+R; [void][W]::GetWindowRect($p.MainWindowHandle, [ref]$r); $w = $r.Rt - $r.L; $h = $r.B - $r.T
  if ($w -le 0 -or $h -le 0) { "no window rect for $state" | Tee-Object -Append diag/errors.txt; return }
  $bmp = New-Object System.Drawing.Bitmap $w, $h; $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size); $bmp.Save("$PWD/$f"); "shot $f ${w}x${h}"
}
function UiaAll($ct) { for ($t = 0; $t -lt 3; $t++) { try { return $win.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $ct))) } catch { if ($t -eq 2) { "UIA read failed after retries: $($_.Exception.Message) | appHasExited=$(if ($p) { $p.HasExited } else { "no-process" }) mainWindowHandle=$(if ($p) { $p.MainWindowHandle } else { "-" })" | Out-File -Append diag/uia-liveness.txt; throw }; Start-Sleep -Milliseconds 400 } } }
function Texts() { @(UiaAll $CT::Text | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) }
function ListLines() { @(UiaAll $CT::ListItem | ForEach-Object { $_.Current.Name } | Where-Object { $_ }) }
function Btn($name) { $win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::Button)), (New-Object $PC($A::NameProperty, $name))))) }
function BtnLike($needle) { @($win.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Button)))) | Where-Object { $_.Current.Name -like "*$needle*" } | Select-Object -First 1 }
function ClickBtn($name, $settle = 900) {
  $b = $null; for ($i = 0; $i -lt 40; $i++) { $b = Btn $name; if ($b -and $b.Current.IsEnabled) { break }; Start-Sleep -Milliseconds 250 }
  if (-not $b) { return "button '$name' not found" }; if (-not $b.Current.IsEnabled) { return "button '$name' stayed disabled" }
  $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds $settle; return 'ok'
}
function WaitText($needle, $secs) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.Elapsed.TotalSeconds -lt $secs) { if (@(Texts) + @(ListLines) | Where-Object { $_ -like "*$needle*" }) { return $true }; Start-Sleep -Milliseconds 250 }; return $false }
function NamedEdit($name) { $win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::Edit)), (New-Object $PC($A::NameProperty, $name))))) }
function NamedCombo($name) { $win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::ComboBox)), (New-Object $PC($A::NameProperty, $name))))) }
function ComboValue($combo) { if ($null -eq $combo) { return '' }; try { $sel = $combo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection(); if ($sel.Count -gt 0) { return $sel[0].Current.Name } } catch { }; return '' }
function ValOf($edit) { if ($null -eq $edit) { return '' }; try { return $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { return '' } }
function InvokeEl($el, $what) { if ($null -eq $el) { Assert "$what found" $false 'element not found'; return $false }; try { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); return $true } catch { Assert "$what invokable" $false "$_"; return $false } }
function ToggleEl($el, $what) { if ($null -eq $el) { Assert "$what found" $false 'element not found'; return $false }; try { $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); return $true } catch { Assert "$what toggleable" $false "$_"; return $false } }
function MenuItemByName($name) { $A::RootElement.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::MenuItem)), (New-Object $PC($A::NameProperty, $name))))) }
function MenuInsideWindow($label) { $m = $A::RootElement.FindFirst($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Menu))); if ($null -eq $m) { Assert "$label menu element found" $false 'no open menu'; return }; $mr = $m.Current.BoundingRectangle; $wr = $win.Current.BoundingRectangle; Assert "$label fully inside the app window" ($mr.Left -ge $wr.Left -and $mr.Right -le $wr.Right -and $mr.Top -ge $wr.Top -and $mr.Bottom -le $wr.Bottom) ("menu=$($mr.Left),$($mr.Top)-$($mr.Right),$($mr.Bottom) window=$($wr.Left),$($wr.Top)-$($wr.Right),$($wr.Bottom)") }
function StoreObj() { if (Test-Path $store) { Get-Content $store -Raw -Encoding UTF8 | ConvertFrom-Json } else { $null } }
function SetText($edit, $text) { if ($null -eq $edit) { Assert 'SetText target present' $false 'edit element not found'; return }; try { $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text) } catch { Assert 'SetText succeeded' $false "$_" }; Start-Sleep -Milliseconds 400 }
function OwnerChainOf($hwnd) { $chain = @(); $h = $hwnd; for ($i = 0; $i -lt 8 -and $h -ne [IntPtr]::Zero; $i++) { $h = [WinN]::OwnerOf($h); if ($h -ne [IntPtr]::Zero) { $chain += ("0x{0:X}" -f $h.ToInt64()) } } return ($chain -join '>') }
function OwnerChainHas($hwnd, $ancestor) { $h = $hwnd; for ($i = 0; $i -lt 8 -and $h -ne [IntPtr]::Zero; $i++) { $h = [WinN]::OwnerOf($h); if ($h -eq $ancestor) { return $true } } return $false }
function PickerEligible($h) {
  # Eligible native picker: a visible top-level window of the app process, not the main window, owned
  # (directly or through its owner chain) by the main app window, and either a native dialog class or
  # carrying the hardcoded picker title. Title alone is NOT required: a queued duplicate must be caught
  # even if its title were to differ unexpectedly.
  if ($h -eq $script:mainHwnd) { return $false }
  if (-not [WinN]::IsWindowVisible($h)) { return $false }
  if (-not (OwnerChainHas $h $script:mainHwnd)) { return $false }
  $cls = [WinN]::ClassOf($h); $title = [WinN]::TitleOf($h)
  return ($cls -eq '#32770' -or $title -like '*העלה תמונה*')
}
function AppWindowDump() {
  # Raw per-window diagnostics for EVERY top-level window of the app process, eligible or not: native
  # HWND/PID/class/owner-chain/visibility/title, the eligibility verdict, and the UIA view (native handle,
  # Name with invisible format marks escaped as \uXXXX, ControlType, WindowPattern IsModal). Logged on
  # success and failure alike so any detection miss - including a visible app-owned window with an
  # unexpected class and title - is classifiable from the artifact.
  $lines = @()
  foreach ($h in [WinN]::TopLevelOfPid([uint32]$p.Id)) {
    $t = [WinN]::TitleOf($h); $c = [WinN]::ClassOf($h); $vis = [WinN]::IsWindowVisible($h); $elig = PickerEligible $h
    $uia = ''; try { $el = [System.Windows.Automation.AutomationElement]::FromHandle($h); $modal = 'n/a'; try { $modal = "$($el.GetCurrentPropertyValue([System.Windows.Automation.WindowPattern]::IsModalProperty))" } catch { }; $uia = ("uiaHandle=0x{0:X} uiaName='{1}' uiaCT={2} uiaModal={3}" -f $el.Current.NativeWindowHandle, [WinN]::Esc($el.Current.Name), $el.Current.ControlType.ProgrammaticName, $modal) } catch { $uia = ("uiaErr='{0}'" -f $_.Exception.Message) }
    $lines += ("hwnd=0x{0:X} pid={1} cls='{2}' ownerChain=[{3}] vis={4} eligible={5} nativeTitle='{6}' {7}" -f $h.ToInt64(), [WinN]::PidOf($h), $c, (OwnerChainOf $h), $vis, $elig, [WinN]::Esc($t), $uia)
  }
  return ($lines -join ' || ')
}
function FindPickers() { @([WinN]::TopLevelOfPid([uint32]$p.Id) | Where-Object { PickerEligible $_ }) }
function SetType($typeName) {
  $combo = NamedCombo 'Item type'
  if (-not $combo) { return 'item type combo not found' }
  $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 400
  $it = $combo.FindFirst($TS::Descendants, (New-Object $PC($A::NameProperty, $typeName)))
  if (-not $it) { $it = $A::RootElement.FindFirst($TS::Descendants, (New-Object $PC($A::NameProperty, $typeName))) }
  if (-not $it) { return "type item '$typeName' not found" }
  $it.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 400; return 'ok'
}
function NotepadCount() { @(Get-Process notepad -ErrorAction SilentlyContinue).Count }
function AnyByName($name) { $e = $win.FindFirst($TS::Descendants, (New-Object $PC($A::NameProperty, $name))); if (-not $e) { $e = $A::RootElement.FindFirst($TS::Descendants, (New-Object $PC($A::NameProperty, $name))) }; $e }
function StoreJson() { if (Test-Path $store) { (Get-Content $store -Raw -Encoding UTF8) -replace '\s+', ' ' } else { '' } }
try {
  $root = Join-Path $env:RUNNER_TEMP 'open-flow'; New-Item -ItemType Directory -Force $root | Out-Null
  Set-Content "$root\ok.txt" 'synthetic'
  $missing = "$root\missing.txt"   # deliberately NOT created: preflight must flag it
  $storeDir = Join-Path $env:LOCALAPPDATA 'StartApp'; if (Test-Path $storeDir) { Remove-Item -Recurse -Force $storeDir }
  $store = Join-Path $storeDir 'store.json'
  $exe = Get-ChildItem -Recurse src/StartApp.App/bin/Release -Filter StartApp.App.exe | Select-Object -First 1
  $p = Start-Process -FilePath $exe.FullName -PassThru
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.Elapsed.TotalSeconds -lt 60) { $p.Refresh(); if ($p.HasExited -or $p.MainWindowHandle -ne 0) { break }; Start-Sleep -Milliseconds 500 }
  Start-Sleep -Seconds 2
  Assert 'app window opened with no store' (-not $p.HasExited -and $p.MainWindowHandle -ne 0) ''
  $win = $A::RootElement.FindFirst($TS::Children, (New-Object $PC($A::ProcessIdProperty, $p.Id)))
  Assert 'automation window found' ($null -ne $win) ''
  $script:mainHwnd = $p.MainWindowHandle
  Info 'main window handle captured for owner-aware dialog discovery' ("mainHwnd=0x{0:X}" -f $script:mainHwnd.ToInt64())
  # 1. overview: create form + create -> details screen opens (spec: אחרי היצירה נפתחים פרטי סביבה)
  Assert 'overview shown at startup' (WaitText 'Create workspace' 10) ((Texts) -join ' | ')
  $wsNameBox = NamedEdit 'Workspace name'; $wsDescBox = NamedEdit 'Workspace description'
  Assert 'create form boxes found' ($null -ne $wsNameBox -and $null -ne $wsDescBox) ''
  SetText $wsNameBox 'E2E ws'; SetText $wsDescBox 'first workspace'
  $c = ClickBtn 'Create workspace'; Assert 'create workspace clicked' ($c -eq 'ok') $c
  Assert 'details screen opened after create' (WaitText 'Created:' 15) ((Texts) -join ' | ')
  Assert 'creation date shown, not unknown' (-not (WaitText 'Created: unknown' 2)) ((Texts) -join ' | ')
  Assert 'empty state message in Hebrew' (WaitText 'עדיין אין אייטמים בסביבה' 10) ((Texts) -join ' | ')
  Assert 'empty state has an add-item button' ($null -ne (Btn 'הוסף אייטם')) ''
  $catCombo = NamedCombo 'Workspace category'
  Assert 'category defaults to כללי' ($null -ne $catCombo -and (ComboValue $catCombo) -eq 'כללי') ($catCombo ? (ComboValue $catCombo) : 'combo not found')
  Shot 'details-after-create-empty'
  # 2. manual adds in the details screen: https link (with instance name), existing file, missing file
  $targetBox = NamedEdit 'Item target'; $itemNameBox = NamedEdit 'Item name'; $descBox = NamedEdit 'Item description'
  Assert 'add form boxes found' ($null -ne $targetBox -and $null -ne $itemNameBox -and $null -ne $descBox) ''
  [void](SetType 'Link'); SetText $targetBox 'https://example.com/'; SetText $itemNameBox 'My link'; $c = ClickBtn 'Add' 600; Assert "add link" ($c -eq 'ok') $c
  Assert 'instance name shown instead of derived name' (WaitText 'My link' 10) ((Texts) -join ' | ')
  Assert 'empty message gone after first add' (-not (WaitText 'עדיין אין אייטמים בסביבה' 2)) ''
  [void](SetType 'File'); SetText $targetBox "$root\ok.txt"; $c = ClickBtn 'Add' 600; Assert "add existing file" ($c -eq 'ok') $c
  [void](SetType 'File'); SetText $targetBox $missing; $c = ClickBtn 'Add' 600; Assert "add missing file" ($c -eq 'ok') $c
  Start-Sleep -Milliseconds 600
  Assert 'three rows listed (targets visible)' ((@(Texts) | Where-Object { $_ -eq 'https://example.com/' -or $_ -like '*ok.txt' -or $_ -like '*missing.txt' }).Count -ge 3) ((Texts) -join ' | ')
  $groups = @($win.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Group))) | ForEach-Object { $_.Current.Name })
  Assert 'items grouped: Sites and Files headers shown (four groups settled Nov 12: sites/folders/files/programs)' (($groups -contains 'Sites') -and ($groups -contains 'Files')) ("groups: $($groups -join ', ')")
  Assert 'details category combo still shows כללי after adds' ((ComboValue (NamedCombo 'Workspace category')) -eq 'כללי') (ComboValue (NamedCombo 'Workspace category'))
  Shot 'details-items-added'
  # 3. duplicate add: warn, cancel keeps one instance, confirm adds an independent second instance
  [void](SetType 'Link'); SetText $targetBox 'https://example.com/'; $c = ClickBtn 'Add' 600; Assert "re-add link clicked" ($c -eq 'ok') $c
  Assert 'duplicate warning shown, nothing added yet' (WaitText 'already in the workspace' 10) ((Texts) -join ' | ')
  Shot 'duplicate-warning'
  $c = ClickBtn 'Cancel' 800; Assert 'duplicate cancel clicked' ($c -eq 'ok') $c
  Start-Sleep -Milliseconds 500
  $o = StoreObj; $wsItems = @($o.workspaces[0].items)
  $linkId = ($o.items.PSObject.Properties.Value | Where-Object { $_.target -eq 'https://example.com/' } | Select-Object -First 1).id
  $linkCount = @($wsItems | Where-Object { $_.itemId -eq $linkId }).Count
  Assert 'cancel kept a single instance' ($wsItems.Count -eq 3 -and $linkCount -eq 1) "items=$($wsItems.Count) linkInstances=$linkCount"
  SetText $targetBox 'https://example.com/'; $c = ClickBtn 'Add' 600; Assert "re-add link clicked again" ($c -eq 'ok') $c
  Assert 'duplicate warning shown again' (WaitText 'already in the workspace' 10) ''
  $c = ClickBtn 'Add anyway' 800; Assert 'add anyway clicked' ($c -eq 'ok') $c
  Start-Sleep -Milliseconds 500
  $o = StoreObj; $wsItems = @($o.workspaces[0].items); $linkCount = @($wsItems | Where-Object { $_.itemId -eq $linkId }).Count
  Assert 'confirm added a second independent instance' ($wsItems.Count -eq 4 -and $linkCount -eq 2) "items=$($wsItems.Count) linkInstances=$linkCount"
  Shot 'duplicate-added'
  Start-Sleep -Milliseconds 800
  $json = StoreJson
  $inst = @($o.workspaces[0].items | Where-Object { $_.itemId -eq $linkId })
  Assert 'store.json: two separate instances of the link, instance name persisted' ($inst.Count -eq 2 -and $inst[0].id -ne $inst[1].id -and $inst[0].name -eq 'My link') $json
  Assert 'store.json: workspace description and creation date persisted' ($json -match 'first workspace' -and $json -match 'createdAt') $json
  # 3b. item row: clicking the name opens that one item only (spec שורה של אייטם: לחיצה על השם פותחת)
  $npB = NotepadCount
  $c = ClickBtn 'ok.txt' 2500; Assert 'item name click sent its open request' ($c -eq 'ok' -and (WaitText 'ok.txt: open request sent' 15)) ((Texts) -join ' | ')
  $sw3 = [Diagnostics.Stopwatch]::StartNew(); $npC = $npB; while ($sw3.Elapsed.TotalSeconds -lt 15) { $npC = NotepadCount; if ($npC -gt $npB) { break }; Start-Sleep -Milliseconds 400 }
  Assert 'single-item open really started notepad' ($npC -gt $npB) "before=$npB after=$npC"
  Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  # 3c. item actions menu (no עריכה inside - spec: direct edit button only) and the edit form changes one instance only
  $c = ClickBtn 'Item actions' 900; Assert 'item actions menu opened' ($c -eq 'ok') $c
  $ifound = @('מחיקה','העתק כתובת','עריכת כתובת','פתח באמצעות','הזז למעלה','הזז למטה') | Where-Object { $null -ne (MenuItemByName $_) }
  Assert 'item menu shows the six settled entries' (@($ifound).Count -eq 6) ("found: $($ifound -join ', ')")
  MenuInsideWindow 'first-row item menu'
  Assert 'no עריכה inside the actions menu (direct row button is the only edit path)' ($null -eq (MenuItemByName 'עריכה')) ''
  $shareMi = MenuItemByName 'שיתוף לסביבה אחרת'
  Assert 'שיתוף לסביבה אחרת hidden with a single workspace' ($null -eq $shareMi -or $shareMi.Current.IsOffscreen) ''
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 500
  $c = ClickBtn 'Edit item' 900; Assert 'direct row edit button clicked' ($c -eq 'ok') $c
  $eName = NamedEdit 'Edit item name'; Assert 'item edit form opened' ($null -ne $eName) ''
  Assert 'edit form shows the address too' ($null -ne (NamedEdit 'Edit item target')) ''
  SetText $eName 'My link edited'
  $c = ClickBtn 'Save item edit' 900; Assert 'item edit saved' ($c -eq 'ok') $c
  Assert 'edited instance name shown' (WaitText 'My link edited' 10) ((Texts) -join ' | ')
  Assert 'store.json: that instance renamed, target untouched' ((StoreJson) -match 'My link edited' -and (StoreJson) -match 'example\.com') (StoreJson)
  # 3d. פופאפ עריכת כתובת: full address shown, copy button present, save is instance-local (the second instance keeps its address)
  $c = ClickBtn 'Item actions' 900; Assert 'item actions menu opened for address popup' ($c -eq 'ok') $c
  $mi = AnyByName 'עריכת כתובת'; Assert 'address edit entry exists' ($null -ne $mi) ''
  [void](InvokeEl $mi 'address edit entry'); Start-Sleep -Milliseconds 800
  $addr = NamedEdit 'Full address'; Assert 'address popup opened with the full address' ($null -ne $addr -and $addr.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq 'https://example.com/') ''
  Assert 'copy button inside the popup' ($null -ne (AnyByName 'Copy full address')) ''
  SetText $addr 'https://edited.example/'
  $c = ClickBtn 'שמור' 900; Assert 'address saved from the popup' ($c -eq 'ok' -and (WaitText 'Address saved' 10)) ((Texts) -join ' | ')
  Assert 'store.json: one instance re-pointed, the other keeps example.com' ((StoreJson) -match 'edited\.example' -and (StoreJson) -match 'example\.com') (StoreJson)
  # 3e. פופאפ פתח באמצעות: per-instance הגדר כברירת מחדל, then the open uses the chosen program
  $c = ClickBtn 'Item actions' 900; Assert 'item actions menu opened for open-with' ($c -eq 'ok') $c
  $mi = AnyByName 'פתח באמצעות'; Assert 'open-with entry exists' ($null -ne $mi) ''
  [void](InvokeEl $mi 'open-with entry'); Start-Sleep -Milliseconds 800
  $prog = NamedEdit 'Program path'; Assert 'open-with popup opened' ($null -ne $prog) ''
  SetText $prog 'C:\Windows\notepad.exe'
  $cb = AnyByName 'Set as default'; Assert 'set-as-default checkbox exists' ($null -ne $cb) ''
  [void](ToggleEl $cb 'set-as-default checkbox'); Start-Sleep -Milliseconds 300
  $c = ClickBtn 'Open with chosen program' 900; Assert 'open-with go clicked' ($c -eq 'ok') $c
  Assert 'open-with sent the request' (WaitText 'open request sent' 15) ((Texts) -join ' | ')
  Assert 'store.json: browser choice saved on the instance' ((StoreJson) -match 'notepad\.exe') (StoreJson)
  Assert 'row shows the chosen program' (WaitText 'notepad.exe' 5) ((Texts) -join ' | ')
  Shot 'open-with-saved'
  Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  # 4. details editing: change shows Save; save persists
  $wsNameBox2 = NamedEdit 'Workspace name'
  SetText $wsNameBox2 'E2E ws renamed'
  $saveBtn = $null; for ($i = 0; $i -lt 20; $i++) { $saveBtn = Btn 'Save workspace details'; if ($saveBtn) { break }; Start-Sleep -Milliseconds 250 }
  Assert 'any change shows a Save button' ($null -ne $saveBtn) ''
  [void](InvokeEl $saveBtn 'Save button'); Start-Sleep -Milliseconds 900
  Assert 'saved name persisted to store.json' ((StoreJson) -match 'E2E ws renamed') (StoreJson)
  Shot 'details-after-save'
  # 5. unsaved-changes guard: edit, Back -> dialog, X returns to editing, Back -> exit without saving
  SetText (NamedEdit 'Workspace description') 'unsaved edit'
  $c = ClickBtn 'Back' 800; Assert 'back clicked' ($c -eq 'ok') $c
  Assert 'unsaved dialog shown with both spec buttons' ((WaitText 'You have changes that were not saved' 10) -and $null -ne (Btn 'שמור וצא') -and $null -ne (Btn 'צא בלי לשמור')) ((Texts) -join ' | ')
  Shot 'unsaved-dialog'
  $c = ClickBtn 'Back to editing' 600; Assert 'dialog X clicked' ($c -eq 'ok') $c
  Assert 'X returned to editing, changes kept' ((WaitText 'Created:' 5) -and (ValOf (NamedEdit 'Workspace description')) -eq 'unsaved edit') ''
  $c = ClickBtn 'Back' 800; Assert 'back clicked again' ($c -eq 'ok') $c
  $c = ClickBtn 'צא בלי לשמור' 900; Assert 'exit without saving clicked' ($c -eq 'ok') $c
  Assert 'back in overview, unsaved edit discarded' ((WaitText 'Create workspace' 10) -and -not ((StoreJson) -match 'unsaved edit')) ((Texts) -join ' | ')
  Shot 'overview-after-guard'
  # 6. open flow from details: preflight, cancel, open anyway
  $card = BtnLike 'E2E ws renamed'; Assert 'workspace card found in overview' ($null -ne $card) ((ListLines) -join ' || ')
  [void](InvokeEl $card 'workspace card'); Start-Sleep -Milliseconds 900
  Assert 'details reopened from card' (WaitText 'Created:' 10) ''
  $npBefore = NotepadCount
  $c = ClickBtn 'שגר' 2500; Assert 'open clicked' ($c -eq 'ok') $c
  Assert 'decision panel lists the missing file' (WaitText 'not found' 30) ((ListLines) -join ' || ')
  Assert 'nothing opened yet message' (WaitText 'Nothing has been opened yet' 10) ((Texts) -join ' | ')
  Shot 'decision-panel'
  $c = ClickBtn 'Cancel' 800; Assert 'cancel clicked' ($c -eq 'ok') $c
  Assert 'cancel status' (WaitText 'Open cancelled, nothing was opened' 10) ((Texts) -join ' | ')
  Start-Sleep -Seconds 2
  Assert 'no notepad started after cancel' ((NotepadCount) -eq $npBefore) "before=$npBefore after=$(NotepadCount)"
  $c = ClickBtn 'שגר' 2500; Assert 'open clicked again' ($c -eq 'ok') $c
  Assert 'decision panel again' (WaitText 'not found' 30) ((ListLines) -join ' || ')
  $c = ClickBtn 'Open anyway' 4000; Assert 'open anyway clicked' ($c -eq 'ok') $c
  Assert 'report: ok.txt open request sent' (WaitText 'ok.txt: open request sent' 30) ((ListLines) -join ' || ')
  Assert 'report: link open request sent' (WaitText 'example.com' 10) ((ListLines) -join ' || ')
  Assert 'report: missing file failed honestly' (WaitText 'missing.txt: failed' 10) ((ListLines) -join ' || ')
  Info 'open report' ((ListLines) -join ' || ')
  $npAfter = 0; $sw2 = [Diagnostics.Stopwatch]::StartNew(); while ($sw2.Elapsed.TotalSeconds -lt 20) { $npAfter = NotepadCount; if ($npAfter -gt $npBefore) { break }; Start-Sleep -Milliseconds 500 }
  Assert 'notepad really started for ok.txt' ($npAfter -gt $npBefore) "before=$npBefore after=$npAfter"
  Shot 'after-open-anyway'
  Start-Sleep -Milliseconds 800
  Assert 'last opened recorded after a real open' ((StoreJson) -match 'lastOpenedAt') (StoreJson)
  # 7. empty workspace: open shows the dialog, go-to-details button leads to details
  $c = ClickBtn 'Back' 900; Assert 'back to overview clicked' ($c -eq 'ok') $c
  SetText (NamedEdit 'Workspace name') 'E2E empty'; $c = ClickBtn 'Create workspace' 900; Assert 'create empty workspace clicked' ($c -eq 'ok') $c
  Assert 'empty workspace created into details with the Hebrew empty message' (WaitText 'עדיין אין אייטמים בסביבה' 15) ((Texts) -join ' | ')
  $c = ClickBtn 'שגר' 2500; Assert 'open clicked on empty workspace' ($c -eq 'ok') $c
  Assert 'empty workspace dialog with the spec title' (WaitText 'סביבת עבודה ריקה' 15) ((Texts) -join ' | ')
  Shot 'empty-workspace-dialog'
  $c = ClickBtn 'עבור לפרטי הסביבה' 900; Assert 'go-to-details clicked' ($c -eq 'ok') $c
  Assert 'dialog closed, still in details with the empty message' (-not (WaitText 'This workspace has no items yet' 2) -and (WaitText 'עדיין אין אייטמים בסביבה' 5)) ((Texts) -join ' | ')
  # 8. card actions menu (spec: הצג / שגר / הוסף אייטם / עצב כרטיס / ערוך קטגוריה; workspace deletion was rejected by the user)
  $c = ClickBtn 'Back' 900; Assert 'back to overview for the menu check' ($c -eq 'ok') $c
  Assert 'no workspace deletion offered anywhere' ($null -eq (BtnLike 'Delete') -and $null -eq (BtnLike 'מחק') -and $null -eq (BtnLike 'מחיקה')) ((ListLines) -join ' || ')
  $c = ClickBtn 'Workspace actions' 900; Assert 'card actions menu opened' ($c -eq 'ok') $c
  $found = @('הצג','שגר','הוסף אייטם','עצב כרטיס','ערוך קטגוריה') | Where-Object { $null -ne (MenuItemByName $_) }
  Assert 'menu shows the five spec actions' (@($found).Count -eq 5) ("found: $($found -join ', ')")
  MenuInsideWindow 'card actions menu'
  Shot 'card-actions-menu'
  $show = MenuItemByName 'הצג'
  [void](InvokeEl $show 'הצג menu entry'); Start-Sleep -Milliseconds 900
  Assert 'details opened from the menu (הצג)' (WaitText 'Created:' 10) ''
  # 9. card design popup + category popup (פופאפ עיצוב כרטיס / פופאפ עריכת קטגוריה)
  $c = ClickBtn 'Back' 900; Assert 'back to overview for the popups' ($c -eq 'ok') $c
  $c = ClickBtn 'Workspace actions' 900; Assert 'card menu opened for design' ($c -eq 'ok') $c
  $mi = MenuItemByName 'עצב כרטיס'; [void](InvokeEl $mi 'design menu entry'); Start-Sleep -Milliseconds 900
  Assert 'design popup opened' (WaitText 'תמונת רקע' 10) ((Texts) -join ' | ')
        Shot 'design-popup-open'
  $colorBox = NamedEdit 'Card color'; Assert 'design color box found' ($null -ne $colorBox) ''
  # design preview baseline: persisted design BEFORE any edits (autosave would otherwise fake a green unchanged-check)
  $oD = StoreObj; $wsD0 = $oD.workspaces[0]; $wsDId = $wsD0.id
  # Design fields are nullable and the store OMITS nulls by contract (JsonStore.cs: DefaultIgnoreCondition
  # = WhenWritingNull; Model.cs: string? Color / string? ImagePath), so a fresh workspace's JSON has NO
  # color/imagePath properties at all. Absent therefore means the product-defined default (null), not an
  # unknown state. Presence is recorded as evidence below; the values are read defensively either way.
  Assert 'baseline design object present and carries its identity' ($null -ne $wsD0 -and -not [string]::IsNullOrEmpty($wsDId)) ("props=$(if ($null -ne $wsD0) { ($wsD0.PSObject.Properties.Name) -join ',' } else { 'no-workspace' })")
  $hasColProp = ($null -ne $wsD0) -and ($wsD0.PSObject.Properties.Name -contains 'color')
  $hasImgProp = ($null -ne $wsD0) -and ($wsD0.PSObject.Properties.Name -contains 'imagePath')
  $col0 = if ($hasColProp) { $wsD0.color } else { $null }; $img0 = if ($hasImgProp) { $wsD0.imagePath } else { $null }
  Info 'baseline design property presence (store contract: absent means null)' ("colorProp=$hasColProp imagePathProp=$hasImgProp col0='$col0' img0='$img0'")
  Info 'design persisted state before preview edits' ("ws=$wsDId color='$col0' imagePath='$img0'")
  # Regression (PR19 review): one click on 'Upload card image' must open exactly ONE native picker - the
  # window used to subscribe the VM events twice (DataContextChanged + Loaded), queuing a second
  # OpenFileDialog behind the first. Discovery is native and owner-aware (see WinN/PickerEligible above):
  # the first live run's desktop-children UIA search failed to find the visibly open owned dialog (topology
  # cause unproven), so discovery here does not depend on the UIA tree shape. Detection, close,
  # disappearance and duplicate checks all bind to the same native HWND; diagnostics are logged on success
  # too. Any failure snapshots the screen and throws: the outer catch writes errors/assertions and the
  # finally kills the fixture, so the flow never continues to unrelated pixel asserts behind a modal.
  $upBtn = Btn 'Upload card image'; Assert 'upload image button present in the design popup' ($null -ne $upBtn) ''
  if ($upBtn) {
    $pre = @(FindPickers)
    Info 'eligible app-owned dialogs before upload click' ("count=$($pre.Count) windows: $(AppWindowDump)")
    if ($pre.Count -gt 0) { Assert 'no picker already open before the upload click' $false (AppWindowDump); Shot 'picker-failure'; throw 'a picker dialog was already open before the upload click' }
    # InvokeEl writes Assert's result line to the output stream before returning $false, so a plain
    # assignment captures a 2-element array (truthy) - take the LAST element to get the exact boolean.
    $invoked = @(InvokeEl $upBtn 'upload image button')[-1]
    if (-not $invoked) { Shot 'picker-failure'; throw 'upload image button invocation failed' }
    $dlgHwnd = [IntPtr]::Zero; $extra = 0; $hits = @()
    for ($t = 0; $t -lt 25 -and $dlgHwnd -eq [IntPtr]::Zero; $t++) { Start-Sleep -Milliseconds 200; $hits = @(FindPickers); if ($hits.Count -gt 0) { $dlgHwnd = $hits[0]; $extra = $hits.Count - 1 } }
    $opened = $dlgHwnd -ne [IntPtr]::Zero
    Assert 'native picker opened on upload click' $opened (AppWindowDump)
    if (-not $opened) { Shot 'picker-failure'; throw 'picker open detection failed after one upload click' }
    Info 'picker discovered' ("hwnd=0x{0:X} extraEligible={1} windows: {2}" -f $dlgHwnd.ToInt64(), $extra, (AppWindowDump))
    Assert 'exactly one eligible app-owned picker after one upload click' ($extra -eq 0) (AppWindowDump)
    if ($extra -gt 0) { foreach ($x in $hits) { if ($x -ne $dlgHwnd) { try { [void][WinN]::PostMessage($x, [WinN]::WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) } catch { } } }; Shot 'picker-failure'; throw 'more than one eligible app-owned picker after a single upload click' }
    # Close THAT specific handle with a bounded native PostMessage WM_CLOSE (UIA WindowPattern.Close and
    # SendMessage can both block on modal message processing). Only the disappearance of the same HWND
    # proves the close.
    [void][WinN]::PostMessage($dlgHwnd, [WinN]::WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero)
    $gone = $false; for ($t = 0; $t -lt 15 -and -not $gone; $t++) { Start-Sleep -Milliseconds 200; $gone = -not [WinN]::IsWindow($dlgHwnd) }
    Assert 'first native picker actually closed before the duplicate check' $gone ("hwnd=0x{0:X} stillAlive={1} windows: {2}" -f $dlgHwnd.ToInt64(), ([WinN]::IsWindow($dlgHwnd)), (AppWindowDump))
    if (-not $gone) { Shot 'picker-failure'; throw 'picker HWND still alive after close' }
    $dup = [IntPtr]::Zero; for ($t = 0; $t -lt 15 -and $dup -eq [IntPtr]::Zero; $t++) { Start-Sleep -Milliseconds 200; $again = @(FindPickers); if ($again.Count -gt 0) { $dup = $again[0] } }
    Assert 'no second native picker within 3s after the first closed (single VM subscription)' ($dup -eq [IntPtr]::Zero) (AppWindowDump)
    if ($dup -ne [IntPtr]::Zero) { try { [void][WinN]::PostMessage($dup, [WinN]::WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) } catch { }; Shot 'picker-failure'; throw 'duplicate picker queued behind the first (double VM subscription regression)' }
    # 'unchanged image path' proof without ValOf's silent '': the editor must EXIST, its ValuePattern read
    # must SUCCEED, and the value is captured once. A fresh workspace persists imagePath as JSON null while
    # the edit Value is '' - interpolation normalizes that to the same 'no image' state without masking a
    # missing editor or failed read, which would otherwise pass a bare '' -eq '' comparison.
    $imgEdit = NamedEdit 'Card image path'
    Assert 'card image path editor present after the cancelled picker' ($null -ne $imgEdit) ''
    if ($null -eq $imgEdit) { Shot 'picker-failure'; throw 'card image path editor not found after the cancelled picker' }
    $imgRead = $false; $imgNow = ''
    try { $imgNow = $imgEdit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value; $imgRead = $true } catch { }
    Assert 'card image path value readable after the cancelled picker' $imgRead ''
    if (-not $imgRead) { Shot 'picker-failure'; throw 'card image path ValuePattern read failed after the cancelled picker' }
    Assert 'image path unchanged after the cancelled picker' (("$imgNow") -eq ("$img0")) ("box='$imgNow' img0='$img0'")
  }
  if ($colorBox) { SetText $colorBox '#BFDBFE' }
  # 9a. design preview reflects UNSAVED values (preview border bound to DesignColor/DesignImagePath via the same converters as the card; save only persists them)
  $prevEl = AnyByName 'Card design preview'; Assert 'design preview element present' ($null -ne $prevEl) ''
  if ($prevEl) {
    $gDpi = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero); $dpiX = $gDpi.DpiX; $dpiY = $gDpi.DpiY; $gDpi.Dispose()
    $scaleX = $dpiX / 96.0; $scaleY = $dpiY / 96.0
    $oD = StoreObj; $wsD = @($oD.workspaces | Where-Object { $_.id -eq $wsDId })[0]
    Assert 'persisted design unchanged after typing the color (preview is unsaved)' ($wsD.color -eq $col0 -and $wsD.imagePath -eq $img0) ("color='$($wsD.color)' imagePath='$($wsD.imagePath)'")
    $pr = $null; $okPix = $false; $seen = ''
    for ($i = 0; $i -lt 20 -and -not $okPix; $i++) {
      $prevEl = AnyByName 'Card design preview'; if ($prevEl) { $pr = $prevEl.Current.BoundingRectangle }
      if ($pr -and $pr.Width -gt 0 -and $pr.Height -gt 0 -and -not $prevEl.Current.IsOffscreen) {
        $bmp = New-Object System.Drawing.Bitmap 1, 1; $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen([int]($pr.Left + $pr.Width / 2), [int]($pr.Top + $pr.Height / 2), 0, 0, $bmp.Size)
        $px = $bmp.GetPixel(0, 0); $g.Dispose(); $bmp.Dispose()
        $seen = "rgb($($px.R),$($px.G),$($px.B))"
        $okPix = ([math]::Abs($px.R - 191) -le 8 -and [math]::Abs($px.G - 219) -le 8 -and [math]::Abs($px.B - 254) -le 8)
      }
      if (-not $okPix) { Start-Sleep -Milliseconds 300 }
    }
    Assert 'preview shows the UNSAVED color before save (fresh element rect each poll)' $okPix ("center=$seen expected~rgb(191,219,254) rect=$($pr.Width)x$($pr.Height)")
    $imgPath = "$env:RUNNER_TEMP\preview-probe.png"
    $tb = New-Object System.Drawing.Bitmap 32, 32; $tg = [System.Drawing.Graphics]::FromImage($tb); $tg.Clear([System.Drawing.Color]::FromArgb(220, 30, 30)); $tg.Dispose(); $tb.Save($imgPath, [System.Drawing.Imaging.ImageFormat]::Png); $tb.Dispose()
    SetText (NamedEdit 'Card image path') $imgPath
    $oD = StoreObj; $wsD = @($oD.workspaces | Where-Object { $_.id -eq $wsDId })[0]
    Assert 'persisted design unchanged after typing the probe image path' ($wsD.color -eq $col0 -and $wsD.imagePath -eq $img0) ("color='$($wsD.color)' imagePath='$($wsD.imagePath)'")
    $isFrame = { param($px) ([math]::Abs($px.R - 191) -le 12 -and [math]::Abs($px.G - 219) -le 12 -and [math]::Abs($px.B - 254) -le 12) }
    $isImg = { param($px) ([math]::Abs($px.R - 220) -le 12 -and $px.G -le 60 -and $px.B -le 60) }
    $pw = [int]$pr.Width; $ph = [int]$pr.Height
    $okImg = $false; $topBand = 0; $botBand = 0; $leftBand = 0; $rightBand = 0; $seenC = ''; $belowTop = ''; $rightOfLeft = ''
    for ($i = 0; $i -lt 20 -and -not $okImg; $i++) {
      $prevEl = AnyByName 'Card design preview'; if ($prevEl) { $pr = $prevEl.Current.BoundingRectangle; $pw = [int]$pr.Width; $ph = [int]$pr.Height }
      if ($pw -le 0 -or $ph -le 0) { Start-Sleep -Milliseconds 300; continue }
      $crop = New-Object System.Drawing.Bitmap $pw, $ph; $cg = [System.Drawing.Graphics]::FromImage($crop); $cg.CopyFromScreen([int]$pr.Left, [int]$pr.Top, 0, 0, $crop.Size); $cg.Dispose()
      $colX = [int]($pw / 2); $rowY = [int]($ph / 2)
      $pxc = $crop.GetPixel($colX, $rowY); $seenC = "rgb($($pxc.R),$($pxc.G),$($pxc.B))"
      $okImg = (& $isImg $pxc)
      if ($okImg) {
        $crop.Save("$PWD/out-images/feat-preview-frame-crop.png")
        $topBand = 0; for ($y = 0; $y -lt $ph; $y++) { if (& $isFrame ($crop.GetPixel($colX, $y))) { $topBand++ } else { break } }
        $botBand = 0; for ($y = $ph - 1; $y -ge 0; $y--) { if (& $isFrame ($crop.GetPixel($colX, $y))) { $botBand++ } else { break } }
        $leftBand = 0; for ($x = 0; $x -lt $pw; $x++) { if (& $isFrame ($crop.GetPixel($x, $rowY))) { $leftBand++ } else { break } }
        $rightBand = 0; for ($x = $pw - 1; $x -ge 0; $x--) { if (& $isFrame ($crop.GetPixel($x, $rowY))) { $rightBand++ } else { break } }
        $p1 = $crop.GetPixel($colX, [math]::Min($ph - 1, $topBand + 1)); $belowTop = "rgb($($p1.R),$($p1.G),$($p1.B))"
        $p2 = $crop.GetPixel([math]::Min($pw - 1, $leftBand + 1), $rowY); $rightOfLeft = "rgb($($p2.R),$($p2.G),$($p2.B))"
        # capture the bottom-edge pixels INSIDE the crop lifetime (it is disposed at loop end);
        # System.Drawing.Color is a value type, so the stored structs stay usable after Dispose.
        # PowerShell parses a bare trailing arithmetic expression in a multi-element comma list as
        # applying to the whole array (Object[] op_Subtraction runtime error, gate run 43461297010);
        # precompute the columns as scalars instead - do NOT inline arithmetic here.
        $bc1 = 20; $bc2 = [int]($pw / 4); $bc3 = [int]($pw / 2); $bc4 = [int](3 * $pw / 4); $bc5 = $pw - 21
        $botCols = @($bc1, $bc2, $bc3, $bc4, $bc5)
        $botLast = @(); $botSec = @()
        foreach ($cx in $botCols) { $botLast += ,$crop.GetPixel($cx, $ph - 1); $botSec += ,$crop.GetPixel($cx, $ph - 2) }
        $okImg = (& $isImg $p1) -and (& $isImg $p2)
        if (-not $okImg) { $seenC += " transitions-not-red-yet(belowTop=$belowTop rightOfLeft=$rightOfLeft)" }
      }
      $crop.Dispose()
      if (-not $okImg) { Start-Sleep -Milliseconds 300 }
    }
    Assert 'preview shows the UNSAVED image before save, frame-to-image transitions verified red' $okImg ("center=$seenC")
    $expTop = 8 * $scaleY; $expSide = [math]::Max(1, [math]::Round($scaleX))
    # Bottom-edge VISIBLE SIGNATURE, bounded to the observed red-image scenario of the controlled
    # same-layout comparison (frame-oracle-diagnostic, run 43458843646: 32 cases, A=1,8,1,1 vs
    # B=1,8,1,0, red image/solid controls, sampled origins, 96 DPI). WITH the bottom frame the
    # last interior row was frame-tinted (observed B 199..255, R 191..223, B-R -11..48) over
    # red-dominant content (observed second-to-last R-B 79..195); WITHOUT it the last row was
    # red or red-white (observed B<=142); an unpainted bottom is white (R=255, rejected).
    # Scope limits: this is a visible-edge signature in the red-image scenario, NOT a 1-DIP
    # thickness proof and NOT border-ownership proof - a synthetic one-pixel blue strip over red
    # content would pass it too - and it is calibrated to the observed sampled origins at 96 DPI,
    # not to arbitrary DPI. The frame==background-color ambiguity (no-image case) is out of
    # scope: the frame only exists when an image exists.
    $botOk = ($null -ne $botLast -and $botLast.Count -eq 5); $botDetail = @()
    if ($null -ne $botLast -and $botLast.Count -eq 5) {
      for ($i = 0; $i -lt 5; $i++) {
        $lc = $botLast[$i]; $sc = $botSec[$i]
        $lastTinted = ($lc.B -ge 180 -and $lc.R -le 240 -and ($lc.B - $lc.R) -ge -30)
        $secondRed = (($sc.R - $sc.B) -ge 60)
        if (-not ($lastTinted -and $secondRed)) { $botOk = $false }
        $botDetail += "c$($botCols[$i]):last=rgb($($lc.R),$($lc.G),$($lc.B)),2nd=rgb($($sc.R),$($sc.G),$($sc.B))"
      }
    }
    $bandsOk = ($topBand -ge ($expTop - 2) -and $topBand -le ($expTop + 3) -and $leftBand -ge 1 -and $leftBand -le ($expSide + 2) -and $rightBand -ge 1 -and $rightBand -le ($expSide + 2) -and $botOk -and $topBand -ge ($leftBand + [math]::Round(4 * $scaleY)))
    Assert 'frame bands match FrameThickness 1,8,1,1 scaled by DPI: thick top, thin sides, bottom edge shows the frame-tinted-over-red visible signature (bounded to the observed red-image scenario, 96 DPI), top clearly thickest' $bandsOk ("measured px: top=$topBand left=$leftBand right=$rightBand bottomSolid=$botBand | $($botDetail -join ' ') | dpiX=$dpiX dpiY=$dpiY expected top~$([math]::Round($expTop,1)) side~$expSide | crop=out-images/feat-preview-frame-crop.png ${pw}x${ph}")
    Shot 'design-preview-unsaved'
    $missingPath = "$env:RUNNER_TEMP\missing-preview-probe.png"
    if (Test-Path $missingPath) { Remove-Item $missingPath -Force }
    Assert 'missing preview probe file is actually absent' (-not (Test-Path $missingPath)) $missingPath
    SetText (NamedEdit 'Card image path') $missingPath
    $oD = StoreObj; $wsD = @($oD.workspaces | Where-Object { $_.id -eq $wsDId })[0]
    Assert 'persisted design unchanged after typing the missing image path' ($wsD.color -eq $col0 -and $wsD.imagePath -eq $img0) ("color='$($wsD.color)' imagePath='$($wsD.imagePath)'")
    $okBack = $false; $seenBack = ''
    for ($i = 0; $i -lt 20 -and -not $okBack; $i++) {
      $bmp = New-Object System.Drawing.Bitmap 1, 1; $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen([int]($pr.Left + $pr.Width / 2), [int]($pr.Top + $pr.Height / 2), 0, 0, $bmp.Size)
      $px = $bmp.GetPixel(0, 0); $g.Dispose(); $bmp.Dispose()
      $seenBack = "rgb($($px.R),$($px.G),$($px.B))"
      $okBack = ([math]::Abs($px.R - 191) -le 8 -and [math]::Abs($px.G - 219) -le 8 -and [math]::Abs($px.B - 254) -le 8)
      if (-not $okBack) { Start-Sleep -Milliseconds 300 }
    }
    Assert 'preview falls back to the color for a missing image path, before save' $okBack ("center=$seenBack expected~rgb(191,219,254)")
  }
  $c = ClickBtn 'Save card design' 1500; Assert 'design saved from the popup' ($c -eq 'ok') $c
  Assert 'design popup closed after save' (-not (WaitText 'תמונת רקע' 2)) ''
  Assert 'design persisted to the store' ((StoreJson) -like '*#BFDBFE*') (StoreJson)
  $oD = StoreObj; $wsD = @($oD.workspaces | Where-Object { $_.id -eq $wsDId })[0]
  Assert 'after save: persisted design is exactly the new color + missing image path' ($wsD.color -eq '#BFDBFE' -and $wsD.imagePath -eq "$env:RUNNER_TEMP\missing-preview-probe.png") ("color='$($wsD.color)' imagePath='$($wsD.imagePath)'")
  Shot 'card-design-saved'
  # missing/moved source image: the path is a reference, so a missing file must fall back to the color without breaking the card
  $c = ClickBtn 'Workspace actions' 900; Assert 'card menu opened for missing-image check' ($c -eq 'ok') $c
  $mi = MenuItemByName 'עצב כרטיס'; [void](InvokeEl $mi 'design menu entry'); Start-Sleep -Milliseconds 900
  Assert 'design popup reopened with the saved color' ((ValOf (NamedEdit 'Card color')) -eq '#BFDBFE') ''
  SetText (NamedEdit 'Card image path') "$root\no-such-image.png"
  $c = ClickBtn 'Save card design' 1500; Assert 'design with a missing image path saved' ($c -eq 'ok') $c
  Assert 'app survived a missing image path' (-not $p.HasExited) ''
  Assert 'missing image path persisted as a reference' ((StoreJson) -like '*no-such-image.png*') (StoreJson)
  Shot 'card-with-missing-image'
  # 9b. native picker SUCCESSFUL acceptance + saved-card image/frame + remove-image + explicit no-image background.
  # Closes the audit-flagged proof gaps on the same bounded design flow: until here every run CANCELLED the picker
  # and the only real image reached the preview through a TYPED path; the saved card never carried an uploaded
  # image. This block accepts a real temp PNG through the native picker (MainWindow.xaml.cs OnPickImageRequested),
  # saves it, measures the SAVED card's frame against the agreed rule (with image: the color is a frame - thick
  # top, thin other sides INCLUDING the bottom; without image: the color is the background), then removes the
  # image and proves the explicit no-image background. v3 addresses authoritative CR 5cea20f9 closeout:
  # bottom narrowness enforced vs top/sides; tint is a DECLARED frame-blend predicate (channel ordering + blue
  # dominance of #BFDBFE, blends toward white/image only) applied at every anchor; picker filename identity is
  # verified unique (named combo / AutomationId 1148 / LabeledBy) with count==1 + enabled + visible + identity
  # logged; Open likewise unique+enabled+visible; the Open invoke is WITHHELD unless the filename readback
  # verified (asserts record AND gate, never just log, before the irreversible click); failure cleanup closes
  # every owned picker HWND (PID-scoped FindPickers only - never unrelated windows) before throwing; an invalid
  # crop rect throws before CopyFromScreen; the no-image poll re-acquires and re-validates the SAME card every
  # iteration; all five candidate columns are measured and reported, no first-winner short-circuit.
  $probeImg = "$env:RUNNER_TEMP\picker-accept-probe.png"
  $pb = New-Object System.Drawing.Bitmap 48, 48; $pg = [System.Drawing.Graphics]::FromImage($pb); $pg.Clear([System.Drawing.Color]::FromArgb(30, 60, 220)); $pg.Dispose(); $pb.Save($probeImg, [System.Drawing.Imaging.ImageFormat]::Png); $pb.Dispose()
  Assert 'picker-accept probe image written to disk' ((Test-Path $probeImg) -and ((Get-Item $probeImg).Length -gt 0)) $probeImg
  # explicit present + read-success edit readback (the serializer omits null, so a fail-open '' masks failures)
  function Read-EditValue { param($edit, [ref]$val)
    $val.Value = ''
    if ($null -eq $edit) { return $false }
    try { $val.Value = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value; return $true } catch { return $false }
  }
  # failure cleanup: close every picker HWND owned by THIS app process (FindPickers is PID-scoped) and wait for
  # each to disappear; never touches unrelated windows.
  function Close-OwnedPickers { param([string]$why)
    $owned = @(FindPickers)
    foreach ($h in $owned) { try { [void][WinN]::PostMessage($h, [WinN]::WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) } catch { } }
    foreach ($h in $owned) { for ($t = 0; $t -lt 15 -and [WinN]::IsWindow($h); $t++) { Start-Sleep -Milliseconds 200 } }
    $leftOpen = @(FindPickers).Count
    Info 'owned picker cleanup' ("$why; closed=$($owned.Count) remaining=$leftOpen")
    Assert 'owned picker cleanup left no picker open' ($leftOpen -eq 0) ("$why; remaining=$leftOpen windows: $(AppWindowDump)")
    return $leftOpen
  }
  # v5diag-r2: diagnostic-only OBSERVATION helpers after CR. They never drive input, never throw into the flow
  # (every read best-effort with its own try), are budgeted in every loop, and stay inside the OWNED dialog
  # subtree - no desktop/shell/content crawl. Escaping uses the existing WinN.Esc (control + bidi format marks).
  Add-Type -TypeDefinition @'
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class DlgEnum {
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lparam);
  delegate bool EnumProc(IntPtr h, IntPtr lp);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder text, int count);
  [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr h);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  static string EscS(string s) { return s.Replace("|", "/").Replace("\r", " ").Replace("\n", " "); }
  public static string Dump(IntPtr root, int cap) {
    var rows = new List<string>();
    bool hitCap = false;
    EnumProc cb = delegate(IntPtr h, IntPtr lp) {
      if (rows.Count >= cap) { hitCap = true; return false; }
      var sb = new StringBuilder(256); GetClassNameW(h, sb, 256);
      uint pid; GetWindowThreadProcessId(h, out pid);
      rows.Add(string.Format("hwnd=0x{0:X} parent=0x{1:X} ctrlId={2} cls='{3}' pid={4} vis={5} en={6}", h.ToInt64(), GetAncestor(h, 1).ToInt64(), GetDlgCtrlID(h), EscS(sb.ToString()), pid, IsWindowVisible(h), IsWindowEnabled(h)));
      return true;
    };
    bool enumRet = EnumChildWindows(root, cb, IntPtr.Zero);
    return "native-children=" + rows.Count + " cap=" + cap + " truncated=" + hitCap + " enumCompleted=" + (enumRet && !hitCap) + " | " + string.Join(" || ", rows);
  }
}
'@
  Add-Type -TypeDefinition @'
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class DlgDrive {
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lparam);
  delegate bool EnumProc(IntPtr h, IntPtr lp);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder text, int count);
  [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr h);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr h);
  [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr SendMessageTimeoutW(IntPtr h, uint msg, IntPtr w, string l, uint flags, uint timeout, out IntPtr result);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr SendMessageTimeoutW(IntPtr h, uint msg, IntPtr w, StringBuilder l, uint flags, uint timeout, out IntPtr result);
  [DllImport("user32.dll", EntryPoint="SendMessageTimeoutW")] static extern IntPtr SendMessageTimeoutPtr(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
  const uint WM_SETTEXT = 0x000C, WM_GETTEXT = 0x000D, WM_GETTEXTLENGTH = 0x000E, BM_CLICK = 0x00F5;
  const uint SMTO = 0x0002 | 0x0001; // SMTO_BLOCK | SMTO_ABORTIFHUNG - bounded; never NOTIMEOUTIFNOTHUNG, never HWND_BROADCAST
  const uint TMS = 2000;
  static string EscS(string s) { if (s == null) return ""; return s.Replace("\\", "\\\\").Replace("|", "\\p").Replace(";;", ";."); }
  public static string Rows(IntPtr root, int cap) {
    var rows = new List<string>(); bool hitCap = false;
    EnumProc cb = delegate(IntPtr h, IntPtr lp) {
      if (rows.Count >= cap) { hitCap = true; return false; }
      var sb = new StringBuilder(256); GetClassNameW(h, sb, 256);
      uint pid; GetWindowThreadProcessId(h, out pid);
      rows.Add(h.ToInt64() + "|" + GetAncestor(h, 1).ToInt64() + "|" + GetDlgCtrlID(h) + "|" + EscS(sb.ToString()) + "|" + pid + "|" + IsWindowVisible(h) + "|" + IsWindowEnabled(h));
      return true;
    };
    EnumChildWindows(root, cb, IntPtr.Zero);
    return "truncated=" + hitCap + " count=" + rows.Count + " ;; " + string.Join(" ;; ", rows.ToArray());
  }
  public static IntPtr Foreground() { return GetForegroundWindow(); }
  public static bool Alive(IntPtr h) { return IsWindow(h); }
  public static bool VisEn(IntPtr h) { return IsWindowVisible(h) && IsWindowEnabled(h); }
  public static uint PidOf(IntPtr h) { uint pid; GetWindowThreadProcessId(h, out pid); return pid; }
  public static bool TrySetText(IntPtr h, string text, out long lresult) {
    IntPtr r; IntPtr ok = SendMessageTimeoutW(h, WM_SETTEXT, IntPtr.Zero, text, SMTO, TMS, out r);
    lresult = r.ToInt64(); return ok != IntPtr.Zero;
  }
  public static bool TryGetText(IntPtr h, out string value, out long declaredLen, out long copied) {
    value = null; declaredLen = -1; copied = -1;
    IntPtr r1; if (SendMessageTimeoutPtr(h, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero, SMTO, TMS, out r1) == IntPtr.Zero) return false;
    declaredLen = r1.ToInt64(); if (declaredLen < 0 || declaredLen > 32768) return false;
    var sb = new StringBuilder((int)declaredLen + 2);
    IntPtr r2; if (SendMessageTimeoutW(h, WM_GETTEXT, new IntPtr(sb.Capacity), sb, SMTO, TMS, out r2) == IntPtr.Zero) return false;
    copied = r2.ToInt64(); value = sb.ToString(); return true;
  }
  public static bool TryClick(IntPtr h, out long lresult) {
    IntPtr r; IntPtr ok = SendMessageTimeoutPtr(h, BM_CLICK, IntPtr.Zero, IntPtr.Zero, SMTO, TMS, out r);
    lresult = r.ToInt64(); return ok != IntPtr.Zero;
  }
}
'@
  # NATIVEID-FN-BEGIN
  # Pure native identity-selection and gate logic, exercised verbatim by the local fixture probes (extracted
  # between the NATIVEID-FN markers and fed fabricated row sets built from the observed 50-child native dump of
  # run 43525189834). Rows carry hwnd/parent as Int64, ctrlId Int32, cls String, pidN UInt32, vis/en Boolean.
  # Everything here is fail-closed: the caller requires exact counts and treats every other count, every
  # truncation, and every exception as a conflict; nothing ever picks a first match.
  function Select-NativeFilenameEdit { param($rows, $appPid, $dlgHwnd)
    # Observed filename chain: dialog -> ComboBoxEx32(ctrlId 1148) -> ComboBox(ctrlId 1148) -> Edit(ctrlId 1148),
    # direct parent links, ALL chain members same PID + visible + enabled. The address band is a SEPARATE chain
    # (ctrlId 41477, invisible) and must never match. Candidates carry the WHOLE chain identity (edit+combo+
    # comboex handles) so revalidation detects parent-handle changes, not just leaf changes. Every OTHER visible
    # Edit-class window - regardless of ctrlId, including a competing malformed ctrlId-1148 Edit - is reported
    # in otherVisible for the conflict gate.
    $edits = @($rows | Where-Object { $_.cls -eq 'Edit' })
    $byHwnd = @{}; foreach ($r in $rows) { $byHwnd["$($r.hwnd)"] = $r }
    $chain = @()
    foreach ($e in $edits) {
      if ($e.ctrlId -ne 1148 -or -not $e.vis -or -not $e.en -or $e.pidN -ne $appPid) { continue }
      $p = $byHwnd["$($e.parent)"]
      if ($null -eq $p -or $p.ctrlId -ne 1148 -or $p.cls -ne 'ComboBox' -or $p.pidN -ne $appPid -or -not $p.vis -or -not $p.en) { continue }
      $g = $byHwnd["$($p.parent)"]
      if ($null -eq $g -or $g.ctrlId -ne 1148 -or $g.cls -ne 'ComboBoxEx32' -or $g.pidN -ne $appPid -or -not $g.vis -or -not $g.en) { continue }
      if ($g.parent -ne $dlgHwnd) { continue }
      $chain += [pscustomobject]@{ edit = $e; combo = $p; comboex = $g }
    }
    $selHwnd = [long]-1; if ($chain.Count -eq 1) { $selHwnd = [long]$chain[0].edit.hwnd }
    $otherVisible = @($edits | Where-Object { $_.vis -and [long]$_.hwnd -ne $selHwnd })
    return [pscustomobject]@{ candidates = $chain; editsTotal = $edits.Count; otherVisible = $otherVisible; otherVisibleCount = $otherVisible.Count }
  }
  function ConvertTo-TitleLog { param($t)
    # Diagnostic rendering ONLY (matching uses the raw value): backslash/pipe/control/bidi-format marks
    # neutralized so the attempt record stays parseable; len + codepoints keep analytic truth.
    if ($null -eq $t) { return '<null>' }
    return ($t.Replace('\', '\\') -replace '[\x00-\x1F\u202A-\u202E\u2066-\u2069]', '.' -replace '\|', '!')
  }
  function Select-NativeOpenButton { param($rows, $appPid, $dlgHwnd, $titleOf)
    # DIRECT root child Button ctrlId 1 (Open), same PID, visible+enabled. id1Total counts ALL matching direct
    # children BEFORE title corroboration: the caller requires id1Total -eq 1 AND exactly one title-corroborated
    # candidate, so an ID collision is never silently discarded by the title filter. Never ID 2 (Cancel), 1136
    # (file-type filter), or anything that is not a direct child. $titleOf returns a RECORD
    # @{ ok=transport success; value=raw text; declared=WM_GETTEXTLENGTH; copied=WM_GETTEXT result; err=message }
    # from the bounded SendMessageTimeoutW WM_GETTEXTLENGTH+WM_GETTEXT read (SMTO_BLOCK|ABORTIFHUNG, 2000ms) -
    # the documented cross-process control-text mechanism. Microsoft documents that GetWindowTextW cannot
    # retrieve another application's control text, which MAY explain the r3 miss; the actual r3 return value
    # was discarded and remains UNOBSERVED, so no cause is asserted here.
    # ACCEPT requires a VALID structured read, not just transport-ok: ok, non-null/non-empty value,
    # 0<=declared<=32768, 0<=copied<=32768, declared==copied==value.Length, THEN the caption check below.
    # CAPTION CHECK: exact raw equality against the ACCEPTED CAPTION LIST ONLY: 'Open', 'פתיחה', and the
    # OBSERVED variant '&Open' (run 43531688040 id1Titles: raw '&Open' len=5 declared=5 copied=5
    # cp=[0026 004F 0070 0065 006E] read-ok - Win32 accelerator marker on 'O', UIA displays 'Open'). This is an
    # explicit observed-variant acceptance, NOT a general accelerator parser: there is no stripping, trimming,
    # bidi/nonprinting removal, case change, or any other normalization, so '&&Open', 'Open&', 'O&pen',
    # whitespace variants, bidi/nonprinting-marked variants, 'Cancel' and '&Cancel' all REFUSE. The matched
    # variant is recorded on the candidate and in id1Titles (variant='...') alongside the raw caption.
    # Comparison is OrdinalIgnoreCase: it preserves the inherited bounded case-insensitive choice, while
    # PowerShell's culture-aware -eq would IGNORE bidi/nonprinting format marks (e.g. 'Open'+U+202E would
    # falsely equal 'Open') - ordinal comparison refuses them by exact codepoints.
    # id1Titles logs raw
    # escaped title + all length fields + codepoints (short titles) + err + the validity verdict for EVERY
    # direct-child ID1 button BEFORE matching, including failed and partial reads, so every refusal (initial
    # selection and, via the caller's rv evidence, every revalidation) is classifiable from the diagnostics.
    $btns = @($rows | Where-Object { $_.cls -eq 'Button' -and $_.ctrlId -eq 1 -and $_.parent -eq $dlgHwnd -and $_.vis -and $_.en -and $_.pidN -eq $appPid })
    $titled = @(); $titles = @()
    foreach ($b in $btns) {
      $tr = & $titleOf $b.hwnd
      $disp = if ($null -ne $tr.value) { ConvertTo-TitleLog $tr.value } else { '<null>' }
      $lenInfo = "len=$(if ($null -ne $tr.value) { $tr.value.Length } else { -1 }) declared=$($tr.declared) copied=$($tr.copied)"
      $errS = "$($tr.err)"; $errInfo = if ($errS -ne '' -and $errS -ne '0') { " err='$(ConvertTo-TitleLog $errS)'" } else { '' }
      $cps = ''; if ($null -ne $tr.value -and $tr.value.Length -le 16) { $cps = ' cp=[' + (($tr.value.ToCharArray() | ForEach-Object { '{0:X4}' -f [int]$_ }) -join ' ') + ']' }
      $why = $null
      if (-not $tr.ok) { $why = 'transport-failed' }
      elseif ($null -eq $tr.value -or $tr.value.Length -eq 0) { $why = 'null-or-empty' }
      elseif ($tr.declared -lt 0 -or $tr.declared -gt 32768 -or $tr.copied -lt 0 -or $tr.copied -gt 32768) { $why = 'bounds' }
      elseif ($tr.declared -ne $tr.copied -or $tr.declared -ne $tr.value.Length) { $why = 'length-mismatch' }
      $verdict = if ($null -eq $why) { 'read-ok' } else { "read-invalid:$why" }
      $variant = $null
      if ($null -eq $why) { foreach ($v in @('Open', 'פתיחה', '&Open')) { if ([string]::Equals($tr.value, $v, [System.StringComparison]::OrdinalIgnoreCase)) { $variant = $v; break } } }
      $varInfo = if ($null -ne $variant) { " variant='$variant'" } else { '' }
      $titles += ('0x{0:X}' -f [int64]$b.hwnd) + "='$disp' $lenInfo$errInfo$cps $verdict$varInfo"
      if ($null -ne $variant) { $titled += [pscustomobject]@{ row = $b; title = $tr.value; variant = $variant } }
    }
    return [pscustomobject]@{ candidates = $titled; buttonsTotal = @($rows | Where-Object { $_.cls -eq 'Button' }).Count; id1Total = $btns.Count; id1Titles = $titles }
  }
  function Test-RootUnique { param($alive, $hitsCount, $hit0IsDlg, $rootVisEn, $rootPidMatch, $rootClassOk, $rootClass)
    # Root freshness + uniqueness required immediately before each mutation: FindPickers membership alone is not
    # uniqueness (the dialog plus a second owned picker would pass a -contains check), and PickerEligible accepts
    # (class #32770 OR the picker title), so the exact native dialog class #32770 is re-required fresh here.
    if (-not $alive) { return 'dialog gone' }
    if ($hitsCount -ne 1 -or -not $hit0IsDlg) { return "owned picker not unique: hits=$hitsCount" }
    if (-not $rootClassOk) { return "dialog root class drifted: '$rootClass' (expected #32770)" }
    if (-not $rootVisEn) { return 'dialog root not visible/enabled' }
    if (-not $rootPidMatch) { return 'dialog root PID changed' }
    return 'ok'
  }
  function Test-NativeIdentityUnchanged { param($editSel, $openSel, $baseEdit, $baseCombo, $baseComboex, $baseOpen)
    # Revalidation compares the ENTIRE chain identity (edit + combo + comboex handles) and the Open handle,
    # not only the leaf; counts must still be exactly one and no other visible Edit may be present.
    if ($editSel.candidates.Count -ne 1 -or $openSel.id1Total -ne 1 -or $openSel.candidates.Count -ne 1) { return "identity no longer unique: edit=$($editSel.candidates.Count) id1=$($openSel.id1Total) titled=$($openSel.candidates.Count)" }
    if ($editSel.otherVisibleCount -gt 0) { return "other visible Edit present: $($editSel.otherVisibleCount)" }
    $c = $editSel.candidates[0]
    if ([long]$c.edit.hwnd -ne [long]$baseEdit -or [long]$c.combo.hwnd -ne [long]$baseCombo -or [long]$c.comboex.hwnd -ne [long]$baseComboex) { return 'filename chain handles changed' }
    if ([long]$openSel.candidates[0].row.hwnd -ne [long]$baseOpen) { return 'Open handle changed' }
    return 'ok'
  }
  function Test-NativeReadback { param($transport, $value, $declaredLen, $copied, $probe)
    # Transport success is distinct from a valid empty; empty, truncated, or length-mismatched reads never pass.
    if (-not $transport) { return 'transport-failed' }
    if ($null -eq $value) { return 'null-value' }
    if ($declaredLen -ne $probe.Length -or $copied -ne $probe.Length -or $value.Length -ne $probe.Length) { return "length-mismatch declared=$declaredLen copied=$copied value=$($value.Length) probe=$($probe.Length)" }
    if ($value -cne $probe) { return 'value-mismatch' }
    return 'ok'
  }
  function Test-NativeAcceptanceGate { param($clickAttempted, $clickTransportOk, $rv1, $setOk, $rvMid, $readbackOk, $rv2, $fgOk, $closedAcc, $noDupOk, $storeSameOk, $appPathOk)
    # Complete acceptance: every pregate, exactly one attempted click, and ALL empirical acceptance evidence
    # (dialog gone, no queued picker, store unchanged until Save, app path readback equals probe). Transport is
    # recorded separately: an unknown-transport click is never retried, and the empirical checks decide the
    # outcome; the claim detail always carries the transport state honestly.
    $failed = @()
    if (-not $clickAttempted) { $failed += 'click-not-attempted' }
    if ($rv1 -ne 'ok') { $failed += "revalidate-before-settext($rv1)" }
    if (-not $setOk) { $failed += 'wm-settext' }
    if ($rvMid -ne 'ok') { $failed += "revalidate-before-readback($rvMid)" }
    if (-not $readbackOk) { $failed += 'readback' }
    if ($rv2 -ne 'ok') { $failed += "revalidate-before-click($rv2)" }
    if (-not $fgOk) { $failed += 'foreground' }
    if (-not $closedAcc) { $failed += 'dialog-still-open' }
    if (-not $noDupOk) { $failed += 'queued-picker' }
    if (-not $storeSameOk) { $failed += 'store-changed' }
    if (-not $appPathOk) { $failed += 'app-path-readback' }
    return [pscustomobject]@{ ok = ($failed.Count -eq 0); failed = $failed; transport = [bool]$clickTransportOk }
  }
  function Invoke-UiaCrossCheck { param($hwndL, $appPid, $expLeafAid, $expLeafCls, $expChain, $fromHandle, $getParent)
    # Optional UIA corroboration with STICKY per-field conflicts: each successfully read field is checked
    # IMMEDIATELY, and a recorded disagreement is never erased by a later provider exception. Fields never read
    # are explicitly listed as unavailable; a missing parent or a short chain is an explicit status, never a
    # silent skip. $expChain: ordered expected direct ancestors, each @{ aid=../$null; cls=../$null; nwh=<long,
    # 0 = skip round-trip> }. This is corroboration, not proof: provider absence degrades to native-only as
    # scoped, but any AVAILABLE disagreement always rejects.
    $conflicts = @(); $unavail = @(); $read = @()
    $el = $null
    try { $el = & $fromHandle ([IntPtr]$hwndL) } catch { $unavail += "fromHandle: $($_.Exception.Message)" }
    if ($null -ne $el) {
      $v = $null; try { $v = "$($el.Current.AutomationId)"; $read += "leaf.aid='$v'" } catch { $unavail += 'leaf.aid' }
      if ($null -ne $v -and $v -ne $expLeafAid) { $conflicts += "UIA leaf AutomationId disagreement: '$v' (expected '$expLeafAid')" }
      $v = $null; try { $v = "$($el.Current.ClassName)"; $read += "leaf.cls='$v'" } catch { $unavail += 'leaf.cls' }
      if ($null -ne $v -and $v -ne $expLeafCls) { $conflicts += "UIA leaf ClassName disagreement: '$v' (expected '$expLeafCls')" }
      $v = $null; try { $v = $el.Current.ProcessId; $read += "leaf.pid=$v" } catch { $unavail += 'leaf.pid' }
      if ($null -ne $v -and [uint32]$v -ne [uint32]$appPid) { $conflicts += "UIA leaf ProcessId disagreement: $v (expected $appPid)" }
      $v = $null; try { $v = [int64]$el.Current.NativeWindowHandle; $read += "leaf.nwh=0x$($v.ToString('X'))" } catch { $unavail += 'leaf.nwh' }
      if ($null -ne $v -and $v -ne 0 -and $v -ne [int64]$hwndL) { $conflicts += "UIA leaf NativeWindowHandle round-trip disagreement: 0x$($v.ToString('X')) vs 0x$([int64]$hwndL.ToString('X'))" }
      $cur = $el
      for ($i = 0; $i -lt $expChain.Count; $i++) {
        $parent = $null
        try { $parent = & $getParent $cur } catch { $unavail += "parent[$i].read: $($_.Exception.Message)"; break }
        if ($null -eq $parent) { $unavail += "parent[$i]: missing (short chain at depth $i of $($expChain.Count))"; break }
        $exp = $expChain[$i]
        $v = $null; try { $v = "$($parent.Current.AutomationId)"; $read += "parent[$i].aid='$v'" } catch { $unavail += "parent[$i].aid" }
        if ($null -ne $v -and $null -ne $exp.aid -and $v -ne "$($exp.aid)") { $conflicts += "UIA parent[$i] AutomationId disagreement: '$v' (expected '$($exp.aid)')" }
        $v = $null; try { $v = "$($parent.Current.ClassName)"; $read += "parent[$i].cls='$v'" } catch { $unavail += "parent[$i].cls" }
        if ($null -ne $v -and $null -ne $exp.cls -and $v -ne "$($exp.cls)") { $conflicts += "UIA parent[$i] ClassName disagreement: '$v' (expected '$($exp.cls)')" }
        $v = $null; try { $v = [int64]$parent.Current.NativeWindowHandle; $read += "parent[$i].nwh=0x$($v.ToString('X'))" } catch { $unavail += "parent[$i].nwh" }
        if ($null -ne $v -and $v -ne 0 -and [int64]$exp.nwh -ne 0 -and $v -ne [int64]$exp.nwh) { $conflicts += "UIA parent[$i] hwnd disagreement: 0x$($v.ToString('X')) vs 0x$([int64]$exp.nwh.ToString('X'))" }
        $cur = $parent
      }
    }
    $status = if ($conflicts.Count -gt 0) { 'DISAGREEMENT' } elseif ($unavail.Count -gt 0) { "native-only (partial: $($unavail.Count) field(s) unavailable)" } else { 'corroborated' }
    return [pscustomobject]@{ status = $status; conflicts = $conflicts; unavailable = $unavail; read = $read }
  }
  # NATIVEID-FN-END
  # RAWDUMP-FN-BEGIN
  function Dialog-RawTreeDump { param([System.Windows.Automation.AutomationElement]$root, [int]$maxNodes = 200, [int]$maxDepth = 12, [int]$maxSiblingsPerNode = 100, [int]$maxSibCalls = 2000)
    if ($null -eq $root) { return 'rawtree: root binding failed (null root)' }
    $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
    $count = 0; $sibCalls = 0; $truncated = $false; $depthCapped = $false; $readErrors = 0; $lines = @()
    $stack = New-Object System.Collections.Stack
    $stack.Push(@($root, 0, '0'))
    while ($stack.Count -gt 0) {
      if ($count -ge $maxNodes -or $sibCalls -ge $maxSibCalls) { $truncated = $true; break }
      $it = $stack.Pop(); $el = $it[0]; $depth = $it[1]; $path = $it[2]
      $ct = ''; $aid = ''; $nm = ''; $cls = ''; $hw = 0; $pidN = 0; $en = '?'; $off = '?'; $rect = '?'; $rtid = ''; $lb = 'none'; $pats = '?'
      try { $ct = $el.Current.ControlType.ProgrammaticName } catch { $ct = 'ERR'; $readErrors++ }
      try { $aid = [WinN]::Esc("$($el.Current.AutomationId)") } catch { $aid = 'ERR' }
      try { $nm = [WinN]::Esc("$($el.Current.Name)") } catch { $nm = 'ERR' }
      try { $cls = [WinN]::Esc("$($el.Current.ClassName)") } catch { $cls = 'ERR' }
      try { $hw = $el.Current.NativeWindowHandle } catch { }
      try { $pidN = $el.Current.ProcessId } catch { }
      try { $en = "$($el.Current.IsEnabled)" } catch { }
      try { $off = "$($el.Current.IsOffscreen)" } catch { }
      try { $br = $el.Current.BoundingRectangle; $rect = "{0},{1} {2}x{3}" -f [int]$br.Left, [int]$br.Top, [int]$br.Width, [int]$br.Height } catch { }
      try { $rtid = (($el.GetRuntimeId()) -join '-') } catch { }
      try { $lbe = $el.Current.LabeledBy; if ($null -ne $lbe) { $lb = "$($lbe.Current.ControlType.ProgrammaticName)/aid='$([WinN]::Esc("$($lbe.Current.AutomationId)"))'/name='$([WinN]::Esc("$($lbe.Current.Name)"))'" } } catch { $lb = 'ERR' }
      try { $sp = @($el.GetSupportedPatterns()); $pats = "value=$($sp -contains [System.Windows.Automation.ValuePattern]::Pattern) invoke=$($sp -contains [System.Windows.Automation.InvokePattern]::Pattern) window=$($sp -contains [System.Windows.Automation.WindowPattern]::Pattern)" } catch { $pats = 'ERR' }
      $nmLen = $nm.Length; if ($nmLen -gt 80) { $nm = $nm.Substring(0, 80) + "..(truncated from $nmLen)" }
      $lines += ("#{0} depth={1} path={2} ct={3} aid='{4}' name='{5}' cls='{6}' hwnd=0x{7:X} pid={8} en={9} off={10} rect={11} rtid={12} labelledby={13} pats[{14}]" -f $count, $depth, $path, $ct, $aid, $nm, $cls, $hw, $pidN, $en, $off, $rect, $rtid, $lb, $pats)
      $count++
      if ($depth -ge $maxDepth) { $depthCapped = $true; $truncated = $true; continue }
      $pushed = 0; $sibTrunc = $false; $childErr = ''
      try {
        $kid = $walker.GetFirstChild($el)
        while ($null -ne $kid) {
          if ($pushed -ge $maxSiblingsPerNode) { $sibTrunc = $true; break }
          if (($count + $stack.Count) -ge $maxNodes) { $sibTrunc = $true; break }
          if ($sibCalls -ge $maxSibCalls) { $sibTrunc = $true; break }
          $stack.Push(@($kid, ($depth + 1), "$path.$pushed")); $pushed++
          $kid = $walker.GetNextSibling($kid); $sibCalls++
        }
      } catch { $readErrors++; $childErr = "$($_.Exception.Message)" }
      if ($sibTrunc) { $truncated = $true; $lines += "#sibling-cap at path=$path pushed=$pushed maxSiblingsPerNode=$maxSiblingsPerNode" }
      if ($childErr) { $lines += "#children-read-error at path=$path : $([WinN]::Esc($childErr))" }
    }
    $unvisited = $stack.Count
    return "rawtree nodes=$count truncated=$truncated depthCapped=$depthCapped readErrors=$readErrors sibCalls=$sibCalls unvisited=$unvisited budgets[nodes=$maxNodes depth=$maxDepth sibPerNode=$maxSiblingsPerNode sibCalls=$maxSibCalls] | " + ($lines -join ' || ')
  }
  # RAWDUMP-FN-END
  function Shot-OwnedDialog { param([string]$state, [IntPtr]$hwnd)
    try {
      if (-not [WinN]::IsWindow($hwnd)) { return "scoped shot '$state' skipped: dialog gone" }
      if (@(FindPickers) -notcontains $hwnd) { return "scoped shot '$state' skipped: hwnd no longer an owned picker" }
      $r = New-Object W+R; [void][W]::GetWindowRect($hwnd, [ref]$r); $w = $r.Rt - $r.L; $h = $r.B - $r.T
      if ($w -le 0 -or $h -le 0) { return "scoped shot '$state' skipped: invalid rect" }
      $script:shot++; $f = "out-images/feat-{0:00}-$state.png" -f $script:shot
      $bmp = New-Object System.Drawing.Bitmap $w, $h; $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size); $g.Dispose(); $bmp.Save("$PWD/$f"); $bmp.Dispose()
      return "scoped shot $f ${w}x${h}"
    } catch { return "scoped shot '$state' failed: $($_.Exception.Message)" }
  }
  function Format-AttemptSummary { param($attempt, $attemptErr)
    return (($attempt.Keys | ForEach-Object { $k = $_; "$k=$($attempt[$k])$(if ($attemptErr[$k]) { ' err=' + [WinN]::Esc("$($attemptErr[$k])") })" }) -join ' | ')
  }
  $c = ClickBtn 'Workspace actions' 900; Assert 'card menu opened for picker-accept upload' ($c -eq 'ok') $c
  $mi = MenuItemByName 'עצב כרטיס'; [void](InvokeEl $mi 'design menu entry for picker-accept'); Start-Sleep -Milliseconds 900
  Assert 'design popup reopened for picker-accept upload' (WaitText 'תמונת רקע' 10) ((Texts) -join ' | ')
  $colEditAcc = NamedEdit 'Card color'; Assert 'card color editor present before accept' ($null -ne $colEditAcc) ''
  $colPre = ''; $colPreOk = (Read-EditValue $colEditAcc ([ref]$colPre))
  Assert 'card color readback succeeded and shows the persisted color before accept' ($colPreOk -and $colPre -eq '#BFDBFE') ("read=$colPreOk value='$colPre'")
  $preAcc = @(FindPickers)
  if ($preAcc.Count -gt 0) { Assert 'no picker already open before the accept click' $false (AppWindowDump); Close-OwnedPickers 'pre-existing picker before accept click'; Shot 'picker-failure'; throw 'a picker dialog was already open before the accept click' }
  $upBtnAcc = Btn 'Upload card image'; Assert 'upload image button present for the accept flow' ($null -ne $upBtnAcc) ''
  $invokedAcc = @(InvokeEl $upBtnAcc 'upload image button (accept flow)')[-1]
  if (-not $invokedAcc) { Close-OwnedPickers 'upload button invocation failed'; Shot 'picker-failure'; throw 'upload image button invocation failed (accept flow)' }
  $dlgAcc = [IntPtr]::Zero; $openedAcc = $false; $multiAcc = $false
  for ($t = 0; $t -lt 20 -and -not $openedAcc -and -not $multiAcc; $t++) { Start-Sleep -Milliseconds 200; $hitsAcc = @(FindPickers); if ($hitsAcc.Count -gt 1) { $multiAcc = $true } elseif ($hitsAcc.Count -eq 1) { $dlgAcc = $hitsAcc[0]; $openedAcc = $true } }
  Assert 'exactly one native picker opened for the accept flow' ($openedAcc -and -not $multiAcc) (AppWindowDump)
  if ($multiAcc) { Close-OwnedPickers 'multiple pickers after one upload click'; Shot 'picker-failure'; throw 'more than one owned picker after a single upload click' }
  if (-not $openedAcc) { Close-OwnedPickers 'picker open detection timed out'; Shot 'picker-failure'; throw 'picker open detection failed (accept flow)' }
  # Drive the dialog with VERIFIED identity only. Observed topology (run 43525189834): on this runner UIA surfaces
  # the comdlg32 filename chain (ComboBoxEx32 1148 > ComboBox 1148 > Edit 1148) and the Open button (ctrlId 1) as
  # ControlType.Pane with NO Value/Invoke patterns. That is THESE observed controls only, not a global claim - the
  # dialog's SearchEditBox and the four UIProperty column values surface as ControlType.Edit WITH patterns; they are
  # a different surface this drive must never touch. So identity and input use an explicit NATIVE-HWND path:
  # enumerate ALL child windows of the verified owned dialog (all-matches or truncation FAIL, never first-match),
  # resolve exactly one filename Edit-leaf via direct parent links dialog > ComboBoxEx32(ctrlId 1148) >
  # ComboBox(ctrlId 1148) > Edit(ctrlId 1148) with ALL chain members same PID + visible + enabled (the invisible
  # address-band chain ctrlId 41477 is excluded; every OTHER visible Edit-class window - any ctrlId, including a
  # competing malformed 1148 - is a conflict), and exactly one DIRECT root child Button ctrlId 1 (id1Total must be
  # exactly 1 BEFORE its native title 'Open'/'פתיחה' corroborates; never ID 2 Cancel or 1136 filter). The owned
  # dialog root must be UNIQUE (exactly one eligible picker, not mere membership) and freshly alive/visible/
  # enabled/same-PID and exactly class #32770. Handles are matched dynamically and the ENTIRE chain identity (edit+combo+comboex+Open
  # handles plus the unique root) is revalidated immediately before WM_SETTEXT, again before the readback, and
  # again before the click. Input: bounded SendMessageTimeoutW WM_SETTEXT (transport AND LRESULT==1), bounded
  # WM_GETTEXTLENGTH+WM_GETTEXT readback on the SAME hwnd (transport distinct from valid empty; exact-case Unicode
  # value with declared length AND copied count AND value length all equal to the probe length - empty, truncated,
  # or mismatched reads withhold the click), foreground-window verification at the last moment after the final
  # revalidation, then exactly ONE BM_CLICK - never WM_COMMAND/IDOK, no code-behind/VM/store injection, and NO
  # retry on timeout or unknown result. After ANY click attempt, even with unknown transport, the COMPLETE
  # acceptance gate runs unconditionally and throws before any downstream save when incomplete: dialog gone, no
  # queued picker (3s bounded poll), store unchanged until the explicit Save (strict raw before/after compare),
  # probe; transport state is recorded separately in the claim. Every ambiguity, truncation, or exception is
  # fail-closed: withhold, log, clean up.
  $driveNote = ''; $rawDump = 'rawtree: not captured'; $nativeDump = 'native: not captured'; $totalsNote = 'totals: not read'
  $storeBeforeAccept = ''; $rv1 = 'not-run'; $setOk = $false; $rvMid = 'not-run'; $readbackOk = $false; $rv2 = 'not-run'; $fgOk = $false; $clickAttempted = $false; $clickTransportOk = $false
  $attempt = [ordered]@{ 'native-rows' = 'not-attempted'; 'native-chain-1148' = 'not-attempted'; 'open-button-ctrl1' = 'not-attempted' }
  $attemptErr = [ordered]@{ 'native-rows' = ''; 'native-chain-1148' = ''; 'open-button-ctrl1' = '' }
  $titleReader = {
    param($h)
    [string]$tv = $null; [long]$td = -1; [long]$tc = -1
    try {
      $tok = [DlgDrive]::TryGetText([IntPtr]$h, [ref]$tv, [ref]$td, [ref]$tc)
      [pscustomobject]@{ ok = [bool]$tok; value = $tv; declared = $td; copied = $tc; err = '' }
    } catch {
      # partial evidence preserved (value/declared/copied keep whatever the failed read left); the exception
      # message is sanitized (control/bidi marks) and capped - diagnostics only, no secret data.
      $em = "$($_.Exception.Message)" -replace '[\x00-\x1F\u202A-\u202E\u2066-\u2069]', '.'
      if ($em.Length -gt 120) { $em = $em.Substring(0, 120) + '..(truncated)' }
      [pscustomobject]@{ ok = $false; value = $tv; declared = $td; copied = $tc; err = $em }
    }
  }
  try {
    $dlgAlive = [WinN]::IsWindow($dlgAcc)
    $rootClassSeen = ''; $rootHitsSeen = -1
    $rootCheck = {
      if (-not [WinN]::IsWindow($dlgAcc)) { return (Test-RootUnique $false 0 $false $false $false $false '') }
      $hitsR = @(FindPickers); $rootHitsSeen = $hitsR.Count
      $h0 = ($hitsR.Count -ge 1) -and ($hitsR[0].ToInt64() -eq $dlgAcc.ToInt64())
      $rootClassSeen = [WinN]::ClassOf($dlgAcc)
      return (Test-RootUnique $true $hitsR.Count $h0 ([DlgDrive]::VisEn($dlgAcc)) ([DlgDrive]::PidOf($dlgAcc) -eq [uint32]$p.Id) ($rootClassSeen -eq '#32770') $rootClassSeen)
    }
    $rootState = & $rootCheck
    $stillOwned = ($rootState -eq 'ok')
    $dlgEl = $null
    if ($stillOwned) { try { $dlgEl = [System.Windows.Automation.AutomationElement]::FromHandle($dlgAcc) } catch { $driveNote = "fromHandle failed: $($_.Exception.Message)" } }
    Info 'owned picker revalidated before any drive' ("alive=$dlgAlive uniqueRoot='$rootState' rootClass='$rootClassSeen' eligibleHits=$rootHitsSeen ownerChain=fresh-via-FindPickers fromHandle=$(if ($null -ne $dlgEl) { 'ok' } else { 'failed' })")
    if ($null -eq $dlgEl) { throw "owned dialog unavailable for identity resolution (alive=$dlgAlive uniqueRoot=$rootState)" }
    try { $nativeDump = [DlgEnum]::Dump($dlgAcc, 120) } catch { $nativeDump = "native enum error: $($_.Exception.Message)" }
    Info 'owned picker dialog native child windows (bounded, before UIA walk)' $nativeDump
    try { $rawDump = Dialog-RawTreeDump $dlgEl 200 12 100 2000 } catch { $rawDump = "rawtree error: $($_.Exception.Message)" }
    Info 'owned picker dialog UIA raw tree (bounded, dialog-only)' $rawDump
    $nativeTrunc = $true; $nativeRows = @(); $editHwndL = [long]0; $openHwndL = [long]0; $openTitle = ''; $openVariant = ''
    $idConflicts = @()
    try {
      $rawRows = "$([DlgDrive]::Rows($dlgAcc, 400))"
      $seg = $rawRows -split ' ;; '
      $nativeTrunc = ($seg[0] -match 'truncated=True')
      $nativeRows = @($seg | Select-Object -Skip 1 | Where-Object { "$_" -ne '' } | ForEach-Object { $f = $_ -split '\|'; [pscustomobject]@{ hwnd = [long]$f[0]; parent = [long]$f[1]; ctrlId = [int]$f[2]; cls = $f[3]; pidN = [uint32]$f[4]; vis = ($f[5] -eq 'True'); en = ($f[6] -eq 'True') } })
      $attempt['native-rows'] = "count=$($nativeRows.Count) truncated=$nativeTrunc"
      if ($nativeTrunc) { $idConflicts += "native enumeration TRUNCATED at the cap: the all-matches guarantee is lost (fail-closed)" }
      if ($nativeRows.Count -eq 0) { $idConflicts += "native enumeration returned no rows for the verified dialog" }
    } catch { $attemptErr['native-rows'] = "$($_.Exception.Message)"; $idConflicts += "native enumeration raised (fail-closed): $($_.Exception.Message)"; $nativeTrunc = $true }
    if (-not $nativeTrunc -and $nativeRows.Count -gt 0) {
      $editSel = Select-NativeFilenameEdit $nativeRows ([uint32]$p.Id) ($dlgAcc.ToInt64())
      $openSel = Select-NativeOpenButton $nativeRows ([uint32]$p.Id) ($dlgAcc.ToInt64()) $titleReader
      $attempt['native-chain-1148'] = "editLeaves=$($editSel.candidates.Count) editsTotal=$($editSel.editsTotal) otherVisible=[$(($editSel.otherVisible | ForEach-Object { "ctrlId=$($_.ctrlId)" }) -join ',')]"
      $attempt['open-button-ctrl1'] = "titledOpen=$($openSel.candidates.Count) id1Direct=$($openSel.id1Total) id1Titles=[$($openSel.id1Titles -join ', ')] buttonsTotal=$($openSel.buttonsTotal)"
      if ($editSel.candidates.Count -ne 1) { $idConflicts += "native filename Edit-leaf count=$($editSel.candidates.Count) (need exactly 1; the Edit-leaf is counted once even though chain members share ctrlId 1148)" }
      if ($editSel.otherVisibleCount -gt 0) { $idConflicts += "other VISIBLE Edit-class windows present: $($editSel.otherVisibleCount) (any ctrlId - a competing or malformed Edit never matches silently)" }
      if ($openSel.id1Total -ne 1) { $idConflicts += "direct root child Button ctrlId 1 count=$($openSel.id1Total) (need exactly 1 before title corroboration - an ID collision is never discarded by the title filter)" }
      if ($openSel.candidates.Count -ne 1) { $idConflicts += "title-corroborated Open count=$($openSel.candidates.Count) (need exactly 1)" }
      if ($idConflicts.Count -eq 0) {
        $editHwndL = [long]$editSel.candidates[0].edit.hwnd; $comboHwndL = [long]$editSel.candidates[0].combo.hwnd; $comboexHwndL = [long]$editSel.candidates[0].comboex.hwnd
        $openHwndL = [long]$openSel.candidates[0].row.hwnd; $openTitle = $openSel.candidates[0].title; $openVariant = $openSel.candidates[0].variant
        # Optional UIA corroboration (scoped): sticky per-field conflicts via Invoke-UiaCrossCheck (NATIVEID-FN).
        $uiaFromHandle = { param($h) [System.Windows.Automation.AutomationElement]::FromHandle($h) }
        $uiaGetParent = { param($e) [System.Windows.Automation.TreeWalker]::RawViewWalker.GetParent($e) }
        $uiaEdit = Invoke-UiaCrossCheck $editHwndL ([uint32]$p.Id) '1148' 'Edit' @(
          [pscustomobject]@{ aid = '1148'; cls = 'ComboBox'; nwh = $comboHwndL },
          [pscustomobject]@{ aid = '1148'; cls = 'ComboBoxEx32'; nwh = $comboexHwndL },
          [pscustomobject]@{ aid = $null; cls = $null; nwh = $dlgAcc.ToInt64() }
        ) $uiaFromHandle $uiaGetParent
        $attempt['uia-crosscheck-edit'] = ("status=$($uiaEdit.status) read=[$($uiaEdit.read -join ',')]" + $(if ($uiaEdit.unavailable.Count -gt 0) { " unavailable=[$($uiaEdit.unavailable -join ',')]" } else { '' }))
        if ($uiaEdit.conflicts.Count -gt 0) { $idConflicts += $uiaEdit.conflicts }
        $uiaOpen = Invoke-UiaCrossCheck $openHwndL ([uint32]$p.Id) '1' 'Button' @(
          [pscustomobject]@{ aid = $null; cls = $null; nwh = $dlgAcc.ToInt64() }
        ) $uiaFromHandle $uiaGetParent
        $attempt['uia-crosscheck-open'] = ("status=$($uiaOpen.status) read=[$($uiaOpen.read -join ',')]" + $(if ($uiaOpen.unavailable.Count -gt 0) { " unavailable=[$($uiaOpen.unavailable -join ',')]" } else { '' }))
        if ($uiaOpen.conflicts.Count -gt 0) { $idConflicts += $uiaOpen.conflicts }
      }
    }
    try {
      $allEditsN = @($dlgEl.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Edit)))).Count
      $allCombos = @($dlgEl.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::ComboBox))))
      $allTexts = @($dlgEl.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Text))))
      $comboNames = (($allCombos | ForEach-Object { $cn = [WinN]::Esc("$($_.Current.Name)"); if ($cn.Length -gt 40) { $cn.Substring(0, 40) + "..(truncated from $($cn.Length))" } else { $cn } }) -join ',')
      $labelNames = (($allTexts | ForEach-Object { $tn = [WinN]::Esc("$($_.Current.Name)"); if ($tn.Length -gt 40) { $tn.Substring(0, 40) + "..(truncated from $($tn.Length))" } else { $tn } } | Select-Object -First 25) -join ',')
      $totalsNote = "totals: edits=$allEditsN combos=$($allCombos.Count) texts=$($allTexts.Count) comboNames=[$comboNames] labels=[$labelNames]"
    } catch { $totalsNote = "totals read raised (diagnostic-only, drive unaffected): $($_.Exception.Message)" }
    if ($idConflicts.Count -gt 0) {
      Assert 'exactly one unambiguous filename edit identity in the picker' $false ("conflicts=[$($idConflicts -join '; ')] attempts=[$(Format-AttemptSummary $attempt $attemptErr)]")
      $driveNote = "native input WITHHELD: identity not uniquely verified (conflicts=[$($idConflicts -join '; ')] attempts=[$(Format-AttemptSummary $attempt $attemptErr)])"
    } else {
      Assert 'exactly one unambiguous filename edit identity in the picker' $true ("edit=0x$($editHwndL.ToString('X')) combo=0x$($comboHwndL.ToString('X')) comboex=0x$($comboexHwndL.ToString('X')) chain=dialog>ComboBoxEx32(1148)>ComboBox(1148)>Edit(1148) open=0x$($openHwndL.ToString('X')) title='$openTitle' variant='$openVariant' attempts=[$(Format-AttemptSummary $attempt $attemptErr)]")
      $storeBeforeAccept = if (Test-Path $store) { Get-Content $store -Raw -Encoding UTF8 } else { '' }
      $script:rvEvidence = 'not-run'
      $revalidate = {
        $rk = & $rootCheck
        if ($rk -ne 'ok') { $script:rvEvidence = "root-gate=$rk"; return $rk }
        $rr = "$([DlgDrive]::Rows($dlgAcc, 400))"; $rseg = $rr -split ' ;; '
        if ($rseg[0] -match 'truncated=True') { $script:rvEvidence = 'enumeration-truncated'; return 'revalidation enumeration truncated' }
        $rrows = @($rseg | Select-Object -Skip 1 | Where-Object { "$_" -ne '' } | ForEach-Object { $f = $_ -split '\|'; [pscustomobject]@{ hwnd = [long]$f[0]; parent = [long]$f[1]; ctrlId = [int]$f[2]; cls = $f[3]; pidN = [uint32]$f[4]; vis = ($f[5] -eq 'True'); en = ($f[6] -eq 'True') } })
        $reE = Select-NativeFilenameEdit $rrows ([uint32]$p.Id) ($dlgAcc.ToInt64())
        $reO = Select-NativeOpenButton $rrows ([uint32]$p.Id) ($dlgAcc.ToInt64()) $titleReader
        $script:rvEvidence = "openTitles=[$($reO.id1Titles -join ', ')] openId1=$($reO.id1Total) titled=$($reO.candidates.Count) editLeaves=$($reE.candidates.Count) otherVisibleEdits=$($reE.otherVisibleCount)"
        return (Test-NativeIdentityUnchanged $reE $reO $editHwndL $comboHwndL $comboexHwndL $openHwndL)
      }
      $rv1 = & $revalidate
      Assert 'native identity revalidated (full chain + unique fresh root) immediately before WM_SETTEXT' ($rv1 -eq 'ok') ("result=$rv1 evidence=[$script:rvEvidence]")
      $setTransport = $false; $setLr = [long]-1
      if ($rv1 -eq 'ok') { try { $setTransport = [DlgDrive]::TrySetText([IntPtr]$editHwndL, $probeImg, [ref]$setLr) } catch { $driveNote = "WM_SETTEXT raised: $($_.Exception.Message)" } }
      $setOk = ($setTransport -and $setLr -eq 1)
      if ($rv1 -eq 'ok') { Assert 'bounded WM_SETTEXT to the verified edit succeeded (transport AND LRESULT)' $setOk ("transport=$setTransport lresult=$setLr") }
      if ($setOk) { $rvMid = & $revalidate }
      if ($setOk) { Assert 'native identity unchanged after WM_SETTEXT, revalidated immediately before the readback' ($rvMid -eq 'ok') ("result=$rvMid evidence=[$script:rvEvidence]") }
      $rbVal = $null; $rbLen = [long]-1; $rbCopied = [long]-1; $getTransport = $false; $rbCheck = 'not-run'
      if ($rvMid -eq 'ok') { try { $getTransport = [DlgDrive]::TryGetText([IntPtr]$editHwndL, [ref]$rbVal, [ref]$rbLen, [ref]$rbCopied) } catch { $driveNote = "WM_GETTEXT raised: $($_.Exception.Message)" } }
      if ($rvMid -eq 'ok') { $rbCheck = Test-NativeReadback $getTransport $rbVal $rbLen $rbCopied $probeImg; $readbackOk = ($rbCheck -eq 'ok') }
      if ($rvMid -eq 'ok') { Assert 'picker filename readback succeeded and equals the probe path (declared length + copied count + value length, exact-case, no clip)' $readbackOk ("check=$rbCheck transport=$getTransport declaredLen=$rbLen copied=$rbCopied probeLen=$($probeImg.Length)") }
      if ($readbackOk) { $rv2 = & $revalidate }
      if ($readbackOk) { Assert 'native identity unchanged through the readback, revalidated immediately before the single Open click' ($rv2 -eq 'ok') ("result=$rv2 evidence=[$script:rvEvidence]") }
      $fgHwnd = [IntPtr]::Zero
      if ($rv2 -eq 'ok') { try { $fgHwnd = [DlgDrive]::Foreground(); $fgOk = ($fgHwnd.ToInt64() -eq $dlgAcc.ToInt64()) } catch { $driveNote = "foreground check raised: $($_.Exception.Message)" } }
      if ($rv2 -eq 'ok') { Assert 'picker dialog is the foreground window at the last moment before the Open click' $fgOk ("foreground=0x$($fgHwnd.ToInt64().ToString('X')) dialog=0x$($dlgAcc.ToInt64().ToString('X'))") }
      if ($readbackOk -and $rv2 -eq 'ok' -and $fgOk) {
        $clickAttempted = $true
        $clickLr = [long]-1
        try { $clickTransportOk = [DlgDrive]::TryClick([IntPtr]$openHwndL, [ref]$clickLr) } catch { $clickTransportOk = $false; $driveNote = "BM_CLICK raised: $($_.Exception.Message)" }
        if ([string]::IsNullOrEmpty($driveNote)) { $driveNote = "verified native chain edit=0x$($editHwndL.ToString('X')) + bounded WM_SETTEXT/GETTEXT readback + last-moment foreground + ONE BM_CLICK attempted (transport=$clickTransportOk lresult=$clickLr) on direct-child Button ctrlId 1 - unknown transport is never retried" }
      } elseif ($rv1 -ne 'ok') {
        $driveNote = "native input WITHHELD: pre-input revalidation failed: $rv1 evidence=[$script:rvEvidence]"
      } elseif (-not $setOk) {
        if ([string]::IsNullOrEmpty($driveNote)) { $driveNote = "native input WITHHELD: WM_SETTEXT failed (transport=$setTransport lresult=$setLr)" }
      } elseif ($rvMid -ne 'ok') {
        $driveNote = "readback WITHHELD: post-settext revalidation failed: $rvMid evidence=[$script:rvEvidence]"
      } elseif (-not $readbackOk) {
        $driveNote = "Open click WITHHELD: readback failed ($rbCheck transport=$getTransport declaredLen=$rbLen copied=$rbCopied)"
      } elseif ($rv2 -ne 'ok') {
        $driveNote = "Open click WITHHELD: pre-click revalidation failed: $rv2 evidence=[$script:rvEvidence]"
      } else {
        $driveNote = "Open click WITHHELD: dialog not foreground at the last moment (foreground=0x$($fgHwnd.ToInt64().ToString('X')))"
      }
    }
  } catch { $driveNote = "dialog drive exception: $($_.Exception.Message)" }
  $skippedStrats = @($attempt.Keys | Where-Object { $attempt[$_] -eq 'not-attempted' })
  Info 'picker identity strategy attempts (always logged)' ("$(Format-AttemptSummary $attempt $attemptErr) | $totalsNote$(if ($skippedStrats.Count -gt 0) { ' | not-attempted: ' + ($skippedStrats -join ',') + ' (drive aborted before them; fail-closed)' })")
  Info 'native picker accept drive path' $driveNote
  $closedAcc = $false; for ($t = 0; $t -lt 20 -and -not $closedAcc; $t++) { Start-Sleep -Milliseconds 200; $closedAcc = -not [WinN]::IsWindow($dlgAcc) }
  if (-not $closedAcc) {
    $preCleanupShot = Shot-OwnedDialog 'picker-identity-failure' $dlgAcc
    Close-OwnedPickers 'accept attempt did not close the dialog'
    Shot 'picker-failure'
    Assert 'native picker closed after the accept attempt' $false ("driveNote=$driveNote; preCleanup=[$preCleanupShot]; windows: $(AppWindowDump)")
    throw 'native picker did not close after the accept attempt'
  }
  $noDupOk = $false; $storeSameOk = $false
  if ($clickAttempted) {
    # A timed-out/unknown-transport BM_CLICK may still have executed: run ALL acceptance checks unconditionally.
    $dupAcc = [IntPtr]::Zero; for ($t = 0; $t -lt 15 -and $dupAcc -eq [IntPtr]::Zero; $t++) { Start-Sleep -Milliseconds 200; $againAcc = @(FindPickers); if ($againAcc.Count -gt 0) { $dupAcc = $againAcc[0] } }
    $noDupOk = ($dupAcc -eq [IntPtr]::Zero)
    Assert 'no picker remains open or queued behind the accepted one (3s bounded poll)' $noDupOk ("queued=0x$($dupAcc.ToInt64().ToString('X')) windows: $(AppWindowDump)")
    $storeAfterAccept = if (Test-Path $store) { Get-Content $store -Raw -Encoding UTF8 } else { '' }
    $storeSameOk = ($storeAfterAccept -ceq $storeBeforeAccept)
    Assert 'store.json unchanged by the picker accept alone (strict raw before/after compare; persist happens only at the explicit Save)' $storeSameOk ("beforeLen=$($storeBeforeAccept.Length) afterLen=$($storeAfterAccept.Length)")
  }
  $imgEditAcc = NamedEdit 'Card image path'; Assert 'card image path editor present for accept readback' ($null -ne $imgEditAcc) ''
  $accPath = ''; $accReadOk = (Read-EditValue $imgEditAcc ([ref]$accPath))
  Assert 'card image path readback succeeded after the accept (no fail-open empty)' $accReadOk ''
  Assert 'card image path filled by the ACCEPTED native picker (code-behind result, not typed)' ($accReadOk -and $accPath -eq $probeImg) ("edit='$accPath' probe='$probeImg' drive=$driveNote")
  $appPathOk = ($accReadOk -and $accPath -eq $probeImg)
  $gate = Test-NativeAcceptanceGate $clickAttempted $clickTransportOk $rv1 $setOk $rvMid $readbackOk $rv2 $fgOk $closedAcc $noDupOk $storeSameOk $appPathOk
  Assert 'complete native acceptance gate before any downstream save (all pregates + exactly one attempted click + dialog gone + no queued picker + store unchanged + app path readback; transport recorded separately)' $gate.ok ("failed=[$($gate.failed -join ',')] transport=$($gate.transport) drive=$driveNote")
  if (-not $gate.ok) { Close-OwnedPickers 'native acceptance gate incomplete'; Shot 'picker-failure'; throw "native acceptance gate incomplete: $($gate.failed -join ',')" }
  $okPrevAcc = $false; $seenPrevAcc = ''
  for ($i = 0; $i -lt 20 -and -not $okPrevAcc; $i++) {
    $prevAcc = AnyByName 'Card design preview'; if ($prevAcc) { $rAcc = $prevAcc.Current.BoundingRectangle
      if ($rAcc.Width -gt 0 -and $rAcc.Height -gt 0) {
        $bmp1 = New-Object System.Drawing.Bitmap 1, 1; $g1 = [System.Drawing.Graphics]::FromImage($bmp1); $g1.CopyFromScreen([int]($rAcc.Left + $rAcc.Width / 2), [int]($rAcc.Top + $rAcc.Height / 2), 0, 0, $bmp1.Size)
        $px1 = $bmp1.GetPixel(0, 0); $g1.Dispose(); $bmp1.Dispose()
        $seenPrevAcc = "rgb($($px1.R),$($px1.G),$($px1.B))"
        $okPrevAcc = ([math]::Abs($px1.R - 30) -le 25 -and [math]::Abs($px1.G - 60) -le 25 -and [math]::Abs($px1.B - 220) -le 25)
      } }
    if (-not $okPrevAcc) { Start-Sleep -Milliseconds 300 }
  }
  Assert 'preview shows the picker-accepted image before save' $okPrevAcc ("center=$seenPrevAcc expected~rgb(30,60,220)")
  $c = ClickBtn 'Save card design' 1500; Assert 'design saved after picker accept' ($c -eq 'ok') $c
  Assert 'design popup closed after accept-save' (-not (WaitText 'תמונת רקע' 2)) ''
  $oAcc = StoreObj; $wsAcc = @($oAcc.workspaces | Where-Object { $_.id -eq $wsDId })[0]
  Assert 'after accept-save: persisted design is exactly the picker-accepted image + kept color' ($wsAcc.imagePath -eq $probeImg -and $wsAcc.color -eq '#BFDBFE') ("color='$($wsAcc.color)' imagePath='$($wsAcc.imagePath)'")
  # MEASURE-FN-BEGIN
  # Pure frame-measurement logic, exercised verbatim by the local fixture probes (extracted between the
  # MEASURE-FN markers; the probes run the SHIPPED logic, never a copy). Every edge anchors at the PAINTED
  # pixel bounds of the crop (never the ListItem rect, never skip-scanning to the first frame pixel).
  # MATHEMATICAL ACCEPTANCE SET for a tinted edge pixel: Euclidean distance <= TOL to one of the two DECLARED
  # blend segments of the frame color F=(191,219,254) - F->white (outer antialias) or F->image I=(30,60,220)
  # (inner antialias) - with blend alpha <= 0.8 (pure white background and pure image are NOT frame). This is a
  # segment-distance test, not a fitted box: grey, purple and off-segment blends fail regardless of channel
  # ranges. The AGREED rule is enforced per clean measured column, in full: image visible, thick top, thin
  # sides AND thin bottom (bottom compared against top AND sides on EVERY column), pure image interior, top
  # consistent across ALL clean columns, bottom re-confirmed at five self-anchored columns. ALL candidate
  # columns are read and reported; at least three must be clean image columns and EVERY clean column must
  # pass the full contract - no first-winner short-circuit.
  function Measure-CardFrame {
    param($crop, [int]$cw, [int]$ch, $isFrame, $isImg, [int[]]$candX, [int]$rowMid, [double]$scaleY)
    $frameC = @(191.0, 219.0, 254.0); $whiteC = @(255.0, 255.0, 255.0); $imgC = @(30.0, 60.0, 220.0)
    $TOL = 16.0; $maxAlpha = 0.8
    $distSeg = { param($px, $a, $bcol)
      $dx = $bcol[0] - $a[0]; $dy = $bcol[1] - $a[1]; $dz = $bcol[2] - $a[2]
      $len2 = $dx * $dx + $dy * $dy + $dz * $dz
      $t = 0.0
      if ($len2 -gt 0) { $t = (($px.R - $a[0]) * $dx + ($px.G - $a[1]) * $dy + ($px.B - $a[2]) * $dz) / $len2 }
      $tc = $t; if ($tc -lt 0) { $tc = 0 }; if ($tc -gt 1) { $tc = 1 }
      $ex = $a[0] + $tc * $dx; $ey = $a[1] + $tc * $dy; $ez = $a[2] + $tc * $dz
      $d = [math]::Sqrt(($px.R - $ex) * ($px.R - $ex) + ($px.G - $ey) * ($px.G - $ey) + ($px.B - $ez) * ($px.B - $ez))
      return @($d, $tc)
    }
    $isTint = { param($px)
      $r1 = & $distSeg $px $frameC $whiteC; if (($r1[0] -le $TOL) -and ($r1[1] -le $maxAlpha)) { return $true }
      $r2 = & $distSeg $px $frameC $imgC; if (($r2[0] -le $TOL) -and ($r2[1] -le $maxAlpha)) { return $true }
      return $false
    }
    $isFrameish = { param($px) ((& $isFrame $px) -or (& $isTint $px)) }
    $isPainted = { param($px) ((& $isFrame $px) -or (& $isImg $px) -or (& $isTint $px)) }
    $res = [ordered]@{ ok = $false; reason = ''; col = -1; mid = ''; yTop = -1; yBot = -1; xLeft = -1; xRight = -1; top = 0; bot = 0; left = 0; right = 0; botSig = $false; interior = $false; topConsistent = $false; belowTop = ''; rightOfLeft = ''; botDetail = ''; colsClean = 0; colsPass = 0; colsDetail = '' }
    if ($cw -le 40 -or $ch -le 20) { $res.reason = "invalid rect ${cw}x${ch}"; return $res }
    $measureCol = {
      param($cxm)
      $m = [ordered]@{ ok = $false; reason = ''; yTop = -1; yBot = -1; top = 0; bot = 0; belowTop = ''; botSig = $false; interior = $false }
      $yT = 0; while ($yT -lt $ch -and -not (& $isPainted ($crop.GetPixel($cxm, $yT)))) { $yT++ }
      $yB = $ch - 1; while ($yB -ge 0 -and -not (& $isPainted ($crop.GetPixel($cxm, $yB)))) { $yB-- }
      if ($yT -ge $yB) { $m.reason = "no painted vertical span (yT=$yT yB=$yB)"; return $m }
      $m.yTop = $yT; $m.yBot = $yB
      if (-not (& $isFrameish ($crop.GetPixel($cxm, $yT)))) { $m.reason = "painted top edge is not frame at y=$yT"; return $m }
      $yy = $yT; while ($yy -le $yB -and (& $isFrameish ($crop.GetPixel($cxm, $yy)))) { $m.top++; $yy++ }
      if ($yy -gt $yB) { $m.reason = 'entire column is frame-colored (no image interior)'; return $m }
      $p1 = $crop.GetPixel($cxm, $yy); $m.belowTop = "rgb($($p1.R),$($p1.G),$($p1.B))"
      if (-not (& $isImg $p1)) { $m.reason = "transition below top frame is not image ($($m.belowTop))"; return $m }
      $yb2 = $yB; while ($yb2 -ge $yy -and (& $isFrameish ($crop.GetPixel($cxm, $yb2)))) { $m.bot++; $yb2-- }
      if ($m.bot -eq 0) {
        $lcC = $crop.GetPixel($cxm, $yB); $scC = $crop.GetPixel($cxm, [math]::Max($yy, $yB - 1))
        $m.botSig = ((& $isTint $lcC) -and (& $isImg $scC))
      } else { $m.botSig = $true }
      if (-not $m.botSig) { $m.reason = "bottom edge missing at the painted bottom (anchor y$yB)"; return $m }
      $botEdgeStart = $yB - [math]::Max($m.bot, 1)
      if ($m.bot -eq 0) { $botEdgeStart = $yB - 1 }
      $badRow = -1
      for ($yi = $yy; $yi -lt $botEdgeStart; $yi++) { if (-not (& $isImg ($crop.GetPixel($cxm, $yi)))) { $badRow = $yi; break } }
      if ($badRow -ge 0) { $m.reason = "interior non-image stripe at y=$badRow"; return $m }
      $m.interior = $true
      $m.ok = $true
      return $m
    }
    # ALL candidate columns read and reported (tagged img/not-img); >= 3 clean required
    $readings = @(); $clean = @()
    foreach ($cx in $candX) { $pm = $crop.GetPixel($cx, $rowMid); $tag = "x${cx}:rgb($($pm.R),$($pm.G),$($pm.B))"; if (& $isImg $pm) { $clean += $cx; $tag += ':img' } else { $tag += ':not-img' }; $readings += $tag }
    $res.mid = ($readings -join ' ')
    $res.colsClean = $clean.Count
    if ($clean.Count -lt 3) { $res.reason = "only $($clean.Count) of $($candX.Count) candidate columns are clean image ($($res.mid))"; return $res }
    # sides at the mid row (card-wide, anchored at the painted horizontal bounds)
    $xL = 0; while ($xL -lt $cw -and -not (& $isPainted ($crop.GetPixel($xL, $rowMid)))) { $xL++ }
    $xR = $cw - 1; while ($xR -ge 0 -and -not (& $isPainted ($crop.GetPixel($xR, $rowMid)))) { $xR-- }
    if ($xL -ge $xR) { $res.reason = "no painted horizontal span (xL=$xL xR=$xR)"; return $res }
    $res.xLeft = $xL; $res.xRight = $xR
    $xx = $xL; while ($xx -le $xR -and (& $isFrameish ($crop.GetPixel($xx, $rowMid)))) { $res.left++; $xx++ }
    if ($xx -le $xR) { $p2 = $crop.GetPixel($xx, $rowMid); $res.rightOfLeft = "rgb($($p2.R),$($p2.G),$($p2.B))"; if (-not (& $isImg $p2)) { $res.reason = "transition right of left frame is not image ($($res.rightOfLeft))"; return $res } }
    while ($xR -ge $xx -and (& $isFrameish ($crop.GetPixel($xR, $rowMid)))) { $res.right++; $xR-- }
    if ($res.left -lt 1 -or $res.right -lt 1) { $res.reason = "side frame run missing at mid row (left=$($res.left) right=$($res.right))"; return $res }
    # FULL contract per clean column: vertical measurement + thickness vs sides, every column reported
    $sideTol = [math]::Max(1, [math]::Round($scaleY))
    $thickGap = [math]::Max(2, [math]::Round(2 * $scaleY))
    $colDetails = @(); $firstFail = ''
    foreach ($cxm in $clean) {
      $cm = & $measureCol $cxm
      if ($cm.ok) {
        if ($cm.bot -gt ([math]::Max($res.left, $res.right) + $sideTol)) { $cm.ok = $false; $cm.reason = "bottom thicker than the sides (bot=$($cm.bot) left=$($res.left) right=$($res.right) tol=$sideTol)" }
        elseif (($cm.top - $res.left) -lt $thickGap -or ($cm.top - $res.right) -lt $thickGap -or (($cm.top - $cm.bot) -lt $thickGap)) { $cm.ok = $false; $cm.reason = "top not clearly thickest (top=$($cm.top) left=$($res.left) right=$($res.right) bot=$($cm.bot) gap=$thickGap)" }
      }
      $colDetails += "x${cxm}:top=$($cm.top),bot=$($cm.bot),yT=$($cm.yTop),yB=$($cm.yBot),ok=$($cm.ok)$(if ($cm.reason) { ' ' + $cm.reason })"
      if ($cm.ok) { $res.colsPass++ } elseif (-not $firstFail) { $firstFail = "x${cxm}: $($cm.reason)" }
      if ($res.col -lt 0) { $res.col = $cxm; $res.yTop = $cm.yTop; $res.yBot = $cm.yBot; $res.top = $cm.top; $res.bot = $cm.bot; $res.belowTop = $cm.belowTop; $res.botSig = $cm.botSig; $res.interior = $cm.interior }
    }
    $res.colsDetail = ($colDetails -join ' | ')
    if ($firstFail) { $res.reason = "per-column contract failed ($firstFail)"; return $res }
    # top consistency across ALL clean columns (same painted top, run within 1px)
    $topConsistent = $true
    foreach ($cxm in $clean) {
      $cm2 = & $measureCol $cxm
      if ($cm2.yTop -ne $res.yTop -or [math]::Abs($cm2.top - $res.top) -gt 1) { $topConsistent = $false }
    }
    $res.topConsistent = $topConsistent
    if (-not $topConsistent) { $res.reason = "top frame not consistent across clean columns (anchor yT=$($res.yTop) run=$($res.top); $($res.colsDetail))"; return $res }
    # bottom edge re-confirmed at FIVE columns, each anchored at its own painted bottom
    $bcA = 20; $bcB = [int]($cw / 4); $bcC = [int]($cw / 2); $bcD = [int](3 * $cw / 4); $bcE = $cw - 21
    $botCols = @($bcA, $bcB, $bcC, $bcD, $bcE); $det = @(); $allSig = $true
    foreach ($bcx in $botCols) {
      $yb3 = $ch - 1; while ($yb3 -ge 1 -and -not (& $isPainted ($crop.GetPixel($bcx, $yb3)))) { $yb3-- }
      $lcC = $crop.GetPixel($bcx, $yb3); $scC = $crop.GetPixel($bcx, [math]::Max(0, $yb3 - 1))
      $colOk = (((& $isFrameish $lcC)) -and ((& $isImg $scC) -or (& $isFrameish $scC)))
      if (-not $colOk) { $allSig = $false }
      $det += "c${bcx}:y=$yb3 last=rgb($($lcC.R),$($lcC.G),$($lcC.B)) 2nd=rgb($($scC.R),$($scC.G),$($scC.B))"
    }
    $res.botDetail = ($det -join ' ')
    if (-not $allSig) { $res.reason = 'bottom edge signature failed on at least one of five anchored columns'; return $res }
    $res.ok = $true
    return $res
  }
  # MEASURE-FN-END
  # DIAG-FN-START (r7c): saved-card STRUCTURAL DIAGNOSTICS - diagnostic only: no assertion, threshold,
  # oracle, mask or drive changes; the original measurements/captures above still run and still gate.
  # The descendant-union region is a STRUCTURE-DERIVED DIAGNOSTIC REGION ONLY (union of descendant element
  # rectangles, NOT proven to equal the painted card Border bounds; RawView may include wrappers or omit the
  # Border). Never used as frame bounds or acceptance geometry. All diagnostic crops/readings in one call
  # derive from ONE immutable APP-WINDOW bitmap captured after identity+geometry revalidation; these are NEW
  # captures, separate from and later than the original oracle crop and Shot (no provenance is shared).
  function RectUnion($rects) {
    $uL = 2147483647; $uT = 2147483647; $uR = -1; $uB = -1; $cnt = 0
    foreach ($r in $rects) {
      if ($r.Width -le 0 -or $r.Height -le 0) { continue }
      $cnt++
      if ([int]$r.Left -lt $uL) { $uL = [int]$r.Left }
      if ([int]$r.Top -lt $uT) { $uT = [int]$r.Top }
      if ([int]$r.Right -gt $uR) { $uR = [int]$r.Right }
      if ([int]$r.Bottom -gt $uB) { $uB = [int]$r.Bottom }
    }
    if ($cnt -eq 0) { return $null }
    return [ordered]@{ L = $uL; T = $uT; R = $uR; B = $uB; n = $cnt }
  }
  # five diagnostic columns at the same fractions the original oracle used (0.4,0.5,0.6,0.3,0.7) but of the
  # descendant-union diagnostic region, never of the full list-item width.
  function UnionDiagColumns($uL, $uR) {
    $uw = $uR - $uL
    return @([int]($uL + $uw * 0.4), [int]($uL + $uw * 0.5), [int]($uL + $uw * 0.6), [int]($uL + $uw * 0.3), [int]($uL + $uw * 0.7))
  }
  # PID gate FIRST: foreign-process nodes yield $null before any name/rect/rid detail is read or returned.
  function Read-DiagNode($e, $appPid) {
    $c = $e.Current
    if ($c.ProcessId -ne $appPid) { return $null }
    $dr = $c.BoundingRectangle
    $drid = ''; try { $drid = ($e.GetRuntimeId() -join ',') } catch { $drid = 'rid-error' }
    return [ordered]@{ rid = $drid; rect = $dr; ct = $c.ControlType.ProgrammaticName; name = $c.Name; off = $c.IsOffscreen; el = $e }
  }
  # descendant walk: each visited node's children enumerated ONCE; visited-rid dedupe; explicit global
  # budgets (nodes, depth, provider calls) all reported; foreign nodes skipped without details and their
  # children never enumerated; per-operation errors counted and the walk continues where safe.
  function Walk-DiagDescendants($root, $getFC, $getNS, $readNode, $maxNodes, $maxDepth, $maxCalls) {
    $visited = @{}
    $lines = New-Object System.Collections.Generic.List[string]
    $rects = New-Object System.Collections.Generic.List[object]
    $queue = New-Object System.Collections.Generic.Queue[object]
    $errs = 0; $foreign = 0; $trunc = $false; $nodes = 0; $depthSkipped = 0; $calls = 0; $offs = 0
    # cap checked BEFORE every provider enumeration call; a skipped read marks truncated.
    # enumCalls counts ENUMERATION calls only (getFC/getNS) - per-node property/rid reads are NOT
    # counted against any global budget (explicitly not claimed).
    $ch = $null
    if ($calls -lt $maxCalls) { try { $calls++; $ch = & $getFC $root } catch { $errs++; $lines.Add("root child-enum-error $($_.Exception.Message)") } } else { $trunc = $true }
    while ($ch) {
      $queue.Enqueue([pscustomobject]@{ el = $ch; depth = 1 })
      if ($calls -ge $maxCalls) { $trunc = $true; break }
      try { $calls++; $ch = & $getNS $ch } catch { $errs++; $lines.Add("root sibling-enum-error $($_.Exception.Message)"); $ch = $null }
    }
    while ($queue.Count -gt 0) {
      if ($nodes -ge $maxNodes) { $trunc = $true; break }
      $qit = $queue.Dequeue()
      if ($qit.depth -gt $maxDepth) { $depthSkipped++; $trunc = $true; continue }
      $nodes++
      $nd = $null
      try { $nd = & $readNode $qit.el } catch { $errs++; $lines.Add("n$nodes read-error $($_.Exception.Message)"); continue }
      if ($null -eq $nd) { $foreign++; continue }
      if ($nd.rid -ne '' -and $nd.rid -ne 'rid-error') { if ($visited.ContainsKey($nd.rid)) { continue }; $visited[$nd.rid] = $true }
      $lines.Add("n$nodes depth=$($qit.depth) $($nd.ct) name='$($nd.name)' rid=[$($nd.rid)] rect=L$([int]$nd.rect.Left),T$([int]$nd.rect.Top),R$([int]$nd.rect.Right),B$([int]$nd.rect.Bottom) offscreen=$($nd.off)")
      if ($nd.off) { $offs++ }
      if ($nd.rect.Width -gt 0 -and $nd.rect.Height -gt 0) { $rects.Add($nd.rect) }
      $ch2 = $null
      if ($calls -lt $maxCalls) { try { $calls++; $ch2 = & $getFC $qit.el } catch { $errs++; $lines.Add("n$nodes child-enum-error $($_.Exception.Message)") } } else { $trunc = $true }
      while ($ch2) {
        $queue.Enqueue([pscustomobject]@{ el = $ch2; depth = ($qit.depth + 1) })
        if ($calls -ge $maxCalls) { $trunc = $true; break }
        try { $calls++; $ch2 = & $getNS $ch2 } catch { $errs++; $lines.Add("n$nodes sibling-enum-error $($_.Exception.Message)"); $ch2 = $null }
      }
    }
    if ($queue.Count -gt 0) { $trunc = $true }
    return [ordered]@{ lines = $lines; rects = $rects; nodes = $nodes; unique = $visited.Count; errs = $errs; foreign = $foreign; depthSkipped = $depthSkipped; truncated = $trunc; enumCalls = $calls; offscreenMembers = $offs }
  }
  # bounded in-app ascent: logs in-app parents only; stops at the verified app root (rid match), at a
  # foreign-pid parent (leftApp, NOT logged), at budget, or when no parent remains.
  function Walk-DiagAscent($start, $getParent, $maxHops, $stopAtRid, $appPid) {
    $nodes = New-Object System.Collections.Generic.List[object]
    $lines = New-Object System.Collections.Generic.List[string]
    $hops = 0; $hitRoot = $false; $leftApp = $false; $budgetHit = $false; $topReached = $false; $errs = 0
    $cur = $null
    try { $cur = & $getParent $start } catch { $errs++; $lines.Add("ascent-start-error $($_.Exception.Message)") }
    while ($cur) {
      if ($hops -ge $maxHops) { $budgetHit = $true; break }
      $nd = $null
      try { $nd = Read-DiagNode $cur $appPid } catch { $errs++; $lines.Add("a$hops read-error $($_.Exception.Message)"); break }
      if ($null -eq $nd) { $leftApp = $true; break }
      if (-not (Test-DiagRectFinitePositive $nd.rect)) { $errs++; $lines.Add("a$($hops + 1): invalid finite-positive ancestor rectangle (details not cast)"); break }
      $hops++
      $rootMark = ($stopAtRid -ne '' -and $stopAtRid -ne 'rid-error' -and $nd.rid -eq $stopAtRid)
      $line = "a$hops $($nd.ct) name='$($nd.name)' rid=[$($nd.rid)] rect=L$([int]$nd.rect.Left),T$([int]$nd.rect.Top),R$([int]$nd.rect.Right),B$([int]$nd.rect.Bottom)"
      if ($rootMark) { $line += ' APP-ROOT-VERIFIED-STOP' }
      $nodes.Add($nd); $lines.Add($line)
      if ($rootMark) { $hitRoot = $true; break }
      try { $cur = & $getParent $cur } catch { $errs++; $lines.Add("a$hops ascent-error $($_.Exception.Message)"); $cur = $null }
    }
    if (-not $cur -and -not $hitRoot -and -not $leftApp -and -not $budgetHit -and $errs -eq 0) { $topReached = $true }
    return [ordered]@{ nodes = $nodes; lines = $lines; hops = $hops; hitRoot = $hitRoot; leftApp = $leftApp; budgetHit = $budgetHit; topReached = $topReached; errs = $errs }
  }
  # explicit finite-positive rectangle validation: false for null, any NaN/Infinity in
  # Left/Top/Right/Bottom/Width/Height, Width<=0, Height<=0, Right<=Left, Bottom<=Top; otherwise true.
  # Must run BEFORE any [int] cast or summary formatting of a rectangle (an exception is not validation).
  function Test-DiagRectFinitePositive($r) {
    if ($null -eq $r) { return $false }
    foreach ($v in @($r.Left, $r.Top, $r.Right, $r.Bottom, $r.Width, $r.Height)) {
      if ($null -eq $v) { return $false }
      try { if ([double]::IsNaN([double]$v) -or [double]::IsInfinity([double]$v)) { return $false } } catch { return $false }
    }
    if ($r.Width -le 0 -or $r.Height -le 0 -or $r.Right -le $r.Left -or $r.Bottom -le $r.Top) { return $false }
    return $true
  }
  # an ascent proves the verified in-app chain ONLY when it hit the verified root with clean flags.
  function Test-DiagAscentClean($asc) {
    if (-not $asc.hitRoot) { return "no-verified-root(hitRoot=$($asc.hitRoot) leftApp=$($asc.leftApp) budget=$($asc.budgetHit) errs=$($asc.errs))" }
    if ($asc.leftApp) { return 'left-app' }
    if ($asc.budgetHit) { return 'budget-hit' }
    if ($asc.errs -gt 0) { return "ascent-errors($($asc.errs))" }
    return 'ok'
  }
  # count of positive member rects lying (even partly) outside the container rect; labeling only.
  function Rect-OutsideCount($rects, $container) {
    $n = 0
    foreach ($r in $rects) {
      if ($r.Width -le 0 -or $r.Height -le 0) { continue }
      if ($r.Left -lt $container.Left -or $r.Top -lt $container.Top -or $r.Right -gt $container.Right -or $r.Bottom -gt $container.Bottom) { $n++ }
    }
    return $n
  }
  # exact identity + geometry + root comparison between the structure pass and the capture moment.
  function Compare-DiagIdentity($btnRid1, $liRid1, $liRect1, $winRid1, $winRect1, $btnRid2, $liRid2, $liRect2, $winRid2, $winRect2) {
    if ($btnRid1 -eq '' -or $btnRid1 -eq 'rid-error' -or $liRid1 -eq '' -or $liRid1 -eq 'rid-error' -or $winRid1 -eq '' -or $winRid1 -eq 'rid-error') { return 'unreadable-rid' }
    if ($btnRid1 -ne $btnRid2 -or $liRid1 -ne $liRid2) { return 'rid-changed' }
    if ($winRid1 -ne $winRid2) { return 'root-identity-changed' }
    if ([int]$liRect1.Left -ne [int]$liRect2.Left -or [int]$liRect1.Top -ne [int]$liRect2.Top -or [int]$liRect1.Right -ne [int]$liRect2.Right -or [int]$liRect1.Bottom -ne [int]$liRect2.Bottom) { return 'item-geometry-changed' }
    if ([int]$winRect1.Left -ne [int]$winRect2.Left -or [int]$winRect1.Top -ne [int]$winRect2.Top -or [int]$winRect1.Right -ne [int]$winRect2.Right -or [int]$winRect1.Bottom -ne [int]$winRect2.Bottom) { return 'window-geometry-changed' }
    return 'ok'
  }
  function Log-SavedCardDiag([string]$tag) {
    $dg = $null; $gDpi3 = $null; $ubmp = $null; $ibmp = $null; $dbmp = $null
    $outcome = 'started'
    $sum = New-Object System.Collections.Generic.List[string]
    try {
      $appPid = $win.Current.ProcessId
      $winRid = ''; try { $winRid = ($win.GetRuntimeId() -join ',') } catch { $winRid = 'rid-error' }
      $winRect1 = $win.Current.BoundingRectangle
      if (-not (Test-DiagRectFinitePositive $winRect1)) { $outcome = 'refused: window rectangle not finite-positive at structure read'; return }
      $sum.Add("appPid=$appPid rootRid=[$winRid] winRect=L$([int]$winRect1.Left),T$([int]$winRect1.Top),R$([int]$winRect1.Right),B$([int]$winRect1.Bottom)")
      $dBtn = Btn 'E2E ws renamed'
      if ($null -eq $dBtn) { $outcome = 'refused: named button not found'; return }
      $btnNode = $null
      try { $btnNode = Read-DiagNode $dBtn $appPid } catch { $outcome = "refused: button read-error $($_.Exception.Message)"; return }
      if ($null -eq $btnNode) { $outcome = 'refused: named button outside app process'; return }
      $tStruct0 = [DateTime]::UtcNow.ToString('HH:mm:ss.fff')
      $asc1 = Walk-DiagAscent $dBtn { param($e) $tw2.GetParent($e) } 8 $winRid $appPid
      $sum.Add("ascent1 hops=$($asc1.hops) hitRoot=$($asc1.hitRoot) leftApp=$($asc1.leftApp) budget=$($asc1.budgetHit) errs=$($asc1.errs) :: $($asc1.lines -join ' || ')")
      $ac1 = Test-DiagAscentClean $asc1
      if ($ac1 -ne 'ok') { $outcome = "refused: structure ascent unclean: $ac1 (same-PID alone is not a verified app subtree)"; return }
      $dLiNode = $null
      foreach ($nd in $asc1.nodes) { if ($nd.ct -eq 'ControlType.ListItem') { $dLiNode = $nd; break } }
      if ($null -eq $dLiNode) { $outcome = 'refused: list item not found in bounded in-app ascent'; return }
      if (-not (Test-DiagRectFinitePositive $dLiNode.rect)) { $outcome = 'refused: item rectangle not finite-positive at structure read'; return }
      $dLi = $dLiNode.el
      $btnRid1 = $btnNode.rid; $liRid1 = $dLiNode.rid; $liRect1 = $dLiNode.rect
      $sum.Add("titleBtnRid=[$btnRid1] listItemRid=[$liRid1] itemRect1=L$([int]$liRect1.Left),T$([int]$liRect1.Top),R$([int]$liRect1.Right),B$([int]$liRect1.Bottom)")
      $sel = 'IsSelected=n/a'
      try { $sel = "IsSelected=$($dLi.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected)" } catch { $sel = 'IsSelected=pattern-unavailable' }
      $foc = 'focused=unread'
      try { $fe = [System.Windows.Automation.AutomationElement]::FocusedElement
        if ($fe) { if ($fe.Current.ProcessId -eq $appPid) { $foc = "focused(in-app)='$($fe.Current.Name)' ($($fe.Current.ControlType.ProgrammaticName))" } else { $foc = 'focused=outside-app (details not logged)' } } else { $foc = 'focused=none' }
      } catch { $foc = 'focused=read-error' }
      $sum.Add("$sel | $foc")
      $walk = Walk-DiagDescendants $dLi { param($e) [System.Windows.Automation.TreeWalker]::RawViewWalker.GetFirstChild($e) } { param($e) [System.Windows.Automation.TreeWalker]::RawViewWalker.GetNextSibling($e) } { param($e) Read-DiagNode $e $appPid } 60 6 400
      $tStruct1 = [DateTime]::UtcNow.ToString('HH:mm:ss.fff')
      $sum.Add("walkBudgets nodes=$($walk.nodes)/60 unique=$($walk.unique) foreignSkipped=$($walk.foreign) depthCap=6 depthSkipped=$($walk.depthSkipped) enumCalls=$($walk.enumCalls)/400 (enumeration calls only; property/rid reads NOT budget-counted) readErr=$($walk.errs) truncated=$($walk.truncated)")
      $sum.Add("descendants: $($walk.lines -join ' || ')")
      $union = RectUnion $walk.rects
      if ($null -eq $union) { $outcome = 'refused: no positive in-app descendant rects (see budgets)'; return }
      $liRect1b = $dLiNode.rect
      $outsideCount = Rect-OutsideCount $walk.rects $liRect1b
      $sum.Add("structureWindowUtc=$tStruct0-$tStruct1 union=L$($union.L),T$($union.T),R$($union.R),B$($union.B) members=$($union.n) offscreenMembers=$($walk.offscreenMembers) outsideItemMembers=$outsideCount (union members from THIS earlier NON-ATOMIC walk, offscreen/outside members counted and INCLUDED, union NOT revalidated at capture, NOT painted-face bounds)")
      # identity + geometry + root revalidation immediately before capture
      $dBtn2 = Btn 'E2E ws renamed'
      if ($null -eq $dBtn2) { $outcome = 'refused: named button gone at capture'; return }
      $btnNode2 = $null
      try { $btnNode2 = Read-DiagNode $dBtn2 $appPid } catch { $outcome = "refused: capture-time button read-error $($_.Exception.Message)"; return }
      if ($null -eq $btnNode2) { $outcome = 'refused: capture-time button outside app process'; return }
      $asc2 = Walk-DiagAscent $dBtn2 { param($e) $tw2.GetParent($e) } 8 $winRid $appPid
      $sum.Add("ascent2 hops=$($asc2.hops) hitRoot=$($asc2.hitRoot) leftApp=$($asc2.leftApp) budget=$($asc2.budgetHit) errs=$($asc2.errs)")
      $ac2 = Test-DiagAscentClean $asc2
      if ($ac2 -ne 'ok') { $outcome = "refused: capture ascent unclean: $ac2"; return }
      $dLiNode2 = $null
      foreach ($nd in $asc2.nodes) { if ($nd.ct -eq 'ControlType.ListItem') { $dLiNode2 = $nd; break } }
      if ($null -eq $dLiNode2) { $outcome = 'refused: list item gone at capture'; return }
      if (-not (Test-DiagRectFinitePositive $dLiNode2.rect)) { $outcome = 'refused: item rectangle not finite-positive at capture read'; return }
      $winRid2 = ''; try { $winRid2 = ($win.GetRuntimeId() -join ',') } catch { $winRid2 = 'rid-error' }
      $winRect2 = $win.Current.BoundingRectangle
      if (-not (Test-DiagRectFinitePositive $winRect2)) { $outcome = 'refused: window rectangle not finite-positive at capture read'; return }
      $rootPid2 = -1; try { $rootPid2 = $win.Current.ProcessId } catch { $rootPid2 = -1 }
      if ($rootPid2 -ne $appPid) { $outcome = "refused: root-pid-changed($appPid->$rootPid2) before capture"; return }
      $ident = Compare-DiagIdentity $btnRid1 $liRid1 $liRect1 $winRid $winRect1 $btnNode2.rid $dLiNode2.rid $dLiNode2.rect $winRid2 $winRect2
      $sum.Add("identityAtCapture=$ident (container rids + int item/window geometry + fresh root pid/rid revalidated; button rect and descendant-union member geometry NOT revalidated)")
      if ($ident -ne 'ok') { $outcome = "refused: $ident before capture (stale structure is not evidence)"; return }
      $liR = $dLiNode2.rect; $wrD = $winRect2
      $vsD = [System.Windows.Forms.SystemInformation]::VirtualScreen
      $contained = ($wrD.Width -gt 40 -and $wrD.Height -gt 20 -and $wrD.Left -ge $vsD.Left -and $wrD.Top -ge $vsD.Top -and $wrD.Right -le $vsD.Right -and $wrD.Bottom -le $vsD.Bottom -and $liR.Left -ge ($wrD.Left - 2) -and $liR.Top -ge ($wrD.Top - 2) -and $liR.Right -le ($wrD.Right + 2) -and $liR.Bottom -le ($wrD.Bottom + 2))
      $sum.Add("containmentAtCapture=$contained")
      if (-not $contained) { $outcome = "refused: window/item containment invalid at capture (win=$([int]$wrD.Left),$([int]$wrD.Top),$([int]$wrD.Right),$([int]$wrD.Bottom) item=$([int]$liR.Left),$([int]$liR.Top),$([int]$liR.Right),$([int]$liR.Bottom))"; return }
      # ONE immutable APP-WINDOW bitmap; every diagnostic crop/reading below derives from these SAME bytes
      $tCap0 = [DateTime]::UtcNow.ToString('HH:mm:ss.fff')
      $aw = [int]$wrD.Width; $ah = [int]$wrD.Height
      $dbmp = New-Object System.Drawing.Bitmap $aw, $ah; $dg = [System.Drawing.Graphics]::FromImage($dbmp); $dg.CopyFromScreen([int]$wrD.Left, [int]$wrD.Top, 0, 0, $dbmp.Size)
      $tCap1 = [DateTime]::UtcNow.ToString('HH:mm:ss.fff')
      $dg.Dispose(); $dg = $null
      $bmpId = "diag-$tag-appbitmap"
      $sum.Add("bitmap=$bmpId capturedUtc=$tCap0-$tCap1 (positive ${aw}x${ah}; item/union crops derive from THESE bytes; the original oracle crop and Shot are SEPARATE earlier captures)")
      $dbmp.Save("$PWD/out-images/$bmpId.png")
      $gDpi3 = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero); $dpiX3 = $gDpi3.DpiX; $dpiY3 = $gDpi3.DpiY
      $iL = [math]::Max(0, [int]$liR.Left - [int]$wrD.Left); $iT = [math]::Max(0, [int]$liR.Top - [int]$wrD.Top)
      $iR = [math]::Min($aw, [int]$liR.Right - [int]$wrD.Left); $iB = [math]::Min($ah, [int]$liR.Bottom - [int]$wrD.Top)
      $readings = ''; $uInfo = "union=L$($union.L),T$($union.T),R$($union.R),B$($union.B) n=$($union.n)"
      if (($iR - $iL) -gt 10 -and ($iB - $iT) -gt 10) {
        $ibmp = $dbmp.Clone((New-Object System.Drawing.Rectangle($iL, $iT, ($iR - $iL), ($iB - $iT))), $dbmp.PixelFormat)
        $ibmp.Save("$PWD/out-images/diag-$tag-item.png")
        $uL2 = [math]::Max(0, $union.L - [int]$wrD.Left); $uT2 = [math]::Max(0, $union.T - [int]$wrD.Top)
        $uR2 = [math]::Min($aw, $union.R - [int]$wrD.Left); $uB2 = [math]::Min($ah, $union.B - [int]$wrD.Top)
        $uInfo += " clamped-rel=$uL2,$uT2-$uR2,$uB2"
        if (($uR2 - $uL2) -gt 10 -and ($uB2 - $uT2) -gt 10) {
          $ubmp = $dbmp.Clone((New-Object System.Drawing.Rectangle($uL2, $uT2, ($uR2 - $uL2), ($uB2 - $uT2))), $dbmp.PixelFormat)
          $ubmp.Save("$PWD/out-images/diag-$tag-union.png")
          $uCols = UnionDiagColumns 0 ($uR2 - $uL2)
          $uMidY = [int](($uB2 - $uT2) / 2)
          $rd = @()
          foreach ($ux in $uCols) { $upx = $ubmp.GetPixel([math]::Min($ux, ($uR2 - $uL2) - 1), $uMidY); $rd += "x$($uL2 + $ux):rgb($($upx.R),$($upx.G),$($upx.B))" }
          $readings = ($rd -join ' ')
        } else { $readings = 'union region too small after clamp; no union crop or readings' }
      } else { $readings = 'item region too small after clamp; no crops or readings'; $uInfo += ' (no crops)' }
      $sum.Add("$uInfo | unionMidReadings: $readings | dpi=$dpiX3,$dpiY3 | imgs=out-images/$bmpId.png,diag-$tag-item.png,diag-$tag-union.png")
      $outcome = 'captured'
    } catch { $outcome = "threw: $($_.Exception.Message)" }
    finally {
      foreach ($res in @($dg, $gDpi3, $ubmp, $ibmp, $dbmp)) { if ($res) { try { $res.Dispose() } catch { } } }
      try { Info "saved-card diag ($tag): $outcome (diagnostic only; original assertions/captures unchanged)" ($sum -join ' | ') } catch { }
    }
  }
  # DIAG-FN-END

  # PWCAP-DIAG-START (r8 candidate): saved-card PrintWindow-capture diagnostic oracle - ADDITIVE
  # diagnostic only (same discipline as DIAG-FN above): no assertion, threshold, oracle, mask, capture
  # or drive changes to the existing native flow; the original measurements/captures/assertions above
  # still run and still gate, byte-for-byte unchanged. One complete self-contained section: new Win32
  # bindings (WinCap), the reviewed structure helper (MEASURE-V4/ORACLE-STRUCTURE-V4), the reviewed
  # adapter (ADAPTER-V4), pure capture helpers (HARNESS-PURE), the real one-shot PrintWindow capture
  # callback, the Read-ValidatedRoot boundary validator (fresh checked Win32 handle/PID/rect/DPI at
  # EVERY root read, incl. the last provider boundary), and the Invoke-PrintWindowDiag driver bound to
  # the landed symbols ($p, $win,
  # $script:mainHwnd, Btn, $tw2). Invoked at TWO additive sites, once per saved-card state
  # ('with-image' and 'no-image'), each immediately after the landed Log-SavedCardDiag call.
  # ----- Win32 capture bindings (new type; no collision with landed W / WinN / DlgDrive) ----------
  Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WinCap {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  // GetWindowThreadProcessId returns the THREAD id; 0 means the call failed. The landed WinN.PidOf
  // discards that return code, so the boundary check below uses this explicit-failure variant
  // (reviewer 04:10:41: refuse failed return codes, never rely on zero-filled out params).
  public static bool TryPidOf(IntPtr h, out uint pid) { uint p; uint tid = GetWindowThreadProcessId(h, out p); pid = p; return tid != 0; }
}
'@

  # HARNESS-PURE-BEGIN
  function Copy-LockedBitsRows([IntPtr]$scan0, [int]$strideSigned, [int]$w, [int]$h) {
    # Row-by-row copy with REAL signed-stride address arithmetic: row y starts at scan0 + y*stride.
    # strideSigned < 0 (bottom-up surface) is handled by the same arithmetic; scan0 must point at the
    # first IMAGE row (as LockBits guarantees). Returns the raw strided buffer (Abs(stride)*h bytes).
    $strideAbs = [Math]::Abs($strideSigned)
    if ($strideAbs -lt ($w * 3)) { throw "stride $strideAbs < width*3 $($w*3)" }
    $raw = New-Object byte[] ($strideAbs * $h)
    for ($y = 0; $y -lt $h; $y++) {
      $src = [IntPtr]($scan0.ToInt64() + [long]$y * $strideSigned)
      [System.Runtime.InteropServices.Marshal]::Copy($src, $raw, $y * $strideAbs, $strideAbs)
    }
    return ,$raw
  }
  function Test-CanonicalNotBlank([byte[]]$canon) {
    foreach ($bb in $canon) { if ($bb -ne 0) { return $true } }
    return $false
  }
  function Compare-HwndGeometry([int]$w32L, [int]$w32T, [int]$w32R, [int]$w32B, [int]$w32Pid, $uiaRect, [int]$uiaPid) {
    # Exact-int consistency between the Win32 handle view and the UIA view (96-DPI scope): the
    # required HWND/PID/rect validation AT the last provider boundary, no fallback, no retry.
    if ($w32Pid -ne $uiaPid) { return "pid mismatch (win32=$w32Pid uia=$uiaPid)" }
    if ([int]$uiaRect.Left -ne $w32L -or [int]$uiaRect.Top -ne $w32T -or [int]$uiaRect.Right -ne $w32R -or [int]$uiaRect.Bottom -ne $w32B) {
      return "rect mismatch (win32=$w32L,$w32T,$w32R,$w32B uia=$([int]$uiaRect.Left),$([int]$uiaRect.Top),$([int]$uiaRect.Right),$([int]$uiaRect.Bottom))"
    }
    return 'ok'
  }
  # HARNESS-PURE-END

  function Read-ValidatedRoot($winEl, $appPid, $mainHwnd, $fnIsWindow, $fnTryPidOf, $fnGetWindowRect, $fnGetDpiForWindow) {
    # THE checked Win32 handle/PID/rect/DPI validation, performed FRESH at EVERY root read (reviewer
    # 04:18:27: an early-only check before pass 1 left the boundary read reusing cached hwnd/dpi).
    # The helper invokes rootReader at structure start AND at the LAST PROVIDER BOUNDARY (after the
    # second finder/ascent, BEFORE the final member walk), so a mutation at the second-finder
    # boundary is caught HERE even when both UIA root snapshots stay consistent. No Win32/UTA
    # validation exists after pass 2; the residual post-boundary TOCTOU window is declared, not
    # hidden. Win32 access goes through the injected lambdas (production: WinN/WinCap/W) so this
    # exact composition is fixture-tested offline (eval RB3/RB4).
    $nd = Read-DiagNode $winEl $appPid   # production-shaped: {rid,rect,ct,name,off,el}, NO pid
    if ($null -eq $nd) { throw 'root unread at boundary read (outside app process)' }
    $hwndNow = [IntPtr][int64]($winEl.Current.NativeWindowHandle)
    if ($hwndNow -eq [IntPtr]::Zero) { throw 'boundary handle validation: UIA NativeWindowHandle is zero' }
    if ($null -eq $mainHwnd) { throw 'boundary handle validation: expected launched main window handle is null at the read (caller binding defect, fail-closed)' }
    if ($hwndNow -ne $mainHwnd) { throw ("boundary handle validation: current UIA handle 0x{0:X} != launched main window handle 0x{1:X}" -f $hwndNow.ToInt64(), ([IntPtr]$mainHwnd).ToInt64()) }
    if (-not (& $fnIsWindow $hwndNow)) { throw 'boundary handle validation: handle is not a live window' }
    $wpid = [uint32]0
    if (-not (& $fnTryPidOf $hwndNow ([ref]$wpid))) { throw 'boundary handle validation: GetWindowThreadProcessId failed (zero thread id)' }
    $wrC = & $fnGetWindowRect $hwndNow
    if ($null -eq $wrC) { throw 'boundary handle validation: GetWindowRect returned false' }
    $geo = Compare-HwndGeometry ([int]$wrC.L) ([int]$wrC.T) ([int]$wrC.Rt) ([int]$wrC.B) ([int]$wpid) $nd.rect $appPid
    if ($geo -ne 'ok') { throw "boundary handle/geometry validation: $geo" }
    $dpiNow = [uint32](& $fnGetDpiForWindow $hwndNow)
    if ($dpiNow -eq 0) { throw 'boundary handle validation: GetDpiForWindow failed (zero)' }
    if ($dpiNow -ne 96) { throw "boundary handle validation: non-96 DPI at boundary read (dpi=$dpiNow); validated scope is 96 DPI only" }
    # root-only fields attached EXPLICITLY after the production-shaped node read (reviewer 04:10:41)
    [pscustomobject]@{ rid = $nd.rid; rect = $nd.rect; rootPid = $appPid; hwnd = [int64]$hwndNow; dpiScale = 1.0 }
  }

  # ----- real one-shot capture callback (adapter RGB-payload contract) -----------------------------
  function Invoke-RealWindowCaptureV1($ctx) {
    # ctx: ONE-SNAPSHOT capture boundary ctx from Get-SavedCardStructureV4:
    #   union, contentRects, liRect, winRect, hwnd, winPid, winRid, dpiScale (copied at the boundary).
    $t0 = [DateTime]::UtcNow
    if ($null -eq $ctx.hwnd -or [int64]$ctx.hwnd -eq 0) { throw 'capture ctx carries no validated hwnd' }
    if ($null -eq $ctx.dpiScale -or [double]$ctx.dpiScale -ne 1.0) { throw "non-96 DPI (scale=$($ctx.dpiScale)); validated scope is 96 DPI only" }
    $w = [int]$ctx.winRect.Width; $h = [int]$ctx.winRect.Height
    if ($w -le 0 -or $h -le 0) { throw "validated window rect has non-positive extent ($w x $h)" }
    $bmp = $null; $g = $null; $data = $null; $locked = $false
    try {
      $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
      $g = [System.Drawing.Graphics]::FromImage($bmp)
      $hdc = $g.GetHdc()
      try {
        $okPrint = [WinCap]::PrintWindow([IntPtr][int64]$ctx.hwnd, $hdc, 2)  # PW_RENDERFULLCONTENT; ONE call, no retry
      } finally { $g.ReleaseHdc($hdc) }
      # Graphics disposed AFTER ReleaseHdc and BEFORE LockBits (a live Graphics makes LockBits throw ObjectBusy)
      $g.Dispose(); $g = $null
      if (-not $okPrint) { throw 'PrintWindow returned false (one-shot capture failed; no fallback, no retry)' }
      $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
      $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
      $locked = $true
      $stride = [int]$data.Stride  # signed; Copy-LockedBitsRows owns the address arithmetic
      $raw = Copy-LockedBitsRows $data.Scan0 $stride $w $h
      $bmp.UnlockBits($data); $locked = $false
      # canonicalize ONCE at the source: de-stride + BGR -> packed RGB
      $canon = ConvertTo-CanonicalRgb $raw $w $h ([Math]::Abs($stride)) 'BGR'
      $t1 = [DateTime]::UtcNow
      # blank/black surface (known WPF PrintWindow failure mode): FLAG, do NOT throw - the pixels are
      # retained and the adapter classifies this as CAPTURE-TRANSPORT, not a render observation.
      $blank = -not (Test-CanonicalNotBlank $canon)
      return [pscustomobject]@{
        rgb = $canon; width = $w; height = $h; stride = ($w * 3); channelOrder = 'RGB'
        sha256 = (Get-BytesSha256 $canon)
        captureStartedUtc = $t0; captureEndedUtc = $t1   # typed UTC DateTimes (Kind=Utc)
        pid = $ctx.winPid; winRid = [string]$ctx.winRid
        originL = [int]$ctx.winRect.Left; originT = [int]$ctx.winRect.Top
        blank = $blank
        bitmap = $null  # no live object leaves the callback
      }
    } finally {
      # consolidated, flag-guarded, exactly-once cleanup (no catch/finally double-dispose)
      if ($locked -and $null -ne $bmp -and $null -ne $data) { try { $bmp.UnlockBits($data) } catch {}; $locked = $false }
      if ($null -ne $g)   { try { $g.Dispose() } catch {}; $g = $null }
      if ($null -ne $bmp) { try { $bmp.Dispose() } catch {}; $bmp = $null }
    }
  }

  # ----- additive diagnostic driver: boundary validation -> structure -> capture -> oracle ---------
  function Invoke-PrintWindowDiag([string]$tag) {
    # DIAGNOSTIC ONLY (same discipline as landed Log-SavedCardDiag): never asserts, never changes the
    # existing flow; every refusal is logged via Info. Uses ONLY landed symbols ($p, $win,
    # $script:mainHwnd, Btn, $tw2 ControlViewWalker, RawViewWalker, Read-DiagNode, Walk-DiagAscent,
    # Test-DiagRectFinitePositive, Compare-DiagIdentity, RectUnion, Rect-OutsideCount, Info) plus the
    # new WinCap type and the reviewed oracle functions above. No undefined external assumptions.
    $sum = New-Object System.Collections.Generic.List[string]
    $outcome = 'started'
    $stage = 'init'
    $oracle = $null  # r12: initialized so every refusal path exposes null (missing evidence refuses)
    try {
      $stage = 'pid-binding'
      # EXPLICIT root pid binding (reviewer 04:10:41): landed Read-DiagNode returns
      # {rid,rect,ct,name,off,el} with NO pid field; the pid comes ONLY from the UIA root itself.
      $appPid = 0; try { $appPid = [int]($win.Current.ProcessId) } catch { $appPid = 0 }
      if ($appPid -le 0) { $outcome = 'refused: UIA root pid unreadable'; return }
      if ($appPid -ne [int]$p.Id) { $outcome = "refused: UIA root pid $appPid != launched process pid $($p.Id)"; return }
      $stage = 'provider-bindings'
      # provider bindings over LANDED functions only; oracle providers are SINGLE-SHOT (no retry).
      $P = @{
        btnFinder = { param($n) Btn $n }.GetNewClosure()
        getParent = { param($e) $tw2.GetParent($e) }.GetNewClosure()
        getFC     = { param($e) [System.Windows.Automation.TreeWalker]::RawViewWalker.GetFirstChild($e) }.GetNewClosure()
        getNS     = { param($e) [System.Windows.Automation.TreeWalker]::RawViewWalker.GetNextSibling($e) }.GetNewClosure()
        readNode  = { param($e) Read-DiagNode $e $appPid }.GetNewClosure()
      }
      # production Win32 validation lambdas for Read-ValidatedRoot
      $fnIsWindow = { param($h) [WinN]::IsWindow($h) }.GetNewClosure()
      $fnTryPid   = { param($h, [ref]$po) [WinCap]::TryPidOf($h, $po) }.GetNewClosure()
      $fnGetRect  = { param($h) $r = New-Object W+R; if (-not [W]::GetWindowRect($h, [ref]$r)) { return $null }; return $r }.GetNewClosure()
      $fnGetDpi   = { param($h) [WinCap]::GetDpiForWindow($h) }.GetNewClosure()
      # rootReader: the helper invokes this at structure start AND at the LAST PROVIDER BOUNDARY
      # (after the second finder/ascent, BEFORE the final member walk). EACH invocation runs the full
      # checked Win32 handle/PID/rect/DPI validation FRESH inside Read-ValidatedRoot - nothing cached
      # (reviewer 04:18:27). Any mismatch THROWS (no fallback, no retry) and surfaces as a named
      # structure refusal.
      # run 43548927818 MEASURED: a GetNewClosure closure binds $script:-qualified reads to the
      # CLOSURE's own module scope, so $script:mainHwnd arrived NULL at the first root read (thrown
      # as 'Cannot convert null to type System.IntPtr' inside Read-ValidatedRoot). Cache ONLY the
      # EXPECTED launched-window identity into a LOCAL here (reviewer 05:17:44): the CURRENT
      # HWND/PID/rect/DPI are still re-read and re-validated FRESH at BOTH root reads inside
      # Read-ValidatedRoot - nothing about the live window is cached.
      $mainHwndForOracle = $script:mainHwnd
      if ($null -eq $mainHwndForOracle -or $mainHwndForOracle -eq [IntPtr]::Zero) { $outcome = 'refused: launched main window handle is null/zero at the driver read'; return }
      $P.rootReader = { Read-ValidatedRoot $win $appPid $mainHwndForOracle $fnIsWindow $fnTryPid $fnGetRect $fnGetDpi }.GetNewClosure()
      # landed source constants: Border Padding=4; FrameThicknessConverter (1,8,1,1) with image else 0.
      $pad = 4
      $isFrameP = { param($px) ([math]::Abs([int]$px.R - 191) -le 12 -and [math]::Abs([int]$px.G - 219) -le 12 -and [math]::Abs([int]$px.B - 254) -le 12) }
      $isWhite  = { param($px) ([int]$px.R -ge 247 -and [int]$px.G -ge 247 -and [int]$px.B -ge 247) }
      if ($tag -eq 'with-image') {
        $frameExp = [ordered]@{ l = 1; t = 8; r = 1; b = 1 }
        $isInterior = { param($px) ([math]::Abs([int]$px.R - 30) -le 25 -and [math]::Abs([int]$px.G - 60) -le 25 -and [math]::Abs([int]$px.B - 220) -le 25) }  # same acceptance set as landed $isImg2
      } else {
        $frameExp = [ordered]@{ l = 0; t = 0; r = 0; b = 0 }
        $isInterior = { param($px) ([math]::Abs([int]$px.R - 191) -le 8 -and [math]::Abs([int]$px.G - 219) -le 8 -and [math]::Abs([int]$px.B - 254) -le 8) }  # same acceptance set as landed no-image center predicate
      }
      $pred = @{ isFrame = $isFrameP; isInterior = $isInterior; isBackground = $isWhite }
      # VERIFIED artifact sink (reviewer 04:10:41): PER-STATE distinct paths (no with-image/no-image
      # overwrite) and read-back SHA256 compared to the expected hash, $true only on equality.
      $sink = { param($name, [byte[]]$bytes)
        $path = Join-Path "$PWD/out-images" "diag-pw-$tag-$name"
        [System.IO.File]::WriteAllBytes($path, $bytes)
        $back = [System.IO.File]::ReadAllBytes($path)
        if ((Get-BytesSha256 $back) -ne (Get-BytesSha256 $bytes)) { throw "artifact read-back SHA256 mismatch for $path" }
        return $true
      }.GetNewClosure()
      # bounded minimums (explicit): minFreePixels 400, minRegionPixels 40, minHaloPixels 200.
      # dpi-scale argument 1.0 is justified: EVERY root read refuses non-96 DPI inside
      # Read-ValidatedRoot, and the capture ctx dpiScale is boundary-fresh; the adapter's
      # measure-scale guard remains as defense in depth.
      $stage = 'oracle-call'
      $oracle = Invoke-SavedCardOracleV4 'E2E ws renamed' $P { param($ctx) Invoke-RealWindowCaptureV1 $ctx } $pred $frameExp $pad 1.0 400 40 200 $sink
      # OLD-vs-NEW (both preserved and inspectable; r12: the old readings above are now non-gating
      # LEGACY Info entries - the old anchor is invalid per reviewer 05:55:38 - and the GATE assertion
      # after this driver call is the gating check):
      $oldState = ''
      if ($tag -eq 'with-image') {
        try { $oldState = "LEGACY(with-image, non-gating reading above): mres.ok=$($script:mres.ok) reason='$($script:mres.reason)' top=$($script:mres.top) left=$($script:mres.left) right=$($script:mres.right) bot=$($script:mres.bot) colsClean=$($script:mres.colsClean) colsPass=$($script:mres.colsPass)" } catch { $oldState = 'LEGACY(with-image): mres unreadable' }
      } else {
        try { $oldState = "LEGACY(no-image, non-gating reading above): okCardBg=$($script:okCardBg) seen='$($script:seenCardBg)'" } catch { $oldState = 'LEGACY(no-image): okCardBg unreadable' }
      }
      $prov = if ($null -ne $oracle.provenance) { "$($oracle.provenance.verification)" } else { 'none' }
      $stage = 'oracle-report'
      # RESULT-FIRST recording (reviewer 04:49:14): gates/reason/captureCalls/provenance go into $sum
      # via List.Add BEFORE any summary formatting, so a formatting failure can never mask the actual
      # result. NEVER array-wrap Generic.List fields: @($list) on List[object] throws 'Argument types do
      # not match' under pwsh 7.4.6 (reviewer 04:49:14 repro + independent author repro) - the exact
      # defect that masked the r8 run's underlying refusal behind an empty-detail throw line.
      try {
        $sum.Add("gates ok=$($oracle.ok) structure=$($oracle.structureOk) capture=$($oracle.captureOk) measure=$($oracle.measureOk) evidence=$($oracle.evidenceOk) transport=$($oracle.captureTransportIssue)")
        $sum.Add("reason=$($oracle.reason)")
        $sum.Add("captureCalls=$($oracle.captureCalls) captureDisposals=$($oracle.captureDisposals) prov=$prov artifactsN=$(if ($null -ne $oracle.artifacts) { $oracle.artifacts.Count } else { 0 }) warningsN=$(if ($null -ne $oracle.cleanupWarnings) { $oracle.cleanupWarnings.Count } else { 0 })")
        if ($null -ne $oracle.cleanupWarnings -and $oracle.cleanupWarnings.Count -gt 0) { $sum.Add("warnings=$($oracle.cleanupWarnings -join '; ')") }
        $sum.Add($oldState)
      } catch { try { $sum.Add("result-record threw: [$($_.Exception.GetType().FullName)] $($_.Exception.Message)") } catch { } }
      $outcome = "oracle ok=$($oracle.ok) structure=$($oracle.structureOk) capture=$($oracle.captureOk) measure=$($oracle.measureOk) evidence=$($oracle.evidenceOk) transport=$($oracle.captureTransportIssue) reason=$($oracle.reason) prov=$prov artifacts=$($oracle.artifacts.Count) warnings=$($oracle.cleanupWarnings -join '; ') | $oldState"
    } catch { $outcome = "threw at stage=${stage}: [$($_.Exception.GetType().FullName)] $($_.Exception.Message) | stack: $($_.ScriptStackTrace) | at: $($_.InvocationInfo.PositionMessage)" }
    finally {
      # r12: expose the ACTUAL oracle result + outcome at flow scope so the GATE assertion uses the SAME
      # single oracle run (no recapture); stays $null on every refusal path (missing evidence refuses).
      $script:pwDiagTag = $tag
      $script:pwDiagOutcome = $outcome
      $script:pwDiagOracle = $oracle
      try { Info "saved-card PrintWindow diag ($tag): $outcome (audit only; native upload/save/remove flow unchanged; LEGACY readings above non-gating, the GATE assertion is the gating check)" ($sum -join ' | ') } catch { }
    }
  }

  # ----- additive gate functions (r12): verified audit export + the gating oracle assertion -----------
  # The GATE replaces the old item-fraction sampling assertions as the gating check: the SAME single
  # oracle result captured by the driver above (no recapture) must show structure+capture+measure+
  # evidence ok on the ONE actual capture, at the named 96 DPI model domain. Unsupported DPI, missing
  # oracle evidence, or an unverifiable audit export all REFUSE (no silent green).
  function Export-PwAudit($tag, $oracle, $outcome) {
    # Persists the ACTUAL oracle runtime data (provenance incl. hashes, structure snapshot with member
    # masks, measurement object, evidence rects/hashes, artifact paths) BEFORE the gate assertion.
    # Verified (reviewer 06:02:12): serialize ONCE to UTF8 bytes -> WriteAllBytes -> ReadAllBytes ->
    # EXACT SHA256 byte-identity -> parse -> required fields. A different/stale valid JSON can never
    # pass as verified. Every failure sets a named $script:pwAuditError and returns $null (gate refuses).
    if ($null -eq $oracle) { $script:pwAuditError = 'oracle result null'; return $null }
    $artList = New-Object System.Collections.Generic.List[object]
    if ($null -ne $oracle.artifacts) { foreach ($a in $oracle.artifacts) { $artList.Add([ordered]@{ name = [string]$a.name; sha256 = [string]$a.sha256; width = $a.width; height = $a.height; path = "out-images/diag-pw-$tag-$($a.name)" }) } }
    $warnList = New-Object System.Collections.Generic.List[string]
    if ($null -ne $oracle.cleanupWarnings) { foreach ($w in $oracle.cleanupWarnings) { $warnList.Add([string]$w) } }
    $audit = [ordered]@{
      tag = $tag; recordedAtUtc = [DateTime]::UtcNow.ToString('o'); outcome = [string]$outcome
      domain = '96 DPI model-frame domain: FrameThicknessConverter (1,8,1,1) with-image / (0,0,0,0) no-image at dpiScale 1.0, Border Padding=4; residual limits DECLARED: model-not-Border, occlusion, corner-AA, post-snapshot TOCTOU'
      gates = [ordered]@{ ok = $oracle.ok; structureOk = $oracle.structureOk; captureOk = $oracle.captureOk; measureOk = $oracle.measureOk; evidenceOk = $oracle.evidenceOk; captureTransportIssue = $oracle.captureTransportIssue; captureCalls = $oracle.captureCalls; captureDisposals = $oracle.captureDisposals }
      reason = [string]$oracle.reason; measureReason = [string]$oracle.measureReason
      provenance = $oracle.provenance; evidence = $oracle.evidence; structure = $oracle.structure; measurement = $oracle.measurement
      artifacts = $artList; cleanupWarnings = $warnList
    }
    $json = $audit | ConvertTo-Json -Depth 10
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    # absolute via $PWD (same convention as the shipped artifact sink): .NET IO resolves relative
    # paths against the PROCESS cwd, not the PowerShell location.
    $p = Join-Path "$PWD/diag" "audit-pw-$tag.json"
    [System.IO.File]::WriteAllBytes($p, $bytes)
    $backBytes = [System.IO.File]::ReadAllBytes($p)
    $hW = Get-BytesSha256 $bytes; $hR = Get-BytesSha256 $backBytes
    if (-not [string]::Equals($hR, $hW, [StringComparison]::Ordinal)) { $script:pwAuditError = "audit read-back SHA256 mismatch (wrote $hW read $hR); refusing to treat the audit as verified"; return $null }
    $back = [System.Text.Encoding]::UTF8.GetString($backBytes) | ConvertFrom-Json
    if ($null -eq $back -or $back.tag -ne $tag) { $script:pwAuditError = 'audit read-back unparseable or tag mismatch'; return $null }
    if ($null -eq $back.gates -or $null -eq $back.provenance -or $null -eq $back.evidence -or $null -eq $back.structure -or $null -eq $back.measurement) { $script:pwAuditError = 'audit read-back missing required fields'; return $null }
    return $back
  }
  function Assert-PwGate($tag) {
    # Cross-state binding (reviewer 06:02:12): the gate accepts only the result the driver exposed for
    # THIS state (Ordinal-exact tag match, known states only). A stale/cross-state/unknown tag refuses
    # BEFORE any audit write, so a wrong-state audit path is never produced. Ordinary functions, no
    # GetNewClosure for globals; no extra capture/provider read.
    $known = ($tag -eq 'with-image' -or $tag -eq 'no-image')
    $bound = $script:pwDiagTag
    $tagOk = $known -and ($null -ne $bound) -and [string]::Equals([string]$bound, [string]$tag, [StringComparison]::Ordinal)
    $o = $script:pwDiagOracle
    $auditOk = $false; $auditPath = "diag/audit-pw-$tag.json"; $script:pwAuditError = ''
    if ($tagOk) { try { $back = Export-PwAudit $tag $o $script:pwDiagOutcome; $auditOk = ($null -ne $back) } catch { $auditOk = $false; if ("$($script:pwAuditError)" -eq '') { $script:pwAuditError = "audit export threw: $($_.Exception.Message)" } } }
    else { $script:pwAuditError = "tag binding failed (known=$known bound='$bound' requested='$tag'); stale/cross-state/unknown state refuses before any audit write" }
    $hasO = ($null -ne $o)
    $gatesOk = $hasO -and $o.ok -and $o.structureOk -and $o.captureOk -and $o.measureOk -and $o.evidenceOk -and (-not $o.captureTransportIssue) -and ($o.captureCalls -eq 1)
    $dpiOk = $hasO -and ($null -ne $o.provenance) -and ($o.provenance.dpiScale -eq 1.0)
    $detail = "oracle result missing (driver outcome=$($script:pwDiagOutcome))"
    if ($hasO) { $detail = "outcome=$($script:pwDiagOutcome) gates ok=$($o.ok) structure=$($o.structureOk) capture=$($o.captureOk) measure=$($o.measureOk) evidence=$($o.evidenceOk) transport=$($o.captureTransportIssue) captureCalls=$($o.captureCalls) prov=$(if ($null -ne $o.provenance) { $o.provenance.verification } else { 'none' }) dpiScale=$(if ($null -ne $o.provenance) { $o.provenance.dpiScale } else { 'none' }) reason='$($o.reason)' measureReason='$($o.measureReason)'" }
    Assert "GATE saved-card PrintWindow oracle ($tag): structure+capture+measure+evidence on the ONE actual capture (96 DPI model domain, painted-bounds anchored; unsupported DPI or missing evidence/audit refuses)" ($tagOk -and $gatesOk -and $dpiOk -and $auditOk) ("tag=$tag bound=$bound tagOk=$tagOk | $detail | audit=$auditPath verified=$auditOk auditErr='$($script:pwAuditError)'")
  }

  # MEASURE-V4-BEGIN (r8 domain correction, THIRD cut - offline evaluator canonical source; NOT landed):
  # saved-card oracle anchored at VERIFIED STRUCTURE (two-pass member+parent topology snapshot, exact
  # every-field compare immediately before immutable capture) with a GROUNDED TOPOLOGY CONTRACT (parent
  # RID bindings, identities grounded in the observed fixture, all-field finite + integral rects).
  # EDGE ELIGIBILITY IS STRUCTURALLY PREDECLARED: per scanline the predicted frame band must be
  # mask-free (computed from member rects alone, BEFORE any pixel read); a mask collision makes the
  # line non-covering (counted) and per-edge coverage below half fails ('required edge hidden by
  # mask'). Defects in unmasked band pixels always count; exclusion is never pixel-run dependent.
  # Frame relations are the LANDED r7f relations at 96 DPI (sideTol=1, thickGap=2): left>=1, right>=1,
  # bot>=1, bot<=max(left,right)+1, top-left>=2, top-right>=2, top-bot>=2; measurement caps 12/6 are
  # bounded 96-DPI choices. OUTSIDE GUARD is bounded: the 1px STRAIGHT-EDGE halo (6px corner squares
  # excluded) must match the observed runner background (white, per-channel >= 247, bounded tolerance
  # choice) - it is NOT a general paint-free proof and cannot see disconnected paint beyond the halo.
  # Validated scope: 96 DPI, integral member rects (fractional refused), straight edges. All content
  # exclusions structural + predeclared (member rects +1px guard; CornerRadius=4 AA 6px corners).
  # Reviewer counterexamples on prior cuts (outermost-row overwrite; erased top-frame segment between
  # sampled columns; go-button member drift between structure and capture; purple halo paint) are
  # permanent offline evaluator fixtures.
  function Get-PaintedRect($union, $frameExp, $pad) {
    if ($null -eq $union) { return $null }
    return [ordered]@{ L = ($union.L - $pad - $frameExp.l); T = ($union.T - $pad - $frameExp.t); R = ($union.R + $pad + $frameExp.r); B = ($union.B + $pad + $frameExp.b) }
  }
  function Test-RectContains($r, $x, $y, $guard) {
    return ($x -ge ($r.L - $guard) -and $x -le ($r.R + $guard) -and $y -ge ($r.T - $guard) -and $y -le ($r.B + $guard))
  }
  function Test-RectFullyFinite($r) {
    # EVERY numeric field finite (reviewer: V4 topology checked only Left/Top). Reuses the landed r7f
    # finite-positive helper first, then validates all six fields explicitly.
    if ($null -eq $r) { return $false }
    if (-not (Test-DiagRectFinitePositive $r)) { return $false }
    foreach ($f in 'Left', 'Top', 'Right', 'Bottom', 'Width', 'Height') {
      $v = [double]$r.$f
      if ([double]::IsNaN($v) -or [double]::IsInfinity($v)) { return $false }
    }
    return $true
  }
  function Test-RectIntegral($r) {
    # 96-DPI straight-edge domain: member rects must be integral so pixel masks need NO rounding.
    foreach ($f in 'Left', 'Top', 'Right', 'Bottom') { if ([math]::Abs([double]$r.$f - [math]::Round([double]$r.$f)) -gt 1e-6) { return $false } }
    return $true
  }
  function Test-SavedCardTopologyV4($nodes, $anchorName, $itemRid) {
    # Grounded accepted topology (observed both r7f states), now with PARENT-RID BINDINGS: exactly 3
    # direct (depth-1) Buttons parented to the item - the named anchor (title), 'Workspace actions',
    # and one other (go) - and exactly 5 depth-2 Texts bound by parentRid: title (== anchor name),
    # category, 'Last opened: *' parented to the anchor; the ellipsis label parented to the actions
    # button; go-label (== go button name) parented to the go button. Every node is a depth-1 Button
    # or a depth-2 Text; anything else refuses (unknown coverage). ALL rect fields finite-positive AND
    # integral (96-DPI straight-edge domain); all RIDs unique and readable; geometric containment kept
    # in ADDITION to parent binding. Masks = ALL node rects (buttons cover their texts).
    $res = [ordered]@{ ok = $false; reason = ''; maskRects = $null }
    if ($null -eq $nodes -or $nodes.Count -eq 0) { $res.reason = 'no nodes'; return $res }
    $ridSeen = @{}
    foreach ($n in $nodes) {
      $r = $n.rect
      if (-not (Test-RectFullyFinite $r)) { $res.reason = "node '$($n.name)' rect not finite-positive (all fields)"; return $res }
      if ($r.Width -le 0 -or $r.Height -le 0) { $res.reason = "node '$($n.name)' rect not positive"; return $res }
      if (-not (Test-RectIntegral $r)) { $res.reason = "node '$($n.name)' rect fractional (outside the 96-DPI integral domain)"; return $res }
      $rk = ($n.rid -join ',')
      if ([string]::IsNullOrEmpty($rk) -or $rk -eq 'rid-error') { $res.reason = "node '$($n.name)' rid unreadable"; return $res }
      if ($ridSeen.ContainsKey($rk)) { $res.reason = "duplicate runtime id '$rk'"; return $res }
      $ridSeen[$rk] = $true
      if ($null -eq $n.parentRid -or [string]::IsNullOrEmpty(($n.parentRid -join ','))) { $res.reason = "node '$($n.name)' has no parent rid (structure walk must record parentage)"; return $res }
      $isBtn = ($n.ct -eq 'ControlType.Button' -and $n.depth -eq 1)
      $isTxt = ($n.ct -eq 'ControlType.Text' -and $n.depth -eq 2)
      if (-not ($isBtn -or $isTxt)) { $res.reason = "unexpected node type/depth '$($n.ct)' depth=$($n.depth) name='$($n.name)' (unknown coverage)"; return $res }
    }
    $btns = @($nodes | Where-Object { $_.ct -eq 'ControlType.Button' })
    $txts = @($nodes | Where-Object { $_.ct -eq 'ControlType.Text' })
    if ($btns.Count -ne 3) { $res.reason = "expected exactly 3 direct buttons, got $($btns.Count)"; return $res }
    if ($txts.Count -ne 5) { $res.reason = "expected exactly 5 text nodes, got $($txts.Count)"; return $res }
    $itemRidK = ($itemRid -join ',')
    foreach ($b in $btns) { if (-not [string]::Equals(($b.parentRid -join ','), $itemRidK, [StringComparison]::Ordinal)) { $res.reason = "button '$($b.name)' not parented to the list item"; return $res } }
    $anchor = @($btns | Where-Object { [string]::Equals([string]$_.name, [string]$anchorName, [StringComparison]::Ordinal) }); if ($anchor.Count -ne 1) { $res.reason = "anchor button '$anchorName' count=$($anchor.Count)"; return $res }
    $actions = @($btns | Where-Object { [string]::Equals([string]$_.name, 'Workspace actions', [StringComparison]::Ordinal) }); if ($actions.Count -ne 1) { $res.reason = "actions button count=$($actions.Count)"; return $res }
    $go = @($btns | Where-Object { $_.rid -ne $anchor[0].rid -and $_.rid -ne $actions[0].rid })
    if ($go.Count -ne 1) { $res.reason = 'go button not uniquely identified'; return $res }
    $aK = ($anchor[0].rid -join ','); $gK = ($go[0].rid -join ','); $xK = ($actions[0].rid -join ',')
    $titleT = @($txts | Where-Object { [string]::Equals([string]$_.name, [string]$anchorName, [StringComparison]::Ordinal) }); if ($titleT.Count -ne 1) { $res.reason = "title text count=$($titleT.Count)"; return $res }
    $openedT = @($txts | Where-Object { $_.name -like 'Last opened:*' }); if ($openedT.Count -ne 1) { $res.reason = "'Last opened' text count=$($openedT.Count)"; return $res }
    $goT = @($txts | Where-Object { [string]::Equals([string]$_.name, [string]$go[0].name, [StringComparison]::Ordinal) }); if ($goT.Count -ne 1) { $res.reason = "go-label text count=$($goT.Count)"; return $res }
    # actions-label glyph MEASURED U+22EF (midline horizontal ellipsis) on the real app: runs 43548927818 +
    # 43551695254 JSONs carry U+22EF x63 and U+2026 x0 in BOTH states (reviewer 05:39:18, independently rechecked).
    # Exact-glyph Ordinal gate kept; U+2026 stays refused (no accept-both without product evidence).
    $dotsT = @($txts | Where-Object { [string]::Equals([string]$_.name, "`u{22ef}", [StringComparison]::Ordinal) }); if ($dotsT.Count -ne 1) { $res.reason = "actions-label text count=$($dotsT.Count)"; return $res }
    $catT = @($txts | Where-Object { $_.rid -ne $titleT[0].rid -and $_.rid -ne $openedT[0].rid -and $_.rid -ne $goT[0].rid -and $_.rid -ne $dotsT[0].rid })
    if ($catT.Count -ne 1) { $res.reason = "category text not uniquely identified ($($catT.Count))"; return $res }
    # parent bindings: each text bound to its GROUNDED parent button (not geometric containment alone)
    foreach ($t in @($titleT[0], $openedT[0], $catT[0])) { if (-not [string]::Equals(($t.parentRid -join ','), $aK, [StringComparison]::Ordinal)) { $res.reason = "anchor text '$($t.name)' parent rid is not the anchor button"; return $res } }
    if (-not [string]::Equals(($goT[0].parentRid -join ','), $gK, [StringComparison]::Ordinal)) { $res.reason = 'go-label text parent rid is not the go button'; return $res }
    if (-not [string]::Equals(($dotsT[0].parentRid -join ','), $xK, [StringComparison]::Ordinal)) { $res.reason = 'actions-label text parent rid is not the actions button'; return $res }
    $inside = { param($ir, $or2) ($ir.Left -ge $or2.Left -and $ir.Top -ge $or2.Top -and $ir.Right -le $or2.Right -and $ir.Bottom -le $or2.Bottom) }
    foreach ($t in @($titleT[0], $openedT[0], $catT[0])) { if (-not (& $inside $t.rect $anchor[0].rect)) { $res.reason = "anchor text '$($t.name)' not inside the anchor button"; return $res } }
    if (-not (& $inside $goT[0].rect $go[0].rect)) { $res.reason = 'go-label text not inside the go button'; return $res }
    if (-not (& $inside $dotsT[0].rect $actions[0].rect)) { $res.reason = 'actions-label text not inside the actions button'; return $res }
    $masks = New-Object System.Collections.Generic.List[object]
    foreach ($n in $nodes) { $masks.Add([ordered]@{ L = [int]$n.rect.Left; T = [int]$n.rect.Top; R = [int]$n.rect.Right; B = [int]$n.rect.Bottom }) }  # exact: integrality verified above
    $res.maskRects = $masks; $res.ok = $true
    return $res
  }
  function Measure-SavedCardFrameV4($bmp, $pRel, $contentRects, $isFrame, $isInterior, $isBackground, $frameExp, $scale, $minFreePixels, $minRegionPixels, $minHaloPixels) {
    $res = [ordered]@{ ok = $false; reason = ''; frameOk = $false; imgOk = $false; haloOk = $false; top = 0; left = 0; right = 0; bot = 0; freePx = 0; matchPx = 0; mismatch = ''; topCols = ''; botCols = ''; sideRowsL = ''; sideRowsR = ''; covTop = 0; covBot = 0; covL = 0; covR = 0; regionTop = 0; regionBottom = 0; regionLeft = 0; regionRight = 0; regionMid = 0; haloPx = 0 }
    if ($null -eq $bmp -or $null -eq $pRel) { $res.reason = 'null bitmap or painted rect'; return $res }
    if ([math]::Abs([double]$scale - 1.0) -gt 0.01) { $res.reason = "non-96 DPI (scale=$scale); validated scope is 96 DPI only"; return $res }
    $bw = $bmp.Width; $bh = $bmp.Height
    if (-not ($pRel.L -ge 1 -and $pRel.T -ge 1 -and $pRel.R -le ($bw - 1) -and $pRel.B -le ($bh - 1) -and ($pRel.R - $pRel.L) -ge 20 -and ($pRel.B - $pRel.T) -ge 10)) { $res.reason = "painted rect invalid/outside bitmap/touching edge - halo not verifiable (L$($pRel.L),T$($pRel.T),R$($pRel.R),B$($pRel.B) bmp ${bw}x${bh})"; return $res }
    if ($null -eq $contentRects -or $contentRects.Count -eq 0) { $res.reason = 'no content-exclusion rects (unknown coverage)'; return $res }
    $corner = 6; $guard = 1
    $noFrame = ($frameExp.l -eq 0 -and $frameExp.t -eq 0 -and $frameExp.r -eq 0 -and $frameExp.b -eq 0)
    # STRUCTURAL MASK HELPERS - computed from member rects ALONE, before/independent of any pixel read.
    $colMasked = { param($cx, $y0, $y1) foreach ($cr2 in $contentRects) { if (($cx -ge ($cr2.L - $guard)) -and ($cx -lt ($cr2.R + $guard)) -and (($cr2.B + $guard) -gt $y0) -and (($cr2.T - $guard) -lt $y1)) { return $true } } return $false }
    $rowMasked = { param($ry, $x0, $x1) foreach ($cr2 in $contentRects) { if (($ry -ge ($cr2.T - $guard)) -and ($ry -lt ($cr2.B + $guard)) -and (($cr2.R + $guard) -gt $x0) -and (($cr2.L - $guard) -lt $x1)) { return $true } } return $false }
    $pxMasked = { param($x, $y) foreach ($cr3 in $contentRects) { if (Test-RectContains $cr3 $x $y $guard) { return $true } } return $false }
    if (-not $noFrame) {
      $maxTop = 12; $maxSide = 6   # bounded 96-DPI measurement caps (model 8/1/1/1; scale refusal above)
      # TOP EDGE: predicted band [T, T+t). A column is COVERING iff its predicted band is mask-free
      # (structural, predeclared). Non-covering columns are counted, never silently skipped; per-edge
      # coverage below half fails. The run below can only stop at a non-frame pixel (defect, counted)
      # or at content BELOW the predicted band (allowed terminator).
      # a run terminated by the MASK is a LOWER BOUND (content hides the rest); a run terminated by a
      # non-frame pixel is EXACT. Consistency/relations use exact runs only; lower bounds must confirm.
      $topExact = @(); $topLB = @(); $topTotal = 0
      for ($cx = $pRel.L + $corner; $cx -lt $pRel.R - $corner; $cx++) {
        $topTotal++
        if (& $colMasked $cx $pRel.T ($pRel.T + $frameExp.t)) { continue }
        $res.covTop++
        $c = 0; $y = $pRel.T; $term = 'pixel'
        while ($c -le $maxTop -and $y -lt $pRel.B) {
          if (& $pxMasked $cx $y) { $term = 'mask'; break }
          $px = $bmp.GetPixel($cx, $y)
          if (-not (& $isFrame $px)) { break }
          $c++; $y++
        }
        if ($term -eq 'mask') { $topLB += $c } else { $topExact += $c }
      }
      if ($res.covTop -lt 2 -or $res.covTop -lt [int]($topTotal / 2)) { $res.reason = "required top edge hidden by mask ($($res.covTop) covering of $topTotal columns)"; return $res }
      if ($topExact.Count -lt 2) { $res.reason = "top edge thickness not exactly verifiable (mask terminates $($topLB.Count) of $($res.covTop) covering columns; the predicted band itself IS fully verified in every covering column)"; return $res }
      $tMin = ($topExact | Measure-Object -Minimum).Minimum; $tMax = ($topExact | Measure-Object -Maximum).Maximum
      $res.topCols = "covering=$($res.covTop)/$topTotal exact=$($topExact.Count) lb=$($topLB.Count) min=$tMin max=$tMax"
      if ($tMax - $tMin -gt 1) { $res.reason = "top frame inconsistent across full-width covering columns (min=$tMin max=$tMax)"; return $res }
      foreach ($lb in $topLB) { if ($lb -lt ($tMin - 1)) { $res.reason = "top frame defect above content (visible run $lb < measured $tMin)"; return $res } }
      if ($tMin -gt $maxTop) { $res.reason = "top frame exceeds the model bound ($tMin > $maxTop)"; return $res }
      $res.top = $tMin
      # BOTTOM EDGE: predicted band [B-b, B), scanned upward.
      $botExact = @(); $botLB = @(); $botTotal = 0
      for ($cx = $pRel.L + $corner; $cx -lt $pRel.R - $corner; $cx++) {
        $botTotal++
        if (& $colMasked $cx ($pRel.B - $frameExp.b) $pRel.B) { continue }
        $res.covBot++
        $c = 0; $y = $pRel.B - 1; $term = 'pixel'
        while ($c -le $maxSide -and $y -ge $pRel.T) {
          if (& $pxMasked $cx $y) { $term = 'mask'; break }
          $px = $bmp.GetPixel($cx, $y)
          if (-not (& $isFrame $px)) { break }
          $c++; $y--
        }
        if ($term -eq 'mask') { $botLB += $c } else { $botExact += $c }
      }
      if ($res.covBot -lt 2 -or $res.covBot -lt [int]($botTotal / 2)) { $res.reason = "required bottom edge hidden by mask ($($res.covBot) covering of $botTotal columns)"; return $res }
      if ($botExact.Count -lt 2) { $res.reason = "bottom edge thickness not exactly verifiable (mask terminates $($botLB.Count) of $($res.covBot) covering columns; the predicted band itself IS fully verified in every covering column)"; return $res }
      $bMin = ($botExact | Measure-Object -Minimum).Minimum; $bMax = ($botExact | Measure-Object -Maximum).Maximum
      $res.botCols = "covering=$($res.covBot)/$botTotal exact=$($botExact.Count) lb=$($botLB.Count) min=$bMin max=$bMax"
      if ($bMax - $bMin -gt 1) { $res.reason = "bottom frame inconsistent across full-width covering columns (min=$bMin max=$bMax)"; return $res }
      foreach ($lb in $botLB) { if ($lb -lt ($bMin - 1)) { $res.reason = "bottom frame defect above content (visible run $lb < measured $bMin)"; return $res } }
      if ($bMin -gt $maxSide) { $res.reason = "bottom frame exceeds the model bound ($bMin > $maxSide)"; return $res }
      $res.bot = $bMin
      # SIDE EDGES: interior rows clear of top/bottom/corner zones; predicted bands [L, L+l) / [R-r, R).
      $rowA = $pRel.T + [math]::Max($res.top, $corner) + $guard; $rowB = $pRel.B - [math]::Max($res.bot, $corner) - $guard
      if (($rowB - $rowA) -lt 2) { $res.reason = "no interior rows after top/bottom/corner inset (rows $rowA..$rowB)"; return $res }
      $leftExact = @(); $leftLB = @(); $rightExact = @(); $rightLB = @(); $sideTotal = 0
      for ($ry = $rowA; $ry -lt $rowB; $ry++) {
        $sideTotal++
        if (-not (& $rowMasked $ry $pRel.L ($pRel.L + $frameExp.l))) {
          $res.covL++
          $c = 0; $x = $pRel.L; $term = 'pixel'
          while ($c -le $maxSide -and $x -lt $pRel.R) {
            if (& $pxMasked $x $ry) { $term = 'mask'; break }
            $px = $bmp.GetPixel($x, $ry)
            if (-not (& $isFrame $px)) { break }
            $c++; $x++
          }
          if ($term -eq 'mask') { $leftLB += $c } else { $leftExact += $c }
        }
        if (-not (& $rowMasked $ry ($pRel.R - $frameExp.r) $pRel.R)) {
          $res.covR++
          $c = 0; $x = $pRel.R - 1; $term = 'pixel'
          while ($c -le $maxSide -and $x -ge $pRel.L) {
            if (& $pxMasked $x $ry) { $term = 'mask'; break }
            $px = $bmp.GetPixel($x, $ry)
            if (-not (& $isFrame $px)) { break }
            $c++; $x--
          }
          if ($term -eq 'mask') { $rightLB += $c } else { $rightExact += $c }
        }
      }
      if ($res.covL -lt 2 -or $res.covL -lt [int]($sideTotal / 2)) { $res.reason = "required left edge hidden by mask ($($res.covL) covering of $sideTotal rows)"; return $res }
      if ($res.covR -lt 2 -or $res.covR -lt [int]($sideTotal / 2)) { $res.reason = "required right edge hidden by mask ($($res.covR) covering of $sideTotal rows)"; return $res }
      if ($leftExact.Count -lt 2) { $res.reason = "left edge thickness not exactly verifiable (mask terminates $($leftLB.Count) of $($res.covL) covering rows; the predicted band itself IS fully verified in every covering row)"; return $res }
      if ($rightExact.Count -lt 2) { $res.reason = "right edge thickness not exactly verifiable (mask terminates $($rightLB.Count) of $($res.covR) covering rows; the predicted band itself IS fully verified in every covering row)"; return $res }
      $lMin = ($leftExact | Measure-Object -Minimum).Minimum; $lMax = ($leftExact | Measure-Object -Maximum).Maximum
      $rMin = ($rightExact | Measure-Object -Minimum).Minimum; $rMax = ($rightExact | Measure-Object -Maximum).Maximum
      $res.sideRowsL = "covering=$($res.covL)/$sideTotal exact=$($leftExact.Count) lb=$($leftLB.Count) min=$lMin max=$lMax"; $res.sideRowsR = "covering=$($res.covR)/$sideTotal exact=$($rightExact.Count) lb=$($rightLB.Count) min=$rMin max=$rMax"
      if ($lMax - $lMin -gt 1) { $res.reason = "left frame inconsistent across rows (min=$lMin max=$lMax)"; return $res }
      if ($rMax - $rMin -gt 1) { $res.reason = "right frame inconsistent across rows (min=$rMin max=$rMax)"; return $res }
      foreach ($lb in $leftLB) { if ($lb -lt ($lMin - 1)) { $res.reason = "left frame defect beside content (visible run $lb < measured $lMin)"; return $res } }
      foreach ($lb in $rightLB) { if ($lb -lt ($rMin - 1)) { $res.reason = "right frame defect beside content (visible run $lb < measured $rMin)"; return $res } }
      if ($lMin -gt $maxSide -or $rMin -gt $maxSide) { $res.reason = "side frame exceeds the model bound (left=$lMin right=$rMin max=$maxSide)"; return $res }
      $res.left = $lMin; $res.right = $rMin
      # LANDED r7f relations at 96 DPI: sideTol = max(1,round(scaleY)) = 1; thickGap = max(2,round(2*scaleY)) = 2.
      $sideTol = 1; $thickGap = 2
      if ($res.left -lt 1 -or $res.right -lt 1) { $res.reason = "side frame not visible (left=$($res.left) right=$($res.right))"; return $res }
      if ($res.bot -lt 1) { $res.reason = 'bottom frame not visible'; return $res }
      if ($res.bot -gt ([math]::Max($res.left, $res.right) + $sideTol)) { $res.reason = "bottom thicker than the sides (bot=$($res.bot) left=$($res.left) right=$($res.right) tol=$sideTol; landed r7f relation)"; return $res }
      if (($res.top - $res.left) -lt $thickGap -or ($res.top - $res.right) -lt $thickGap -or (($res.top - $res.bot) -lt $thickGap)) { $res.reason = "top not clearly thickest (top=$($res.top) left=$($res.left) right=$($res.right) bot=$($res.bot) gap=$thickGap; landed r7f relation)"; return $res }
      $res.frameOk = $true
    } else {
      # no-image: FrameThicknessConverter returns 0, so there is no distinguishable frame band (the frame
      # IS the card color). Frame absence is enforced indirectly: EVERY occlusion-free pixel of the
      # painted face, edges included, must match the background predicate below.
      $res.frameOk = $true
    }
    # interior purity: no-frame mode measures the painted face EDGES INCLUDED (inset 0); framed mode
    # insets by measured frame + guard. Corner AA squares (CornerRadius=4 -> 6px) excluded structurally.
    $inL = $(if ($noFrame) { 0 } else { $res.left + $guard }); $inR = $(if ($noFrame) { 0 } else { $res.right + $guard })
    $inT = $(if ($noFrame) { 0 } else { $res.top + $guard }); $inB = $(if ($noFrame) { 0 } else { $res.bot + $guard })
    $iL = $pRel.L + $inL; $iR = $pRel.R - $inR; $iT = $pRel.T + $inT; $iB = $pRel.B - $inB
    if (($iR - $iL) -lt 5 -or ($iB - $iT) -lt 5) { $res.reason = "no interior region after frame inset (x $iL..$iR y $iT..$iB)"; return $res }
    $cL = 2147483647; $cT = 2147483647; $cR = -1; $cB = -1
    foreach ($cr4 in $contentRects) { if ($cr4.L -lt $cL) { $cL = $cr4.L }; if ($cr4.T -lt $cT) { $cT = $cr4.T }; if ($cr4.R -gt $cR) { $cR = $cr4.R }; if ($cr4.B -gt $cB) { $cB = $cr4.B } }
    $free = 0; $match = 0; $mm = @()
    for ($y = $iT; $y -lt $iB; $y++) {
      for ($x = $iL; $x -lt $iR; $x++) {
        $dxC = [math]::Min($x - $pRel.L, $pRel.R - 1 - $x); $dyC = [math]::Min($y - $pRel.T, $pRel.B - 1 - $y)
        if ($dxC -lt $corner -and $dyC -lt $corner) { continue }
        if (& $pxMasked $x $y) { continue }
        $free++
        $hit = (& $isInterior ($bmp.GetPixel($x, $y)))
        if ($hit) { $match++ } else { if ($mm.Count -lt 3) { $px2 = $bmp.GetPixel($x, $y); $mm += "($x,$y)=rgb($($px2.R),$($px2.G),$($px2.B))" } }
        if ($y -lt $cT) { $res.regionTop++ } elseif ($y -ge $cB) { $res.regionBottom++ } elseif ($x -lt $cL) { $res.regionLeft++ } elseif ($x -ge $cR) { $res.regionRight++ } else { $res.regionMid++ }
      }
    }
    $res.freePx = $free; $res.matchPx = $match; $res.mismatch = ($mm -join ' ')
    if ($free -lt $minFreePixels) { $res.reason = "insufficient occlusion-free interior pixels ($free < $minFreePixels; explicit bounded minimum)"; return $res }
    if ($res.regionTop -lt $minRegionPixels -or $res.regionBottom -lt $minRegionPixels -or $res.regionLeft -lt $minRegionPixels -or $res.regionRight -lt $minRegionPixels) { $res.reason = "per-region coverage below minimum (top=$($res.regionTop) bottom=$($res.regionBottom) left=$($res.regionLeft) right=$($res.regionRight) min=$minRegionPixels; explicit bounded minimum)"; return $res }
    if ($match -ne $free) { $res.reason = "interior not pure: $($free - $match) of $free occlusion-free pixels off-predicate (e.g. $($res.mismatch))"; return $res }
    $res.imgOk = $true
    # OUTSIDE HALO (BOUNDED CLAIM): the 1px straight-edge band around the predicted painted rect must
    # match the OBSERVED RUNNER BACKGROUND (white within bounded tolerance, predicate injected). 6px
    # corner squares excluded. This is NOT a general paint-free proof: it sees only the 1px halo and
    # only on straight edges; disconnected paint beyond the halo is out of domain.
    $halo = 0; $haloBad = @()
    for ($x = $pRel.L + $corner; $x -lt $pRel.R - $corner; $x++) {
      foreach ($ey in @(($pRel.T - 1), $pRel.B)) {
        $px3 = $bmp.GetPixel($x, $ey)
        if (-not (& $isBackground $px3)) { if ($haloBad.Count -lt 3) { $haloBad += "($x,$ey)=rgb($($px3.R),$($px3.G),$($px3.B))" } }
        $halo++
      }
    }
    for ($y = $pRel.T + $corner; $y -lt $pRel.B - $corner; $y++) {
      foreach ($ex in @(($pRel.L - 1), $pRel.R)) {
        $px4 = $bmp.GetPixel($ex, $y)
        if (-not (& $isBackground $px4)) { if ($haloBad.Count -lt 3) { $haloBad += "($ex,$y)=rgb($($px4.R),$($px4.G),$($px4.B))" } }
        $halo++
      }
    }
    $res.haloPx = $halo
    if ($halo -lt $minHaloPixels) { $res.reason = "insufficient 1px halo coverage ($halo < $minHaloPixels; explicit bounded minimum)"; return $res }
    if ($haloBad.Count -gt 0) { $res.reason = "non-background paint in the 1px straight-edge halo (bounded 96-DPI guard, e.g. $($haloBad -join ' '))"; return $res }
    $res.haloOk = $true
    $res.ok = ($res.frameOk -and $res.imgOk -and $res.haloOk)
    return $res
  }
  # MEASURE-V4-END
  # ORACLE-STRUCTURE-V4-BEGIN: fail-closed structural anchor with a GROUNDED TOPOLOGY CONTRACT. Fully
  # injectable (btnFinder/getParent/getFC/getNS/readNode/rootReader lambdas + capture callback) so the
  # composition itself is fixture-tested offline. ORDER: pass-1 walk -> provider boundary (second
  # named-finder/ascent/root/identity) -> pass-2 walk (final provider reads) -> snapshot compare ->
  # topology/masks -> injected immutable capture. No provider calls after pass 2 (fixture-verified). Reuses r7f-reviewed Walk-DiagAscent/Test-DiagAscentClean/Read-DiagNode/
  # RectUnion/Rect-OutsideCount/Test-DiagRectFinitePositive/Compare-DiagIdentity; the structured
  # descendant walk below adds the same budget discipline returning structured nodes for topology.
  function Walk-CardStructure($root, $rootRid, $getFC, $getNS, $readNode, $maxNodes, $maxDepth, $maxCalls) {
    $visited = @{}
    $nodes = New-Object System.Collections.Generic.List[object]
    $queue = New-Object System.Collections.Generic.Queue[object]
    $errs = 0; $foreign = 0; $trunc = $false; $count = 0; $depthSkipped = 0; $calls = 0; $offs = 0
    # cap checked BEFORE every provider enumeration call; a skipped read marks truncated.
    $ch = $null
    if ($calls -lt $maxCalls) { try { $calls++; $ch = & $getFC $root } catch { $errs++ } } else { $trunc = $true }
    while ($ch) {
      $queue.Enqueue([pscustomobject]@{ el = $ch; depth = 1; parentRid = $rootRid })
      if ($calls -ge $maxCalls) { $trunc = $true; break }
      try { $calls++; $ch = & $getNS $ch } catch { $errs++; $ch = $null }
    }
    while ($queue.Count -gt 0) {
      if ($count -ge $maxNodes) { $trunc = $true; break }
      $qit = $queue.Dequeue()
      if ($qit.depth -gt $maxDepth) { $depthSkipped++; $trunc = $true; continue }
      $count++
      $nd = $null
      try { $nd = & $readNode $qit.el } catch { $errs++; continue }
      if ($null -eq $nd) { $foreign++; continue }
      if ($nd.rid -ne '' -and $nd.rid -ne 'rid-error') { if ($visited.ContainsKey($nd.rid)) { continue }; $visited[$nd.rid] = $true }
      if ($nd.off) { $offs++ }
      $nodes.Add([ordered]@{ ct = $nd.ct; name = $nd.name; rid = $nd.rid; parentRid = $qit.parentRid; rect = $nd.rect; depth = $qit.depth; off = $nd.off })
      $ch2 = $null
      if ($calls -lt $maxCalls) { try { $calls++; $ch2 = & $getFC $qit.el } catch { $errs++ } } else { $trunc = $true }
      while ($ch2) {
        $queue.Enqueue([pscustomobject]@{ el = $ch2; depth = ($qit.depth + 1); parentRid = $nd.rid })
        if ($calls -ge $maxCalls) { $trunc = $true; break }
        try { $calls++; $ch2 = & $getNS $ch2 } catch { $errs++; $ch2 = $null }
      }
    }
    if ($queue.Count -gt 0) { $trunc = $true }
    return [ordered]@{ nodes = $nodes; errs = $errs; foreign = $foreign; depthSkipped = $depthSkipped; truncated = $trunc; enumCalls = $calls; offscreenMembers = $offs; unique = $visited.Count }
  }
  function Compare-StructureSnapshot($a, $b) {
    # TWO-PASS member+parent topology stability: exact every-field compare (no int-rounding - rects
    # compared as raw doubles). ANY drift/missing/new member refuses; masks derive from pass 1 only
    # after pass 2 proves the structure did not move.
    if ($a.Count -ne $b.Count) { return "member count drift ($($a.Count) -> $($b.Count))" }
    $mapB = @{}
    foreach ($n in $b) { $k = ($n.rid -join ','); if ($mapB.ContainsKey($k)) { return "duplicate rid '$k' in the second pass" }; $mapB[$k] = $n }
    foreach ($n in $a) {
      $k = ($n.rid -join ',')
      if (-not $mapB.ContainsKey($k)) { return "member '$k' ($($n.name)) missing in the second pass" }
      $m = $mapB[$k]
      if ($m.ct -ne $n.ct) { return "member '$k' control-type drift ($($n.ct) -> $($m.ct))" }
      if (-not [string]::Equals([string]$m.name, [string]$n.name, [StringComparison]::Ordinal)) { return "member '$k' name drift ('$($n.name)' -> '$($m.name)')" }
      if (-not [string]::Equals(($m.parentRid -join ','), ($n.parentRid -join ','), [StringComparison]::Ordinal)) { return "member '$k' parent drift" }
      foreach ($f in 'Left', 'Top', 'Right', 'Bottom') { if ([double]$m.rect.$f -ne [double]$n.rect.$f) { return "member '$k' ($($n.name)) rect drift ($f $($n.rect.$f) -> $($m.rect.$f))" } }
      if ($m.off -ne $n.off) { return "member '$k' offscreen drift" }
    }
    return 'ok'
  }
  function Get-SavedCardStructureV4($anchorName, $btnFinder, $getParent, $getFC, $getNS, $readNode, $rootReader, $capture) {
    # ORDER (reviewer-mandated): pass-1 member walk -> PROVIDER BOUNDARY (second named-finder, capture
    # ascent, root revalidation, container identity) -> pass-2 member walk (FINAL provider reads) ->
    # snapshot compare -> topology/mask derivation -> INJECTED immutable capture callback. NO provider
    # calls exist after pass 2 (fixture B1 verifies). A mutation at the known second-finder boundary is
    # therefore caught by the walk1-vs-walk2 snapshot compare (fixture M0 = the original reviewer timing).
    $st = [ordered]@{ ok = $false; reason = ''; union = $null; contentRects = $null; liRect = $null; winRect = $null; winPid = $null; winRid = $null; ctxHwnd = $null; ctxDpiScale = $null; members = 0; walkCalls1 = 0; walkCalls2 = 0; captured = $null; stats = '' }
    try {
      $root = & $rootReader
      if ($null -eq $root -or $null -eq $root.rect) { $st.reason = 'root unread'; return $st }
      # EXPLICIT root pid binding (reviewer 04:10:41): landed Read-DiagNode returns
      # {rid,rect,ct,name,off,el} with NO pid field. The rootReader MUST attach rootPid sourced from
      # the UIA root's own Current.ProcessId; a missing/null/non-positive rootPid refuses here rather
      # than silently nulling the ascent pid gate (the fixture suite carries a production-shaped root).
      $appPid = 0
      try { $appPid = [int]$root.rootPid } catch { $appPid = 0 }
      if ($appPid -le 0) { $st.reason = 'root pid not explicitly bound (rootReader must attach rootPid from the UIA root ProcessId; Read-DiagNode carries no pid)'; return $st }
      $winRid = $root.rid; $winRect1 = $root.rect
      if (-not (Test-DiagRectFinitePositive $winRect1)) { $st.reason = 'window rectangle not finite-positive at structure read'; return $st }
      $sBtn = & $btnFinder $anchorName
      if ($null -eq $sBtn) { $st.reason = 'named button not found'; return $st }
      $btnNode = $null
      try { $btnNode = & $readNode $sBtn } catch { $st.reason = "button read-error $($_.Exception.Message)"; return $st }
      if ($null -eq $btnNode) { $st.reason = 'named button outside app process'; return $st }
      $asc1 = Walk-DiagAscent $sBtn $getParent 8 $winRid $appPid
      $ac1 = Test-DiagAscentClean $asc1
      if ($ac1 -ne 'ok') { $st.reason = "structure ascent unclean: $ac1 (same-PID alone is not a verified app subtree)"; return $st }
      $sLiNode = $null
      foreach ($nd in $asc1.nodes) { if ($nd.ct -eq 'ControlType.ListItem') { $sLiNode = $nd; break } }
      if ($null -eq $sLiNode) { $st.reason = 'list item not found in bounded in-app ascent'; return $st }
      if (-not (Test-DiagRectFinitePositive $sLiNode.rect)) { $st.reason = 'item rectangle not finite-positive at structure read'; return $st }
      $btnRid1 = $btnNode.rid; $liRid1 = $sLiNode.rid; $liRect1 = $sLiNode.rect
      # PASS 1 member walk
      $walk = Walk-CardStructure $sLiNode.el $sLiNode.rid $getFC $getNS $readNode 60 6 400
      if ($walk.truncated -or $walk.errs -gt 0 -or $walk.depthSkipped -gt 0) { $st.reason = "descendant walk pass 1 not clean (truncated=$($walk.truncated) errs=$($walk.errs) depthSkipped=$($walk.depthSkipped)); partial structure is not an oracle anchor"; return $st }
      if ($walk.foreign -gt 0 -or $walk.offscreenMembers -gt 0) { $st.reason = "descendant walk pass 1 has foreign/offscreen members (foreign=$($walk.foreign) offscreen=$($walk.offscreenMembers))"; return $st }
      # PROVIDER BOUNDARY: second named-finder + capture ascent + root + container identity, all BEFORE
      # the final member walk.
      $sBtn2 = & $btnFinder $anchorName
      if ($null -eq $sBtn2) { $st.reason = 'named button gone at capture'; return $st }
      $btnNode2 = $null
      try { $btnNode2 = & $readNode $sBtn2 } catch { $st.reason = "capture-time button read-error $($_.Exception.Message)"; return $st }
      if ($null -eq $btnNode2) { $st.reason = 'capture-time button outside app process'; return $st }
      $asc2 = Walk-DiagAscent $sBtn2 $getParent 8 $winRid $appPid
      $ac2 = Test-DiagAscentClean $asc2
      if ($ac2 -ne 'ok') { $st.reason = "capture ascent unclean: $ac2"; return $st }
      $sLiNode2 = $null
      foreach ($nd in $asc2.nodes) { if ($nd.ct -eq 'ControlType.ListItem') { $sLiNode2 = $nd; break } }
      if ($null -eq $sLiNode2) { $st.reason = 'list item gone at capture'; return $st }
      if (-not (Test-DiagRectFinitePositive $sLiNode2.rect)) { $st.reason = 'item rectangle not finite-positive at capture read'; return $st }
      $root2 = & $rootReader
      $rootPid2 = 0; try { $rootPid2 = [int]$root2.rootPid } catch { $rootPid2 = 0 }
      if ($null -eq $root2 -or $rootPid2 -ne $appPid) { $st.reason = "root-pid-changed($appPid)"; return $st }
      if (-not (Test-DiagRectFinitePositive $root2.rect)) { $st.reason = 'window rectangle not finite-positive at capture read'; return $st }
      $ident = Compare-DiagIdentity $btnRid1 $liRid1 $liRect1 $winRid $winRect1 $btnNode2.rid $sLiNode2.rid $sLiNode2.rect $root2.rid $root2.rect
      if ($ident -ne 'ok') { $st.reason = "$ident before capture (stale structure is not evidence)"; return $st }
      # PASS 2 member walk - the FINAL provider reads.
      $walk2 = Walk-CardStructure $sLiNode.el $sLiNode.rid $getFC $getNS $readNode 60 6 400
      if ($walk2.truncated -or $walk2.errs -gt 0 -or $walk2.depthSkipped -gt 0) { $st.reason = "descendant walk pass 2 not clean (truncated=$($walk2.truncated) errs=$($walk2.errs) depthSkipped=$($walk2.depthSkipped)); member freshness unverifiable"; return $st }
      if ($walk2.foreign -gt 0 -or $walk2.offscreenMembers -gt 0) { $st.reason = "descendant walk pass 2 has foreign/offscreen members (foreign=$($walk2.foreign) offscreen=$($walk2.offscreenMembers))"; return $st }
      $snap = Compare-StructureSnapshot $walk.nodes $walk2.nodes
      if ($snap -ne 'ok') { $st.reason = "member snapshot drift before capture: $snap (stale masks are not fresh authority)"; return $st }
      $nodeRects = New-Object System.Collections.Generic.List[object]
      foreach ($n in $walk.nodes) { $nodeRects.Add($n.rect) }
      $outsideCount = Rect-OutsideCount $nodeRects $liRect1
      if ($outsideCount -gt 0) { $st.reason = "$outsideCount descendant rects outside the item"; return $st }
      $topo = Test-SavedCardTopologyV4 $walk.nodes $anchorName $sLiNode.rid
      if (-not $topo.ok) { $st.reason = "topology refused: $($topo.reason)"; return $st }
      $union = RectUnion $nodeRects
      if ($null -eq $union) { $st.reason = 'no positive in-app descendant rects'; return $st }
      $st.union = $union; $st.contentRects = $topo.maskRects; $st.liRect = $sLiNode2.rect; $st.winRect = $root2.rect; $st.winPid = $appPid; $st.winRid = $winRid; $st.members = $walk.nodes.Count
      $st.walkCalls1 = $walk.enumCalls; $st.walkCalls2 = $walk2.enumCalls
      # ONE-SNAPSHOT CAPTURE BOUNDARY: injected callback, invoked immediately after mask derivation. The
      # consumer measures ONLY the bytes the callback carries (same-bytes boundary, fixtures B2/B3).
      # 'One snapshot + pre/post integrity checks' - NOT an immutable type. The ctx carries the validated
      # identity/geometry COPIED from the provider-boundary reads (hwnd/dpiScale surface only when the
      # caller's rootReader attaches them; null-safe here). Required Win32 handle/PID/rect validation
      # belongs at THIS boundary (before the final member walk); no provider/Win32 calls exist after
      # pass 2 (fixture B1), and the residual post-boundary TOCTOU window is declared, not hidden.
      $ctxHwnd = $null; $ctxDpi = $null
      if ($null -ne $root2) {
        $ph = $root2.PSObject.Properties['hwnd']; if ($null -ne $ph) { $ctxHwnd = $ph.Value }
        $pd = $root2.PSObject.Properties['dpiScale']; if ($null -ne $pd) { $ctxDpi = $pd.Value }
      }
      $st.ctxHwnd = $ctxHwnd; $st.ctxDpiScale = $ctxDpi
      if ($null -ne $capture) { $st.captured = & $capture ([pscustomobject]@{ union = $union; contentRects = $st.contentRects; liRect = $st.liRect; winRect = $st.winRect; hwnd = $ctxHwnd; winPid = $appPid; winRid = $winRid; dpiScale = $ctxDpi }) }
      $st.stats = "members=$($walk.nodes.Count) walkCalls pass1=$($walk.enumCalls)/400 pass2=$($walk2.enumCalls)/400 (PER-WALK caps, not combined) snapshot=ok boundary=pre-pass2 ascent1(hops=$($asc1.hops) root=$($asc1.hitRoot)) ascent2(hops=$($asc2.hops) root=$($asc2.hitRoot)) identity=ok"
      $st.ok = $true
    } catch { $st.reason = "threw: [$($_.Exception.GetType().FullName)] $($_.Exception.Message) | stack: $($_.ScriptStackTrace)" }
    return $st
  }
  # ORACLE-STRUCTURE-V4-END

  # ADAPTER-V4-BEGIN: OFFLINE runtime callsite adapter candidate REV2 (NOT landed; for separate source review).
  # REV2/REV3 contract change (reviewer 03:58:32/04:05:44): the capture callback returns an RGB PAYLOAD
  # (byte[] + width/height/stride/channelOrder + identity + start/end UTC + declared SHA256), NOT a
  # live bitmap object. The adapter takes ONE canonical snapshot (packed RGB, defensive copy on the fast
  # path) with pre/post integrity hash checks - 'canonical one snapshot + integrity checks', NOT an
  # immutable type; measurement, crops, hashing and PNG artifacts ALL derive from that single array. An incoming live object's
  # GetPixel is NEVER consulted for pixels (the stable-Bytes-different-GetPixel evasion is structurally
  # closed). An optional incoming 'bitmap' field may carry a live object for disposal tracking only.
  # Gates: structure ok -> EXPLICIT capture gate (distinct; structure ok NEVER implies capture ok) ->
  # canonical hash verification -> measurement -> post-measurement rehash -> evidence+PNG artifacts.
  # Exception safety: measurement-stage exceptions are caught into reason with partial provenance and
  # artifacts preserved; every cleanup step is individually guarded and records a warning instead of
  # throwing. PNG artifacts are built and flushed BEFORE disposal, on success AND failure (whenever a
  # canonical payload exists). pid/rid/origin are callback ASSERTIONS carried from validated values,
  # not independent binding proof (labeled in provenance).
  function Get-BytesSha256([byte[]]$bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose() }
  }
  $script:CrcTable = $null
  function Get-Crc32([byte[]]$bytes) {
    if ($null -eq $script:CrcTable) {
      $script:CrcTable = New-Object 'uint32[]' 256
      for ($n = 0; $n -lt 256; $n++) { $c = [uint32]$n; for ($k = 0; $k -lt 8; $k++) { $c = $(if ($c -band 1) { 0xEDB88320 -bxor ($c -shr 1) } else { $c -shr 1 }) }; $script:CrcTable[$n] = $c }
    }
    $crc = [uint32]4294967295
    foreach ($b in $bytes) { $crc = $script:CrcTable[($crc -bxor $b) -band 0xFF] -bxor ($crc -shr 8) }
    return ([uint32]($crc -bxor 4294967295L))
  }
  function Get-Adler32([byte[]]$bytes) {
    $a = [uint32]1; $b = [uint32]0
    foreach ($x in $bytes) { $a = ($a + $x) % 65521; $b = ($b + $a) % 65521 }
    return (($b -shl 16) -bor $a)
  }
  function Write-Be32([byte[]]$buf, [int]$off, [uint32]$v) {
    $buf[$off] = [byte](($v -shr 24) -band 0xFF); $buf[$off+1] = [byte](($v -shr 16) -band 0xFF); $buf[$off+2] = [byte](($v -shr 8) -band 0xFF); $buf[$off+3] = [byte]($v -band 0xFF)
  }
  function Add-PngChunk($ms, [string]$type, [byte[]]$data) {
    $len = New-Object byte[] 4; Write-Be32 $len 0 ([uint32]$data.Length); $ms.Write($len, 0, 4)
    $tb = [System.Text.Encoding]::ASCII.GetBytes($type); $ms.Write($tb, 0, 4)
    if ($data.Length -gt 0) { $ms.Write($data, 0, $data.Length) }
    $crcBuf = New-Object byte[] (4 + $data.Length); [Array]::Copy($tb, $crcBuf, 4); if ($data.Length -gt 0) { [Array]::Copy($data, 0, $crcBuf, 4, $data.Length) }
    $crc = New-Object byte[] 4; Write-Be32 $crc 0 (Get-Crc32 $crcBuf); $ms.Write($crc, 0, 4)
  }
  function ConvertTo-PngRgb([byte[]]$rgb, [int]$w, [int]$h) {
    $raw = New-Object byte[] (($w * 3 + 1) * $h)
    for ($y = 0; $y -lt $h; $y++) { [Array]::Copy($rgb, $y * $w * 3, $raw, $y * ($w * 3 + 1) + 1, $w * 3) }
    $dms = New-Object System.IO.MemoryStream
    $ds = New-Object System.IO.Compression.DeflateStream($dms, [System.IO.Compression.CompressionMode]::Compress)
    $ds.Write($raw, 0, $raw.Length); $ds.Dispose()
    $def = $dms.ToArray(); $dms.Dispose()
    $zlib = New-Object byte[] ($def.Length + 6)
    $zlib[0] = 0x78; $zlib[1] = 0x01
    [Array]::Copy($def, 0, $zlib, 2, $def.Length)
    Write-Be32 $zlib ($def.Length + 2) (Get-Adler32 $raw)
    $ms = New-Object System.IO.MemoryStream
    $sig = [byte[]](137, 80, 78, 71, 13, 10, 26, 10); $ms.Write($sig, 0, 8)
    $ihdr = New-Object byte[] 13; Write-Be32 $ihdr 0 ([uint32]$w); Write-Be32 $ihdr 4 ([uint32]$h)
    $ihdr[8] = 8; $ihdr[9] = 2  # 8-bit, truecolor RGB
    Add-PngChunk $ms 'IHDR' $ihdr
    Add-PngChunk $ms 'IDAT' $zlib
    Add-PngChunk $ms 'IEND' ([byte[]]@())
    $png = $ms.ToArray(); $ms.Dispose()
    return ,$png
  }
  function Get-CropBytes([byte[]]$canon, [int]$bw, [int]$l, [int]$t, [int]$r, [int]$b) {
    $w = $r - $l; $h = $b - $t
    $crop = New-Object byte[] ($w * $h * 3)
    for ($y = 0; $y -lt $h; $y++) { [Array]::Copy($canon, ([int]($t + $y) * $bw + [int]$l) * 3, $crop, $y * $w * 3, $w * 3) }
    return ,$crop
  }
  function New-CanonicalBitmap([byte[]]$rgb, [int]$w, [int]$h) {
    $o = [pscustomobject]@{ Width = $w; Height = $h; Bytes = $rgb }
    $o | Add-Member -MemberType ScriptMethod -Name GetPixel -Value { param($x, $y) $i = ([int]$y * $this.Width + [int]$x) * 3; [pscustomobject]@{ R = $this.Bytes[$i]; G = $this.Bytes[$i+1]; B = $this.Bytes[$i+2]; Lum = 0.2126*$this.Bytes[$i] + 0.7152*$this.Bytes[$i+1] + 0.0722*$this.Bytes[$i+2] } }
    return $o
  }
  function ConvertTo-CanonicalRgb([byte[]]$raw, [int]$w, [int]$h, [int]$stride, [string]$order) {
    if ($stride -eq ($w * 3) -and $order -eq 'RGB') { return ,([byte[]]$raw.Clone()) }  # defensive copy: the callback's array may be reused/mutated later; the snapshot must not alias it
    $out = New-Object byte[] ($w * $h * 3)
    for ($y = 0; $y -lt $h; $y++) {
      for ($x = 0; $x -lt $w; $x++) {
        $si = $y * $stride + $x * 3; $di = ($y * $w + $x) * 3
        if ($order -eq 'BGR') { $out[$di] = $raw[$si+2]; $out[$di+1] = $raw[$si+1]; $out[$di+2] = $raw[$si] }
        else { $out[$di] = $raw[$si]; $out[$di+1] = $raw[$si+1]; $out[$di+2] = $raw[$si+2] }
      }
    }
    return ,$out
  }
  function Test-Hex64([object]$s) { return ($null -ne $s -and ([string]$s) -match '^[0-9a-fA-F]{64}$') }
  function ConvertTo-UtcOffset([object]$v, [ref]$dto) {
    # require ACTUAL UTC: DateTime Kind must be Utc; DateTimeOffset offset must be zero; strings must
    # carry an explicit zero offset (e.g. 'Z'). Kind=Unspecified/Local or unzoned strings are refused
    # (reviewer 04:05:44: unzoned acceptance made the UTC claim false).
    try {
      if ($v -is [DateTime]) { if ($v.Kind -ne [DateTimeKind]::Utc) { return $false }; $dto.Value = [DateTimeOffset]$v; return $true }
      if ($v -is [DateTimeOffset]) { if ($v.Offset -ne [TimeSpan]::Zero) { return $false }; $dto.Value = $v; return $true }
      $sv = ([string]$v).Trim()
      # explicit zone marker REQUIRED on string inputs (Z or +/-HH:MM): unzoned strings parse against
      # the HOST timezone, so a host at TZ=UTC would silently accept them and the UTC claim would be
      # false (reviewer 04:10:41 reproduction). Typed UTC DateTimes / zero-offset DateTimeOffsets only,
      # or strings with an explicit marker that parses to a zero offset.
      if ($sv -notmatch '(?i)(Z|[+-]\d{2}:?\d{2})$') { return $false }
      $t = [DateTimeOffset]::MinValue
      if (-not [DateTimeOffset]::TryParse($sv, [ref]$t)) { return $false }
      if ($t.Offset -ne [TimeSpan]::Zero) { return $false }
      $dto.Value = $t; return $true
    } catch { return $false }
  }
  function Test-UtcInterval([object]$s, [object]$e, [ref]$ds, [ref]$de) {
    $os = [DateTimeOffset]::MinValue; $oe = [DateTimeOffset]::MinValue
    if (-not (ConvertTo-UtcOffset $s ([ref]$os))) { return $false }
    if (-not (ConvertTo-UtcOffset $e ([ref]$oe))) { return $false }
    $ds.Value = $os.UtcDateTime; $de.Value = $oe.UtcDateTime
    return ($ds.Value -le $de.Value)
  }
  function Invoke-SavedCardOracleV4($anchorName, $P, $captureOnce, $pred, $frameExp, $pad, $dpiScale, $minFreePixels, $minRegionPixels, $minHaloPixels, $artifactSink = $null) {
    $res = [ordered]@{ ok = $false; structureOk = $false; captureOk = $false; measureOk = $false; evidenceOk = $false; captureTransportIssue = $false; reason = ''; provenance = $null; evidence = $null; measureReason = ''; structure = $null; measurement = $null; captureCalls = 0; captureDisposals = 0 }
    $res.artifacts = New-Object System.Collections.Generic.List[object]
    $res.cleanupWarnings = New-Object System.Collections.Generic.List[string]
    $capState = @{ calls = 0; err = $null }  # hashtable: GetNewClosure scopes would swallow string reassignments
    $incomingBitmap = $null
    $capCb = { param($ctx)
      $capState.calls++
      if ($capState.calls -gt 1) { $capState.err = 'capture callback invoked more than once'; return $null }
      try {
        $raw = & $captureOnce $ctx
        if ($null -eq $raw) { $capState.err = 'capture callback returned null'; return $null }
        return $raw
      } catch { $capState.err = "capture threw: [$($_.Exception.GetType().FullName)] $($_.Exception.Message) | stack: $($_.ScriptStackTrace)"; return $null }
    }.GetNewClosure()
    try {
      $st = Get-SavedCardStructureV4 $anchorName $P.btnFinder $P.getParent $P.getFC $P.getNS $P.readNode $P.rootReader $capCb
      if (-not $st.ok) { $res.reason = "structure refused: $($st.reason)"; return $res }
      $res.structureOk = $true
      $res.captureCalls = $capState.calls
      # per-walk budget validation: each pass has its OWN 400-call cap (not a combined 800)
      if (-not ($st.walkCalls1 -le 400 -and $st.walkCalls2 -le 400)) { $res.reason = "walk budget violated (pass1=$($st.walkCalls1) pass2=$($st.walkCalls2); per-walk cap 400)"; return $res }
      # r12: surface the ACTUAL structure snapshot (member masks = contentRects, rects, identity, walk
      # budgets) so the per-state audit JSON exports the oracle's OWN runtime data, not an earlier-DIAG
      # reconstruction.
      $res.structure = [ordered]@{ union = $st.union; contentRects = $st.contentRects; liRect = $st.liRect; winRect = $st.winRect; winPid = $st.winPid; winRid = [string]$st.winRid; members = $st.members; ctxHwnd = $(if ($null -ne $st.ctxHwnd) { [string]$st.ctxHwnd } else { '' }); ctxDpiScale = $st.ctxDpiScale; walkCalls1 = $st.walkCalls1; walkCalls2 = $st.walkCalls2 }
      # ---- EXPLICIT CAPTURE-SUCCESS GATE (structure ok says NOTHING about capture) ----
      if ($null -ne $capState.err) { $res.provenance = [ordered]@{ verification = 'no-capture: ' + $capState.err }; $res.reason = "capture gate: $($capState.err)"; return $res }
      $c = $st.captured
      if ($null -eq $c) { $res.provenance = [ordered]@{ verification = 'no-capture: callback absent or returned null' }; $res.reason = 'capture gate: no capture object (callback absent or returned null)'; return $res }
      # incremental partial provenance - recorded BEFORE validation, labeled unverified
      $prov = [ordered]@{
        verification = 'unverified'
        pid = $c.pid; winRid = [string]$c.winRid; origin = "($($c.originL),$($c.originT))"
        width = $c.width; height = $c.height; stride = $c.stride; channelOrder = [string]$c.channelOrder
        captureStartedUtc = $c.captureStartedUtc; captureEndedUtc = $c.captureEndedUtc; declaredSha256 = [string]$c.sha256
        winPidExpected = $st.winPid; winRidExpected = [string]$st.winRid
        walkCalls1 = $st.walkCalls1; walkCalls2 = $st.walkCalls2; dpiScale = $dpiScale
        bindingNote = 'pid/rid/origin are callback assertions carried from validated walk values, not independent binding proof'
      }
      $res.provenance = $prov
      $incomingBitmap = $c.bitmap  # optional live object: disposal tracking ONLY, never pixel reads
      # finite positive INTEGRAL dimensions
      $w = 0; $h = 0
      try { $w = [int]$c.width; $h = [int]$c.height; if (($w -ne [double]$c.width) -or ($h -ne [double]$c.height)) { throw 'fractional' } } catch { $prov.verification = 'gate-failed: dimensions not finite positive integral'; $res.reason = "capture gate: dimensions not finite positive integral (width=$($c.width) height=$($c.height))"; return $res }
      if ($w -le 0 -or $h -le 0) { $prov.verification = 'gate-failed: non-positive dimensions'; $res.reason = "capture gate: non-positive dimensions ($w x $h)"; return $res }
      # exact captured surface extent == validated window rect
      $ww = [int]$st.winRect.Width; $wh = [int]$st.winRect.Height
      if ($w -ne $ww -or $h -ne $wh) { $prov.verification = 'gate-failed: extent mismatch'; $res.reason = "capture gate: captured extent $w x $h != validated window extent $ww x $wh"; return $res }
      # stride + checked RGB length
      $stride = 0
      try { $stride = [int]$c.stride; if ($stride -ne [double]$c.stride) { throw 'fractional' } } catch { $prov.verification = 'gate-failed: stride not integral'; $res.reason = 'capture gate: stride not integral'; return $res }
      if ($stride -lt ($w * 3)) { $prov.verification = 'gate-failed: stride too small'; $res.reason = "capture gate: stride $stride < width*3 $($w*3)"; return $res }
      if ($null -eq $c.rgb -or $c.rgb.Length -ne ($stride * $h)) { $prov.verification = 'gate-failed: rgb length mismatch'; $res.reason = "capture gate: rgb length $(if ($c.rgb) { $c.rgb.Length } else { 0 }) != stride*height $($stride * $h)"; return $res }
      if ($c.channelOrder -ne 'RGB' -and $c.channelOrder -ne 'BGR') { $prov.verification = 'gate-failed: unknown channel order'; $res.reason = "capture gate: unknown channel order '$($c.channelOrder)'"; return $res }
      # declared capture-time hash: 64-hex
      if (-not (Test-Hex64 $c.sha256)) { $prov.verification = 'gate-failed: declared hash not 64-hex'; $res.reason = 'capture gate: declared capture hash is not 64 lowercase/uppercase hex'; return $res }
      # start/end capture interval metadata
      $ds = [DateTime]::MinValue; $de = [DateTime]::MinValue
      if (-not (Test-UtcInterval $c.captureStartedUtc $c.captureEndedUtc ([ref]$ds) ([ref]$de))) { $prov.verification = 'gate-failed: invalid capture interval'; $res.reason = "capture gate: invalid capture interval (started='$($c.captureStartedUtc)' ended='$($c.captureEndedUtc)')"; return $res }
      # PID-first + root binding (callback assertions carried from validated values)
      if ($c.pid -ne $st.winPid) { $prov.verification = 'gate-failed: pid mismatch'; $res.reason = "capture gate: pid mismatch (capture $($c.pid) vs validated $($st.winPid))"; return $res }
      if (-not [string]::Equals([string]$c.winRid, [string]$st.winRid, [StringComparison]::Ordinal)) { $prov.verification = 'gate-failed: window rid mismatch'; $res.reason = 'capture gate: window rid mismatch'; return $res }
      $ox = [int]$st.winRect.Left; $oy = [int]$st.winRect.Top
      if ($c.originL -ne $ox -or $c.originT -ne $oy) { $prov.verification = 'gate-failed: origin mismatch'; $res.reason = "capture gate: origin mismatch (capture $($c.originL),$($c.originT) vs validated window $ox,$oy)"; return $res }
      # canonicalize: ONE frozen packed RGB array underlies measurement, crops, hashing, PNG
      try { $canon = ConvertTo-CanonicalRgb ([byte[]]$c.rgb) $w $h $stride ([string]$c.channelOrder) } catch { $prov.verification = 'gate-failed: canonicalization threw'; $res.reason = "capture gate: canonicalization threw: [$($_.Exception.GetType().FullName)] $($_.Exception.Message) | stack: $($_.ScriptStackTrace)"; return $res }
      $h0 = Get-BytesSha256 $canon
      if ($h0 -ne ([string]$c.sha256).ToLowerInvariant()) { $prov.verification = 'gate-failed: canonical hash != declared hash'; $res.reason = 'capture gate: canonical payload hash does not match the declared capture-time hash'; return $res }
      $cb = New-CanonicalBitmap $canon $w $h
      $prov.canonicalSha256 = $h0; $prov.captureIntervalMs = [int]($de - $ds).TotalMilliseconds
      $prov.verification = 'capture-verified'
      $res.captureOk = $true
      # full-surface PNG artifact built from the canonical bytes BEFORE any further gate (failure evidence
      # is retained for EVERY later refusal, including capture-transport classification)
      try { $pngFull = ConvertTo-PngRgb $canon $w $h; $res.artifacts.Add([ordered]@{ name = 'captured.png'; bytes = $pngFull; sha256 = (Get-BytesSha256 $pngFull); width = $w; height = $h }) } catch { $res.cleanupWarnings.Add("artifact captured.png build threw: $($_.Exception.Message)") }
      # blank/black surface (real WPF PrintWindow failure mode): classify as CAPTURE-TRANSPORT, retain the
      # pixels (captured.png above), do NOT treat as a render/product observation
      $blank = $true
      foreach ($bb in $canon) { if ($bb -ne 0) { $blank = $false; break } }
      if ($blank) { $prov.verification = 'gate-failed: blank capture'; $prov.classification = 'capture-transport-blank'; $res.captureTransportIssue = $true; $res.reason = 'capture gate: blank/black surface (capture returned success with an all-zero payload) - classified CAPTURE-TRANSPORT, not a render/product observation; pixels retained in captured.png'; return $res }
      # painted rect verifiability inside the canonical surface
      $uRel = [ordered]@{ L = ($st.union.L - $ox); T = ($st.union.T - $oy); R = ($st.union.R - $ox); B = ($st.union.B - $oy) }
      $pRel = Get-PaintedRect $uRel $frameExp $pad
      $prov.union = $st.union; $prov.paintedRel = $pRel; $prov.winRect = $st.winRect
      if (-not ($pRel.L -ge 1 -and $pRel.T -ge 1 -and $pRel.R -le ($w - 1) -and $pRel.B -le ($h - 1) -and ($pRel.R - $pRel.L) -ge 20 -and ($pRel.B - $pRel.T) -ge 10)) { $prov.verification = 'gate-failed: painted rect not verifiable'; $res.reason = "capture gate: painted rect not verifiable inside the captured bitmap (L$($pRel.L),T$($pRel.T),R$($pRel.R),B$($pRel.B) bmp $($w)x$($h))"; return $res }
      # measurement on the canonical bytes ONLY, with exception safety
      $contentRel = New-Object System.Collections.Generic.List[object]
      foreach ($r in $st.contentRects) { $contentRel.Add([ordered]@{ L = ($r.L - $ox); T = ($r.T - $oy); R = ($r.R - $ox); B = ($r.B - $oy) }) }
      try {
        $m = Measure-SavedCardFrameV4 $cb $pRel $contentRel $pred.isFrame $pred.isInterior $pred.isBackground $frameExp $dpiScale $minFreePixels $minRegionPixels $minHaloPixels
        $res.measureReason = $m.reason
        $res.measurement = $m  # r12: the ACTUAL measurement object for the per-state audit export
        # post-measurement integrity rehash of the SAME canonical array
        $h2 = Get-BytesSha256 $canon
        if ($h2 -ne $h0) { $prov.verification = 'integrity-failed: canonical mutated during measurement'; $res.reason = 'canonical bytes mutated during measurement (mutable-payload guard)'; return $res }
        $res.measureOk = $m.ok
        # crop evidence derived ONLY from the canonical bytes, plus a real PNG artifact
        $cropBytes = Get-CropBytes $canon $w $pRel.L $pRel.T $pRel.R $pRel.B
        $cropW = $pRel.R - $pRel.L; $cropH = $pRel.B - $pRel.T
        try { $cropPng = ConvertTo-PngRgb $cropBytes $cropW $cropH; $res.artifacts.Add([ordered]@{ name = 'painted-crop.png'; bytes = $cropPng; sha256 = (Get-BytesSha256 $cropPng); width = $cropW; height = $cropH }) } catch { $res.cleanupWarnings.Add("artifact painted-crop.png build threw: $($_.Exception.Message)") }
        $res.evidence = @(
          [ordered]@{ name = 'captured-canonical'; sha256 = $h0; width = $w; height = $h },
          [ordered]@{ name = 'painted-crop'; sha256 = (Get-BytesSha256 $cropBytes); rect = "L$($pRel.L),T$($pRel.T),R$($pRel.R),B$($pRel.B)" }
        )
        if (-not $m.ok) { $prov.verification = 'measurement-refused'; $res.reason = "measurement refused: $($m.reason) (evidence crops + PNG artifacts derived from the captured bytes)"; return $res }
        $prov.verification = 'measured-ok'
      } catch {
        $prov.verification = 'measurement-threw'
        $res.reason = "measurement stage threw: [$($_.Exception.GetType().FullName)] $($_.Exception.Message) | stack: $($_.ScriptStackTrace) (partial provenance/evidence/artifacts preserved)"
        return $res
      }
    } catch {
      # OUTER clean refusal: hash/metadata/gate-stage exceptions never escape (reviewer 04:05:44)
      if ($null -eq $res.provenance) { $res.provenance = [ordered]@{ verification = 'threw-before-capture-metadata' } }
      $res.provenance.verification = 'adapter-threw: ' + $_.Exception.Message
      $res.reason = "adapter stage threw: [$($_.Exception.GetType().FullName)] $($_.Exception.Message) | stack: $($_.ScriptStackTrace) (partial provenance/artifacts preserved)"
      return $res
    } finally {
      # artifacts flushed to the sink BEFORE any disposal, each individually guarded
      if ($null -ne $artifactSink) {
        foreach ($art in $res.artifacts) {
          # VERIFIED-write sink contract (reviewer 04:10:41): the sink writes, reads the bytes back and
          # compares the read-back SHA256 to the expected hash, returning $true only on equality.
          # A non-$true return fails evidence exactly like a throw.
          try {
            $sinkRet = & $artifactSink $art.name $art.bytes
            if ($sinkRet -ne $true) { $res.cleanupWarnings.Add("artifact sink failed verification for $($art.name)") }
          } catch { $res.cleanupWarnings.Add("artifact sink threw for $($art.name): $($_.Exception.Message)") }
        }
      }
      if ($null -ne $incomingBitmap -and $incomingBitmap.PSObject.Methods['Dispose']) {
        try { $incomingBitmap.Dispose(); $res.captureDisposals++ } catch { $res.cleanupWarnings.Add("incoming bitmap Dispose threw: $($_.Exception.Message)") }
      }
      # EVIDENCE GATE (reviewer 04:05:44): when a canonical payload existed, a PASS requires BOTH PNG
      # artifacts built AND every sink write succeeded - a warning-level artifact failure never yields
      # a ready PASS without usable evidence. Disposal warnings do not gate evidence.
      if ($res.captureOk) {
        $hasFull = (@($res.artifacts | Where-Object { $_.name -eq 'captured.png' }).Count -eq 1)
        $hasCrop = (@($res.artifacts | Where-Object { $_.name -eq 'painted-crop.png' }).Count -eq 1)
        $sinkFailed = (@($res.cleanupWarnings | Where-Object { $_ -like 'artifact sink *' }).Count -gt 0)
        $res.evidenceOk = ($hasFull -and $hasCrop -and -not $sinkFailed)
        if ($res.measureOk -and -not $res.evidenceOk) {
          $res.reason = "evidence gate: artifact pipeline incomplete (captured=$hasFull crop=$hasCrop sinkFailed=$sinkFailed); ready PASS never claimed without usable evidence"
        }
      }
      $res.ok = ($res.structureOk -and $res.captureOk -and $res.measureOk -and $res.evidenceOk)
    }
    return $res
  }
  # ADAPTER-V4-END

  # PWCAP-DIAG-END


  # SAVED card face (overview): the card Border is a plain Border with the same converters, NOT the popup's
  # PreviewBorder - this is the measurement the audit flagged as missing. Resolve the card's ListItem from its
  # named button, re-acquire FRESH rects right before the capture, validate (positive, inside the app window
  # and the virtual screen; an invalid rect THROWS before any CopyFromScreen), crop, measure painted-anchored.
  $tw2 = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  $cardBtn2 = $null; $li2 = $null
  for ($t = 0; $t -lt 15 -and $null -eq $li2; $t++) {
    Start-Sleep -Milliseconds 300
    $cardBtn2 = Btn 'E2E ws renamed'
    if ($cardBtn2) { $li2 = $cardBtn2; while ($li2 -and $li2.Current.ControlType -ne $CT::ListItem) { $li2 = $tw2.GetParent($li2) } }
  }
  Assert 'saved card list item resolved from its named button' ($null -ne $li2) ((ListLines) -join ' || ')
  if ($null -eq $li2) { throw 'saved card list item not resolvable; refusing to crop' }
  $cr = $li2.Current.BoundingRectangle; $wr2 = $win.Current.BoundingRectangle
  $vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
  $rectOk = ($cr.Width -gt 40 -and $cr.Height -gt 20 -and $cr.Left -ge $vs.Left -and $cr.Top -ge $vs.Top -and $cr.Right -le $vs.Right -and $cr.Bottom -le $vs.Bottom -and $cr.Left -ge ($wr2.Left - 2) -and $cr.Top -ge ($wr2.Top - 2) -and $cr.Right -le ($wr2.Right + 2) -and $cr.Bottom -le ($wr2.Bottom + 2))
  Assert 'saved card crop rect positive and contained in the app window and screen' $rectOk ("card=L$($cr.Left),T$($cr.Top),R$($cr.Right),B$($cr.Bottom) size=$($cr.Width)x$($cr.Height) win=$($wr2.Left),$($wr2.Top),$($wr2.Right),$($wr2.Bottom) screen=$($vs.Left),$($vs.Top),$($vs.Right),$($vs.Bottom)")
  if (-not $rectOk) { throw "saved card crop rect invalid; refusing CopyFromScreen (card=$($cr.Left),$($cr.Top),$($cr.Right),$($cr.Bottom))" }
  $cw = [int]$cr.Width; $ch = [int]$cr.Height
  $gDpi2 = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero); $dpiX2 = $gDpi2.DpiX; $dpiY2 = $gDpi2.DpiY; $gDpi2.Dispose()
  $scaleY2 = $dpiY2 / 96.0
  $isFrame2 = { param($px) ([math]::Abs($px.R - 191) -le 12 -and [math]::Abs($px.G - 219) -le 12 -and [math]::Abs($px.B - 254) -le 12) }
  $isImg2 = { param($px) ([math]::Abs($px.R - 30) -le 25 -and [math]::Abs($px.G - 60) -le 25 -and [math]::Abs($px.B - 220) -le 25) }
  $crop2 = New-Object System.Drawing.Bitmap $cw, $ch; $cg2 = [System.Drawing.Graphics]::FromImage($crop2); $cg2.CopyFromScreen([int]$cr.Left, [int]$cr.Top, 0, 0, $crop2.Size); $cg2.Dispose()
  $crop2.Save("$PWD/out-images/feat-saved-card-frame-crop.png")
  $cx1 = [int]($cw * 0.4); $cx2 = [int]($cw * 0.5); $cx3 = [int]($cw * 0.6); $cx4 = [int]($cw * 0.3); $cx5 = [int]($cw * 0.7)
  $candX = @($cx1, $cx2, $cx3, $cx4, $cx5)
  $rowMid = [int]($ch / 2)
  $mres = Measure-CardFrame $crop2 $cw $ch $isFrame2 $isImg2 $candX $rowMid $scaleY2
  $crop2.Dispose()
  Info 'saved card frame measured px vs preview px (all candidate columns reported)' ("cols clean=$($mres.colsClean) pass=$($mres.colsPass) readings: $($mres.mid) | card: top=$($mres.top) left=$($mres.left) right=$($mres.right) bottomRun=$($mres.bot) anchor=y$($mres.yBot) | preview(9a): top=$topBand left=$leftBand right=$rightBand | dpiX=$dpiX2 dpiY=$dpiY2 bounds=T$($mres.yTop),B$($mres.yBot),L$($mres.xLeft),R$($mres.xRight) transitions(belowTop=$($mres.belowTop) rightOfLeft=$($mres.rightOfLeft))")
  # r12: LEGACY reading, NON-GATING. Historical observation (run 43553053012, reviewer 05:55:38): the
  # item-fraction sample columns landed outside the painted face (that run: item 864 wide, painted 249
  # wide) - the old anchor is invalid, so it cannot gate; the PrintWindow GATE below gates. Measurement
  # code and native flow unchanged.
  Info 'LEGACY (non-gating; item-fraction sampling, painted-face anchor invalid in observed run 43553053012 - superseded by the GATE below) saved card face columns' ("colsClean=$($mres.colsClean) (old threshold >=3) readings: $($mres.mid) crop=${cw}x${ch}")
  Info 'LEGACY (non-gating; item-fraction sampling, painted-face anchor invalid in observed run 43553053012 - superseded by the GATE below) saved card frame vs AGREED rule' ("ok=$($mres.ok) reason='$($mres.reason)' measured px: top=$($mres.top) left=$($mres.left) right=$($mres.right) bot=$($mres.bot) botSig=$($mres.botSig) interior=$($mres.interior) topConsistent=$($mres.topConsistent) colsClean=$($mres.colsClean) colsPass=$($mres.colsPass) | $($mres.botDetail) | dpi=$dpiX2,$dpiY2 | crop=out-images/feat-saved-card-frame-crop.png ${cw}x${ch}")
  Shot 'card-with-picker-image'
  Log-SavedCardDiag 'with-image'
  Invoke-PrintWindowDiag 'with-image'
  Assert-PwGate 'with-image'
  # remove image (הסר תמונה) + explicit NO-IMAGE background on the saved card (image property cleared, not a
  # missing file): the audit gap was that only the missing-path fallback was ever exercised.
  $c = ClickBtn 'Workspace actions' 900; Assert 'card menu opened for remove image' ($c -eq 'ok') $c
  $mi = MenuItemByName 'עצב כרטיס'; [void](InvokeEl $mi 'design menu entry for remove image'); Start-Sleep -Milliseconds 900
  Assert 'design popup reopened for remove image' (WaitText 'תמונת רקע' 10) ((Texts) -join ' | ')
  $imgEditRm = NamedEdit 'Card image path'; Assert 'card image path editor present before remove' ($null -ne $imgEditRm) ''
  $rmPre = ''; $rmPreOk = (Read-EditValue $imgEditRm ([ref]$rmPre))
  Assert 'readback shows the picker-accepted image path before remove' ($rmPreOk -and $rmPre -eq $probeImg) ("read=$rmPreOk value='$rmPre'")
  $rmBtn = Btn 'Remove card image'; Assert 'remove card image button present' ($null -ne $rmBtn) ''
  $rmInvoked = @(InvokeEl $rmBtn 'remove card image')[-1]
  if (-not $rmInvoked) { Shot 'picker-failure'; throw 'remove card image invocation failed' }
  Start-Sleep -Milliseconds 500
  $rmPost = ''; $rmPostOk = (Read-EditValue (NamedEdit 'Card image path') ([ref]$rmPost))
  Assert 'remove image clears the unsaved design path (editor present, read succeeded, value empty)' ($rmPostOk -and $rmPost -eq '') ("read=$rmPostOk value='$rmPost'")
  $okNoImgPrev = $false; $seenNoImg = ''
  for ($i = 0; $i -lt 20 -and -not $okNoImgPrev; $i++) {
    $prevN = AnyByName 'Card design preview'; if ($prevN) { $rN = $prevN.Current.BoundingRectangle
      if ($rN.Width -gt 0 -and $rN.Height -gt 0) {
        $bmp2 = New-Object System.Drawing.Bitmap 1, 1; $g2 = [System.Drawing.Graphics]::FromImage($bmp2); $g2.CopyFromScreen([int]($rN.Left + $rN.Width / 2), [int]($rN.Top + $rN.Height / 2), 0, 0, $bmp2.Size)
        $px2 = $bmp2.GetPixel(0, 0); $g2.Dispose(); $bmp2.Dispose()
        $seenNoImg = "rgb($($px2.R),$($px2.G),$($px2.B))"
        $okNoImgPrev = ([math]::Abs($px2.R - 191) -le 8 -and [math]::Abs($px2.G - 219) -le 8 -and [math]::Abs($px2.B - 254) -le 8)
      } }
    if (-not $okNoImgPrev) { Start-Sleep -Milliseconds 300 }
  }
  Assert 'preview falls back to the color after remove image (before save)' $okNoImgPrev ("center=$seenNoImg expected~rgb(191,219,254)")
  $c = ClickBtn 'Save card design' 1500; Assert 'design saved after remove image' ($c -eq 'ok') $c
  $oRm = StoreObj; $wsRm = @($oRm.workspaces | Where-Object { $_.id -eq $wsDId })[0]
  $hasImgPropRm = ($null -ne $wsRm) -and ($wsRm.PSObject.Properties.Name -contains 'imagePath')
  $imgRm = if ($hasImgPropRm) { "$($wsRm.imagePath)" } else { '' }
  Assert 'after remove-save: persisted image cleared (explicit no-image), color kept' (($imgRm -eq '') -and $wsRm.color -eq '#BFDBFE') ("imagePathProp=$hasImgPropRm imagePath='$imgRm' color='$($wsRm.color)'")
  # no-image background on the SAVED card: re-acquire the SAME card and re-validate containment on EVERY poll
  # iteration (a cached rect from before a re-render is not evidence).
  $okCardBg = $false; $seenCardBg = ''
  for ($i = 0; $i -lt 20 -and -not $okCardBg; $i++) {
    Start-Sleep -Milliseconds 300
    $cardBtn3 = Btn 'E2E ws renamed'; $li3 = $null
    if ($cardBtn3) { $li3 = $cardBtn3; while ($li3 -and $li3.Current.ControlType -ne $CT::ListItem) { $li3 = $tw2.GetParent($li3) } }
    if ($li3) {
      $r3 = $li3.Current.BoundingRectangle; $wr3 = $win.Current.BoundingRectangle; $vs3 = [System.Windows.Forms.SystemInformation]::VirtualScreen
      $r3Ok = ($r3.Width -gt 40 -and $r3.Height -gt 20 -and $r3.Left -ge $vs3.Left -and $r3.Top -ge $vs3.Top -and $r3.Right -le $vs3.Right -and $r3.Bottom -le $vs3.Bottom -and $r3.Left -ge ($wr3.Left - 2) -and $r3.Top -ge ($wr3.Top - 2) -and $r3.Right -le ($wr3.Right + 2) -and $r3.Bottom -le ($wr3.Bottom + 2))
      if ($r3Ok) {
        $bmp3 = New-Object System.Drawing.Bitmap 1, 1; $g3 = [System.Drawing.Graphics]::FromImage($bmp3); $g3.CopyFromScreen([int]($r3.Left + $r3.Width / 2), [int]($r3.Top + $r3.Height / 2), 0, 0, $bmp3.Size)
        $px3 = $bmp3.GetPixel(0, 0); $g3.Dispose(); $bmp3.Dispose()
        $seenCardBg = "rgb($($px3.R),$($px3.G),$($px3.B)) at L$($r3.Left),T$($r3.Top) $($r3.Width)x$($r3.Height)"
        $okCardBg = ([math]::Abs($px3.R - 191) -le 8 -and [math]::Abs($px3.G - 219) -le 8 -and [math]::Abs($px3.B - 254) -le 8)
      } else { $seenCardBg = "rect invalid this poll: $($r3.Left),$($r3.Top),$($r3.Right),$($r3.Bottom)" }
    }
  }
  Info 'LEGACY (non-gating; same-card center pixel, painted-face anchor invalid in observed run 43553053012 - superseded by the GATE below) saved card color background with NO image' ("ok=$okCardBg center=$seenCardBg expected~rgb(191,219,254)")
  Shot 'card-no-image-background'
  Log-SavedCardDiag 'no-image'
  Invoke-PrintWindowDiag 'no-image'
  Assert-PwGate 'no-image'
  $c = ClickBtn 'Workspace actions' 900; Assert 'card menu opened for category' ($c -eq 'ok') $c
  $mi = MenuItemByName 'ערוך קטגוריה'; [void](InvokeEl $mi 'category menu entry'); Start-Sleep -Milliseconds 900
  Assert 'category popup opened with כללי current' (WaitText 'Current: כללי' 10) ((Texts) -join ' | ')
  $newCat = NamedEdit 'New category name'; Assert 'new category box found' ($null -ne $newCat) ''
  if ($newCat) { SetText $newCat 'E2Ecat' }
  $c = ClickBtn 'Add category' 900; Assert 'custom category added' ($c -eq 'ok') $c
  Assert 'custom category listed' (WaitText 'E2Ecat' 5) ((Texts) -join ' | ')
  Shot 'category-popup-custom-added'
  $c = ClickBtn 'Close category popup' 900; Assert 'category popup closed' ($c -eq 'ok') $c
  Assert 'custom category persisted to the store' ((StoreJson) -like '*E2Ecat*') (StoreJson)
  # 10. existing-resource picker (בחר מסביבות קיימות): four type buttons, pick fills the form (original-name and
  # usage-sort behavior are unit-tested; the instance-name text also exists in the row below, so E2E does not assert on it)
  $c = ClickBtn 'Workspace actions' 900; Assert 'card menu opened for details' ($c -eq 'ok') $c
  $mi = MenuItemByName 'הצג'; [void](InvokeEl $mi 'הצג menu entry for picker'); Start-Sleep -Milliseconds 900
  Assert 'details opened for the picker' (WaitText 'Created:' 10) ''
  $c = ClickBtn 'Pick from existing resources' 900; Assert 'picker opened' ($c -eq 'ok') $c
  Assert 'picker shows the four type buttons' ((WaitText 'Program' 5) -and (WaitText 'Folder' 5)) ((Texts) -join ' | ')
  Shot 'resource-picker'
  $c = ClickBtn 'Pick resource' 900; Assert 'resource picked' ($c -eq 'ok') $c
  $targetVal = ValOf (NamedEdit 'Item target')
  Assert 'pick filled the add form address' ($targetVal -eq 'https://example.com/') $targetVal
  # 11. user feedback Nov 12: the item-row actions menu clipped off-screen. The LAST row is the worst case (window right edge, least room below).
  $miBtns = @($win.FindAll($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::Button)), (New-Object $PC($A::NameProperty, 'Item actions'))))))
  Assert 'item rows present for the menu placement check' ($miBtns.Count -gt 0) "count=$($miBtns.Count)"
  if ($miBtns.Count -gt 0) { [void](InvokeEl $miBtns[$miBtns.Count - 1] 'last-row item actions button'); Start-Sleep -Milliseconds 900
    Assert 'last-row item menu opened' ($null -ne (MenuItemByName 'מחיקה')) ''
    MenuInsideWindow 'last-row item menu'
    $mEl = $A::RootElement.FindFirst($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Menu)))
    $wrM = $win.Current.BoundingRectangle
    $hidden = @($mEl.FindAll($TS::Children, (New-Object $PC($A::ControlTypeProperty, $CT::MenuItem))) | Where-Object { $it = $_.Current; $it.IsOffscreen -or $it.BoundingRectangle.Top -lt $wrM.Top -or $it.BoundingRectangle.Bottom -gt $wrM.Bottom -or $it.BoundingRectangle.Left -lt $wrM.Left -or $it.BoundingRectangle.Right -gt $wrM.Right })
    Assert 'every menu entry fully visible inside the window (all actions usable, none scroll-hidden or clipped)' ($hidden.Count -eq 0) ("clipped: " + (@($hidden | ForEach-Object { $_.Current.Name }) -join ', '))
    Shot 'item-actions-menu-bottom-row'
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 500 }
  
  # 12. four-group visual proof (Nov 12 feedback: files and programs shown separately, like sites and folders)
  $c = ClickBtn 'Back' 900; Assert 'back to overview for the four-group workspace' ($c -eq 'ok') $c
  SetText (NamedEdit 'Workspace name') 'E2E four groups'; $c = ClickBtn 'Create workspace' 900; Assert 'create four-group workspace clicked' ($c -eq 'ok') $c
  Assert 'four-group workspace opened in details' (WaitText 'Created:' 15) ((Texts) -join ' | ')
  $targetBox4 = NamedEdit 'Item target'; $itemNameBox4 = NamedEdit 'Item name'
  Assert 'four-group add form boxes found' ($null -ne $targetBox4 -and $null -ne $itemNameBox4) ''
  [void](SetType 'Link'); SetText $targetBox4 'https://four.example/'; SetText $itemNameBox4 'Four link'; $c = ClickBtn 'Add' 600; Assert 'add four link' ($c -eq 'ok') $c
  [void](SetType 'Folder'); SetText $targetBox4 "$root"; SetText $itemNameBox4 'Four folder'; $c = ClickBtn 'Add' 600; Assert 'add four folder' ($c -eq 'ok') $c
  [void](SetType 'File'); SetText $targetBox4 "$root\ok.txt"; SetText $itemNameBox4 'Four file'; $c = ClickBtn 'Add' 600; Assert 'add four file' ($c -eq 'ok') $c
  [void](SetType 'Program'); SetText $targetBox4 'C:\Windows\notepad.exe'; SetText $itemNameBox4 'Four program'; $c = ClickBtn 'Add' 600; Assert 'add four program' ($c -eq 'ok') $c
  Start-Sleep -Milliseconds 800
  $groups4 = @($win.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Group))) | ForEach-Object { $_.Current.Name })
  Assert 'four group headers shown: Sites, Folders, Files, Programs' (($groups4 -contains 'Sites') -and ($groups4 -contains 'Folders') -and ($groups4 -contains 'Files') -and ($groups4 -contains 'Programs')) ("groups: $($groups4 -join ', ')")
  Assert 'four rows listed, one per type' ((@(Texts) | Where-Object { $_ -eq 'https://four.example/' -or $_ -like '*ok.txt' -or $_ -like '*notepad.exe' -or $_ -eq "$root" }).Count -ge 4) ((Texts) -join ' | ')
  Shot 'four-groups'
  # 13. saved-item delete (מחיקה): removes exactly one membership by id; canonical full-field snapshots prove the rest untouched
  $snapWs = { param($o, $wsId) $w = @($o.workspaces | Where-Object { $_.id -eq $wsId })[0]; (@($w.items | ForEach-Object { $m = $_; $t = ($o.items.PSObject.Properties.Value | Where-Object { $_.id -eq $m.itemId } | Select-Object -First 1).target; "$($m.id)|$($m.itemId)|$t|$($m.name)|$($m.description)|$($m.launch.openWith)|$($m.launch.browser)" }) | Sort-Object) -join ';' }
  $o = StoreObj
  $okTargets = @($o.items.PSObject.Properties.Value | Where-Object { $_.target -eq "$root\ok.txt" })
  Assert 'shared fixture: exactly one catalog item for ok.txt' (@($okTargets).Count -eq 1) ("count=$(@($okTargets).Count)")
  $okItemId = $okTargets[0].id
  $catBefore = $okTargets[0]; $catSnapBefore = "$($catBefore.id)|$($catBefore.type)|$($catBefore.name)|$($catBefore.target)|$($catBefore.description)"
  $ws1Id = $o.workspaces[0].id; $ws4Id = (@($o.workspaces | Where-Object { $_.name -eq 'E2E four groups' })[0]).id
  $ws1m = @($o.workspaces[0].items | Where-Object { $_.itemId -eq $okItemId })
  $ws4m = @((@($o.workspaces | Where-Object { $_.id -eq $ws4Id })[0]).items | Where-Object { $_.itemId -eq $okItemId })
  Assert 'shared fixture: both workspaces hold a membership of the SAME itemId' ($ws1m.Count -ge 1 -and $ws4m.Count -eq 1) ("ws1=$($ws1m.Count) ws4=$($ws4m.Count) itemId=$okItemId")
  $delId = $ws4m[0].id
  $ws1SnapBefore = & $snapWs $o $ws1Id
  $ws4SnapBefore = & $snapWs $o $ws4Id
  $tw = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  $okTexts = @($win.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Text))) | Where-Object { try { $_.Current.Name -eq "$root\ok.txt" } catch { $false } })
  Assert 'exactly one ok.txt target text in the four-group view' ($okTexts.Count -eq 1) ("count=$($okTexts.Count)")
  $li = $okTexts[0]; while ($li -and $li.Current.ControlType -ne $CT::ListItem) { $li = $tw.GetParent($li) }
  $rowBtn = $null
  if ($li) { $rb = @($li.FindAll($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::Button)), (New-Object $PC($A::NameProperty, 'Item actions')))))); if ($rb.Count -eq 1) { $rowBtn = $rb[0] } }
  if (-not $rowBtn) {
    $rcy = $okTexts[0].Current.BoundingRectangle.Top + $okTexts[0].Current.BoundingRectangle.Height / 2
    $rb = @($win.FindAll($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::Button)), (New-Object $PC($A::NameProperty, 'Item actions'))))) | Where-Object { $b = $_.Current.BoundingRectangle; $rcy -ge $b.Top -and $rcy -le $b.Bottom })
    Assert 'unique same-row Item actions button by strict Y containment (fallback)' ($rb.Count -eq 1) ("count=$($rb.Count) rowCenterY=$rcy")
    if ($rb.Count -eq 1) { $rowBtn = $rb[0] }
  }
  Assert 'Item actions button resolved inside the ok.txt row' ($null -ne $rowBtn) ''
  [void](InvokeEl $rowBtn 'ok.txt row item actions'); Start-Sleep -Milliseconds 900
  $delMi = MenuItemByName 'מחיקה'; Assert 'delete entry present in the ok.txt row menu' ($null -ne $delMi) ''
  [void](InvokeEl $delMi 'מחיקה')
  $gone = $false; $swD = [Diagnostics.Stopwatch]::StartNew(); while ($swD.Elapsed.TotalSeconds -lt 10) { if (-not (@(Texts) + @(ListLines) | Where-Object { $_ -like '*ok.txt*' })) { $gone = $true; break }; Start-Sleep -Milliseconds 300 }
  Assert 'deleted row gone from the list (bounded local disappear check)' $gone ((Texts) -join ' | ')
  Shot 'saved-item-deleted'
  $o = StoreObj
  $ws1SnapAfter = & $snapWs $o $ws1Id
  Assert 'store: other workspace canonical snapshot identical (ids, itemIds, targets, names, descriptions, launch prefs)' ($ws1SnapAfter -eq $ws1SnapBefore) ("before=$ws1SnapBefore after=$ws1SnapAfter")
  $ws4SnapAfter = & $snapWs $o $ws4Id
  $ws4Expected = (@(($ws4SnapBefore -split ';') | Where-Object { $_ -notlike "$delId|*" }) | Sort-Object) -join ';'
  Assert 'store: four-group workspace = before minus exactly the deleted membership, survivors field-identical' ($ws4SnapAfter -eq $ws4Expected) ("expected=$ws4Expected after=$ws4SnapAfter deleted=$delId")
  $catAfter = $o.items.PSObject.Properties.Value | Where-Object { $_.id -eq $okItemId } | Select-Object -First 1
  $catSnapAfter = "$($catAfter.id)|$($catAfter.type)|$($catAfter.name)|$($catAfter.target)|$($catAfter.description)"
  Assert 'store: shared catalog item survives with identical identity and target' ($null -ne $catAfter -and $catSnapAfter -eq $catSnapBefore) ("before=$catSnapBefore after=$catSnapAfter")
  Assert 'FLOW-COMPLETE' $true $env:GITHUB_SHA
}
catch { $il = $_.InvocationInfo; "script error: $_ at line $($il.ScriptLineNumber): $($il.Line.Trim())" | Tee-Object -Append diag/errors.txt; [void]$results.Add([ordered]@{ name = 'script error'; ok = $false; detail = "$_ at line $($il.ScriptLineNumber)" }) }
finally {
  $results | ConvertTo-Json -Depth 4 | Out-File diag/assertions.json
  Get-Process notepad -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  if ($p -and -not $p.HasExited) { $null = $p.CloseMainWindow(); Start-Sleep 3; if (-not $p.HasExited) { $p.Kill() } }
}
if (@($results | Where-Object { $_.ok -eq $false }).Count -gt 0) { 'UI ASSERTIONS FAILED' | Out-File -Append diag/errors.txt; exit 1 }
}
catch { $il = $_.InvocationInfo; "harness setup error: $_ at line $($il.ScriptLineNumber): $($il.Line.Trim())" | Out-File -Append diag/errors.txt; @([ordered]@{ name = 'harness setup error'; ok = $false; detail = "$_ at line $($il.ScriptLineNumber)" }) | ConvertTo-Json -Depth 4 | Out-File diag/assertions.json; exit 1 }
