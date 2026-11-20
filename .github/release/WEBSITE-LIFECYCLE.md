# Website lifecycle isolation, preparation only

## Status

No remote branches, pull requests, environment changes, merges, deployments,
workflow dispatches, tags, packages or software releases were created.

The owner approved keeping the repository and preparing separate website
work, development, test and main branches. Every merge and publication
still needs the owner's approval. Proposed names:

- `website/feat/*`
- `website/dev`
- `website/test`
- `website/main`

The repository default branch remains software `main`.

## Separate packets

1. Website content already qualified at PR74 head
   `919522f08772b9e0dfaf803db0d61f0709761b1d`, whole tree
   `50786ecd60f35eb3eb39780045f5a9fa40b97dda`.
   Local TEST-based preparation commit `be6aa2fa13b0af9b20dd33481b9a0f43469ba9dd`
   has exactly that tree. That earlier app-TEST promotion route is superseded.
   Preserve the bytes and apply the website delta to a website feature branch
   after the dedicated branches exist. Do not include software PR75.
2. This infrastructure packet changes website CI routing and the existing
   sole Pages writer. It does not change website content or app code.

## Bootstrap plan, not executed

Initialize all three website lifecycle branches from current default main
`3779d174923a592d7c56a22671624cbe041404b1`, tree
`824974b55b34c1b70494f5eb884ce58538316c28`.
Its `website` subtree `4be0e86befd605139fd67b524729aba8db9eaaec` equals the
subtree at last successful Pages workflow source `8e55048afaef806a3d971469ab4de844cd45198e`.
This bootstraps current approved website content, not the unpublished upgrade.
The presence of app files in a monorepo branch does not authorize app builds,
app merges, releases or deployments. CI rejects unrelated/app path changes.

## Publication boundary

A merge into website/main is NOT publication approval. The trusted default
main file `.github/release/website-live.json` records the immutable website
commit already approved for publication. Moving website/main alone leaves
that LIVE pin unchanged.

Both manual site publication and software-release pointer reconciliation use
this same pinned website source. They must never use default main website
content or moving website/test or website/main content implicitly.

The stage helper verifies the exact commit exists, is reachable from
website/main, contains the required site files, and has no symlink entries.
It exports only website data, excluding only the root website/tests directory and generated
pointer/catalog documents. It does not execute any website branch scripts.
Trusted selector/catalog/publisher logic stays on default main. The existing
release inventory, safe empty state, Pages environment, pages concurrency
lock and post-deploy pointer/catalog byte verifier remain in use.

Publishing a new website version therefore needs approval for the exact LIVE
pin change plus the explicit dispatch. A small infrastructure-only default
main change is necessary to install this mechanism and later update that
pin. This is a proposed operational tradeoff, not authority to merge it.
No repository default/security/environment setting change is needed.
If this tradeoff is rejected, choose another separately approved persistent
LIVE identity store before implementation; never silently publish test head.

## CI

Website PR checks target website/dev, website/test and website/main. They
remain read-only on public Ubuntu. They reject app/unrelated path changes,
run the offline publisher tests, existing website suites, actual Chrome DOM
harness and the unchanged default all-eight-locale harness. Python YAML
parsing runs in an isolated temporary virtual environment.

The local content qualification used two built-in ONLY_LOCALE batches due
to the command execution time limit; CI retains the original full run.
No new hosted CI run was started.

## Evidence and limits

The fresh offline publisher suite passed 193 tests on the V4 candidate content. This is local source/test evidence, not hosted workflow or Pages
qualification. The helper creates `website-source.json` with the content
commit, subtree and file hashes; it is staged and frozen with the artifact.
The existing automated live verifier still checks pointer.json and
downloads.json only, NOT every website asset or the source marker. Live
source-marker comparison, representative page pixels and download checks
remain explicit publication acceptance steps. No automatic rollback or
redeploy is introduced. A failed verification requires an owner decision.

CR should independently inspect the branch ancestry, exact content parity,
LIVE-pin approval boundary, stage helper's fail-closed cases, CI path guard,
all Pages writers, unchanged selector behavior, and the default-main
infrastructure activation prerequisite. Do not approve merges or publication
from this packet alone.

## Required rollout order, not authorized execution

1. Independently recover the owner approvals and verify current remote main,
   current live website assets and current selected software release.
2. BEFORE installing or merging the new writer into default main, create
   website/main at the verified bootstrap commit 3779d174923a592d7c56a22671624cbe041404b1.
   Read it back and verify the pin is reachable and website subtree matches.
   Create website/dev and website/test at that same base, without content
   promotion. Branch creation is not approval to merge or publish.
3. Qualify the exact infrastructure merge result and obtain owner approval
   for that merge. Install only after step 2 is confirmed. Do not activate
   the unconditional fetch while the required branch is absent.
4. Recheck the LIVE pin and branch before any separately approved dispatch.
   This pin is a process-controlled record, NOT a cryptographic signature of
   owner approval. Recover the actual owner decision for each change.
5. If website/main history is rewritten to drop the pin, STOP. Do not
   repoint the pin or rewrite history automatically. Ask the owner to choose
   recovery. Never fall back to default main or a test branch.

Rollback before activation is simply not installing the candidate. After
activation any missing branch/pin fails before deployment and leaves the
previous served site intact, but pointer updates will be unavailable until
owner-approved recovery. This dependency must not be described as harmless.

## V3 infrastructure-only PR coverage boundary

The current live website baseline has no npm lockfile. The separately
qualified PR74 website-content change includes it. Running npm ci on the
infrastructure-only PR would fail without testing the publisher change.
CI therefore always runs publisher regression tests and the five offline
website suites, but runs npm ci and actual Chrome DOM/all-eight-locale
harnesses only when the exact base-to-head diff includes website/ paths.
Nested additions, removals and rename destinations count. Infrastructure
and workflow changes receive publisher/static workflow policy tests; they
are NOT claimed to have fresh hosted browser qualification. The skipped
coverage is explicitly written to the job summary. The later content PR
must run the full browser harness with its committed lockfile. No old
baseline missing-lock failure is reported as a pass. V2 review does not
cover this V3 CI behavior change; fresh finite review is required.

## V4 rename and check-identity correction

The inline guard now uses git diff --no-renames --name-only -z. Both sides
of every rename are visible as delete/add paths, regardless of diff.renames.
A regression builds real temporary Git repositories, explicitly enables
rename detection, confirms R100 pairs, then executes the actual inline
workflow guard using real git. Software-to-website renames reject; website
to allowed infrastructure and nested website renames require browser coverage.

Infrastructure/static qualification and full website browser qualification
now have separate job IDs and visible check names. `infrastructure-checks`
is "Website infrastructure and static checks". `website-browser-full` is
"Website full Chrome DOM and eight locales" and visibly SKIPPED when there
is no website delta. It cannot appear as a successful browser job just
because infrastructure passes. The browser job waits for infrastructure
success, checks out and binds the same exact PR head, and runs the unchanged
full DOM and eight-locale harness without conditional individual probes.
No branch protection/required-check settings are changed. If protection is
later desired, its policy and GitHub skipped-job semantics require a separate
owner-approved settings decision. A skipped browser check is not evidence
of website runtime coverage and must never be reported as such.
V3 is superseded and was not uploaded or opened as a PR. Fresh finite V4
review is required before advancing this preparation.

## Publication safeguards: visibility and enforcement layers

Changing the LIVE pin must be both visible and review-gated. The layers are
distinct; neither replaces the other.

1. Visibility (this repository, in this change): the always-present pull
   request gate `Website infrastructure and static checks` runs on every
   pull request to `main` and the website lifecycle branches. Pull requests
   that do not touch `website/**`, the publisher workflow, or
   `.github/release/**` pass trivially after cheap scope detection; pull
   requests that do touch them run the publisher regression and website
   suites. `test_live_pin_matches_approved_baseline` records the exact pin
   the owner approved at publication on 2026-11-20 (website/main commit
   f94789e0f94c6f8a30c544bc4d6beb61a8409d4b, whose served bytes an
   independent post-publication review verified byte-identical to the
   pinned source), so any later pin change must update the test in the same
   pull request and cannot pass unnoticed. This is a consistency and
   visibility mechanism only; it does not by itself authenticate review.

2. Enforcement (repository settings, applied and maintained by the owner,
   not by any file change): on the `main` branch rule, require the status
   check whose exact name is `Website infrastructure and static checks`,
   keep requiring pull request reviews, and disallow bypassing these
   settings, so a pin change can only arrive through an open, reviewed,
   green pull request. The conditional browser job `Website full Chrome DOM
   and eight locales` is skipped by design when no website bytes change and
   must never be a universally required check, or unrelated pull requests
   would deadlock.

3. Deployment approval (already in place, to preserve): the `github-pages`
   environment requires the owner as reviewer on every release-pointer
   deployment. This is the standing approval layer between any pin and the
   live site, independent of how the pin arrived.

Prerequisite recorded from review: requiring approvals while disallowing
bypass deadlocks owner-authored pull requests unless an eligible reviewer
distinct from the author exists, because the code-owner entry is the owner's
own account and authors cannot approve their own pull requests. The
reviewer arrangement is an owner decision recorded outside this file; these
settings must only be tightened once it is resolved.

Residual exposure not addressed here: other repository workflows remain
dispatchable by repository-write actors; the scope guard in the gate
workflow is co-editable in the same pull requests it guards and must itself
be watched in review; administrator bypass on the `github-pages`
environment protection remains available to the owner account.
