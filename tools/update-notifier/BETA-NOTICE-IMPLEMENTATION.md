# Prototype beta-availability notification: implementation boundary

2026-11-18. Local feature preparation based on DEV dd5e67819e482cbbbd51949327617495b501b91a. No publication, endpoint activation, timer, installer, beta automatic-update implementation, or website pointer change.

## User outcome and current boundary

The prototype must identify itself as a prototype and notify users when an approved beta becomes available. Beta itself will later include automatic updating and full design. This feature prepares the prototype notification path only. Production remains inactive until the publisher and endpoint activation are reviewed and approved. An inactive binary does not yet fulfill the final notification outcome.

## Contract

Reuse the existing strict eligible-catalog envelope and record validation, with a distinct `beta-notice` channel. Existing `prototype` catalog behavior and the website's prototype-only pointer are unchanged. No fallback to that pointer, GitHub latest, tags, draft artifacts or arbitrary release URLs.

Version syntax: X.Y.Z-beta.N. Numeric base version first, then stable > beta > prototype within the same base version. Stable installs never receive experimental beta notices. Prototype and beta installs can discover a strictly newer eligible beta; no downgrade on withdrawal.

Envelope: schema 1, positive revision, UTC generation/expiry seconds, channel beta-notice, at most128 records. Record: version, matching tag, full commit, release ID, asset ID, fixed asset name, byte size, SHA256, portable-v1 layout, approval record reference. All fields required; unknown/duplicate properties, conflicting duplicate versions/assets, non-integers, malformed text or invalid dates are rejected. Maximum response64KiB, total HTTP budget10seconds, maximum validity24hours, no future/expired grace. In-session revision floor only, no cross-restart rollback guarantee.

This is validation, not authentication of an owner decision. The publisher must independently bind the exact approved final package bytes to the actual published release and asset identities. A self-asserted approval string is not enough. Publication or a green job alone never supplies approval.

## Transport and application

The beta source reuses ApprovedCatalogTransport rather than introducing a parallel HTTP reader. The existing prototype default is preserved. Beta transport adds exact HTTP200 and uses existing charset/encoding/length/deadline checks.

Injected HTTPS source confined to the official Pages origin and Start-app path. Default transport disables redirects/cookies/default credentials. Require200+JSON, cap response length even without Content-Length, honor cancellation, no retry/cache/polling. Startup checks once, manual retry remains. Click rechecks eligibility; withdrawn/error clears offer, changed identity requires a new explicit click. Opens only the fixed official site, never metadata URLs, no download/install/execute.

Read build identity from executing App assembly, not Presentation or test assembly. Explicit StartAppReleaseVersion and StartAppSourceCommit build inputs create InformationalVersion plus assembly metadata; invalid/missing partial inputs fail compilation. Ordinary unstamped builds stay unidentified. Portable validation and release packaging pass the same version and commit to both reproducibility builds. Actual production endpoint remains null, so identity alone cannot enable a network check. Settings displays prototype identity separately from check availability.

## Required remaining evidence and activation

Local pure .NET/HTTP hostile fixtures, existing tests, app and real-XAML harness cross-build precede exact-target review. Windows beta popup/settings screenshots and packaged assembly stamp verification remain owed, not supplied by Linux builds. Final functional capture/save/reopen validation is separate; prototype upgrade-data preservation is no longer a release gate, but no data should be deleted as a side effect.

An approved operational publisher is still required: exact release approval and published-byte verification, withdrawal and asset-change reconciliation, serialized monotonic revisions, whole-site deployment or a separate authorized endpoint. A24hour catalog expires without renewal. No recurring renewal job is introduced here. Before activation, choose and review a bounded authorized renewal lifecycle and its failure alerts; do not pretend a static one-time catalog keeps working indefinitely. Offline/propagation/withdrawal races remain possible; fail closed and retain manual website recovery. Future activation and final MAIN/release approval are separate gates.

Tests must use synthetic catalog/transport/browser sinks, never a real publisher or owner data. Feature preparation is not permission to publish a beta, activate Pages, alter protections or change the website's current channel.
