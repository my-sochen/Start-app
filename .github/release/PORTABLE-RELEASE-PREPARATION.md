# Portable release wiring: local preparation only

Base DEV: 9b3b56ee8ce90066fc4fd2026847524d266deedd.
The change uses the validated portable packaging chain in release packaging.
The DEV draft job remains hard-off, byte-for-byte. No trigger, permissions,
environment, on-main condition, tag collision rule or owner gate is changed.
The package checks the archive before extraction and smoke-launches its root
entry. It records candidate SHA in smoke evidence even when the workflow SHA
is different. BUILDINFO retains both revisions. Candidate code and workflow
helpers have separate revision checks; neither is a certification boundary.

## Later MAIN reconciliation

Current MAIN at 38262167d390650d16e3cb003243769ec542e454 has an activated,
owner-gated draft job. DEV has the older dormant version. Do not copy the DEV
workflow wholesale to MAIN. Apply the packaging/dependency-list hunks only to
the then-current MAIN workflow after review. Preserve the entire MAIN draft
job, including --repo, --target, Environment, variable and success/on-main
conditions. Re-read policy at that time, rather than assuming this snapshot
remains current. The companion main-relative patch is an inspection aid only;
it requires the portable helpers/verifier already present in the target tree.

TEST stays held until DEV is satisfactory. MAIN merge, release run, protected
Environment approval, draft publication and site/pointer changes are separate
owner decisions, not granted by this preparation. Do not reuse the occupied
v0.1.0-proto.1 tag or replace its existing assets. A future run must use a free
prototype version and its resulting artifact must be checked again.

## Scope of evidence

Static YAML checks and local Python tests do not execute this release workflow.
Previous feature/DEV Windows runs proved the packaging chain, not the distinct
release input/provenance path. A future permitted run must check both source
SHAs, full gates, archive digest/layout, extracted-launch smoke and actual pixels.
The comparison remains report-only: one native build is shared by two payloads,
so matching hashes do not prove independent native or cross-run reproducibility.
No signing, Store submission, account setup, identity collection or license
change is part of this patch. The existing download remains unchanged.

## Normal feature validation route

The validation workflow names feat/portable-release-wiring-local and runs the
four static policy checks in its full-history resolve checkout. It still has
read-only permissions and no release jobs. Its ordinary Windows tests, UI gate,
portable package and extracted-launch smoke validate this feature revision.
They do not execute the separate manual release workflow or prove its distinct
workflow-input/candidate-input path at runtime. No release dispatch is included.
