# Update notifier preparation (inactive)

## Implemented locally

- Dedicated update status row, manual check, and an explicit 'Open official download' action only after a newer eligible version is returned.
- The action opens https://my-sochen.github.io/Start-app/ in the default browser. It never executes a URL from release metadata, downloads an archive, installs files, closes the app, or writes workspace data.
- Numeric major/minor/patch/prototype ordering. A patch is an update. New version lines outrank older lines even if their prototype counter is smaller. Stable installations do not receive prototypes.
- One in-flight check, 10-second deadline, lifecycle cancellation, stale action cleared before each check, honest unavailable/no-candidate results, and browser failure fallback.
- Production composition is deliberately inactive: installed identity is unconfigured and the check button is disabled with an explanation. There is no startup request, timer, telemetry, network transport, legacy pointer fallback, or feed activation.

## Not implemented / not a finished updater

This is reversible UI and decision-logic preparation, not working update discovery in a distributed build. `IApprovedUpdateSource` is a trust boundary: a future adapter must establish eligibility before returning data. The `ApprovedUpdate` type name does not prove approval. Commit/hash shape checks establish syntax only, not approval, authenticity, publication or downloaded-byte integrity. The action hands off to the website; it does not claim the browser fetched or installed the offered version.

The existing prototype pointer is unsuitable as owner-approval evidence: declared passing tests or publication alone are insufficient. No existing release or prototype feed was enrolled by this change. Installed version is not guessed from default assembly 1.0.0 or a candidate timestamp.

## Before activation

1. Settle a reviewed per-release owner-approval mechanism. Recommended: an explicit publication allowlist binding version/tag, commit and artifact identity, updated only for the owner's approved publication. Alternative: a tightly controlled owner-only publication step with equivalent auditable evidence. The owner accepted the per-release exact approval rule on 2026-11-17 at 06:55:48; the technical record/control mechanism still needs review; a JSON `approved: true` asserted by an arbitrary source is not proof.
2. Choose and verify the exact HTTPS source and hosting/control model. Source adapter must bound time and response bytes, reject redirects to unapproved sources, validate schema/identity/channel, and fail closed. Do not reuse GitHub `latest` for prereleases or select DEV/TEST artifacts.
3. Stamp every distributed patch/build with a unique version and commit identity. Define rollback/revocation handling before automatic startup notification.
4. Add adapter tests using synthetic data, then actual Windows UI evidence for status wrapping, min-window width, disabled/available/failure states, browser handoff, close-during-check and regression coverage. No real download or browser opening should occur in synthetic CI tests.
5. Review exact changes with Yehonatan under the established workflow. DEV/TEST merges, MAIN, website changes, feed activation and publishing remain separate gated steps. No deployment is part of this preparation.

## Validation at this revision

The inactive baseline completed feature run 60, DEV run 61 and TEST run 62. TEST run 62 (43990661707, attempt 1, commit d2806060b16c8377a5cbe5ea9dc274f8dcc964db) received independent bounded evidence QA: actual package hashes, 287 cycle + 337 capture assertions, eight synthetic WPF states at 900/720 and native inactive packaged-app pixels. This is hosted 96 DPI evidence, not physical Windows 11, all DPI, real feed/browser handoff or distribution/install E2E. New adapter work needs its own tests and review. See design/INTEGRATION-CONTRACT-DRAFT.md for preparation only; its proposals are not approved activation settings.
