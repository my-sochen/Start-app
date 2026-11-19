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

The fresh offline publisher suite passed 191 tests on the revised candidate content. This is local source/test evidence, not hosted workflow or Pages
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
