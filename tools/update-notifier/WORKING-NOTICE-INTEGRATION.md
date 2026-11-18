# Prototype working notice integration: narrow delta from82ef

Private source preparation, no commit/push/MAIN enrollment/merge/release. Owner approved prototype beta-release detection behavior, not a fake beta. This proposal makes the next prototype's production factory use MainBetaNoticeSource while reviewed MAIN descriptor candidate:null is the initial source state. It requires source reviews and normal owner integration approval before any merge. No new paid infrastructure or recurring upkeep.

## Exact change

- Add `.github/release/approved-beta-notice.json` with strict schema1/channel beta-notice/candidate:null. No beta is enrolled. Until this source file is actually approved into MAIN, live reads correctly report unavailable rather than known null.
- Production `UpdateComposition.Create()` uses the same actual-executable-identity-bound MAIN source factory used by the controlled native tests. Unstamped identity stays inactive and handler is disposed. Browser action stays fixed officialsite, explicit click only. The legacy rolling catalog endpoint remains null and is not a fallback.
- Verified null/equal/noapplicable source result has precise Hebrew no-approved-newer-beta-for-this-installation text. Unknown/error still says unable to check/current app keeps working, not up-to-date.
- Native fixture root now injects InactiveUpdateSource explicitly so changing production composition cannot cause public network I/O from the synthetic UI harness. Exact new source event fixture still uses real same factory with fixed synthetic handler.
- Feature-only synthetic workflow must match the new working feature branch for a later exact reviewed candidate; no job runs until pushed under parent direction.

## Lifecycle

Before first beta: one startup check, no popup on verifiednull, manual check uses cooldown and honest nooffer/unavailable distinction. Seven public API reads for a knownnull check under current path proof,11for directtag/onepage positive case, up to24hardcap. No polling/dailyrenewals/auth/token/statewriter. SharedIP60/hour remains a material availability limit.

First actual beta: release/source/package reviewed and owner-approved, actually published, official site presents matching approved beta, then separate owner-reviewed descriptor candidate enrollment. Existing shipped prototype can discover it without its own new release. This integration proposal does not approve any future candidate or change the firstbeta release gate.

Withdrawal: reviewed candidate:null through normal source control; next consistent read clears offer. Missing/malformed/API unavailable suppresses offer. API cache and final-read races mean no instant revocation promise.

## User's future beta clarification

From beta onward, a user who skips releases should upgrade directly to the latest approved/current offered release and preserve data, without installing intermediate versions. This is a future beta updater/publication-selection and migration regression requirement, not the current prototype feature. One enrolled candidate comparison in current notice code does NOT automatically discover/select globally latest approved release. Future publication process must keep the selected candidate current, and future updater/migration must be verified per supported release. No migration work/support-window/upgrader is added here.

## Real read evidence reused

An externalconsole against unchanged82ef actual .NET source+product SocketsHttpHandler performed one unauthenticated live check18November12:13:38-39 local. MAIN3826216,commit,3tree200JSON/UTF8/unencoded responses, no Authorization, then `Descriptor path unavailable`. This expected failclosed result proves real transport/tree-read mechanics up to missing descriptor. It does not prove liveknownnull, actualbeta, Windowsnetwork, site/download or activation. Log and harness accompany review. No more identical82ef runtime needed.

## Capability and publication labels
Stamped production builds are live-read capable, not inactive. This local proposal does not enroll the descriptor on MAIN or distribute a build. Missing MAIN descriptor means unavailable, not verified null. Workflow metadata distinguishes product capability, legacy rolling endpoint inactivity, isolated fixture traffic, and publication actions not performed by the workflow; it does not assert the current external MAIN state.

## Default factory evidence boundary
The native harness now constructs actual default Create, inspects exact selected source type and actual installed identity, then disposes it without starting commands/events. This is source-selection evidence only, not default-path HTTP or browser execution. Positive Loaded/window behavior remains injected transport; root remains explicitly inactive. Runtime evidence awaits a reviewed exact candidate.
