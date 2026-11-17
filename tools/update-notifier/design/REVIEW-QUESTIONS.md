# Scoped design review questions

The adjacent catalog example is wholly synthetic, not a real approval or published artifact.
Its example expiry is 2026-11-18T00:00:00Z, so it is not expired at the initial
2026-11-17 handoff; tests use an explicit synthetic clock. Its one-day TTL is illustrative, not chosen.
No catalog is uploaded or served. Owner accepted the exact-release approval rule,
not a specific publication/activation. No scripts, source or binary execution is
needed to review these documents.

## Malka: publisher/build identity

1. Can an auditable owner decision bind the FINAL version/tag, tagged commit,
   release/asset IDs, hash and size without treating MAIN merge, manifest pass
   flags or the draft-release job as publication approval? Recommend the concrete
   storage/control step, including revocation and who can alter it. Actual account
   and environment protection checks belong before activation, not assumptions.
2. Proposed publisher flow: build/test final candidate -> verify final bytes ->
   collect exact owner approval -> publish approved artifact -> verify published
   bytes/identity -> generate shared eligible catalog -> deploy full website unit.
   Evaluate ordering/races and fail-closed paths. Do not implement or publish yet.
3. Version/commit stamping must reach the executing WPF assembly and every package
   metadata surface. Identify the minimum release/validation wiring changes.
4. Reuse one version-order fixture suite with website and desktop. Current global
   prototype-counter selector cannot remain the approval source unchanged.
5. Propose freshness bounds, asset deletion/withdrawal handling and publishing
   controls. No instant revocation guarantee or automatic downgrade.

## Moshe: website reader contract, not redesign

1. Keep current website design. Can the current reader accept the shared catalog,
   select exactly the same highest eligible prototype version as desktop and show
   honest expired/no-eligible/error states without a fallback to an older pointer?
2. Validate displayed version/hash/size and download artifact identity from the
   approved record, not untrusted URLs from another source. Resolve actual release
   assets only through the reviewed distribution contract.
3. Describe the click-time race when eligibility changes after an app notice.
   The website may offer a newer eligible build, but never an ineligible build or
   imply download/install of the app's displayed version already happened.
4. Return reader/schema compatibility findings only. No website edits, activation,
   deploy, new account, PR, CI or asset change is requested by this design review.

## Yehonatan: later review/test planning

Review exact implementation only when supplied under the normal review method.
Suggested fixtures: 0.2.0-proto.1 > 0.1.0-proto.99; stable rejects prototype;
conflicting duplicate versions; withdrawn/expired/unknown schema; redirect and
oversize with missing/misleading Content-Length; cancellation at close; forged
approval flag; unstamped/mismatched installed identity; offline retains app use;
website and desktop select same eligible set. TEST62 is complete, do not repeat it.

## Deferred decisions that must not become silent implementation defaults

- Exact trusted endpoint plus protected record storage/writers and verification.
- TTL/clock-skew/cache/revision policy and catalog renewal without a new hidden
  cadence. A 24-hour example is not a commitment to daily deployment.
- Whether stable records join the first production catalog or the first adapter
  explicitly remains prototype-only; no beta/preview fallback.
- Startup-once discovery is not yet confirmed. Existing manual browser/install
  scope and exact-release approval product rule are confirmed.
