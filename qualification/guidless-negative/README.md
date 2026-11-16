# Negative GUIDLESS flag-mode diagnostic N1

LOCAL SOURCE FOR READINESS REVIEW. No push/run has been performed or authorized by this artifact.

## Question and result limits

On the two exact preinstalled Windows browser builds below, does a single GUIDLESS WebSocket upgrade to `/devtools/browser/` fail in explicit flag mode? This is a negative compatibility observation, not an approval-mode implementation, browser permission, metadata capture, product eligibility or tabs/windows coverage test.

- Valid complete HTTP/1.1 404 header after stable owned binding: `NEGATIVE_404_PASS_ONLY`.
- Any syntactically parsed 101 header: `NEGATIVE_TEST_FAILED_DISCRIMINATOR_NOT_VALID`, close socket immediately. `observed101` remains recorded even if later binding/cleanup is inconclusive. Never send CDP/JSON or read beyond header terminator.
- Missing/changed identity, timeout, unexpected status, malformed/capped header, setup or cleanup uncertainty: inconclusive unless a parsed101 already established negative failure; that failure stays recorded and cleanup remains separately unconfirmed. No second URI, GUID fallback, retry run, positive inspect attempt or pin relaxation.
- Second browser case runs only if the first case passes with confirmed cleanup. A stop on Chrome leaves Edge not tested, not passed.

No statement here asserts that a successful result will occur. Exact Chromium source evidence is separately recorded in the earlier source slice; it does not prove these branded binaries behave the same.

## Exact bounded delta

Base `dev` 03c887e8fbd89ae69cb44d200bf2deabbe9f1605, rechecked by public `git ls-remote` on 2026-11-16 at 15:50 Asia/Jerusalem.

Exactly five added files: `.github/workflows/guidless-negative.yml` and this directory's csproj, Program.cs, Preflight.ps1 and README.md. No existing file changes. No reference to product projects or v4 source. v3/v4 commits remain unchanged. No NuGet package reference, SDK installation, native UIA/WPF dependency or consent automation.

Workflow uses an exact diagnostic branch push filter, run_attempt 1, repository-public guard, contents:read, sparse source checkout without persisted credentials and existing pinned checkout/upload action revisions. Existing workflows at this base were inspected: no other push trigger matches this diagnostic branch. This is not GitHub platform validation or a guarantee a future push can run. No push is included in local validation.

## Existing setup and identity gates

Historical runnable route: public repository https://github.com/my-sochen/Start-app; Windows runner label `windows-2025-vs2026`; image `20261103.298.1`; x64; installed SDK `10.0.403`. Recent existing q6 run https://github.com/my-sochen/Start-app/actions/runs/43865072600 ran on that route. Its failure was unrelated owned UIA observation, not a negative-GUIDLESS result.

Historical browser measurements recovered from original downloaded Chrome-full-v19 and Edge-e12 evidence:

| Browser | Fixed path | Exact version | Historical measured SHA256 |
|---|---|---|---|
| Chrome | ProgramFiles/Google/Chrome/Application/chrome.exe | 156.0.8078.80 | 46461accbdc87dc88a5bdd6d611be095dd6147db009863d794f742ca90662efa |
| Edge | ProgramFiles(x86)/Microsoft/Edge/Application/msedge.exe | 156.0.4314.58 | d35d48b11f0439eeeddbbff0bf54856e922f5e1506a2f3098b5a808fcec4068b |

These digests are historical measurements, NOT an independently known-good authenticity baseline. Preflight requires current valid Authenticode signature from expected publisher (Google LLC / Microsoft Corporation), exact version, equal measured digest, expected image and existing SDK. It holds both executable files against writes/deletion before either launch. Current allocation, installed image/binaries and signature validation cannot be verified without the later run. Any mismatch stops, no install/update/downgrade or pin loosening. Public-repo/free-hosted-run assumptions must be rechecked before an eventual run; nothing here authorizes paid capacity.

Build restores only against an empty local feed, so missing reference packs fail rather than download. Local Linux build uses existing SDK10.0.404; target net10.0 avoids requiring a WindowsDesktop download. P/Invoke is only reached by the Windows observation path. The `--self-test` path performs pure assertions without OS/browser/socket calls.

## One case

1. Create a collision-checked fresh TEMP dummy root. Reject reparse ancestors. No personal/default profile copy or read, no account login. Launch only about:blank using explicit new root, loopback port=0 debug flags and no-first-run/no-default-browser-check. No inspect toggle/preferences/policy write or native permission input.
2. Reuse the owned native CREATE_SUSPENDED / assign to kill-on-close job / membership check / resume pattern. Hold original process handle, verify creation time, native image and own-job membership. No WMI, command-line enumeration, activation, physical tab selection or global kill.
3. Require exactly one matching `Chrome_MessageWindow` found as a child of HWND_MESSAGE using the exact dummy-root caption; owner must be that original process. Hold the root's bounded DevToolsActivePort file read handle without write/delete sharing; record native file ID and content hash, never raw content/GUID. Parser validates stored path shape but never requests it. Table diagnostics consider IPv4 and IPv6 for that port; exactly one IPv4 numeric-loopback LISTEN owned by the browser root is required. All table sizes/counts are capped and only matching tuple data is exported.
4. One raw IPv4 socket to that exact listener. Before sending any bytes, verify local/remote numeric-loopback tuple, server-side ESTABLISHED row owner, and repeat full binding. Then one standards-shaped GUIDLESS upgrade request, no Origin/cookies/proxy/DNS/redirect/discovery. Header reads use one byte per call, stop at CRLFCRLF or 8192 bytes. Never consume body/WS frames. Case cancellation deadline25 seconds; readiness window10 seconds. Post-header binding checked again; the 101 case closes before post-check. No hard OS-call wall-clock guarantee; separate exact-helper supervisor45 seconds and workflow timeouts bound the run operationally.
5. Dispose own socket/endpoint handle. Terminate only own job, query active processes until zero (up to5 seconds), verify original root handle signaled; delete only created root without following reparse points, max4096 entries/depth32. Incomplete cleanup cannot pass. Killing this disposable browser is diagnostic cleanup, NOT browser permission revocation proof. If supervisor kills a hung helper, job-handle close is a containment backstop, NOT observed cleanup proof.

## Evidence target and checks

Proposed future artifact `guidless-negative-n1-${github.sha}`, retention3 days. Explicit bounded allowlist only: inventory.json (16KB), chrome-result.json (32KB), edge-result.json (32KB), compile.txt (1MB), completion.json (4KB), preflight-error-type.txt (1KB). No profile directories, endpoint GUID text, raw response headers/body, screenshots, binaries, credentials or scratch files. `completion.json` must NOT be read as positive compatibility or product readiness. Upload artifacts on failure does not make a failed/inconclusive run green.

Local author-only validation: offline restore/build using existing Linux SDK10.0.404, zero warnings/errors; 21 pure self-test assertions passed; YAML parsed with PyYAML and the three workflow source hashes match. `git diff --check` passed. PowerShell is not installed in this local environment, so real PowerShell parsing and all Windows P/Invoke, browser, runner-allocation and cleanup behavior remain UNVERIFIED. Workflow has a PowerShell parser gate before preflight execution, but that is future validation, not a completed check. Independent readiness review is required before push/run.

Authority: original owner Nov11 free Windows/dummy UI testing plus Nov15 additional BOTH-browser isolated metadata feasibility tests were personally inspected. Parent scope now permits preparation of this narrower negative-only diagnostic, not execution. No new general test permission is claimed or needed. Native positive-consent method and the user's manual-disable product clarification remain separate open questions.
