#!/usr/bin/env python3
"""Select the prototype release the website download link should point at, and emit a
pointer document on stdout. Writes NOTHING: no repository file, no release, no site
content. Where the pointer document is stored and how the site reads it is an
unresolved hosting decision (see .github/release/RELEASE-PIPELINE.md).

Selection rules (from the reviewed release plan):
  - published GitHub Releases only (drafts excluded), prerelease=true only
    (the channel is prototype; a stable release is never auto-selected);
  - tag must match vX.Y.Z-proto.N (prototype versioning);
  - the release must carry StartApp-win-x64.zip and manifest.json assets;
  - the manifest must declare channel=prototype, prerelease=true,
    unit_tests=passed and ui_gate=passed for the same commit;
  - among qualifying releases the highest proto build number wins; a tie is an
    error, never a guess. No release ever qualifies today, because no published
    prerelease with ui_gate=passed exists; "no candidate" is a normal result.

  Documented limits (not behavior decisions): the build-number ordering is
  GLOBAL across version lines: only the proto build number is compared, so
  v0.1.0-proto.9 outranks v0.2.0-proto.1. A version-line-aware ordering is a
  product decision for the owner if it ever matters. Live mode reads the
  first gh api page (up to 100 releases, newest first); a repository with
  more than 100 releases needs pagination added.

Scope limit (carried from the plan): the selector trusts DECLARED metadata
(release fields and the manifest file). It does not verify ZIP bytes, hashes or
independent run evidence. It never uses the GitHub 'latest' pointer, which
ignores prereleases in a way that would mis-select for this channel.

Offline-testable: `select_pointer(releases, manifests)` is a pure function.
Live mode (requires gh, read-only API calls only):
  pick_release_pointer.py --repo OWNER/REPO
Offline mode (fixtures):
  pick_release_pointer.py --releases releases.json --manifests manifests.json
"""
import argparse, json, re, subprocess, sys
from datetime import datetime, timezone

ASSET = "StartApp-win-x64.zip"
TAG_RE = re.compile(r"v([0-9]+\.[0-9]+\.[0-9]+-proto\.([0-9]+))$")
SHA_RE = re.compile(r"[0-9a-f]{40}")


class Reject(Exception):
    """One release failed one selection rule; carries the reason for reporting."""


def _assets(release):
    return {a.get("name"): a for a in release.get("assets") or [] if isinstance(a, dict)}


def evaluate(release, manifest):
    """Return the pointer fields for one release or raise Reject(reason)."""
    tag = release.get("tag_name") or ""
    m = TAG_RE.fullmatch(tag)
    if not m:
        raise Reject(f"tag {tag!r} is not a prototype tag vX.Y.Z-proto.N")
    if release.get("draft"):
        raise Reject("draft release")
    if not release.get("prerelease"):
        raise Reject("not a prerelease (stable releases are never auto-selected on the prototype channel)")
    assets = _assets(release)
    if ASSET not in assets:
        raise Reject(f"missing asset {ASSET}")
    if "manifest.json" not in assets:
        raise Reject("missing asset manifest.json")
    if manifest is None:
        raise Reject("manifest.json could not be read")
    if not isinstance(manifest, dict):
        raise Reject("manifest is not a JSON object")
    if manifest.get("channel") != "prototype" or manifest.get("prerelease") is not True:
        raise Reject("manifest is not a prototype prerelease manifest")
    if manifest.get("version") != m.group(1):
        raise Reject(f"manifest version {manifest.get('version')!r} != tag version {m.group(1)!r}")
    commit = manifest.get("commit")
    if not isinstance(commit, str) or SHA_RE.fullmatch(commit) is None:
        raise Reject("manifest commit is not a full 40-char sha")
    if manifest.get("unit_tests") != "passed":
        raise Reject("manifest unit_tests is not 'passed'")
    if manifest.get("ui_gate") != "passed":
        raise Reject("manifest ui_gate is not 'passed'")
    if manifest.get("asset") != ASSET:
        raise Reject(f"manifest asset {manifest.get('asset')!r} != {ASSET!r}")
    url = assets[ASSET].get("browser_download_url")
    if not isinstance(url, str) or not url.startswith("https://"):
        raise Reject("asset has no https browser_download_url")
    return {
        "version": m.group(1),
        "build": int(m.group(2)),
        "tag": tag,
        "commit": commit,
        "url": url,
        "sha256": manifest.get("sha256"),
        "size": manifest.get("size"),
    }


def select_pointer(releases, manifests):
    """Pick the pointer target. releases: list of release dicts (gh api shape).
    manifests: {tag_name: manifest dict or None}. Returns (pointer_fields, rejects).
    pointer_fields is None when nothing qualifies; rejects lists (tag, reason).
    Raises ValueError on an unresolvable tie."""
    best, rejects = None, []
    for rel in releases or []:
        tag = rel.get("tag_name") or "?"
        try:
            cand = evaluate(rel, manifests.get(tag))
        except Reject as e:
            rejects.append((tag, str(e)))
            continue
        if best is None or cand["build"] > best["build"]:
            best = cand
        elif cand["build"] == best["build"] and cand["tag"] != best["tag"]:
            raise ValueError(f"two qualifying releases share proto build {cand['build']}: {best['tag']} and {cand['tag']}")
    return best, rejects


def pointer_document(cand):
    """The pointer document a future host can serve. No URL is invented: the asset
    URL comes from the published release only."""
    return {
        "schema": 1,
        "channel": "prototype",
        "label": "אב-טיפוס להערכה - לא גרסה יציבה",
        "version": cand["version"],
        "tag": cand["tag"],
        "commit": cand["commit"],
        "asset": ASSET,
        "url": cand["url"],
        "sha256": cand["sha256"],
        "size": cand["size"],
        "generated_utc": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "scope": "Declared release metadata only; ZIP bytes and run evidence were not re-verified by this selector.",
    }


def gh_json(*args):
    out = subprocess.run(["gh", *args], capture_output=True, text=True)
    if out.returncode != 0:
        raise SystemExit(f"gh {' '.join(args)} failed: {out.stderr.strip()}")
    return json.loads(out.stdout)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", help="OWNER/REPO for live read-only selection via gh")
    ap.add_argument("--releases", help="fixture: JSON array of releases (gh api shape)")
    ap.add_argument("--manifests", help="fixture: JSON object {tag_name: manifest or null}")
    a = ap.parse_args()
    if a.repo and (a.releases or a.manifests):
        ap.error("--repo is live mode; do not mix with fixtures")
    if not a.repo and not (a.releases and a.manifests):
        ap.error("give either --repo or both --releases and --manifests")
    if a.repo:
        releases = gh_json("api", f"repos/{a.repo}/releases?per_page=100")
        manifests = {}
        for rel in releases:
            tag = rel.get("tag_name")
            asset = next((x for x in rel.get("assets") or [] if x.get("name") == "manifest.json"), None)
            if not tag or not asset:
                continue
            try:
                manifests[tag] = gh_json("api", f"repos/{a.repo}/releases/assets/{asset['id']}", "-H", "Accept: application/octet-stream")
            except SystemExit:
                manifests[tag] = None
    else:
        releases = json.loads(open(a.releases, encoding="utf-8").read())
        manifests = json.loads(open(a.manifests, encoding="utf-8").read())
    if not isinstance(releases, list):
        raise SystemExit("releases must be a JSON array")
    best, rejects = select_pointer(releases, manifests)
    for tag, why in rejects:
        print(f"rejected {tag}: {why}", file=sys.stderr)
    if best is None:
        print(json.dumps({"schema": 1, "channel": "prototype", "candidate": None,
                          "reason": "no published prerelease with ui_gate=passed exists"},
                         ensure_ascii=False, indent=2))
        return 3
    print(json.dumps(pointer_document(best), ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
