# Start-app: version delivery preparation (plan, patch, runbook)

Status: PREPARATION WITH BLOCKERS, CORRECTIVE REVISION 2 (supersedes revision 1 of this file and the bundle files it names). Open: section 4 (live evidence), 5 (pointer), 9. It is not apply-ready or production-ready. All live effects are disabled by default. Nothing is applied. No push, tag, Release, draft Release, workflow run, website change or account change has been made. Public distribution stays off until the prototype is ready and the owner approves it.

## 1. Target
The first future website download is a clearly labeled PROTOTYPE with known limits, not a stable release. The earlier package from commit 163f439 is not automatically that version: the candidate must be a fresh build of the then-current commit that passes the gates below. Not an in-app updater.

## 2. What is in the bundle
- `.github/workflows/release-prepare.yml`: manual-dispatch workflow: tests, package, verify, 5-day artifact; optional draft prerelease (disabled by default).
- `.github/workflows/update-pointer.yml`: manual-dispatch selector run; produces a pointer file as a 5-day artifact only (disabled by default; fails loudly when disabled; no destination yet).
- `.github/release/`: `verify_candidate.py`, `make_manifest.py`, `check_tag_free.sh`, `select_release.py`, `KNOWN-LIMITS.txt` (text needs product-owner confirmation).
- `.github/release/ui_gate_check.py` (new): trusted evaluator that reproduces the final aggregator of `windows-open-flow-feat.yml`.
- `fixture/run_fixture_test.sh` and `fixture/static_check.py`: offline tests.
`package-windows.yml` and `windows-open-flow-feat.yml` are untouched.

## 3. Pipeline and gates
All jobs use the one input sha in one run, so no gate can be met by an unrelated earlier run.
1. resolve (fails loudly, never skips: private repo, dry_run=false without both variables or sha not on main, missing trusted files, candidate lacking the e2e scripts): sha is 40 lowercase hex and exists; version matches `N.N.N-proto.N`; records whether sha is on main; checks the tag is free (see 4). Inputs are passed through environment variables, not interpolated into scripts.
2. tests (hard gate): `dotnet test StartApp.slnx` on the sha.
3. ui-gate (HARD GATE for the draft, see 4): builds the candidate, runs its two UI flow scripts, evaluates with `ui_gate_check.py`, both on the one input sha.
4. package (runs after tests; also after a failed ui-gate so the artifact can be inspected, then manifest `ui_gate=failed`): two same-path publishes, reproducibility comparison REPORT ONLY, prototype notice and known limits inside the ZIP, normalized ZIP with ordinally sorted entries, sha256, BUILDINFO, manifest, smoke of the extracted shipped ZIP, verifier.
5. draft-release: runs only if every one of these holds: repo variable `RELEASE_PREP_ENABLED=true`, repo variable `RELEASE_APPROVAL_CONFIGURED=true`, dry_run is false, sha is on main, tests, ui-gate and package succeeded; then Environment approval. It creates a DRAFT PRERELEASE only (hard-coded). Nothing in the files publishes, edits or deletes a release.
Trusted vs candidate sources: the verifier, manifest writer, smoke script and notice text are checked out from the workflow's own revision (`github.workflow_sha`, explicit in every checkout; a static check enforces an explicit ref). The candidate commit supplies product code and its own unit tests. This separates where files come from; it is not a sandbox, because candidate build code runs on the same runner. No security review is claimed. Constraint: the revision that holds the workflow must also hold `.github/release/*` and `.github/e2e/smoke-package.ps1`; an older candidate sha does not need them.
Tag safety: `check_tag_free.sh` checks the exact tag ref (`git/ref/tags/NAME`; GitHub returns 404 only when absent) and every release including drafts. Any lookup error is a failure, not "absent". It runs again right before creation. A concurrency group per version prevents two runs of the same version at once.

## 4. UI gate: correction of revision 1
Revision 1 said the UI checks are not a hard gate. That was wrong. Verified live on `feat/per-instance-items` at 9e681f1b (raw file, 2026-11-13): in `windows-open-flow-feat.yml` the two flow steps are `continue-on-error`, but the last step ("Fail the job if anything failed", `if: always()`, ~lines 294-319) exits 1 when: assertions.json or capture-assertions.json is missing or empty; any assert has ok=false; the last entry is not an ok `FLOW-COMPLETE`; the marker detail differs from `github.sha`; or `diag/errors.txt` exists. So a green run of that workflow on a sha means those aggregator assertions held and the marker matched that sha; that is a hard gate on the assertions only, not independent provenance and not a full-UI claim, with the limits below. The workflow triggers only on push to that branch touching `ci-trigger-feat.txt`; it has no dispatch.
Revision 2: `release-prepare.yml` has its own `ui-gate` job doing the same on the dispatched sha, using `ui_gate_check.py` (offline-tested, 12 cases). The scripts set `FLOW-COMPLETE` detail from `$env:GITHUB_SHA`; in a dispatch run that is the workflow sha, so the job sets it to the input sha inside the step process first. Checkout order in ui-gate: candidate at the root first, then the trusted revision into `trusted/` (a later root checkout would clean `trusted/`); a static invariant enforces the order and the job checks the checker file exists. Manifest `ui_gate` is `passed` only when that job succeeded in the same run; otherwise `failed` (default of the manifest writer is `not-gated`). Draft creation needs ui-gate success; the selector needs `ui_gate=passed`.
What the gate does and does not show (limits, not a full-UI claim):
- Coverage is exactly what the two scripts assert: cycle1 (create, details, add, duplicates, edit guard, empty dialog, open) and capture (real scan of Notepad/Explorer windows, new and existing workspace, staging, duplicates, guards, rescan). Nothing else.
- Real UI automation on a hosted windows-latest runner: flakiness is possible; determinism is not measured (no repeat-run data). A red run can be a runner problem; a green run is one sample.
- It exercises the `dotnet build` output, not the shipped ZIP bytes. The shipped ZIP only gets the smoke script.
- The e2e scripts and their assertions come from the candidate commit, so a candidate can weaken its own gate. The checker and workflow are trusted, the scripts are not. The commit marker is written by the candidate script from an env value the job sets, so it is a consistency check, not independent attestation.
- Not tested on a clean user machine or other Windows versions. Browser coverage is limited to two runner test scenarios in capture-flow@9e681f1, each run only if the browser starts on the runner and skipped (logged, not failed) otherwise: section 7 launches real Chrome with iana.org and rfc-editor.org tabs and hard-asserts both are listed (lines 556-570; skip at 572); section 8 launches Edge with Hebrew UI (--lang=he, separate profile) on example.com and hard-asserts that tab is listed (skip at 593). The main flow uses Edge (msedge) throughout. No unconditional cross-browser gate and no universal browser, version, window-count or language coverage. A green run does not show which of these sections ran unless its logs are checked. Whether v47 executed them is a peer report, not an independent retest here. `KNOWN-LIMITS.txt` wording is corrected accordingly (owner still confirms the text).
- Not run on GitHub. PowerShell steps in ui-gate, the env override, and artifact upload were not executed. Static YAML parse and offline Python tests only.
Live facts found: `windows-open-flow-feat.yml` and `.github/e2e/smoke-package.ps1` exist on the feature branch but return 404 on `main`. `release-prepare.yml` needs `smoke-package.ps1` and the e2e scripts in its workflow revision and candidate, and a draft needs the sha on main. So the pipeline cannot run until the feature work and these files are on main. Premerge live test of the workflow files stays an engineering gap (dispatch needs the default branch); not decided here.

## 5. Stable link and "no manual link replacement"
Verified in GitHub docs: drafts and prereleases cannot be "latest", so `/releases/latest/download/...` does not serve a prototype. The prototype therefore stays a prerelease.
Prepared: `select_release.py --repo OWNER/NAME` picks which version to offer. The tag must match the exact prototype pattern before it is used in any file path (the pointer workflow filters tags the same way before download). It requires a published prerelease, no duplicate asset names, the three expected assets, the asset URL equal to exactly `https://github.com/OWNER/NAME/releases/download/TAG/StartApp-win-x64.zip` for the explicit expected repo, a manifest agreeing with tag/asset/size, unit tests passed and `ui_gate=passed`; highest version number wins; otherwise it prints nothing and fails. What it evaluates: GitHub release metadata plus the manifest the release itself claims. It does not re-hash bytes or check run evidence, and there is no run or byte attestation. `ui_gate=passed` is written only when the same run's ui-gate job succeeded; the selector trusts that manifest claim. Tested offline (section 7).
NOT solved yet: where the website reads the selection. The pointer artifact lasts 5 days, so it is not a stable address. Options for engineering to settle once the host is known: a pointer file committed to a dedicated place by an approved workflow, or the site calling the releases list and applying the same rules. Until one is built and tested, a human still sets the site's link. This is the one open integration, and it needs no purchase.

## 6. Platform setup the project owner must do (not done by us)
- Environment `release-approval` must have a required reviewer. Naming an Environment in a workflow does not enforce review: an Environment with no rules runs immediately, and a missing one is auto-created without rules (docs). Reviewers need at least read access, so a collaborator can be one; no second person is required. On a personal-account repo only the owner can configure environments. Optional: "prevent self-review" (only useful if the reviewer is someone other than the dispatcher) and disallowing admin bypass. Required reviewers are available on the Free plan for public repos.
- Check before enabling: `gh api repos/my-sochen/Start-app/environments/release-approval` shows the reviewer rule; only then set `RELEASE_APPROVAL_CONFIGURED=true`. The workflow cannot check this itself, so the variable is a human attestation. Anyone who can edit repository variables or workflow files can weaken it; keep repo write access narrow.
- Dispatch: GitHub docs say the workflow must be on the default branch to be triggered with `workflow_dispatch`. So the files can be reviewed in a PR, but cannot be dispatched until merged to main. I did not ground running them from another branch, and do not claim it works.
- Actions are pinned to released versions seen on 2026-11-13: checkout v7.0.1, setup-dotnet v6.1.0, upload-artifact v7.0.2, download-artifact v8.0.2 (latest releases per the actions repos). Cross-version artifact compatibility was not tested.
- Runner tools: runner image readmes list Python 3.12 and GitHub CLI 2.103 on windows and ubuntu images. The workflow fails fast if python or the trusted files are missing. Not run on GitHub.

## 7. Evidence from offline tests (run here, not on GitHub)
`bash fixture/run_fixture_test.sh`: 62 PASS lines, exit 0 (revision 1 had 47 cases; added: manifest ui_gate wiring 4, ui_gate_check 12 incl. single-object JSON, missing/empty/garbage files, failing assert, no marker, wrong sha, marker not ok, errors.txt; extra static checks). Mutation check: disabling the sha comparison in the checker makes "marker commit differs" fail as expected. Static checks now also assert: ui-gate job exists and gates draft-release; resolve and update-pointer have no job-level `if`; no continue-on-error in release-prepare; ordinal ZIP sorting; manifest wiring. Earlier cases (verifier, tag-free with fake gh, selector) unchanged and passing.
NOT tested: any real GitHub run, PowerShell steps (ZIP sort, ui-gate, env override), smoke script, real asset digests, action version compatibility. ZIP sorting is code-only; reproducibility remains report-only.

## 8. Responsibilities
Project side: apply the files by PR, merge to main (owner only), set variables and Environment rules, dispatch, press Publish, set the site link, first-line response to failed runs.
Not built: stable website pointer integration (no-manual-links not achieved). Ownership: requester management owns site/pointer coordination; implementation is not accepted yet (executor Moshe's acceptance pending).
Review support: review PRs touching `.github/workflows`, `.github/release`, `.github/e2e`; diagnose a failed run when given its URL (pipeline, runner or action drift versus product code); check an approved version before publish when given version, sha on main, green run URL, draft URL and the owner's written approval (sha/tag/run agreement, hash equality across ZIP/BUILDINFO/manifest, prerelease and notice, asset name; result pass or fail only). Each is one bounded review on request. No response-time promise, no monitoring, no per-version operation, no uptime, cost or security review, no compatibility claims.
Product code and tests: product owner.

## 9. Decisions only the owner can make
1. Release approval policy: who approves a version for public (reviewer on `release-approval`, and whether a written approval per version is required).
2. Who owns UI gate flakiness and coverage (the gate exists; determinism and coverage are not measured).
3. The known-limits wording shown to downloaders.
4. After publication: what support is expected.

## Revision 2 sources (fetched 2026-11-13)
https://raw.githubusercontent.com/my-sochen/Start-app/feat/per-instance-items/.github/workflows/windows-open-flow-feat.yml (head 9e681f1b)
https://raw.githubusercontent.com/my-sochen/Start-app/feat/per-instance-items/.github/e2e/cycle1-flow.ps1 and capture-flow.ps1
https://api.github.com/repos/my-sochen/Start-app/branches/feat/per-instance-items
https://github.com/my-sochen/Start-app/pull/19 (open, draft, base dev, head 9e681f1b)
https://github.com/my-sochen/Start-app/actions/runs/43406675497 (package-windows at 163f439, old prototype only)

## Sources (revision 1)
https://docs.github.com/en/rest/releases/releases
https://docs.github.com/en/repositories/releasing-projects-on-github/linking-to-releases
https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository
https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments
https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments
https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow
https://docs.github.com/en/rest/git/refs
https://api.github.com/repos/actions/download-artifact/releases/latest (and checkout, setup-dotnet, upload-artifact)
https://github.com/actions/runner-images (readmes)
