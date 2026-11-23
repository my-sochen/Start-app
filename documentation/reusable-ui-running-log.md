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
