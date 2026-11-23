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
