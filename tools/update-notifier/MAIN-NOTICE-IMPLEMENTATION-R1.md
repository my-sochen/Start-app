# MAIN-backed beta notice private implementation R1

Owner approved behavior at18November11:54:26 in the originating authenticated conversation. This implements private source only, no merge/release/activation/endpoint/descriptor enrollment. Source base is DEVee75 with the earlier reviewed52d9 dormant notifier net delta already applied locally. All work remains on DEV-derived feat/prototype-beta-integration-local, uncommitted.

## New source

MainBetaNoticeSource is a separate source from rolling catalogs. Fixed proposed descriptor path `.github/release/approved-beta-notice.json`; production UpdateComposition.Create remains inactive and existing catalog false gates unchanged. No real descriptor file created. Strict schema1/beta-notice/candidate:null-or-one reviewed exact catalog-style record. It reuses exact record validation through an in-memory synthetic envelope, not a public generated feed or a change to ApprovedCatalogContract policy.

Read sequence direct-tag, one asset page:11GETs. MAIN ref, exact commit/root tree,3nonrecursive trees for fixed path with regularblob mode proof, exactblob with SHA1 Git digest/size/base64, release, dedicatedasset, complete releaseasset page, tagref, MAINrecheck. Known null/nonnewer checks use7GETs. Each additional asset page or annotatedtag adds1. Hard cap24requests,8assetpages,5tagloopiterations,1MiB/response,4MiB/session,10s cancellation deadline. No cookies/credentials/redirects/decompression in product handler. No dynamic returned URLs or package downloads. Exact fixedrepo asset URL is checked as metadata, not navigated.

Published/nondraft/prerelease/exacttag/strictvalidnonfuturepublished_at, exactassetID/name/size/uploadedstate+membership/expectedURL, annotatedtag→approvedcommit, finalMAINsame all required. Any unknown/error gives unavailable through VM, not up-to-date. Same-session one-in-flight and30s normal-check cooldown; separate30s click cooldown permits immediate first explicit click after startup while shared403/429 retry-after/reset block applies to both. A changed candidate requires second click; an immediate repeated click during cooldown is conservatively unavailable rather than consuming another burst. No timers/polling/retries. Cooldown is per-process, not distributed shared-IP quota management.

Click-specific source interface added to UpdateViewModel, preserving legacy sources. Existing normal checks unchanged except dispatching click revalidation to that interface. App has an internal native-test-only factory bound to actual executable identity; no environment/CLI/config activation. Production still returns InactiveUpdateSource.

## Evidence

35new actual .NET tests pass;380total Presentation tests pass. Focused covers exact11read chain, startup-once/immediateclick, stable/equal/newer andnull nooffer, premature publication, assetstate/identity/membership, symlink/submodule/directory, movedtag/annotatedtag, source drift, cooldown/sharedratelimit, duplicate/extra descriptor, tree truncation/blobtamper, pagedmembership, redirect/non200, precancellation, unknownVMstate. Broad base had345tests, now380. Existing analyzer warnings remain in old tests. No nativeWindows/runtime/pixel/exactcombinedcommit proof yet. No fake beta/real external availability read performed by the new client.

## Review focus and limits

Synthetic HTTP handlers are not live transport/host/API quota proof. Source records identities but does not verify live ZIPbytes. Public API caching/finalread→website race and sharedIP60/hour remain. Website must actually offer matching approvedbeta before activation. Descriptor is not self-authenticating owner permission: eventual enrollment needs CR/tests/exactownerapproval/normalprotectedMAINmerge. No candidate is enrolled automatically from release metadata.

Source review needed on fixed descriptor path/interface, strict API framing/JSON/type handling, request budgets/cancellation/cooldown, publication metadata and test coverage. Tests do not yet exercise all allocation/deadline/inflight/annotatedcycle failure branches. Native source injection and exact combined App assembly stamp/runtime remain before any candidate push or deployment, under both source reviews and parent's sequencing.
