# Reusable UI refactor running log

## Current scope
Refactor branches only. No merge into dev/main, no release or publish. Preserve behavior and exact pixels. Reuse existing UI pieces before introducing new ones. Report completed features, decisions requiring the owner, or blockers that stop work. Keep intermediate details here and in draft PRs.

## Dialog surfaces
- Draft PR127 introduced a Border-derived shared dialog surface, without another visual or layout layer. Adopted by13overlays and2code-built prompt windows.
- Original9capture fixture states compare exactly against a plain Border in the same process. This is scoped visual evidence, not all-dialog qualification.
- Draft PR128 expands coverage by forcing each overlay visible. It currently fails on the fifth visible overlay, the open-with dialog, with4glyph-edge pixels in the default Windows text. First4visible overlays compare exactly.
- Diagnostic44ad459 preserved both actual images and comparison data. The same4pixels remain. Root cause unproven. No tolerance, masks, rerun-until-green or product workaround.
- Diagnostic874c856 adds plain-Border reparent control images and counts. Local Windows-targeted compile passes; Windows workflow pending. Original strict shared/plain gate remains unchanged.
- Cross-process baseline repeat control has its own known glyph-edge discrepancies. Do not represent that check as passing.

## Menu audit
-8menu families: Options, View, Help, capture button, workspace right-click, saved-item right-click, staged-item right-click, Add-to submenu.
- Shared visual menu styles and anchoring/clamping already exist. Add-to submenu already reused across saved-item locations.
-7XAML menu roots include2dynamic placeholders. Entries and opening rules are still spread over several implementations.
-9dropdown selector locations.3open-with selectors already share program/default/browse behavior. Category/type/language selectors are different interactions from action menus.
- Proposed plain-language plan sent to parent: retain appearance, arrow/checkmark drawing and placement; consolidate menu construction/opening with menu-specific entries, preserve special submenu and staged-row rules. Selector wrappers later. No larger menu implementation started.

## Button audit
- Current exact XAML count136Button elements, not139. Counts use Button start tags only, excluding property tags and code-built buttons.
-69explicit shared/local keyed styles,67implicit or nested-style buttons.14Button-targeted style definitions, including4global base styles,6named local styles,4implicit local overrides. One implicit Capture style already extends the common toolbar button.
- Explicit uses:21toolbar,15primary action,7title-bar tabs,6quiet text,6swatch,5secondary,4dialog focus,3add link,2settings primary.
- Reuse-first order: preserve existing primary action, toolbar/secondary, quiet-text and title-tab bases; derive danger, icon, swatch and focus variants only where current measured appearance needs them. Unify duplicated settings primary/secondary and local dialog action styles next, then move repeated dimensions/margins into context-specific variants without making every button identical.
- Button extraction follows dialog-frame qualification alongside menus. No button appearance change started. Specialized caption tabs, swatches and row links retain their semantics rather than being flattened into one style.

## Current diagnostic
-874c856Windows control: plain reparent is0pixels on all reached states, but initial shared/plain remains4pixels on frame05. This alone does not establish product causation.
-a795cc0adds restored-shared comparison and collects every overlay failure before throwing. Same strict any-pixel gate; no product change. Local compilePASS. Windows pending.
- a795cc0Windows result: all13visible overlays reached.12are exact; only open-with frame05differs4pixels initially. Restoring the original shared frame then matches the plain Border exactly and differs from its own initial image by the same4pixels. That rules against a persistent class-dependent rendering difference in this fixture, but first-exposure/reparent cause needs a direct self-control.
-60950cfadds that shared-to-same-shared control before any class substitution, logs equal-invalidation comparison separately, and adds actual notice/conflict/new-workspace prompt windows. Local compilePASS. Windows pending. Initial gate remains red if any initial pixel differs.
-60950cfWindows self-control: open-with4pixels occur when reparenting the SAME shared frame before introducing any plain Border. Equal-invalidation shared/plain0. All13overlays reached. Notice exact. Conflict1glyph-edge pixel remains; restored shared matches plain, so first/second invalidation/control order needs checking. New-workspace1810pixels are actual focused-input border/caret loss on reparent, visually inspected.
-3e0806bclears posted loaded focus and records a second same-shared invalidation; gate unchanged. Local compilePASS; Windows pending.

## Dialog surface complete
-3e0806bWindows PASS: all initial/shared/plain/self/repeat/restored pixel controls0across9capture states,13visible overlays and3prompt fixtures.9cross-process comparisons and baseline repeat also0in this run. Posted focus settling was the harness issue; product unchanged. Scope remains frame/synthetic fixture, not all workflows.
-Run https://github.com/my-sochen/Start-app/actions/runs/45274588666
-Evidence https://github.com/my-sochen/Start-app/actions/runs/45274588666/artifacts/14865834957
-Completed-feature report sent to parent. Next menu reuse, then buttons alongside it.

## Shared menu component
-Draft129head93af878: shared owner-bound opening/placement and entry builders extracted, preserving two anchoring policies and special left-opening. Local actual build+7menu source contractsPASS.
-First Windows fixture stopped on baseline before any candidate comparison: clearing menu focus closed the popup before PointToScreen. Fix fixture by leaving open menu focus untouched, not product logic.
