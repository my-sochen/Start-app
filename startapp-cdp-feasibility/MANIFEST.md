# CDP consent diagnostic - corrected wrapper candidate manifest

Status: LOCAL REVIEW ARTIFACT. Nothing here has been pushed, run, or authorized
for push/run. The push itself starts a run and consumes minutes and needs its
own separate approval. Supersedes the old dispatch-only wrapper
(c5a1fd3afb236eb3b165fd6009f6cfe50f41a2eec7a0a9b4fda7e65430b47e88), which must
never run: it allowed false PASS on missing phases and did not propagate the
child exit.

## File inventory (sha256)

| Path | SHA-256 | Lane |
| --- | --- | --- |
| `.github/workflows/cdp-consent-probe.yml` | `f0f4b90e87b15c6e0286c1eb246a815d207fd880ac60fb5bad7b9f12db4286fb` | wrapper (this delivery) |
| `startapp-cdp-feasibility/classify-assertions.mjs` | `de610e0e29b855801dc0a83a69b87f371b4b2b19230579b7811df720519cfab5` | wrapper (this delivery) |
| `startapp-cdp-feasibility/.gitattributes` | `a79691a93b46e49ce460c26ef22afcc03d6eca1e63bf2edbc20e96159510f6c9` | wrapper (this delivery) |
| `startapp-cdp-feasibility/read-targets.mjs` | `e3cf9ab79d66fb3841ba04787899eeec6f4272e05c4b3670bbdab22ab4e5b1c7` | AUTHOR v3, byte-identical |
| `startapp-cdp-feasibility/windows-consent-probe.ps1` | `8650f9b088234f16656cd9766d1cc410cda8e80fe75bd2e78edad056c118a5f5` | AUTHOR v3, byte-identical |

Author v3 packet (contract `V3-CONTRACT.md` sha256
`c934d49aeeaa112a915fe98cb5405506289a20d72cae0d8010f4f695eaf9ea2b`, delivered
through i2i, not part of the branch tree) supersedes ALL prior author bytes and
contracts. All files are LF-only; `.gitattributes` content is the single line
`* text eol=lf`, which keeps the author hashes intact after Windows checkout.

## Driver/manifest lineage (explicit, no same-file claim)

- Current portable driver `ddb677ac7d564863cae0e9d214e549a0df7dd293a6e0614bc7e3ef17a5e2e06f` SUPERSEDES old absolute-path driver `d5259a57cf3d169ba48f24b620c97a71f89a20d6199047a8e816b464f39a70ee`. Different files; the match is a lineage statement, not same-file proof.
- Historical mock manifest `ab378343938deedd39c315e4c4e330d82d2e0b389ed89c3d117f116dec66a87c` references the OLD driver and is stale.
- Server `fa43ae06b32e39610b203de5452cbf4fd464d77a10c22e0a95804c64c7feda9f` unchanged.

## What the corrected wrapper changes vs the old dispatch-only wrapper

1. Route: exact-name branch push `test/cdp-consent-isolated-v1` only, with a
   `paths` filter and a job-level deletion guard (`github.event.deleted != true`).
   No default-branch merge, no workflow_dispatch, no wildcard, no schedule, no
   PR/release trigger. Official basis: push runs workflows not merged to the
   default branch (GITHUB_SHA = pushed tip):
   https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows
   Manual dispatch on an unmerged branch remains unsupported:
   https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow
2. Integrity gate before any execution: Node 24.x check plus sha256 of both
   author files against the v3 packet hashes above; any mismatch (content or
   line-ending drift) fails the job and the probe never runs unreviewed bytes.
3. Raw child exit is preserved to `probe-exit-code.txt`; the run step never
   fails on the probe's own exit code, and whenever the probe step executes,
   the classification step runs on its output. Scope note: an earlier failure
   in the integrity/toolchain gate (or checkout/setup) stops the job before
   the probe exists, so there is nothing to classify; "always classify"
   covers completed probe runs, including nonzero child exit.
4. Independent classifier enforces the v3 matrix: required phases exactly once
   per browser, permitted statuses, nonempty version INFO, per-browser and
   global `complete` INFO markers exactly once. Missing/duplicate/invalid
   phase, any UNKNOWN/UNAVAILABLE, nonzero child exit, or an exit-0 run
   contradicting its records is INCONCLUSIVE, never PASS. Any FAIL is
   TEST_FAILURE. INFRA only when classification inputs are missing/unparseable.
5. Job exit semantics: COMPLETE_PASS and INCONCLUSIVE exit 0 (green means the
   diagnostic ran and was classified - never substantive PASS); TEST_FAILURE
   and INFRA exit 1. Verdict is printed as a `VERDICT=` line, a log annotation
   and a job-summary block.
6. Upload LOCKED OFF: no artifact upload step anywhere, so this workflow
   uploads nothing; screenshots and output are runner-local and would
   ordinarily be discarded with the ephemeral hosted runner (no absolute
   retention or destruction guarantee is made). Push has no dispatch inputs;
   there is no input gate to bypass. Console logs are visible to the
   repository audience (fixture-only data).
7. Actions pinned to full SHAs verified against the GitHub API on 2026-11-15:
   `actions/checkout@11d5960a326750d5838078e36cf38b85af677262` (tag v4),
   `actions/setup-node@49933ea5288caeca8642d1e84afbd3f7d6820020` (tag v4).
8. `permissions: contents: read`, `timeout-minutes: 10`, standard
   `windows-latest`, concurrency serialized per ref.

## Packaging note

The combined review archive additionally contains, OUTSIDE the branch tree
under `review-only/`, the author v3 contract copy and the runnable fixture
test harness for the eight classification cases below. Those are review
materials, not branch content; only the exact paths in the inventory table
belong on the candidate branch.

## Wrapper-strictness choices beyond the v3 contract minimum (for CR review)

- Requires `all/scope` INFO exactly once in addition to the contract-required
  `all/complete` INFO. The v3 probe always emits both.
- An unparseable/missing assertions.json or exit-code file is INFRA (harness
  could not produce records), not INCONCLUSIVE; the contract's INCONCLUSIVE
  bucket covers phase-level violations.
- COMPLETE_PASS additionally requires zero FAIL/UNKNOWN/UNAVAILABLE records
  anywhere (contract: no success on PASS count alone).

## Honest local proof and limits

- Classifier tested locally (Linux, Node 22.23.4) against 8 synthetic
  assertions fixtures: full-PASS matrix -> COMPLETE_PASS (exit 0); v3-realistic
  (post-disable UNKNOWN x2, child exit 2) -> INCONCLUSIVE; any FAIL ->
  TEST_FAILURE (exit 1); missing phase, duplicate phase, exit-0-with-UNKNOWN,
  and the old bug shape (chrome-only records, exit 0) -> INCONCLUSIVE;
  garbage JSON -> INFRA (exit 1).
- The author mock results (6 helper modes, old fixed-path driver, Node 22) are
  the AUTHOR's evidence, not ours; we did not re-run them and make no
  retroactive lineage claims.
- The probe's runtime behavior on windows-latest under pwsh 7 (WinForms /
  System.Drawing / UIAutomation loads) is NOT verified by us; first real run
  may yield INCONCLUSIVE on preflight. That is a safe outcome by design.
- No Windows runner, no push, no remote state was touched from this side.
  Before any later push: coordinator confirms branch name/reservation, the
  executor freshly verifies public repo + standard runner billing
  (https://docs.github.com/en/billing/concepts/product-billing/github-actions),
  and one combined CR approves the exact delta.
