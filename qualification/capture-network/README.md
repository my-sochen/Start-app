# Published prototype capture connectivity diagnostic

Prepared 2026-11-20. Test infrastructure only, no product changes, merge, release, or website publication.

## Request and target

Owner requests offline capture followed immediately by online capture, with Edge and Chrome normally X-closed or open with several tabs. Recent bug context adds minimized windows and an app launched before connectivity was available. Separately observe whether Chrome/Edge executables or Start-app itself appear as program items. Do not change filtering.

Work branch descends from software DEV `42d33fd341951225e22dbbdb009049cf2da28b88`. The workflow downloads and SHA-256 verifies the actual published portable proto2 ZIP. It does not rebuild or modify the app. Published commit is `8e55048afaef806a3d971469ab4de844cd45198e`, ZIP hash `f5084c0898bccb9e2e69943cdbf40fb41087c86c656b0a71c34896e2ce766a57`.

## Important limitation before approval

This safely tests **app/browser per-executable network egress blocking**, followed by restoring their egress. It does **not** disconnect the runner's network adapter or reproduce the Windows system-wide offline indicator. The runner/control channel remains online. This is a partial approximation of the user's whole-computer offline scenario and cannot rule out an OS-connectivity-notification-dependent bug. Full-machine offline remains explicitly UNTESTED. Do not label a pass as proof that the full reported scenario works.

Firewall rules affect only the exact published app executable and the current owned fixture browser executable on an ephemeral public-repository runner. All profiles are covered. The browser proves real blocked navigation with an accessible rendered network error, then fresh online responses with Example Domain content. No CDP offline flag, synthetic network adapter, injected capture result, or mocked URL classification is used.

## Matrix and meaning

For each browser independently: open 3 tabs, normal close all windows (X-equivalent UI Automation WindowPattern.Close, never force-kill as test action), and minimized. This gives 6 pairs of scans, 12 scans total. Only **8 open/minimized scans have live rendered browser-connectivity proof**. All **4 X-closed scans, both blocked and restored halves, are firewall-configuration-only observations**: neither verifies connectivity or its restoration without a browser window. Do not describe this as 12 verified offline-to-online connectivity scenarios.

1. Create a new disposable profile and verify native 3-tab fixture plus actual online responses.
2. For closed case, normal-close window and verify no owned browser window remains. Background process presence is recorded rather than assumed absent.
3. Install app/browser-only outbound firewall rules. For open/minimized cases, cache-busted navigation verifies rendered offline errors in all 3 tabs. Closed case has no browser to navigate, so firewall configuration is its only network-state evidence.
4. Start published StartApp.App.exe while its egress is blocked. Never restart between the pair.
5. Invoke capture through the actual app's UI. Require visible scanning start then scanning completion. Read resulting rows and take a desktop screenshot.
6. Remove only owned rules. For open/minimized cases, fresh cache-busted navigation verifies online content before the next capture. Re-minimize where applicable. Same app process, no intervening app restart. The closed case verifies rule removal only, NOT restored egress or live connectivity.
7. Capture again. Record expected fixture website presence, all displayed rows, program-executable observations, and minimized state before/after.
8. Close app/browser owned fixtures and restore firewall in finally. No workspace is created or saved.

Open/minimized expectation: all three fixture hosts captured. Closed expectation: none of those hosts captured. A missing fixture/precondition is BLOCKED, not product pass/fail. A settled capture with missing expected host is FAIL. Minimized-state preservation is a separate desired-behavior result, not conflated with site detection. Browser executable appearance is an observation against newly requested filtering, not a source change.

## Safety and evidence

- Public `my-sochen/Start-app` repository only, free standard Windows runner, one run attempt, 18-minute hard job timeout.
- Three new allowlisted files only, branch ancestry and complete tree delta bound to current DEV. No old branch modification, credentials persisted, account sign-in, paid usage, settings, or release effects.
- No user's browser profiles or saved data. Fresh disposable profiles contain public example domains only. Fail closed on preexisting Chromium windows.
- No disabling adapters, firewall service, global connectivity, or GitHub communication. No unrelated firewall rules changed.
- Finally cleanup plus independent workflow cleanup of diagnostic-prefixed rules. Runner destruction remains last safety net for hard timeout.
- JSON identities, actual UI observations, all results and desktop screenshots retained 3 days. Only JSON/PNG outputs, per-file 6MB and total90MB caps.
- Local PowerShell parser/static review is not Windows runtime execution. Screenshots must be visually inspected after run before closeout.

## Review focus

Check native UI selectors/timing, whether firewall scope and rendered error/content prove the stated limited network condition, closed-window proof, same-app offline-start transition, no synthetic pass, cleanup on failure/timeout, package/hash binding, no product changes, and honest untested full-machine-offline boundary. Review before any push, because the isolated push triggers the one-shot run. No runtime result exists yet.
