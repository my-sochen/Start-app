# Published prototype capture connectivity diagnostic

Prepared 2026-11-20. Test infrastructure only, no product changes, merge, release, or website publication.

## Request and target

Owner requests offline capture followed immediately by online capture, with Edge and Chrome normally X-closed or open with several tabs. Recent bug context adds minimized windows and an app launched before connectivity was available. Separately observe whether Chrome/Edge executables or Start-app itself appear as program items. Do not change filtering.

Work branch descends from software DEV `42d33fd341951225e22dbbdb009049cf2da28b88`. The workflow downloads and SHA-256 verifies the actual published portable proto2 ZIP. It does not rebuild or modify the app. Published commit is `8e55048afaef806a3d971469ab4de844cd45198e`, ZIP hash `f5084c0898bccb9e2e69943cdbf40fb41087c86c656b0a71c34896e2ce766a57`.

## Important limitation before approval

This safely tests **app/browser per-executable network egress blocking**, followed by restoring their egress. It does **not** disconnect the runner's network adapter or reproduce the Windows system-wide offline indicator. The runner/control channel remains online. This is a partial approximation of the user's whole-computer offline scenario and cannot rule out an OS-connectivity-notification-dependent bug. Full-machine offline remains explicitly UNTESTED. Do not label a pass as proof that the full reported scenario works.

Firewall rules affect only the exact published app executable and the current owned fixture browser executable on an ephemeral public-repository runner. All profiles are covered. The browser proves real blocked navigation with an accessible rendered network error, then fresh online responses with exact normalized content of pinned owner-repository files. No CDP offline flag, synthetic network adapter, injected capture result, or mocked URL classification is used.

## Matrix and meaning

For each browser independently: open 3 tabs, normal close all windows (X-equivalent UI Automation WindowPattern.Close, never force-kill as test action), and minimized. This gives 6 pairs of scans, 12 scans total. Only **8 open/minimized scans have live rendered browser-connectivity proof**. All **4 X-closed scans, both blocked and restored halves, are firewall-configuration-only observations**: neither verifies connectivity or its restoration without a browser window. Do not describe this as 12 verified offline-to-online connectivity scenarios.

1. Create a new disposable profile and verify native 3-tab fixture plus actual online responses.
2. For closed case, normal-close window and verify no owned browser window remains. Background process presence is recorded rather than assumed absent.
3. Install app/browser-only outbound firewall rules. For open/minimized cases, cache-busted navigation verifies rendered offline errors in all 3 tabs. Closed case has no browser to navigate, so firewall configuration is its only network-state evidence.
4. Start published StartApp.App.exe while its egress is blocked. Never restart between the pair.
5. Invoke capture through the actual app's UI. Require visible scanning start then scanning completion. Read resulting rows and take a desktop screenshot.
6. Remove only owned rules. For open/minimized cases, fresh cache-busted navigation verifies online content before the next capture. Re-minimize where applicable. Same app process, no intervening app restart. The closed case verifies rule removal only, NOT restored egress or live connectivity.
7. Capture again. Record expected distinct fixture URL-path presence, all displayed rows, program-executable observations, and minimized state before/after.
8. Close app/browser owned fixtures and restore firewall in finally. No workspace is created or saved.

Open/minimized expectation: all three distinct pinned fixture URL paths captured. Closed expectation: none of those URL paths captured. A missing fixture/precondition is BLOCKED, not product pass/fail. A settled capture with missing expected path is FAIL. Minimized-state preservation is a separate desired-behavior result, not conflated with site detection. Browser executable appearance is an observation against newly requested filtering, not a source change.

## Safety and evidence

- Public `my-sochen/Start-app` repository only, free standard Windows runner, one run attempt, 18-minute hard job timeout.
- Three new allowlisted files only, branch ancestry and complete tree delta bound to current DEV. No old branch modification, credentials persisted, account sign-in, paid usage, settings, or release effects.
- No user's browser profiles or saved data. Fresh disposable profiles contain pinned public owner-repository files only. Fail closed on preexisting Chromium windows.
- No disabling adapters, firewall service, global connectivity, or GitHub communication. No unrelated firewall rules changed.
- Finally cleanup plus independent workflow cleanup of diagnostic-prefixed rules. Runner destruction remains last safety net for hard timeout.
- JSON identities, actual UI observations, all results and desktop screenshots retained 3 days. Only JSON/PNG outputs, per-file 6MB and total90MB caps.
- Local PowerShell parser/static review is not Windows runtime execution. Screenshots must be visually inspected after run before closeout.

## Review focus

Check native UI selectors/timing, whether firewall scope and rendered error/content prove the stated limited network condition, closed-window proof, same-app offline-start transition, no synthetic pass, cleanup on failure/timeout, package/hash binding, no product changes, and honest untested full-machine-offline boundary. Review before any push, because the isolated push triggers the one-shot run. No runtime result exists yet.

## V4 fixture correction after actual V3 runtime

V3 run 44667761789 reached zero captures: every pair stopped at initial online proof because the third-party example-domain page body no longer matched the expected title. Its fail-closed outcome is preserved, not counted as a capture failure or pass.

V4 uses three immutable public files in this owner's repository at pinned DEV: README.md, .github/CODEOWNERS, StartApp.slnx, through raw.githubusercontent.com. No hosting, source fixture, or website change is introduced. Full normalized expected bodies are derived from git show at that immutable commit. These are three distinct URLs on one host, not three hosts. Capture requires all three distinct complete URL paths, ignoring only the cache-busting query. Browser accessibility is explicitly enabled through its normal startup flag. Each proof records the unique requested URL, actual address, actual document-only UIA body/text names, document metadata and TextPattern errors. A matching tab title or browser-toolbar string cannot establish online success. Each tab proof has its own screenshot. Online proof requires exact requested scheme/host/path/query, full pinned body and absence of explicit Chromium network-error codes; offline proof requires matching requested URL and an explicit Chromium network-error code. Missing document accessibility remains BLOCKED.

The fresh diagnostic branch/workflow is diag/capture-network-v6-nov20. Same three-path tree-delta restriction, exact published proto2, per-executable isolation, 8 planned live-proof plus 4 configuration-only scan limits, cleanup and 18-minute timeout remain unchanged. Each new push is reviewed first. No product change or prior run rerun is included. Windows checkout CRLF conversion is expected; independently compare normalized reviewed script bytes to runtime hash when binding evidence.

## V5 selected-document proof attribution

V4 was not pushed or run: review found whole-window document pooling could misattribute background/stale text. V5 removes pooling. Require exactly one selected native tab and exactly one non-offscreen, positive-size browser Document intersecting its window bounds. Read only that document's own ValuePattern URL and its own TextPattern/text descendants. Both document URL and independent omnibox URL must exactly match requested HTTPS host/path/unique query before content/error can establish success. No URL-from-title fallback, no background document aggregation. If ValuePattern is unsupported on normal or error documents, or visibility is ambiguous, record the actual exception and BLOCK the case. This intentionally retains a runtime compatibility uncertainty rather than claiming unbound proof.

Live source header checks on 2026-11-20: all three immutable raw files returned text/plain; charset=utf-8 and no Content-Disposition. This is not a browser rendering claim. Expected bodies are short; actual capture filenames/paths still require runtime validation. V5 changes only attribution and isolated branch/workflow identifier; V3 runtime, V4 packet, V4-to-V5 delta and cumulative V3-to-V5 delta accompany fresh review so unchanged regions are available.

## V6 actual Chromium compatibility correction

V5 run 44670145636 completed two Edge-X-closed configuration-only captures successfully; the other ten scans were never executed due fail-closed fixture guards. Actual UIA recorded Chrome omitting HTTPS from its omnibox value, and Edge native firewall error documents exposing chrome-error://chromewebdata/ rather than the requested URL. Neither is a product capture failure.

V6 retains exactly one selected native tab and one visible positive-size intersecting Document. Online proof: accept omitted HTTPS only for the hard-coded raw.githubusercontent.com/ omnibox prefix, never normalize the document URL, require independent full HTTPS document URL including the unique query, full pinned body and no explicit network error. Offline proof: exact unique requested omnibox address (same bounded scheme-elision rule), exact document URL chrome-error://chromewebdata/, and explicit ERR_NETWORK_ACCESS_DENIED in that single document's own body. Other error types remain BLOCKED, not broadened. No background pooling, title-derived URL or firewall-configuration-only shortcut for open/minimized proof. All actual requested/omnibox/document URLs, body, attribution flags and selected-tab name remain recorded. Tab name is not an intended-index assertion; screenshots and manual interpretation remain required. Missing URL pattern or ambiguous visibility remains BLOCKED.

Same-source/unique-query/visibility binding proves a native browser network error for the requested address, not OS-wide offline status. Four X-closed scans still configuration-only. V6 is a new isolated branch/workflow, same pinned DEV and three-path source guard, no product edits. V5 actual source ZIP and focused V5-to-V6 delta accompany review. Do not use older historical V5 attribution prose as V6 behavior; this section defines the exact reviewed change.
