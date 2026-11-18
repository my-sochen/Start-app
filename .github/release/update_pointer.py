#!/usr/bin/env python3
"""Assemble the Pages deploy unit: the FULL website plus the selector-emitted
pointer document, atomically. Activated code, owner-gated: it runs only from
the merged companion workflow once the owner enables Pages, creates the
github-pages environment protection, and sets PROTOTYPE_RELEASE_ENABLE=true.

Semantics (see POINTER-WORKFLOW-DESIGN.md):
- Selector finds a qualifying published prerelease -> artifact carries the
  candidate pointer document ("deploy-candidate").
- Selector finds NO qualifying prerelease (exit-3 case) -> artifact carries the
  selector's own safe "none" document ("deploy-none"). A withdrawn/deleted or
  unpublished release therefore reconciles to the honest disabled-download
  state on the next approved run. The previous live pointer is NEVER preserved
  as a stale "last good" by choice; it is only preserved when the true state
  is UNKNOWN (infrastructure failure), which aborts with no deploy.
- The artifact is the entire website/ tree plus pointer.json. A pointer-only
  deploy is impossible by construction: a missing/incomplete website dir is a
  hard error and nothing is written.

Offline-testable: classify() is pure over fixtures; build_artifact() touches
only the given directories. Live mode uses pick_release_pointer.gh_json
(read-only gh api calls only).

Usage:
  update_pointer.py --repo OWNER/REPO --website DIR --out DIR
  update_pointer.py --releases releases.json --manifests manifests.json \
      --website DIR --out DIR
Exit codes: 0 = artifact assembled (see "action" in stdout JSON);
            1 = aborted, no deploy performed (previous live state untouched).
"""
import argparse, json, os, shutil, sys, tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pick_release_pointer as sel

REQUIRED_SITE_ENTRIES = ("index.html",)  # minimal completeness check; the whole tree is copied
EXCLUDE_DIRS = ("tests",)  # offline test code (e.g. website/tests/) is never published


def classify(releases, manifests):
    """Return ("candidate"|"none", pointer_document). Raises on unknown state."""
    best, rejects = sel.select_pointer(releases, manifests)
    if best is None:
        doc = {"schema": 1, "channel": "prototype", "candidate": None,
               "reason": "no published prerelease with ui_gate=passed exists"}
        return "none", doc
    return "candidate", sel.pointer_document(best)


def build_artifact(website_dir, pointer_doc, out_dir):
    """Copy the full website tree plus pointer.json into out_dir, atomically.
    Any prior content of out_dir is replaced wholesale (no stale leftovers).
    Refuses to produce a pointer-only artifact."""
    if not os.path.isdir(website_dir):
        raise SystemExit(f"abort: website dir {website_dir!r} missing - refusing pointer-only deploy")
    missing = [e for e in REQUIRED_SITE_ENTRIES if not os.path.isfile(os.path.join(website_dir, e))]
    if missing:
        raise SystemExit(f"abort: website dir {website_dir!r} incomplete (missing {missing}) - refusing partial deploy")
    out_dir = os.path.abspath(out_dir)
    parent = os.path.dirname(out_dir) or "."
    tmp = tempfile.mkdtemp(prefix=".artifact-", dir=parent)
    try:
        shutil.copytree(website_dir, tmp, dirs_exist_ok=True,
                        ignore=shutil.ignore_patterns(*EXCLUDE_DIRS))
        with open(os.path.join(tmp, "pointer.json"), "w", encoding="utf-8") as f:
            json.dump(pointer_doc, f, ensure_ascii=False, indent=2)
        if os.path.isdir(out_dir):
            shutil.rmtree(out_dir)
        os.replace(tmp, out_dir)
    except BaseException:
        shutil.rmtree(tmp, ignore_errors=True)
        raise
    return out_dir


def collect_live(repo):
    """Live read-only collection via gh. Distinguishes two cases that must
    never be conflated:
    - manifest ASSET absent on a release -> manifests[tag] = None: an ordinary
      candidate rejection ("no candidate" remains DATA, deploys safe none).
    - manifest FETCH failure (api error) -> SystemExit abort BEFORE any
      artifact is built: an error must never degrade into a deploy-none that
      silently disables or a stale kept pointer.
    """
    releases = sel.gh_json("api", f"repos/{repo}/releases?per_page=100")
    manifests = {}
    for rel in releases:
        tag = rel.get("tag_name")
        if not tag:
            continue
        asset = next((x for x in rel.get("assets") or [] if x.get("name") == "manifest.json"), None)
        if asset is None:
            manifests[tag] = None
            continue
        manifests[tag] = sel.gh_json("api", f"repos/{repo}/releases/assets/{asset['id']}",
                                     "-H", "Accept: application/octet-stream")
    return releases, manifests


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", help="OWNER/REPO for live read-only selection via gh")
    ap.add_argument("--releases", help="fixture: JSON array of releases (gh api shape)")
    ap.add_argument("--manifests", help="fixture: JSON object {tag_name: manifest or null}")
    ap.add_argument("--website", required=True, help="path to the full website tree")
    ap.add_argument("--out", required=True, help="artifact output dir (uploaded to Pages)")
    a = ap.parse_args()
    if a.repo:
        releases, manifests = collect_live(a.repo)
    elif a.releases and a.manifests:
        releases = json.loads(open(a.releases, encoding="utf-8").read())
        manifests = json.loads(open(a.manifests, encoding="utf-8").read())
    else:
        raise SystemExit("abort: need --repo or both --releases and --manifests")
    if not isinstance(releases, list):
        raise SystemExit("abort: releases must be a JSON array")
    action, doc = classify(releases, manifests)
    build_artifact(a.website, doc, a.out)
    summary = {"action": f"deploy-{action}", "tag": doc.get("tag"), "reason": doc.get("reason")}
    print(json.dumps(summary, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
