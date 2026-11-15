# Prototype release pipeline - plan and current state

Status: ACTIVATED CODE, OWNER-GATED. The activation slice below is a code
proposal: the release gate, pointer deployment and website wiring are written
and locally verified, but they cannot run until the owner finishes the
settings steps (hosting, environments, variable) and approves each release.
Nothing here publishes, tags, hosts, or enables a download on its own.

## Product requirements

- Version updates should become fully automatic once the website exists.
- The upcoming prototype build is the FIRST version that will be offered on
  the site for download - after the prototype is finished, not before.
- The website carries full system documentation.

## What already exists (integrated, verified at df27fca)

- `.github/workflows/validation-feature-e2e.yml` - push-triggered validation on
  feat/dev/test branches: unit-test hard gate, UI hard gate evaluated by
  `ui_gate_check.py`, reviewed packaging chain (double publish, prototype
  notice, ordinal ZIP, BUILDINFO, manifest with the ui-gate job's ACTUAL
  result, smoke run of the exact shippable ZIP, `verify_candidate.py`).
  Scoped evidence for the validated revision only.
- `.github/release/` - `check_tag_free.sh`, `make_manifest.py`,
  `ui_gate_check.py`, `verify_candidate.py`, `KNOWN-LIMITS.txt`.
- `website/` - static Hebrew RTL site + documentation, download CTAs wired
  to the same-origin pointer document (all non-ok states keep both CTAs
  disabled and the copy truthful), hosting pending the owner decision
  (proposal on record: GitHub Pages project site).

## What this change adds (ACTIVATED, owner-gated)

- `.github/workflows/release-prototype.yml` - MANUAL-trigger-only workflow:
  candidate code from a `sha` input, trusted scripts from the workflow
  revision (source separation the validation workflow does not have), hard
  tag-collision check, same test/UI/package gates, and an ACTIVATED
  draft-prerelease job gated by the documented design condition
  (`workflow_run` of the release workflow on main, all gates green, and the
  repository variable `PROTOTYPE_RELEASE_ENABLE == "true"`), protected by the
  `prototype-release` Environment, with `contents: write` scoped to that job.
  Still cannot run on any push.
- `.github/workflows/release-pointer-update.yml` - ACTIVATED pointer-update
  workflow: on release lifecycle events (published, unpublished, edited,
  deleted) it re-selects the pointer from PUBLISHED prereleases and deploys
  the full site + pointer.json as ONE atomic Pages artifact (a pointer-only
  deploy is impossible by design). Gated by the same variable and the
  `github-pages` Environment; checks out the default branch only; a selector
  failure aborts before any deploy.
- `.github/release/pick_release_pointer.py` - offline-testable selector that
  picks, among PUBLISHED prereleases, the highest prototype build whose
  manifest declares unit_tests=passed and ui_gate=passed, and emits a pointer
  document on stdout. Writes nothing. Selects nothing until a published
  prerelease with ui_gate=passed exists. Trusts declared metadata only.
- `website/download-pointer.js` - the reader contract: validates the pointer
  (same-repo release-asset URL pinning, sha256), loads it over https with
  `cache: no-store`, and renders both CTAs via onclick assignment (non-ok
  states unbind any stale URL).
- `website/download-wire.js` - the activation wiring: resolves pointer.json
  same-origin, renders both CTAs, and resolves the state-bearing static copy
  (availability badge + download heading) per ok / none (incl. withdrawal) /
  invalid; the initial static copy is the truthful "checking" state.
- `.github/release/tests/` - offline unit tests for the selector and static
  checks for the activated workflow shapes.

## Open gaps

1. Settings steps are NOT done: Pages hosting, both Environments and the
   `PROTOTYPE_RELEASE_ENABLE` variable do not exist yet. Until the owner
   completes them, no job in these workflows can run.
2. The release workflow has never run; the draft-prerelease step has never
   produced anything (no draft, no published prerelease, no ui_gate=passed
   manifest in any release); the site will truthfully show "no verified
   version yet".
3. UI gate is a runner-scoped hard gate, not full UI coverage or determinism
   (see KNOWN-LIMITS.txt).
4. Workflow schema/actionlint review of the finalized workflows is still owed
   (not runnable offline here).

## Host-specific decisions (owner / product)

- Hosting + pointer target: proposal on record is the GitHub Pages project
  site with the pointer deployed atomically with the site (no manual link
  swap; the site reads pointer.json same-origin). Pending owner approval.
- Download enablement on the website: code done in this slice; live
  enablement rides on the owner settings steps below.
- GitHub Environments: `prototype-release` (release stage) and `github-pages`
  (pointer deploy), each with a required reviewer; the owner creates and
  verifies the protection actually prompts.
- Who approves each release: the owner; final wording of KNOWN-LIMITS.txt
  ("to be confirmed by the product owner before any public use").

## What remains for the owner (settings, all owner-gated)

1. Approve this activation slice (reviewed code, exact merge approval).
2. Approve the hosting prep package: enable Pages (Actions source), create the
   `prototype-release` and `github-pages` Environments with required
   reviewers, verify the protection actually prompts.
3. Create repository variable PROTOTYPE_RELEASE_ENABLE=true.
4. First run is a manually dispatched, explicitly approved trial with version,
   sha and evidence recorded; maintenance afterwards is per-event by request
   (no monitoring, no SLA).

## Boundaries

- Only the repository owner merges to main, approves releases, publishes, and
  sets support policy.
- Pipeline review and diagnosis happen per scoped request with version, sha
  and evidence; no standing monitoring or unlimited operation.
