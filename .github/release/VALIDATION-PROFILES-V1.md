# Validation profiles V1 (review candidate)

Scope: one workflow, no pass cache, no release or MAIN enrollment changes.

| Ref | Profile | Automatic work | Meaning |
| --- | --- | --- | --- |
| `dev` | fast | Resolve/static release policy, profile contracts, all solution unit tests, App build, shared stamped synthetic 30-state WPF capture | Subset passed only. No candidate package and no full QA claim. |
| `test/validation` | full | All fast work plus existing Windows cycle, readiness helper, browser capture, assertion checker, portable package and final extracted-ZIP launcher smoke | Automation evidence for this exact candidate, not reviewer QA or owner approval. |
| Explicit named feature refs | full | Same full route as TEST | Unknown impact is never inferred safe for fast. New refs must be explicitly added as full. |
| Anything else | rejected | Resolver fails closed if invoked | No implicit fast route. Normal triggers exclude these refs; exclusion is not validation. |

`validation-profile.py` owns exact-ref selection and final required-result rules.
`synthetic-update-capture.ps1` is the single shared synthetic capture command. It preserves the existing exact checkout stamp, 30 image states, actual loaded App identity, default source construction and two injected event-path checks. These synthetic cases do not exercise live HTTP/browser delivery.

No path/impact detector, receipt cache or cross-run result reuse is introduced. Cheap unit/static checks deliberately repeat at TEST. Unknown feature impact gets full work, not a guessed subset. Full heavy commands remain in the same workflow. Current full runs already in progress use their committed definitions and are unaffected by this local candidate.

## Evidence and results

The same-run `validation-binding.json` records full commit/tree, ref/profile, run ID and attempt, SDK, runner image, capture/package versions, and SHA-256 digests of checked-in workflow/check/build definitions. The package copy also records the actual final ZIP size and digest. The existing manifest, BUILDINFO, consistency verifier and extracted launcher smoke continue to bind the final package. A dirty tracked checkout is rejected when evidence is made. These fields are identity evidence, not a hermetic or cross-machine reproducibility guarantee, and do not by themselves establish a pass. Actual artifacts/logs and runtime evidence must still be checked.

A final status job rejects missing, failed, cancelled, stale or skipped required jobs. In fast, package skipping is intentional and required. In full, all required results including package must succeed. A failed UI can still yield a diagnostic package labeled `ui_gate=failed`; artifact availability cannot make final full status pass. Workflow cancellation is not a pass, even if no final summary is produced. A green DEV subset must not be presented as QA, release readiness or a candidate.

The workflow is self-gating on the same SHA, not an independently trusted checker. Reviewer source review remains necessary. Yehonatan's exact-candidate QA and the owner's approval remain separate gates. This workflow does not grant or automate either. Any newly rebuilt ZIP has fresh identity and needs fresh package QA. Merge and publication are separate decisions.

## Availability and known limits

UI diagnostics, package and portable smoke artifacts are retained for 5 days. This is bounded availability, not a promise to preserve evidence for an open-ended review. Review must use retrievable evidence; if it expires, recover/rebuild and perform fresh package QA instead of claiming the old bytes remain qualified. The only retention change is smoke from 1 to 5 days, matching the other artifacts.

Windows runner remains `windows-latest`, SDK `10.0.404`. Existing checkout/setup/upload version tags remain unchanged; this change does not claim immutable action/runner pinning. Per-ref concurrency remains non-cancelling. No machine reuse, receipt service, new paid infrastructure, distribution or publication is added. Hosted Windows/96-DPI evidence does not cover physical consumer machines or other scaling settings.

## Review and execution plan

1. Review this exact local source/patch with both reviewers. Run standard-library routing/negative contracts locally.
2. After source review corrections, qualify the exact committed feature candidate through the full existing feature route, inspect actual current artifacts/log tails/pixels and extracted final ZIP evidence.
3. Present exact candidate evidence for owner approval before any DEV merge. The earlier workflow correction's integrated full run must close independently first.
4. After an approved merge, DEV uses fast; verify its subset declaration and intentional lack of package. TEST stays full and requires its own current candidate QA and owner-controlled promotion. No TEST/MAIN merge is authorized by this document.
