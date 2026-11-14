# Prototype release pipeline - plan and current state

Status: INFRASTRUCTURE PREPARATION. Distribution is intentionally stopped by the
repository owner: no distribution now, but infrastructure building continues.
Nothing here publishes, tags, merges to main, hosts, or enables a download.

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
- `website/` - static Hebrew RTL site + documentation, download button
  DISABLED, hosting NOT selected.

## What this change adds (DORMANT - no capability is activated)

- `.github/workflows/release-prototype.yml` - the release design as a
  MANUAL-trigger-only workflow: candidate code from a `sha` input, trusted
  scripts from the workflow revision (source separation the validation
  workflow does not have), hard tag-collision check, same test/UI/package
  gates, and a hard-disabled (`if: false`, `contents: none`) draft-prerelease
  job that documents the activation condition. Cannot run on any push.
- `.github/release/pick_release_pointer.py` - offline-testable selector that
  picks, among PUBLISHED prereleases, the highest prototype build whose
  manifest declares unit_tests=passed and ui_gate=passed, and emits a pointer
  document on stdout. Writes nothing. Selects nothing until a published
  prerelease with ui_gate=passed exists. Trusts declared metadata only.
- `.github/release/tests/` - offline unit tests for the selector and static
  checks for the dormant workflow shape.

## Open gaps

1. NO STABLE POINTER TARGET: nothing hosts the pointer document and the site
   cannot read one. The 5-day Actions artifact is the only current artifact
   path. "No manual link swap" is therefore not yet achieved.
2. The release workflow has never run; the draft-prerelease step has never
   produced anything (no draft, no published prerelease, no ui_gate=passed
   manifest in any release).
3. UI gate is a runner-scoped hard gate, not full UI coverage or determinism
   (see KNOWN-LIMITS.txt).

## Host-specific decisions left unresolved (owner / product decisions)

- Hosting for the site and for the pointer document (candidates recorded in
  the plan: GitHub Pages; Netlify Free; both advisory only).
- Whether/where the site reads the pointer (static file, API, build-time).
- Download enablement on the website (button currently disabled).
- GitHub Environment name + required reviewer for the release stage.
- Who approves each release; final wording of KNOWN-LIMITS.txt ("to be
  confirmed by the product owner before any public use").

## Activation checklist for the draft-prerelease stage (all owner-gated)

1. Owner selects hosting + pointer target (gap 1) and approves release flow.
2. Owner creates the GitHub Environment with a required reviewer and verifies
   the protection actually prompts.
3. Owner creates repository variable PROTOTYPE_RELEASE_ENABLE=true.
4. Edit release-prototype.yml: replace `if: ${{ false }}` with the documented
   design condition, add the `environment:` key, set job permissions to
   `contents: write`. Code change, reviewed, merged to main by the owner only.
5. First run is a manually dispatched, explicitly approved trial with version,
   sha and evidence recorded; maintenance afterwards is per-event by request
   (no monitoring, no SLA).

## Boundaries

- Only the repository owner merges to main, approves releases, publishes, and
  sets support policy.
- Pipeline review and diagnosis happen per scoped request with version, sha
  and evidence; no standing monitoring or unlimited operation.
