# Private prototype catalog projection v1

Status: uncommitted local combined source proposal. Not activated, merged, deployed or a promise of live publication state. This candidate changes the existing workflow invocation to opt into `--historical-catalog`; no trigger, permissions, environment or concurrency change is proposed. Merge and publication approvals remain separate.

## Source and boundary

Combined candidate base: main b627547ea873e383b2f6194091651838b92e515d, tree 48946d507e6a7ee96d1b9723d93b6595637b0daf, verified 2026-11-18. The original producer preparation used 38262167d390650d16e3cb003243769ec542e454. This combined candidate includes the complete final website source and the workflow opt-in alongside this producer. Product source, release selector, beta descriptor, protection and live publication remain unchanged. No new downloadable release identity is selected by this source preparation.

## Interface

Opt-in assembly produces pointer.json plus downloads.json alongside the complete website in the same assembly operation. It does not deploy them. Default invocation follows the existing first-page selector path and produces its existing pointer shape without a catalog or layout addition.

Catalog envelope: schema integer 1, channel `prototype`, complete boolean true, selected_tag string or null, entries array. Each entry has schema/channel/label/version/tag/commit/asset/url/size/sha256 and layout. Entries omit the pointer's generated_utc and scope annotations; consumers must not require those annotations. All identity fields are shared with the selected pointer, including layout. Entry identity is not just a matching tag. selected_tag is null exactly when entries is empty. The optional primary pointer annotations are not identity fields.

The existing strict website pointer validation remains the baseline for each entry. Catalog consumers additionally enforce envelope, uniqueness, build ordering, layout enum and selected membership/agreement. Old schema1 pointers without layout remain valid downloads with layout normalized to `unknown`. They must not be reclassified as legacy merely because layout is absent. A new catalog with a layout-bearing selected entry cannot claim agreement with an old unknown-layout pointer. Catalog unavailable/disagreement clears historical handlers and rows; it never selects a new primary or disables a separately valid existing pointer.

Layouts: `portable-v1`, `legacy-flat-v1`, `unknown`. Portable follows an explicit eligible manifest layout `portable-v1`. Legacy is only the following exact tuple with no manifest layout field:

- tag v0.1.0-proto.1
- commit db94adc19056b6dd68d338c030c6ce4983a3057c
- ZIP sha256 7e8ee4d53bcba3626543aebae97c08c5bfc41057582af1e03f3762f68ec6c702
- size 65356245 bytes

All other missing or unrecognized layouts are unknown. Unknown does not remove a valid download, but supplies no installation instructions. This pin is based on the existing recorded package and the declared manifest read at https://github.com/my-sochen/Start-app/releases/download/v0.1.0-proto.1/manifest.json on 2026-11-18. No fresh ZIP-byte verification is claimed. The declared layout of a future eligible manifest also does not independently prove those bytes; package verification remains a separate release duty.

## Producer rules

- Fixed repository my-sochen/Start-app. No stable/beta discovery, enrollment, cross-channel ranking, browser GitHub API, credentials, background service, CSP change, rolling feed or TTL infrastructure.
- Eligibility and primary winner reuse the existing prototype selector. Global integer proto build ordering remains, not semantic version ordering. Historical build collisions abort the optional projection, including lower-build collisions, rather than present an ambiguous inventory. The old default writer is unaffected.
- Release listing: sequential read-only pages of up to 100. Short page establishes end of this scan. Maximum ten pages; a full tenth page aborts because completeness is unknown. At most 999 records can therefore complete with these exact limits. No partial catalog is emitted. Manifests are fetched only for nondraft prototype prereleases. API/manifest fetch errors propagate, not empty results. Missing manifest asset is known ineligible, as before. Duplicate tags or asset names, incomplete supplied manifest collection, invalid eligible fixed-repository URL/digest/browser-safe size and conflicting builds abort.
- Parsed JSON response bound 1 MiB, indented catalog bound 4 MiB, browser-safe positive integer size up to 9007199254740991 or null. These bounds apply after the existing gh JSON helper has received and parsed the response. They are not transport-stream memory bounds. Offline fixture input must represent a complete snapshot.
- Empty eligible inventory emits known-none pointer and empty complete catalog. Unknown collection aborts before artifact assembly, preserving the existing output directory in tested collection/input failures.

## Limits retained

This is declared metadata eligibility, not independent ZIP hashing, run evidence, release authorship or immutable API snapshot verification. The API may change while pages/manifests are read; complete means the bounded scan reached its end without a detected conflict, not a transactionally consistent live snapshot. No instantaneous withdrawal or perpetual availability is promised. Existing event/manual reconciliation and owner gates remain. Unchanged selector/asset-list limitations remain; no independent complete release-asset-membership audit is added.

Both JSON files are staged in the same temporary full-site directory before final replacement. The inherited updater removes an existing output directory before os.replace. It is not a rollback guarantee for a filesystem failure in that final replacement window, and no stronger atomicity claim is made here. Failed input/collection before assembly leaves the previous artifact unchanged in tests.

## Review and execution

Tests are synthetic offline preparation only. Run `python3 -m unittest discover -s .github/release/tests -v`. New tests cover paging beyond 100, bounds, incomplete reads, unknown/legacy/portable layouts, duplicates/ties, identity, inherited eligibility, empty state, selection parity, default-mode compatibility and paired JSON assembly. They do not run the app, download ZIPs, invoke the new live collector or publish a site.

The combined candidate includes Moshe's final website reader/guide. Its pointer/catalog modules are byte-identical to the previously checked corrected-v7 integration. Exact candidate review remains required. No current release version is chosen here. Upcoming guide instructions must match the eventual verified selected package, and unknown layout must remain honest.
