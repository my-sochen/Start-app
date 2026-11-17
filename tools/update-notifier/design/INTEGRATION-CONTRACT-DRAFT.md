# Approved update integration contract (draft, inactive)

Baseline: TEST d2806060b16c8377a5cbe5ea9dc274f8dcc964db, 2026-11-17.
No endpoint, approval record, release, MAIN merge, startup network request or live
configuration is created by this document. The owner accepted at 06:55:48 that each exact release must be approved before
either website or app offers it, with preparation/tests automatic. This does not
approve a particular release or activation. The technical approval-record storage
and startup-check UX remain proposed, not silently chosen.

## Settled product behavior

- Include bugfix/patch updates, using numeric version precedence.
- First stage opens the official website on an explicit click. Download and
  installation are manual. Do not add an installer or replace workspace data.
- Keep the current free Pages/Releases distribution route. Developer and TEST
  artifacts are not user releases. Publication and MAIN promotion need the owner.
- People using the already-published binary need one manual update to receive
  this new capability. A server feed cannot add code to their old executable.

## Separate facts that must not collapse into an "approved" boolean

1. Owner decision: the authenticated approval identifies the particular release.
2. Published identity: tag/version, exact tagged commit, release and asset IDs.
3. Verified payload: actual final ZIP bytes match hash and size, build identity,
   test evidence and the approved publication. A TEST ZIP with a different commit
   is not the final published ZIP, even when its source tree matches.
4. Eligibility: an approved, published, non-withdrawn artifact is currently offered.
5. Installed identity: the running payload carries its own version and commit.

A JSON field, a release author's label, a passing job or publication alone proves
none of the other facts. HTTPS establishes the selected host connection, not the
owner's decision. Internal hash agreement is not code signing or publisher proof.

## Proposed shared eligible-catalog contract

The exact endpoint and gate are deliberately unselected. An authorized publisher
would generate one catalog for both website and desktop from verified publication
records. It must not accept an arbitrary collaborator's self-asserted approval.

Proposed envelope: schema, monotonically increasing revision, generated UTC,
expiry UTC, channel and releases. Proposed eligible record: version, tag,
40-lowercase-hex commit, release ID, asset ID/name, size, SHA256, layout and the
approval record identifier. IDs are provenance references, not credentials.
No tokens, personal details, instructions or arbitrary browser URLs belong in it.
Only the fixed official website is opened by the desktop.

Keep approval records append-only/auditable; withdraw eligibility separately.
Reject duplicate versions with conflicting identities, unknown schemas/channels,
invalid dates/hashes/sizes, expired documents, and missing provenance references.
A proposed TTL is a design parameter, not chosen in this draft. A client unable
to establish freshness must not claim its installed version is the latest.

Current prototype records use X.Y.Z-proto.N. Stable support must either be covered
by the same reviewed publisher or rejected by this first adapter; do not silently
interpret arbitrary beta/preview channels. Stable installations reject prototypes.
Order numerically by major, minor, patch, then prototype number; a stable version
outranks its same-line prototype. Never compare proto N globally across lines.
Website and application must use identical fixtures and selection results.

## Build identity stamping

Use one validated build input for payload Version/InformationalVersion, manifest,
BUILDINFO, prototype notice and catalog record. Include exact commit in the
informational identity. Read from the executing application assembly, not a
Presentation test assembly or a candidate timestamp. Unstamped/debug builds
remain disabled. Reject a stamp whose version/commit disagrees with final package
metadata. Preserve reproducibility by using the same identity on both builds.

Validation currently labels every run 0.1.0-proto.1; this cannot be treated as a
sequence of distinct distributed updates. Proposed release numbering must move
forward, including bugfixes. Decide exact next publication version at release
preparation, not by silently rewriting an existing published tag or asset.

## Client adapter and composition

- Bound total time (existing ten seconds) and response bytes before parsing.
- Only selected HTTPS host/endpoint; reject cross-host redirects, unsafe schemes,
  embedded credentials, unexpected content types and malformed/oversized data.
- Use a dedicated HttpClient handler with automatic redirects disabled. Stream
  with ResponseHeadersRead and enforce byte cap even without Content-Length.
- No authenticated API token in a distributed application. No DEV/TEST fallback,
  GitHub latest shortcut, arbitrary metadata URL or legacy pointer fallback.
- Clear stale download state before each check; handle cancellation, concurrent
  clicks and application close. Fail closed without blocking normal app work.
- Production remains InactiveUpdateSource/null installed identity until reviewed
  integration and an explicitly approved activation change.
- Startup-once async discovery plus manual retry is proposed, not approved here.
  No background timer, polling cadence, telemetry or network-on-start change yet.

## Website/download handoff and revocation

The website must select from the same eligible catalog. The app must revalidate
before opening if its notice could be stale, or give an honest stale/unavailable
state. Because opening a browser is not an atomic install, the website may move
to a newer eligible release after the app checks. Its eligibility checks must fail closed on known ineligible data and must not
promise an exact version was installed. A static catalog cannot guarantee that
withdrawn bytes are never offered during propagation or after a check. No automatic downgrade of an
already installed application on withdrawal. Stop offering a withdrawn version;
if a newer eligible version remains, offer that; otherwise show no eligible update.

Pages propagation, release-asset deletion and offline clients prevent an instant
revocation guarantee. The existing pointer preserves its previous deployment on
infrastructure failure. Approval, freshness and withdrawal defenses therefore
need a bounded policy before auto startup notification is enabled.

## Ownership and review sequence

1. Local pure contract/version/build-identity preparation and synthetic tests.
2. Main routes Malka the publisher/stamping/gate design; Moshe the same-catalog
   website reader contract. They act only under their own accepted scopes.
3. Implement the owner-accepted per-release eligibility gate using a reviewed
   technical record design. Confirm startup-check behavior if not otherwise settled.
4. Implement actual source and publisher/reader adapters on feature branches,
   preserving disabled production wiring until activation is separately approved.
5. Yehonatan exact review, normal DEV verification, TEST promotion and per-run QA.
6. Only then present the exact MAIN/publication/activation decision to the owner.

## Evidence already complete vs still required

TEST62 evidence QA is complete for the inactive baseline: exact run/commit,
actual package hashes, 287+337 assertion results, eight synthetic 900/720 WPF
states and native inactive packaged-app pixels. Do not repeat it for this draft.

Required for new code: hostile catalog fixtures, bounded HTTP transport tests,
actual installed stamp inspection, integration tests for publication eligibility
and shared website selection, browser-handoff and close-during-check coverage.
Test offline/timeout/redirect/oversize/expired/withdrawn/race/mismatched identity,
prototype/stable ordering and preservation of existing workspace data. Use fake
transport/browser sinks in synthetic CI, not real web requests or installers.
Physical Windows11/other DPI and download/install distribution E2E remain known
coverage gaps, not proof supplied by hosted96DPI scripted screenshots.

## Publisher design constraints from Malka's first review (not implementation)

- Protect append-only approval/withdrawal events and name their permitted writers,
  credential custody and authenticated approver/event time/order. Scope exact
  version/channel/tag/commit/asset name/hash/size plus publication IDs.
- If release or asset IDs are allocated after the owner approves the final bytes,
  preserve the byte-bound approval in a separate transition that binds the newly
  allocated IDs. A draft ID being absent is not permission to approve different
  bytes later. Re-read published asset identity and hash actual published bytes
  before making it eligible.
- Serialize catalog publishers or use compare-and-swap on revision against the
  approval/withdrawal event order. A delayed publisher must not overwrite a later
  withdrawal with an older eligible record.
- Failed generation/deployment may retain the prior complete website unit only
  with an explicit freshness/expiry defense and a visible operational failure.
  Asset deletion must remove eligibility; ordinary release-event reconciliation
  alone misses asset-only deletion. Do not claim instant invalidation for offline
  readers, cached Pages edges or an already downloaded ZIP.

These are initial design recommendations relayed by main. No corresponding live
records, permissions, serialized publisher, deployment policy or expiry exists as
an effect of this preparation. Version/stamping and reader review remain open.

## Malka final design closure and proposed policy profile

The design review is closed, not implementation acceptance. Use one validated
version+commit across the executing WPF InformationalVersion, ZIP, manifest,
BUILDINFO and catalog; fail disagreement before approval. Never reuse a published
version/tag for different bytes. Unknown schemas should show an honest manual
update recovery route, not silently fall back to the legacy pointer. Reader
cache lifetime must not exceed document validity. Actual protections/settings
must be audited before activation. Withdrawal never silently downgrades an install.

Technical proposals for review, NOT deployed policy:

| Item | Proposed initial profile | Consequence / remaining proof |
| --- | --- | --- |
| Catalog bytes / records | 64 KiB / 128 records | Small fixed client budget; pagination/rotation is a publisher concern. |
| HTTP total budget | existing 10 seconds | Includes headers/body/parse; do not retry silently inside the budget. |
| Document lifetime | at most 24 hours | No refresh deployment is authorized by this draft. Without one, availability expires honestly. |
| Future clock tolerance | reject future generation; allow no expired grace | Fail-closed at cost of clock-skew availability; give manual website recovery. |
| Cache | no-store transport; no durable cache initially | Offline means cannot confirm latest, not current. Pages edge freshness remains bounded by document expiry. |
| Revision rollback | monotonic publisher CAS; client in-session high-water mark | Cross-restart rollback needs durable client state or signed expiry/authority design; do not claim it solved by a session counter. |
| Click freshness | repeat bounded eligibility check on explicit click | If withdrawn/error/expired, clear action and explain; never open stale metadata URL. |
| Approval IDs | byte-bound approval event followed by published-ID binding event | Owner approves final bytes once, not one approval before and another silently inferred after substitution. |

TTL is a real availability-vs-revocation tradeoff. A one-day catalog with purely
release-triggered deployments would stop working the next day without renewal.
Do not silently add a daily workflow to avoid that. Before activation choose a
reviewed bounded renewal mechanism or a different validity policy, within the
owner's standing automation scope and without weakening withdrawal defenses.

Shared fixtures must include major/minor/patch/prototype/stable/channel ordering,
conflicting duplicates, unknown schema, expiry/future time, withdrawal and
unstamped/mismatched identity. Python draft tests currently exercise the proposed
prototype-only contract; a production .NET implementation must pass the same
fixtures, not rely on the Python pass as evidence for different code.


## Reconciled website findings and concrete implementation phases

Moshe's read-only review of the live homepage/reader found the existing flat
schema1 prototype pointer incompatible with this envelope: it has one candidate
or none, nullable hash/size and no expiry. No older-pointer fallback was observed.
Do not mutate that reader to accept both shapes silently. A reviewed reader must
strictly select the new schema and fail closed on unsupported shape. Preserve
current design. Construct release download routes only within the approved
host/repository/asset contract, not arbitrary catalog URLs.

Click-time contract proposal: no-store fetch, validate freshness and full identity,
reselect against the same installed version; if identity changed, stop navigation,
show the new identity and require another explicit click. If unchanged and still
eligible, open only the fixed official website. Website repeats the same check
before its explicit download action. Any failure blocks the automatic offer and
gives honest manual recovery. This reduces risk but is NOT an atomic transaction
with a mutable GitHub asset. No asset404 guarantee or instant withdrawal claim.

Implementation sequence within existing feature scope:

A. Shared golden fixtures and pure contract/version/installed-identity parsers,
   with production composition unchanged. No host/TTL/clock policy hardcoded as
   an approved fact. These can proceed before transport/publisher decisions.
B. Offline publisher state-transition model: approved bytes -> published verified
   identity -> eligible revision; withdrawal invalidates eligibility; delayed
   generation cannot regress revision. Tests do not authenticate real decisions.
C. Malka pipeline/stamp adapter and Moshe reader changes through main's concrete
   coordination, exact review and normal branch workflow. Pure .NET source adapter
   uses injected policy/clock/transport until trusted host/control is verified.
D. Integration/hostile-transport tests and exact source review, DEV then TEST QA.
E. Owner-facing activation proposal names exact build, approved eligibility store,
   check behavior and residual freshness/revocation bounds. No approval to E is
   inferred from product agreement to individual release approval.

Only genuine product/authority tradeoffs to main: startup discovery behavior;
whether a bounded withdrawal delay on static Pages/Releases is acceptable versus
stronger controlled distribution (which could add hosting/operational changes);
actual MAIN/release/feed activation. TTL, clock skew and storage mechanics are
engineering recommendations to justify, not a menu of unexplained owner chores.
No stronger distribution service/account is selected or provisioned here.

### Workable recommended freshness lifecycle (proposal, not scheduled)

Keep existing free static hosting, propose release/withdrawal-triggered reconcile
PLUS a narrowly bounded catalog renewal job every six hours, document lifetime
at most24hours, no new desktop polling timer. Each renewal reads current protected
approval/withdrawal events and CURRENT release/asset identity, verifies the current
approved bytes or matching immutable identity proof, and generates a new monotonic
revision only for the same still-approved set. Renewal must never infer approval
for a new release. Atomic whole-site deployment and CAS prevent racing publishers.
This is a proposed workflow change requiring review and cadence authorization,
not an action performed by the draft or a standing role created here.

Costs: no new paid hosting/account; four small runner jobs/day plus event-driven
runs and API/asset transfer. Rehashing65MB per candidate each time incurs bandwidth
and runner/storage usage; it must be measured against actual free allowances and
zero-spend controls, not called costless by assumption. Do not use cached metadata
as if it proved unchanged bytes; if immutable asset guarantees cannot be verified,
hash the published bytes or fail renewal. A small catalog limits client bandwidth.

Failure: alert main on failed renewal/deploy; last complete artifact may remain
only until its expiry, then desktop/site disable the automatic offer and explain
that updates cannot be confirmed. The current installed application still works.
Do not silently extend expiry while upstream checks fail. After repair, a reviewed
reconcile restores availability; no extra owner approval for unchanged bytes is
inferred to mean approval for substituted bytes.

Withdrawal: process known events immediately through the same serialized path,
revalidate on click; six-hour renewal catches asset-only changes not represented
by a release event. Worst-case propagation/offline/TTL windows still exist and
must be disclosed/accepted before activation. A signed revocation service or
controlled download gateway would be a materially different design and is not
introduced to promise instant withdrawal. Stronger guarantees need separate
infrastructure/cost/security review, not a hidden dependency of this free route.


### Free allowance and reliability checks (official docs read 2026-11-17)

Standard public-repository hosted runner compute is free; larger runners are not.
Artifact/cache storage has separate allowances, so audit actual account usage,
zero-spend controls and short deployment-artifact retention before activation.
No new machine/account/subscription is needed for the recommended static route.
Pages documents a100GB/month soft bandwidth limit,1GB site limit; its10build/hour
soft limit does not apply to custom Actions deployments. GITHUB_TOKEN permits
1000requests/hour/repository; do not embed it in a client (unauthenticated public
API reads are60/hour/IP). Four bounded renewals/day should fit compute/API scale,
not proof that all current account storage usage fits its free allowance.

Reliability: scheduled jobs can be delayed or dropped at high load and public
repository schedules auto-disable after60days without repository activity. A
24hour expiry tolerates some missed6hour cycles but ultimately fails closed.
Operational failure alerts and a deliberate authorized re-enable/reconcile path
are required; do not create artificial activity to bypass inactivity policy. No
always-on SLA is claimed. Existing schedules/roles are not changed by this design.

Official sources:
- https://docs.github.com/en/actions/how-tos/write-workflows/choose-where-workflows-run/choose-the-runner-for-a-job
- https://docs.github.com/en/billing/concepts/product-billing/github-actions
- https://docs.github.com/en/pages/getting-started-with-github-pages/github-pages-limits
- https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api
- https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows
