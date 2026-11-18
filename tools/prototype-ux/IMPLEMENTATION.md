# Prototype UX: bounded local candidate

Based on DEV dd5e67819e482cbbbd51949327617495b501b91a. Preparation only. No push, merge or Windows runtime result is implied by this file.

- Keep existing appearance, opening semantics, persistence and dirty guards.
- Move existing saved-item editor from the global block above the list into the exact row identified by instance ID and selected workspace. Keep one active editor and existing VM buffer/commands. Visible Edit remains separate from the name/open action. Local Hebrew Save/Cancel/errors; focus moves into the active local editor after command/layout.
- Replace ellipsis buttons with native context menus on workspace cards, saved item rows and staged capture rows. Preserve original commands/row parameters via window VM Tag plus explicit popup DataContext. Program excludes Open with. Sharing preserves the exact disabled placeholder: visible only with another workspace, no command, saved and staged menus only. Add separators around reorder/design actions only.
- Capture source and saved target lists remain read-only and menu-free. Staged text fields remain direct unsaved editors, not a second saved-item edit mode. No Program opener capability was added or changed.
- No home search/new view machinery, auto-save, launch-on-edit, new shortcut, beta design, updater or release work.

Tests: VM identity/dirty cancel/local-save no-launch regression; source menu-host/row-editor contracts; complete existing Presentation and Platform tests; real-XAML synthetic four-group/long-Hebrew-LTR harness at 900x750, 720x750 and 720x400, actual native right-click and keyboard Menu plus Shift+F10, menu pixels/geometry, local error/cancel/save, staged/readonly distinctions. Harness compiled locally but has NOT run on Windows yet. Pixels and geometry may require fixes after first run.

Existing cycle1/capture UIA scripts route used action aliases through native Shift+F10 instead of removed ellipsis buttons. The capture staged route focuses the existing non-text remove button without invoking it. The unused cycle1 staged alias still points at its name TextBox and is not a qualified action route. Saved/card anchors use Edit/title. Specific last-row/delete checks remain row-bound. Saved-card geometry mask expects exactly two direct buttons and four bound text children instead of the removed actions-button topology; all type/count/parent/containment/unknown-coverage refusals remain. This topology is expected from source only until Windows qualification; no broadened accept-both fallback. Scripts parse with real PowerShell, but full journeys remain unrun. Synthetic focused harness supplements, not replaces, full candidate E2E.

The feature workflow is read-only, branch-specific, free hosted Windows evidence only, no retry, deployment, endpoint, release, promotion or protection changes. Exact reviews plus owner approval remain required before any merge.

R2 consolidated source corrections:
- Single details ScrollViewer keeps existing forms, saved list and expanded local row reachable at minimum height. Actual viewport bounds checked for Edit/name/error/Save/Cancel per row; scrolling retains exact draft/one editor/focus. Screenshots include both width and height.
- Menu placement clamps origin inside owner, independent of full expanded/scrolled host extent; exact share-placeholder visibility restored before opening.
- Staged row focusable for its action menu. TextBox context event remains ordinary native editing menu; focused harness tests Copy/Paste menu separately from row Apps/Shift+F10 routes. UIA journey alias anchors the existing remove button without invoking it to avoid taking over TextBox editing menu.
- Required readonly lists asserted exactly once each under actual names, then menu-free, not vacuous.
- Branch workflow exact HEAD guard and three-minute child timeout, artifacts on success and failure with three-day retention, no automatic rerun. Full E2E unrun; strict updated card topology remains runtime-unqualified.
- 294 Presentation and 62 Platform tests pass locally; WPF and focused harness cross-build pass. Native Windows checks/pixels still required after exact source reviews. No current UX commit/push/merge.

R3 narrow test-only follow-up after Moshe R2 review: expanded invalid editor remains open/draft-bound while its menu is measured at every size including720x400; staged action menu also exercised by physical right-click on its non-text label. Documentation now correctly limits the non-text staged UIA alias claim to capture, not unused cycle1. No product/workflow code change from reviewed R2.

R4 final narrow harness correction: expanded-editor menu key starts from enabled saved-name/open button in the exact host, Focus return and actual Keyboard.FocusedElement asserted, no invocation. Ordinary staged TextBox menu now tested by physical right-click in addition to Apps/ShiftF10; row action physical right-click remains separate. No product/workflow change from R2. Artifacts upload on BOTH success and failure for three days, not failure-only. These are intended runtime assertions, not observed Windows success.
