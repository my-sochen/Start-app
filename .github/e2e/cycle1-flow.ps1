try {
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class W { [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; } [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags); }'
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
  $oD = StoreObj; $wsD0 = $oD.workspaces[0]; $wsDId = $wsD0.id; $col0 = $wsD0.color; $img0 = $wsD0.imagePath
  Info 'design persisted state before preview edits' ("ws=$wsDId color='$col0' imagePath='$img0'")
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
        $botCols = @(20, [int]($pw/4), [int]($pw/2), [int](3*$pw/4), $pw-21)
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
