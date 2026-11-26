"""Build targeted harnesses from the candidate scripts' real setup and helper definitions.
Only selected contract actions run. This is not FLOW-COMPLETE or full TEST evidence.
"""
from pathlib import Path
p=Path(__file__).parent
cycle=(p/'cycle1-flow.ps1').read_text(encoding='utf-8-sig')
cap=(p/'capture-flow.ps1').read_text(encoding='utf-8-sig')
common=r'''
function Section($name,[scriptblock]$work) {
  $before=$results.Count
  try { & $work; Assert "SECTION $name complete" $true '' }
  catch { [void]$results.Add([ordered]@{name="SECTION $name error";ok=$false;detail="$($_.Exception.Message)"}) }
  # Each next section starts from a fresh process/store, even when this section failed.
  try { Shot ("contract-"+$name) } catch { }
}
function ResetContractApp {
  if($p -and -not $p.HasExited){$p.Kill();$p.WaitForExit()}
  if(Test-Path $storeDir){Remove-Item $storeDir -Recurse -Force}
  $script:p=Start-Process -FilePath $exe.FullName -PassThru
  $deadline=[Diagnostics.Stopwatch]::StartNew()
  do{$p.Refresh();if($p.HasExited){throw 'App exited'};if($p.MainWindowHandle -ne 0){break};Start-Sleep -Milliseconds 100}while($deadline.Elapsed.TotalSeconds -lt 30)
  if($p.MainWindowHandle -eq 0){throw 'App window not ready'}
  $script:mainHwnd=$p.MainWindowHandle;$script:win=$A::FromHandle($p.MainWindowHandle)
  [void][W]::SetWindowPos($p.MainWindowHandle,[IntPtr]::Zero,0,0,960,650,0x40)
  Start-Sleep -Milliseconds 400
}
'''
cyclework=r'''
function MakeWorkspace($name='Contract workspace') {
  OpenCreateScreen; SetText (NamedEdit 'Workspace name') $name
  $c=ClickBtn 'Create workspace' 400; Assert 'contract workspace created' ($c -eq 'ok' -and (DetailsShown)) $c
}
function AddManual($type,$target,$name) {
  EnsureDetailsAddForm; [void](SetType $type)
  SetText (NamedEdit 'Item target') $target; SetText (NamedEdit 'Item name') $name
  $c=ClickBtn 'Add item' 400; Assert "manual $type added" ($c -eq 'ok') $c
}
Section 'home-create-four-types' {
  ResetContractApp; Assert 'home has dedicated create entry' (HomeShown) ''
  MakeWorkspace
  AddManual 'Link' 'https://example.com/' 'Contract site'
  AddManual 'File' "$root\ok.txt" 'Contract file'
  AddManual 'Folder' $root 'Contract folder'
  AddManual 'Program' 'C:\Windows\notepad.exe' 'Contract program'
  $o=StoreObj;$w=@($o.workspaces|Where-Object name -eq 'Contract workspace')[0]
  Assert 'all four exact types persisted' (@($w.items).Count -eq 4 -and @($o.items.PSObject.Properties.Value|Select-Object -ExpandProperty type -Unique).Count -eq 4) (StoreJson)
  foreach($label in @('אתרים','תוכנות','קבצים','תיקיות')){Assert "Hebrew group $label rendered" (WaitText $label 2) ((Texts)-join '|')}
  $c=ClickBtn 'Back' 300;Assert 'current back route reaches home' ($c -eq 'ok' -and (HomeShown)) $c
}
Section 'manual-duplicates' {
  ResetContractApp;MakeWorkspace
  AddManual 'Link' 'https://example.com/' 'Original'
  AddManual 'Link' 'https://example.com/' 'Second'
  Assert 'Hebrew manual duplicate prompt' (WaitText 'הכתובת כבר קיימת בסביבה' 2) ''
  $c=ClickBtn 'ביטול' 300;Assert 'duplicate cancel changes no membership' ($c -eq 'ok' -and @((StoreObj).workspaces[0].items).Count -eq 1) $c
  AddManual 'Link' 'https://example.com/' 'Second'
  $c=ClickBtn 'הוסף בכל זאת' 300;Assert 'duplicate confirm adds independent copy' ($c -eq 'ok' -and @((StoreObj).workspaces[0].items).Count -eq 2) $c
}
Section 'saved-menu-and-picker' {
  ResetContractApp;MakeWorkspace;AddManual 'Link' 'https://example.com/' 'Resource'
  $c=ClickBtn 'Item actions' 300;Assert 'saved menu opens' ($c -eq 'ok') $c
  $submenu=MenuItemByName 'הוסף ל...';Assert 'add-to enabled with one workspace' ($null -ne $submenu -and $submenu.Current.IsEnabled) ''
  $submenu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand();Start-Sleep -Milliseconds 300
  foreach($label in @('סביבה חדשה','סביבה נבחרת','רשימה שחורה','רשימה לבנה','סביבה גלובלית')){Assert "destination $label" ($null -ne (MenuItemByName $label)) ''}
  [Windows.Forms.SendKeys]::SendWait('{ESC}{ESC}');Start-Sleep -Milliseconds 200
  EnsureDetailsAddForm;$c=ClickBtn 'Pick from existing resources' 300;Assert 'resource picker opens' ($c -eq 'ok') $c
  foreach($label in @('אתר','תוכנה','קובץ','תיקייה')){Assert "resource type $label" (WaitText $label 2) ''}
  $c=ClickBtn 'Pick resource' 300;Assert 'resource picker fills exact address' ($c -eq 'ok' -and (ValOf (NamedEdit 'Item target')) -eq 'https://example.com/') $c
}
Section 'category-and-unsaved' {
  ResetContractApp;MakeWorkspace
  $c=ClickBtn 'Back' 300;$c=ClickBtn 'Workspace actions' 300
  [void](InvokeEl (MenuItemByName 'ערוך קטגוריה') 'category action');Start-Sleep -Milliseconds 300
  Assert 'current Hebrew category label' (WaitText 'קטגוריה נוכחית: כללי' 2) ''
  $c=ClickBtn 'Close category popup' 300;Assert 'category closes' ($c -eq 'ok') $c
  $c=ClickBtn 'Contract workspace' 300;SetText (NamedEdit 'Workspace description') 'Unsaved contract draft'
  $c=ClickBtn 'Back' 300;Assert 'Hebrew dirty guard shown' (WaitText 'יש שינויים שלא נשמרו' 2) ''
  $c=ClickBtn 'Back to editing' 300;Assert 'X preserves draft' ($c -eq 'ok' -and (ValOf (NamedEdit 'Workspace description')) -eq 'Unsaved contract draft') $c
}
Info 'excluded product paths' 'Context open-with free-path popup, English opener label and capture-help design decision are not certified here.'
'''
capwork=r'''
function ExistingCapture {
  ResetContractApp
  foreach($n in 'Cap A','Cap B'){OpenCreateScreen;SetText (NamedEdit 'Workspace name') $n;$c=ClickBtn 'Create workspace' 300;Assert "created $n" ($c -eq 'ok') $c;$c=ClickBtn 'Back' 300}
  $c=OpenCaptureMenu 'סביבה קיימת';Assert 'existing capture opens' ($c -eq 'ok' -and (CaptureShown 10)) $c
  Assert 'scan finished' (WaitGone 'סורק' 30) ''
  $c=ClickBtn 'Pick workspace Cap B' 300;Assert 'exact workspace picked' ($c -eq 'ok' -and (CaptureTargetText) -eq 'Cap B') $c
}
function UntickSource {foreach($cb in @(Checks)){[void](EnsureToggle $cb 'source selection reset' $false)}}
Section 'capture-new-palette-dirty-guard' {
  ResetContractApp;$c=OpenCaptureMenu 'סביבה חדשה';Assert 'capture new visible' ($c -eq 'ok' -and (CaptureShown 10)) $c
  Assert 'capture new scan complete' (WaitGone 'סורק' 30) ''
  ShotRaw 'current-capture-note-20261126'
  $note=@(UiaAll $CT::Text | Where-Object { $_.Current.Name -like 'לא נמצא קובץ?*' -and -not $_.Current.IsOffscreen })
  Assert 'persistent limitation note exposed for requested screenshot' ($note.Count -eq 1) ''
  $rect=$note[0].Current.BoundingRectangle
  [void][W]::SetCursorPos([int]($rect.Left+$rect.Width/2),[int]($rect.Top+$rect.Height/2));Start-Sleep -Milliseconds 1600
  $tip=$A::RootElement.FindFirst($TS::Descendants,(New-Object $PC($A::ControlTypeProperty,$CT::ToolTip)))
  Assert 'real hover tooltip visible' ($null -ne $tip -and -not $tip.Current.IsOffscreen) ''
  $bounds=(CurWin).Current.BoundingRectangle
  $bitmap=New-Object System.Drawing.Bitmap ([int]$bounds.Width),([int]$bounds.Height)
  $graphics=[Drawing.Graphics]::FromImage($bitmap);$graphics.CopyFromScreen([int]$bounds.Left,[int]$bounds.Top,0,0,$bitmap.Size)
  $bitmap.Save("$PWD/out-images/current-capture-tooltip-20261126.png");$graphics.Dispose();$bitmap.Dispose()
  [void][W]::SetCursorPos(5,5)
  $palette=CapPalette;Assert 'unique scoped current palette exposed' ($null -ne $palette) ''
  Info 'observed palette UIA type' $palette.Current.ControlType.ProgrammaticName
  $swatch=CapSwatch '#8BAA9D';Assert 'capture swatch exists under palette' ($null -ne $swatch) ''
  RevealColorSwatch $swatch;[void](InvokeEl $swatch 'capture swatch');Start-Sleep -Milliseconds 200
  $c=ClickBtn 'Close capture' 300;Assert 'capture-only design invokes Hebrew guard' ($c -eq 'ok' -and (WaitText 'יש שינויים שלא נשמרו' 2)) $c
  $c=ClickBtn 'צא בלי לשמור' 300;Assert 'discard closes capture without saving' ($c -eq 'ok' -and -not (CaptureShown 1) -and @((StoreObj).workspaces).Count -eq 0) $c
}
Section 'capture-stage-arrow-dropdown-delete' {
  ExistingCapture;UntickSource
  Assert 'add-all current label' (CaptureAddLabelMatches 0) ''
  $folder=CheckByLike '*capfolder*';Assert 'owned folder captured' ($null -ne $folder) '';[void](EnsureToggle $folder 'select folder' $true)
  Assert 'add-one current label' (CaptureAddLabelMatches 1) ''
  $c=ClickBtn 'Add selected capture items' 300;Assert 'folder staged' ($c -eq 'ok' -and @(StagedRowRecords).Count -eq 1) $c
  OpenStagedEditor 'capfolder';$combo=NamedCombo 'Added item open with';Assert 'folder dropdown disabled' ($null -ne $combo -and -not $combo.Current.IsEnabled) ''
  $c=ClickBtn 'Toggle added capfolder' 300;Assert 'arrow collapses editor' ($c -eq 'ok' -and $null -eq (NamedEdit 'Added item name')) $c
  $c=ClickBtn 'Staged item actions' 300;Assert 'real staged row context opens' ($c -eq 'ok') $c
  $ow=MenuItemByName 'פתח באמצעות';Assert 'folder context opener visible disabled' ($null -ne $ow -and -not $ow.Current.IsEnabled) ''
  Assert 'unsaved row has no sharing' ($null -eq (MenuItemByName 'הוסף ל...') -and $null -eq (MenuItemByName 'שיתוף לסביבה אחרת')) ''
  [Windows.Forms.SendKeys]::SendWait('{ESC}')
  $c=RemoveStagedRow 'capfolder';Assert 'exact staged row deleted via visible action' ($c -eq 'ok' -and @(StagedRowRecords).Count -eq 0) $c
}
Section 'capture-site-default-save-duplicate' {
  ExistingCapture;UntickSource
  $site=CheckByLike '*example.com*';Assert 'fixture site captured' ($null -ne $site) '';[void](EnsureToggle $site 'select site' $true)
  $c=ClickBtn 'Add selected capture items' 300;OpenStagedEditor 'example.com'
  $combo=NamedCombo 'Added item open with';Assert 'site dropdown selects source browser' ($null -ne $combo -and $combo.Current.IsEnabled -and (ComboValue $combo) -like '*Edge*') (ComboValue $combo)
  SetText (NamedEdit 'Added item name') 'Contract site default'
  Assert 'staged preference not prematurely saved' (@((StoreObj).workspaces|Where-Object name -eq 'Cap B')[0].items.Count -eq 0) ''
  $c=ClickBtn 'Capture save' 300;$o=StoreObj;$workspace=@($o.workspaces|Where-Object name -eq 'Cap B')[0]
  $member=@($workspace.items|Where-Object name -eq 'Contract site default')
  Assert 'saved exact site has Edge launch default' ($c -eq 'ok' -and $member.Count -eq 1 -and $member[0].launch.browser -like '*msedge.exe') ($member|ConvertTo-Json -Depth 8)
  $c=ClickBtn 'Add selected capture items' 300;Assert 'Hebrew grouped duplicate choice' ($null -ne (CheckByLike 'הוסף שוב: example.com*')) ''
  $c=ClickBtn 'Capture duplicates skip' 300;Assert 'skip changes no saved count' ($c -eq 'ok' -and @(StagedRowRecords).Count -eq 0) $c
}
Section 'capture-program-and-below-viewport-removal' {
  ExistingCapture;UntickSource
  $program=CheckByLike '*Notepad*';Assert 'program fixture captured' ($null -ne $program) '';[void](EnsureToggle $program 'program tick' $true)
  $c=ClickBtn 'Add selected capture items' 300;OpenStagedEditor 'Notepad'
  Assert 'program editor has no opener dropdown' ($null -eq (NamedCombo 'Added item open with')) ''
  $c=RemoveStagedRow 'Notepad';Assert 'program cleanup' ($c -eq 'ok') $c
  UntickSource
  $c=ClickBtn 'Add selected capture items' 300;Assert 'add-all stages multiple fixture rows' (@(StagedRowRecords).Count -ge 4) ''
  $record=@(StagedRowRecords|Where-Object {$_.Edit.Current.Name -eq 'Toggle added example.net'})
  Assert 'deep target exact identity' ($record.Count -eq 1) ''
  for($i=0;$i -lt 20 -and $record[0].Edit.Current.IsOffscreen;$i++){ScrollStaged -360;$record=@(StagedRowRecords|Where-Object {$_.Edit.Current.Name -eq 'Toggle added example.net'})}
  Assert 'deep target made visible by scrolling' (-not $record[0].Edit.Current.IsOffscreen) ''
  $before=@(StagedRowRecords).Count;$c=RemoveStagedRow 'example.net'
  Assert 'deep row deletion removes only that identity' ($c -eq 'ok' -and @(StagedRowRecords).Count -eq $before-1 -and @(StagedRowRecords|Where-Object {$_.Edit.Current.Name -eq 'Toggle added example.net'}).Count -eq 0) $c
}
Info 'excluded product path' 'No verdict on missing capabilities-help affordance. Context free-path launch and English opener-row wording excluded.'
'''
for name,source,marker,work in [('cycle',cycle,'  # 1. overview:',cyclework),('capture',cap,'  # 1. dropdown ->',capwork)]:
    end=source.index(marker)
    cleanup=source.rindex('catch { $il = $_.InvocationInfo; "script error:')
    (p/f'contract-{name}.generated.ps1').write_text(source[:end]+common+work+'\n}\n'+source[cleanup:], encoding='utf-8-sig')
