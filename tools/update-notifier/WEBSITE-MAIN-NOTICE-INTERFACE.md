# Website interface: MAIN-backed approved beta notice, no rolling catalog

Private reusable interface for Moshe via Main. Website authoring remains Moshe's; no request to deploy/change source is embedded here. Source candidate82ef and narrow working integration are requester-owned. No new website work is authorized by this file alone.

## Shared source of selected approved publication

Fixed repository my-sochen/Start-app, protected MAIN. Proposed actual tracked source path `.github/release/approved-beta-notice.json`.

Exact envelope fields: schema positive raw integer1, channel exact `beta-notice`, candidate either JSONnull or one exact record. No revision/generated/expires/feed-renewal fields. Do not reuse rolling beta-catalog schema, infer missing=null, or discover a beta just because release metadata mentions approval.

Exact record fields: version/tag/commit/release_id/asset_id/asset/size/sha256/layout/approval_record_id. Same reviewed constraints as previous catalog record: numeric version X.Y.Z-beta.N with dot/no leadingzero and9digit components, tag=v+version, lowercase40hex commit/64hexSHA, positive rawint64IDs/size, asset StartApp-win-x64.zip, layout portable-v1, approvalref ASCIIletters/digits/dot/underscore/hyphen1-128. No arbitrary URL or HTML. Null is known no enrollment. Unknown/malformed source is not null.

Desktop actualsource files accompany packet. ParseDescriptor reuses strict reviewed record parser in memory; descriptor itself has no TTL. The selected candidate is explicitly owner-controlled, not a runtime proof it is globally newest. Publication process must select latest approved/current offer. Prototype only detects this candidate if newer than installed nonstable build, no installer.

## Live publication requirement

Enrollment alone is insufficient. Desktop validates actual current release exactID, nondraft/prerelease/strict nonfuturepublished_at/exacttag, dedicatedasset exactID/name/size/uploadedstate/fixedrepo browserURL and complete bounded release-membership pages, tag→approvedcommit, consistent MAIN ref. It never downloads/hashchecks fullZIP at startup and never navigates assetURL. Click rechecks then opens only officialsite. Metadata/cache/finalread races and shared publicIP60GETs/hour remain. Source uses11GETdirecttag/onepage positive,7null; no loops/polling/token.

## Website delivery proposal to evaluate, not selected implementation

Before first beta the existing prototype website can remain unchanged. Working prototype's candidate:null produces no beta popup. For first realbeta activation, the official root destination MUST actually offer matching approvedbeta. Otherwise notice has nowhere honest to send the user.

Prefer reuse existing owner-gated single fullsite writer on actual publication/withdrawal, consuming this reviewed descriptor and verifying current target release/asset/tag before assembling one site artifact. It can emit a small sameorigin selected-beta pointer derived from that exact descriptor identity, alongside existing prototype pointer, with no dailyrenewal/globalrevision service. Path/schema of this site projection must be agreed with Main before implementation. Do not serve the tracked metadata blindly as a claim of live bytes; writer validates publication. Do not insert machine revision state/App/newrepo/CSP relaxation as a hidden prerequisite.

Null approveddescriptor should retain existing honestprototype path. Nonnull eligible beta should render matchingbeta manualdownload with explicitnotstablecopy and fullidentity. Unknown publication/source should not silently retain stale beta. Event-only reconciliation cannot promise instantaneous withdrawal, and a projection without TTL can become stale if a release changes outside reconciliation. Source/owner must explicitly accept or mitigate that limitation before activation. A new continuous reader on the website would need CSP/APIquota/transport review and is NOT implicitly approved here.

Reusable prior BigInt renderer, exactrawJSON/version parser, sequenceguards/clearhandlers/clickidentity and router/defaultprototype evidence can be retained. Rollingcatalog load/TTL/sessionrevision-specific code is not automatically the new contract. No broad locale redesign needed; preserve known native-language limits. Propose minimal specific delta/guarantees first, no duplicate authoring.

## Approval boundaries

Workingnull descriptor merge and prototype factory wiring need bothsource reviews and owner normal integration approval. Firstrealbeta publication and nonnull descriptor enrollment remain separate exact approval; website matchingbeta delivery ready before activation. No newversion approves itself. No publication, credentials, settings, paid resources or external communication is requested by this packet.
