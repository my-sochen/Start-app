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
  # Drive the dialog with VERIFIED identity only. Filename edit resolved by up to three INDEPENDENT strategies:
  # (a) the ComboBox named exactly 'File name:'/'שם הקובץ:' with EXACTLY ONE child Edit, (b) the Edit with
  # AutomationId 1148 (comdlg32 filename control id), (c) the Edit whose LabeledByElement carries the label.
  # Any internal ambiguity (count > 1 inside a strategy) or any DISAGREEMENT between strategies is a conflict:
  # refuse to drive with a full diagnostic - never silently fall through to another identity. Open: exactly one
  # enabled + visible button named 'Open'/'פתיחה' or AutomationId 1, identity logged. The invoke executes ONLY
  # when the filename readback verified AND the Open identity verified - asserts gate the irreversible click,
  # they do not merely log.
  $driveNote = ''; $rawDump = 'rawtree: not captured'; $nativeDump = 'native: not captured'; $totalsNote = 'totals: not read'
  $attempt = [ordered]@{ 'named-combo' = 'not-attempted'; 'automationid-1148' = 'not-attempted'; 'labeledby' = 'not-attempted' }
  $attemptErr = [ordered]@{ 'named-combo' = ''; 'automationid-1148' = ''; 'labeledby' = '' }
  try {
    $dlgAlive = [WinN]::IsWindow($dlgAcc)
    $stillOwned = $dlgAlive -and (@(FindPickers) -contains $dlgAcc)
    $dlgEl = $null
    if ($stillOwned) { try { $dlgEl = [System.Windows.Automation.AutomationElement]::FromHandle($dlgAcc) } catch { $driveNote = "fromHandle failed: $($_.Exception.Message)" } }
    Info 'owned picker revalidated before any drive' ("alive=$dlgAlive stillOwned=$stillOwned fromHandle=$(if ($null -ne $dlgEl) { 'ok' } else { 'failed' })")
    if ($null -eq $dlgEl) { throw "owned dialog unavailable for identity resolution (alive=$dlgAlive owned=$stillOwned)" }
    try { $nativeDump = [DlgEnum]::Dump($dlgAcc, 120) } catch { $nativeDump = "native enum error: $($_.Exception.Message)" }
    Info 'owned picker dialog native child windows (bounded, before UIA walk)' $nativeDump
    try { $rawDump = Dialog-RawTreeDump $dlgEl 200 12 100 2000 } catch { $rawDump = "rawtree error: $($_.Exception.Message)" }
    Info 'owned picker dialog UIA raw tree (bounded, dialog-only)' $rawDump
    $idResults = @(); $idConflicts = @()
    try {
      $comboName = New-Object System.Windows.Automation.OrCondition((New-Object $PC($A::NameProperty, 'File name:')), (New-Object $PC($A::NameProperty, 'שם הקובץ:')))
      $namedCombo = @($dlgEl.FindAll($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::ComboBox)), $comboName))))
      if ($namedCombo.Count -gt 1) { $idConflicts += "named-combo ambiguous: $($namedCombo.Count) filename combos"; $attempt['named-combo'] = "combos=$($namedCombo.Count) -> ambiguous" }
      elseif ($namedCombo.Count -eq 1) {
        $childEdits = @($namedCombo[0].FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Edit))))
        if ($childEdits.Count -eq 1) { $idResults += [ordered]@{ el = $childEdits[0]; id = "named-combo:'$($namedCombo[0].Current.Name)'" } }
        elseif ($childEdits.Count -gt 1) { $idConflicts += "named-combo has $($childEdits.Count) child edits" }
        $attempt['named-combo'] = "combos=1 childEdits=$($childEdits.Count)"
      } else { $attempt['named-combo'] = 'combos=0' }
    } catch { $attemptErr['named-combo'] = "$($_.Exception.Message)"; $idConflicts += "named-combo query raised (fail-closed): $($_.Exception.Message)" }
    try {
      $byId = @($dlgEl.FindAll($TS::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object $PC($A::ControlTypeProperty, $CT::Edit)), (New-Object $PC($A::AutomationIdProperty, '1148'))))))
      if ($byId.Count -gt 1) { $idConflicts += "automationid-1148 ambiguous: $($byId.Count) edits" }
      elseif ($byId.Count -eq 1) { $idResults += [ordered]@{ el = $byId[0]; id = 'automationid:1148' } }
      $attempt['automationid-1148'] = "edits=$($byId.Count)"
    } catch { $attemptErr['automationid-1148'] = "$($_.Exception.Message)"; $idConflicts += "automationid-1148 query raised (fail-closed): $($_.Exception.Message)" }
    try {
      $byLabel = @($dlgEl.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Edit))) | Where-Object { try { $lb = $_.Current.LabeledBy; ($null -ne $lb) -and ($lb.Current.Name -eq 'File name:' -or $lb.Current.Name -eq 'שם הקובץ:') } catch { $false } })
      if ($byLabel.Count -gt 1) { $idConflicts += "labeledby ambiguous: $($byLabel.Count) edits" }
      elseif ($byLabel.Count -eq 1) { $idResults += [ordered]@{ el = $byLabel[0]; id = 'labeledby:filename-label' } }
      $attempt['labeledby'] = "edits=$($byLabel.Count)"
    } catch { $attemptErr['labeledby'] = "$($_.Exception.Message)"; $idConflicts += "labeledby query raised (fail-closed): $($_.Exception.Message)" }
    try {
      $allEditsN = @($dlgEl.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Edit)))).Count
      $allCombos = @($dlgEl.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::ComboBox))))
      $allTexts = @($dlgEl.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Text))))
      $comboNames = (($allCombos | ForEach-Object { $cn = [WinN]::Esc("$($_.Current.Name)"); if ($cn.Length -gt 40) { $cn.Substring(0, 40) + "..(truncated from $($cn.Length))" } else { $cn } }) -join ',')
      $labelNames = (($allTexts | ForEach-Object { $tn = [WinN]::Esc("$($_.Current.Name)"); if ($tn.Length -gt 40) { $tn.Substring(0, 40) + "..(truncated from $($tn.Length))" } else { $tn } } | Select-Object -First 25) -join ',')
      $totalsNote = "totals: edits=$allEditsN combos=$($allCombos.Count) texts=$($allTexts.Count) comboNames=[$comboNames] labels=[$labelNames]"
    } catch { $totalsNote = "totals read raised (diagnostic-only, drive unaffected): $($_.Exception.Message)" }
    $distinct = @()
    foreach ($idr in $idResults) { $foundDup = $false; foreach ($d in $distinct) { if ($d.el.Equals($idr.el)) { $foundDup = $true } }; if (-not $foundDup) { $distinct += $idr } }
    $idNames = (($idResults | ForEach-Object { $_.id }) -join ' | ')
    $fileEdit = $null; $editIdentity = 'none'
    if ($idConflicts.Count -gt 0 -or $distinct.Count -ne 1) {
      Assert 'exactly one unambiguous filename edit identity in the picker' $false ("conflicts=[$($idConflicts -join '; ')] distinct=$($distinct.Count) strategies=[$idNames] attempts=[$(Format-AttemptSummary $attempt $attemptErr)]")
      $driveNote = "Open invoke WITHHELD: filename identity not uniquely verified (conflicts=[$($idConflicts -join '; ')] distinct=$($distinct.Count) strategies=[$idNames] attempts=[$(Format-AttemptSummary $attempt $attemptErr)])"
    } else {
      $fileEdit = $distinct[0].el; $editIdentity = "$($distinct[0].id) strategies=[$idNames]"
      $editVis = $false; $editEn = $false; $editAid = ''; $editName = ''
      try { $editVis = -not $fileEdit.Current.IsOffscreen; $editEn = $fileEdit.Current.IsEnabled; $editAid = "$($fileEdit.Current.AutomationId)"; $editName = "$($fileEdit.Current.Name)" } catch { }
      $editVerified = $editVis -and $editEn
      Assert 'verified filename edit is enabled and visible' $editVerified ("identity=$editIdentity name='$editName' aid='$editAid' visible=$editVis enabled=$editEn")
      $readbackOk = $false
      if ($editVerified) {
        $fileEdit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($probeImg)
        Start-Sleep -Milliseconds 400
        $fbVal = ''; $fbOk = (Read-EditValue $fileEdit ([ref]$fbVal))
        $readbackOk = ($fbOk -and $fbVal -eq $probeImg)
        Assert 'picker filename readback succeeded and equals the probe path' $readbackOk ("read=$fbOk combo='$fbVal' probe='$probeImg'")
        $openCands = @($dlgEl.FindAll($TS::Descendants, (New-Object $PC($A::ControlTypeProperty, $CT::Button))) | Where-Object { try { ($_.Current.Name -eq 'Open' -or $_.Current.Name -eq 'פתיחה' -or $_.Current.AutomationId -eq '1') -and (-not $_.Current.IsOffscreen) -and $_.Current.IsEnabled } catch { $false } })
        $openInfo = (($openCands | ForEach-Object { "name='$($_.Current.Name)' aid='$($_.Current.AutomationId)' rect=$($_.Current.BoundingRectangle)" }) -join ' | ')
        Assert 'exactly one enabled visible Open button in the picker' ($openCands.Count -eq 1) ("count=$($openCands.Count) $openInfo")
        if ($readbackOk -and $openCands.Count -eq 1) {
          $openCands[0].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
          $driveNote = "verified filename ($editIdentity) + readback + unique enabled Open invoked"
        } else { $driveNote = "Open invoke WITHHELD: readbackOk=$readbackOk openCount=$($openCands.Count)" }
      } else { $driveNote = "Open invoke WITHHELD: filename edit not enabled/visible (identity=$editIdentity)" }
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
  $imgEditAcc = NamedEdit 'Card image path'; Assert 'card image path editor present for accept readback' ($null -ne $imgEditAcc) ''
  $accPath = ''; $accReadOk = (Read-EditValue $imgEditAcc ([ref]$accPath))
  Assert 'card image path readback succeeded after the accept (no fail-open empty)' $accReadOk ''
  Assert 'card image path filled by the ACCEPTED native picker (code-behind result, not typed)' ($accReadOk -and $accPath -eq $probeImg) ("edit='$accPath' probe='$probeImg' drive=$driveNote")
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
  Assert 'saved card face renders the picker-uploaded image (at least 3 clean candidate columns, all measured and reported)' ($mres.colsClean -ge 3) ("readings: $($mres.mid) crop=${cw}x${ch}")
  Assert 'saved card frame matches the AGREED rule with a real uploaded image: image visible, thick top, thin visible sides AND thin bottom, pure image interior, every clean column passing (painted-bounds anchored; measured px reported, not assumed thickness)' $mres.ok ("reason='$($mres.reason)' measured px: top=$($mres.top) left=$($mres.left) right=$($mres.right) bot=$($mres.bot) botSig=$($mres.botSig) interior=$($mres.interior) topConsistent=$($mres.topConsistent) colsClean=$($mres.colsClean) colsPass=$($mres.colsPass) | $($mres.botDetail) | dpi=$dpiX2,$dpiY2 | crop=out-images/feat-saved-card-frame-crop.png ${cw}x${ch}")
  Shot 'card-with-picker-image'
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
  Assert 'saved card shows the color as background with NO image (explicit empty, not missing file; same card re-acquired and validated per poll)' $okCardBg ("center=$seenCardBg expected~rgb(191,219,254)")
  Shot 'card-no-image-background'
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
