# Settings and startup followup, separate from mechanics review

Owner original 2026-11-17 07:06:53, in reply to startup-once async/manual check:
"כן, אבל הכפתור בדיקה יהיה בהגדרות תחת ההגדרה גרסאות ועדכונים"

The behavior is settled: one nonblocking check per application launch, normal
operation offline, manual check in Settings under exactly גרסאות ועדכונים. No
automatic installation. It does not activate a source or publish a release.
The previous mechanics head a0f633bb975bffb023a4f49fe35febf87816ac5f is frozen
for source review; this followup is a separate delta.

Current source has no general Settings surface. Minimum implementation:
- Add a Settings entry, not unrelated preference categories.
- A small settings window contains the named section, installed version/identity,
  status and the manual check button. It shares one UpdateViewModel with the main
  window and does not start another check merely on opening/closing Settings.
- Remove manual check from main top bar. An available-update notice/action may
  remain visible outside Settings to satisfy the owner's original request.
- Give the Settings window an owner, keyboard close, accessible names and sensible
  minimum-size wrapping. Opening it must not dismiss existing unsaved-work guards
  or change data context/workspace. Close settings without disposing the app-wide
  updater; app close cancels and disposes once.
- Once-only startup discovery needs an explicit guard across repeated Loaded
  events. Missing installed stamp or inactive composition must still make zero
  transport calls. Network work is async and bounded by the existing deadline.
- On changed catalog identity at click, block and show newly selected identity
  before a second explicit click; never auto-install.

Required separate review/evidence: exact XAML/code-behind and capture-harness
changes; local unit/lifecycle/semantic build checks; actual Windows screenshots of
Settings at900/720 (inactive/busy/available/failure), native packaged-app window,
main availability notice, close-during-check, repeatedsettingsopen/startupguard
and existing overlays. Tests are synthetic/inert, no real feed/browser launch.
No Windows run, visual signoff, source hookup or activation is performed by this
planning document. Do not reuse TEST62 evidence as proof of the new Settings UI.
