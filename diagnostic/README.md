# Compile and non-browser fixture candidate

Local candidate only, 16 November 2026. No push, dispatch, compilation or Windows runtime has occurred. This packet is an adjunct to observer v1, not a new capture diagnostic or a C1 rerun.

## Proposed layout
Copy `observer/`, `fixture/` and `run-fixture.ps1` under a new `diagnostic/` directory on an independently reviewed diagnostic branch. Copy the workflow under `.github/workflows/`. No DEV/TEST/MAIN merge is proposed. The existing exact action pins are taken from the current DEV portable-entry validation workflow; both exact commit objects were freshly fetched via the public GitHub API and resolve to checkout v7.0.1 / upload-artifact v7.0.2.

## Scope and outcome
Feature-push-only to exact diag/raw-observer-fixture-v1 branch, standard public-repo `windows-2025`, 5-minute job cap, contents:read, checkout credentials not persisted, no secrets, no setup/install action, no browser launch or network socket. .NET 10 SDK must already be present or the job stops UNSUPPORTED. Build/restore uses the SDK defaults and may contact normal package feeds; no package dependencies were added. No runtime-version downgrade or compatibility changes are attempted.

The collector is compiled with FIXTURE_ONLY. This replaces the two-browser-name gate with a fixed `RawObserverFixture` executable name and cannot observe Chrome/Edge through the intended CLI. That build switch is the only observer-code delta from v1. It is not an identity proof against a malicious same-name binary. The wrapper launches the exact just-built local fixture binary, records hashes and the returned Process object, checks its PID against the fixed fixture readiness record, and launches the exact just-built observer. No untrusted launch path is discovered or substituted.

The fixture creates two windows using the built-in STATIC class and starts one short-lived copy of itself with --child, no browser. It publishes a readiness record by exclusive temp-file creation then move, and destroys its windows/exits after 15 seconds. It does not alter settings, register accounts or inject input. The observer's two raw native-window/PID endpoints must match the fixture's two handles, while continuing to label continuity UNPROVEN, UIA NOT_COLLECTED and effect authorization NONE. A synthetic-only desktop PNG is captured after the endpoint samples and must be visually inspected before treating the native-window fixture as verified. It is not an app-feature screenshot or capture-success evidence.

External timeout is bound to the Process object returned from the observer's own launch. After 12 seconds the wrapper kills only that collector object, never a process-tree/name/PID search. The refusal probe has its own 3-second object-bound limit. Fixture self-exit is awaited up to 20 seconds; failure throws with no kill fallback. The job-level 5-minute limit provides the outer disposable-VM stop. No browser process exists in this test.

Only these results are asserted: compile succeeds, process root present in both samples, both synthetic HWND/PID endpoints present, explicit non-authorization labels preserved, and output overwrite returns 2 while exact evidence hash stays unchanged. A PASS is `compileAndFixture:PASS`, browserCapture:NOT_TESTED, identityAuthorization:NONE. No child-churn attribution, PID-reuse, missing-owner/UIA/provider or hard-timeout behavior has been tested yet; those cases are NOT covered by this first fixture.

## How it changes the next decision
If compilation or basic marshalling fails, fix this small standalone observer rather than spend another browser attempt. If endpoints/refusal labels fail, no browser progression. If this limited fixture passes, the Win32 collection mechanics have evidence on one Server 2025 runner. The next review can focus on a separately grounded synthetic-browser launcher/root provenance and an explicit consent operator, without re-debugging raw process/window collection. It does NOT justify bypassing old attribution gates or clicking a browser consent prompt. If no exact safe disposable consent operator is available, stop there and report that remaining blocker rather than keep expanding the observer.

No UI regression, app package, release, pointer, website, TEST or MAIN action belongs to this packet. All observer-v1 caveats still apply, especially uncollected UIA/parent binding and no HWND continuity proof.

## Precision notes from source review

- The second sample begins only after the first sample completes and a 2-second sleep. It is not exactly 2 seconds from first-sample onset. Monotonic tick intervals are authoritative.
- `exitFileTime` from GetProcessTimes is undefined for a process that has not exited. Consumers MUST NOT interpret it as an exit event unless a signaled state for that same retained original process object establishes exit. The end-of-collection wait result is a later observation, not an atomic timestamp bracket around the earlier GetProcessTimes read; even an end-signaled object does not establish that the earlier exitFileTime was valid. This fixture does not consume or assert exitFileTime.
- Enumeration-to-open has a race: an opened handle binds its current process object and creation time, not the earlier enumerated occupant. No historical PID identity is established by opening it later.
- Owner depth 8 includes the starting window: at most 7 owner edges. A cycle or further owner leaves an incomplete result.
- The code collects IsWindowVisible and IsIconic, not IsWindow. IsWindow's documentation is cited only for its explicit HWND recycling/lifetime warning.
- Neither an error-free sample nor a full returned PID list establishes complete machine/session coverage or complete process history. The fixture checks only its explicitly launched synthetic root and two known windows.


## Execution route correction, pending focused delta review

The earlier manual-only route was not established: live repo default branch is main and workflow discovery lists no raw-observer-fixture.yml. GitHub requires workflow_dispatch's file on the default branch. Do not merge/activate it there. Replacement is only push on exact diagnostic feature branch diag/raw-observer-fixture-v1 with paths limited to this workflow or diagnostic/**, plus a redundant job event/ref guard. No workflow_dispatch, PR, schedule, release, tag or MAIN trigger exists. A push WILL start this fixture job, so a future reviewed push is itself the execution start and requires the already-scoped execution decision first. No push has occurred. No trigger change for any existing workflow is proposed. Existing repository workflows must still be checked for unintended matching triggers before that future push.

Live evidence: https://api.github.com/repos/my-sochen/Start-app returns default_branch main and private false; https://api.github.com/repos/my-sochen/Start-app/actions/workflows does not list this new filename. Exact action commits verified: https://github.com/actions/checkout/commit/3d3c42e5aac5ba805825da76410c181273ba90b1 and https://github.com/actions/upload-artifact/commit/cf430e030ddbb5b0abf93d22962f4752f3646cd9 . API object URLs used replace /commit/ with /git/commits/ under https://api.github.com/repos/actions/{repo}. GitHub event/branch rules read at https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows and https://docs.github.com/en/actions/writing-workflows/workflow-syntax-for-github-actions .

The overwrite probe proves only the File.Exists precheck refuses a preexisting output and preserves its hash. It does not test a CreateNew race or general overwrite-proof semantics. The short-lived child can exit between enumeration/open/image queries and legitimately cause INCOMPLETE; inspect those raw errors before diagnosing collector/Win32 failure. No browser conclusion follows either outcome.


## v1c visual evidence delta

Only run-fixture.ps1 adds System.Drawing.Common/System.Windows.Forms desktop PNG capture after endpoint assertions, before final result. No install, input, activation or browser. Bitmap/Graphics disposed in finally. Failure fails the single fixture attempt. This uses only the new disposable synthetic hosted desktop; never apply to a personal desktop. The captured PNG travels in the same bounded artifact. Actual pixels must be inspected to confirm the two synthetic windows; a PNG file alone is not visual verification. All prior source and workflow bytes stay unchanged.
