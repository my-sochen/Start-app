# Pointer-update workflow - design, guarantees and honest limits (DRAFT for review, nothing applied)

Baseline: feat/dormant-release-pipeline @ cd028cd8344f207033fce250a87935d450efa0c7.
Companion artifacts: .github/workflows/release-pointer-update.yml, .github/release/update_pointer.py,
.github/release/tests/test_update_pointer.py. Baseline files reused unchanged:
.github/release/pick_release_pointer.py, website/ (reader contract owned by the requesting author's side).

## Deploy unit (settled with the author design addendum)

ONE Actions Pages workflow assembles the FULL website tree plus the selector-emitted
pointer.json into a single artifact and deploys it atomically (upload-pages-artifact +
deploy-pages, concurrency group "pages", cancel-in-progress false). No gh-pages branch,
no second writer, no pointer-only overwrite: update_pointer.py refuses to assemble an
artifact without the complete website tree (hard error, nothing deployed).
GITHUB_TOKEN pushes do not trigger branch-source Pages builds (GitHub docs), which is
why the publish source must be GitHub Actions, not a branch.

## Events and reconciliation matrix (manual approved execution model)

| Event | Trigger fires? | Result |
| --- | --- | --- |
| Owner publishes a qualifying prerelease (draft -> published) | yes (release: published) | selector picks highest qualifying among ALL published; candidate document deployed |
| Owner publishes an older build | yes | selector still picks the HIGHEST qualifying build; republish is idempotent |
| Owner dispatches manually | yes (workflow_dispatch) | same selection; bootstrap and manual reconciliation path |
| Owner unpublishes (back to draft), edits (incl. out of channel), or deletes a release | yes (release: unpublished / edited / deleted) | selector re-runs: next-highest qualifying build, or the safe "none" document, deploys automatically |
| Selector finds no qualifying prerelease (exit-3 case) | - | the selector's own safe "none" document is deployed: honest disabled-download state. NEVER an abort that preserves a stale "last good" pointer |
| Infrastructure failure (gh api error, malformed data, tie, missing/incomplete website, deploy failure) | - | loud abort, exit non-zero, NO deploy; previous live deployment stays untouched (deploy-pages replaces only on success) |

ACTIVATION PREREQUISITE (explicit): the workflow, selector, and website must
actually LAND on the default branch before any live run - the checkout pin
deploys whatever the default branch contains at run time, and current main does
not yet contain these files (the driver fails closed on a missing website tree,
so an early accidental run aborts rather than deploying, but that is not
readiness). The default-ref pin alone is not a readiness claim; the owner's
reviewed merge is.

Guard: vars.PROTOTYPE_RELEASE_ENABLE == 'true' only. The release payload is
deliberately NOT filtered by prerelease/tag shape: the selector re-evaluates the
whole CURRENT published set, and a release EDITED out of the channel (prerelease
cleared, tag changed) must still trigger reconciliation - filtering on the
changed release's new shape would skip exactly that run and strand a stale
pointer. A guard rejection exits without deploying.

## Withdrawal / rollback / revocation semantics (the requested fail-closed design)

- Withdrawal or rollback reconciles to the safe state on the next reconciliation
  event: unpublishing, editing, or deleting the current highest fires the trigger
  itself, and the selector then deploys the next-highest qualifying build, or the
  "none" document when nothing qualifies. Manual dispatch covers everything else
  (bootstrap, asset-level changes). The site then shows the honest
  disabled-download note.
- Rollback is therefore a selector property (highest qualifying among currently
  published), not a stored history: no last-good pointer is ever served by choice.

## Honest limits (residual, inside the manual approved execution model)

1. RESIDUAL STALENESS: release-level owner actions (publish/withdraw/edit/delete)
   reconcile automatically via the trigger. What NO event covers: deletion of an
   individual release ASSET without editing the release (no webhook fires), and
   Pages CDN edge propagation after a deploy. In the asset-deletion window the
   live pointer can name a release whose asset 404s (a failed download, not a
   silent wrong install); a manual dispatch reconciles. No schedule or recurring
   watcher is introduced, so there is no claim of instant revocation.
2. READER FRESHNESS: download-pointer.js fetches with cache: no-store, but Pages
   CDN edge propagation after a deploy is outside this design's control; treat the
   pointer as fresh only after the deploy run reports success. Optional defense in
   depth, owned by the author's side (requested, not overwritten): a reader-side
   existence check on the asset URL before enabling the button.
3. TRUST SCOPE: the pointer trusts declared release/manifest metadata (selector
   scope); ZIP bytes are not re-verified by this pipeline (carried from KNOWN-LIMITS).

## Atomic deploy unit and pinned versions

The artifact is the ENTIRE website tree from a pinned trusted source - the
repository DEFAULT BRANCH - plus the freshly emitted pointer.json: one
deployment unit, no gh-pages branch, no second writer, no pointer-only
overwrite (the builder hard-fails on a missing or incomplete website tree). A
redeploy replaces the artifact wholesale, so stale files from an earlier
deploy cannot linger.

PROVENANCE PIN (reviewer finding): on a release event GITHUB_SHA is the tagged
commit, so a bare actions/checkout would take the site and selector from the
RELEASE'S tag - an old published release could roll the website back, and a
deleted tag would fail checkout and block the very withdrawal reconciliation it
triggered. The workflow therefore checks out the default branch explicitly
(release events also run the workflow file itself from the default branch).
Trade-off accepted: the workflow cannot be exercised against a feature branch
via dispatch; the deployed source is the reviewed default branch by design.

SCOPE OF THE PIN (reviewer follow-up, corrected): the checkout pin guarantees
only what THIS workflow deploys from. It is not a repo-wide provenance
guarantee. The repository has multiple collaborators with push access
(confirmed at review time: mysochen2, zamfa4, mysochen3), so an "only the
owner can push" assumption does not hold. A DIFFERENT workflow - added by any
collaborator with push - could still target and deploy to the github-pages
environment if that environment is left unprotected, and this workflow's
checkout pin would do nothing to stop it. Protection of the Pages deployment
surface therefore depends on the environment and branch-protection choices
below, not on this pin alone. The default branch is also a mutable ref: the
exact resolved source commit of each run should be recorded from the run log
before any live acceptance is claimed.

## Actor / approver / self-review (owner policy decision - NOT pre-decided here)

Facts from current GitHub docs: the github-pages environment is the deploy-pages
convention (auto-created if absent); environments on public repos support
protection rules including required reviewers and an optional
"prevent self-review" setting (default: self-approval allowed); deployment-branch
restrictions limit which refs may deploy to an environment. Fact established at
review time (not from docs): the repo has push collaborators mysochen2,
zamfa4 and mysochen3, so any policy reasoning that assumes "only the owner can
push" is wrong for this repository. The CURRENT live state of the environment's
protection rules and of branch protection on main was NOT inspected; nothing
below asserts that protections are absent or present.

RECOMMENDED BASELINE (a recommendation for Yitzhak to decide - NOT a configured
or approved policy, no setting has been changed or chosen by us):
1. Add the OWNER as a required reviewer on the github-pages environment and
   leave "prevent self-review" OFF. Effect: when the owner himself publishes a
   release or dispatches (owner actor on tag or dispatch refs), the deployment
   pauses for his own extra approval click - a deliberate confirmation moment
   that also covers runs queued while he is away - without needing a second
   person. Any OTHER actor's run to the same environment would equally wait on
   the owner's approval, which is what closes the collaborator-added-workflow
   gap named above.
2. Stronger separation of duties (prevent-self-review ON, so actor and approver
   must be distinct people) remains OPTIONAL and only with an owner-selected,
   explicitly authorized approver. This document assigns nobody to that role.
3. Branch protection on the default branch (main): recommended to restrict
   pushes so that only the owner can push to main, keeping the reviewed
   default-branch source that the checkout pin trusts under owner control.
   Again a recommendation to verify and configure - the live branch-protection
   state was not inspected and no absence of protection is claimed.

Open owner choices, deliberately left to Yitzhak (we do not choose his approval
policy):
4. The separate required-reviewer gate for the draft-prerelease job (activation
   Part 1) has the same actor/approver question and is likewise his to resolve
   before enabling.
5. Environment deployment-branch restrictions: NOT the coherent protection
   mechanism for this workflow. On release events the run's deployment ref is
   the TAG, so a default-branch-only rule would deny exactly the lifecycle runs
   this workflow exists for (and a proto-tag-pattern rule would break
   reconciliation when a release is edited OUT of the channel). The recommended
   required-reviewer baseline above is the proposed substitute. This is a
   mechanics statement, not a settings choice - any protection rule remains the
   owner's to configure, and nothing here recommends removing or bypassing an
   existing protection.
6. Pages action versions here are major-tag pins (configure-pages@v5,
   upload-pages-artifact@v3, deploy-pages@v4); exact verified pins are an
   integration-time review item.
