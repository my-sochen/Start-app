"""Prototype history projection. The private integration proposal opts in explicitly;
that local proposal is not merged or deployed.
Selection reuses the existing selector; this adds complete inventory/identity checks,
not stable/beta enrollment or independent ZIP verification.
"""
import json
import re
import pick_release_pointer as sel

REPO = "my-sochen/Start-app"
MAX_PAGES = 10
MAX_RELEASES = 1000
MAX_JSON_BYTES = 1024 * 1024
MAX_CATALOG_BYTES = 4 * 1024 * 1024
SAFE_INT = 9007199254740991
IDENTITY = ("schema", "channel", "label", "version", "tag", "commit", "asset", "url", "size", "sha256", "layout")
LEGACY = ("v0.1.0-proto.1", "db94adc19056b6dd68d338c030c6ce4983a3057c",
          "7e8ee4d53bcba3626543aebae97c08c5bfc41057582af1e03f3762f68ec6c702", 65356245)


def bounded_json(value):
    if len(json.dumps(value, ensure_ascii=False).encode("utf-8")) > MAX_JSON_BYTES:
        raise ValueError("JSON response exceeds projection bound")
    return value


def layout_for(candidate, manifest):
    declared = manifest.get("layout")
    if declared == "portable-v1":
        return "portable-v1"
    identity = tuple(candidate[k] for k in ("tag", "commit", "sha256", "size"))
    if "layout" not in manifest and identity == LEGACY:
        return "legacy-flat-v1"
    return "unknown"


def entry_for(candidate, manifest):
    doc = sel.pointer_document(candidate)
    doc.pop("generated_utc", None)
    doc.pop("scope", None)
    expected = "https://github.com/" + REPO + "/releases/download/" + doc["tag"] + "/" + sel.ASSET
    if doc["url"] != expected:
        raise ValueError("eligible entry has unexpected release asset URL")
    if doc["sha256"] is not None and (not isinstance(doc["sha256"], str) or not re.fullmatch("[0-9a-f]{64}", doc["sha256"])):
        raise ValueError("eligible entry has invalid digest")
    if doc["size"] is not None and (type(doc["size"]) is not int or not 0 < doc["size"] <= SAFE_INT):
        raise ValueError("eligible entry has invalid browser-safe size")
    doc["layout"] = layout_for(candidate, manifest)
    return doc


def project(releases, manifests):
    """Inputs must be a complete collected snapshot, not a truncated API page.
    Offline fixtures must make the same assertion; no live writes are done here.
    """
    if not isinstance(releases, list) or len(releases) > MAX_RELEASES:
        raise ValueError("invalid or oversized release inventory")
    if not isinstance(manifests, dict):
        raise ValueError("manifests must be an object")
    tags = set()
    for release in releases:
        if not isinstance(release, dict):
            raise ValueError("release is not an object")
        tag = release.get("tag_name")
        if not isinstance(tag, str) or tag in tags:
            raise ValueError("missing or duplicate release tag")
        tags.add(tag)
        assets = release.get("assets") or []
        if not isinstance(assets, list) or any(not isinstance(a, dict) for a in assets):
            raise ValueError("invalid assets")
        names = [a.get("name") for a in assets]
        if len(names) != len(set(names)):
            raise ValueError("duplicate asset names")
        if sel.TAG_RE.fullmatch(tag) and not release.get("draft") and release.get("prerelease") and "manifest.json" in names and tag not in manifests:
            raise ValueError("manifest collection incomplete for eligible channel")
    # Retain exact existing selected winner and highest-build tie semantics.
    best, _ = sel.select_pointer(releases, manifests)
    candidates = []
    builds = set()
    for release in releases:
        try:
            candidate = sel.evaluate(release, manifests.get(release["tag_name"]))
        except sel.Reject:
            continue
        if candidate["build"] in builds:
            raise ValueError("ambiguous historical prototype build")
        builds.add(candidate["build"])
        candidates.append(candidate)
    candidates.sort(key=lambda c: c["build"], reverse=True)
    entries = [entry_for(c, manifests[c["tag"]]) for c in candidates]
    catalog = {"schema": 1, "channel": "prototype", "complete": True,
               "selected_tag": best["tag"] if best else None, "entries": entries}
    if len(json.dumps(catalog, ensure_ascii=False, indent=2).encode("utf-8")) > MAX_CATALOG_BYTES:
        raise ValueError("catalog exceeds projection bound")
    if best:
        pointer = sel.pointer_document(best)
        pointer["layout"] = entries[0]["layout"]
        assert all(pointer[k] == entries[0][k] for k in IDENTITY)
    else:
        pointer = {"schema": 1, "channel": "prototype", "candidate": None,
                   "reason": "no published prerelease with ui_gate=passed exists"}
    return pointer, catalog


def collect_complete(repo):
    """Bounded sequential read-only API collection, no retries or polling.
    A full tenth page aborts rather than claim completeness without a next check.
    """
    if repo != REPO:
        raise ValueError("projection is restricted to fixed repository")
    releases = []
    for page in range(1, MAX_PAGES + 1):
        batch = bounded_json(sel.gh_json("api", f"repos/{repo}/releases?per_page=100&page={page}"))
        if not isinstance(batch, list) or len(batch) > 100:
            raise ValueError("invalid release page")
        releases.extend(batch)
        if len(releases) > MAX_RELEASES:
            raise ValueError("release count bound reached")
        if len(batch) < 100:
            break
    else:
        raise ValueError("pagination bound reached; completeness unknown")
    manifests = {}
    for release in releases:
        if not isinstance(release, dict):
            raise ValueError("invalid release")
        tag = release.get("tag_name")
        # Ineligible channels/drafts do not require a manifest request.
        if not isinstance(tag, str) or not sel.TAG_RE.fullmatch(tag) or release.get("draft") or not release.get("prerelease"):
            continue
        assets = release.get("assets") or []
        matches = [a for a in assets if isinstance(a, dict) and a.get("name") == "manifest.json"]
        if len(matches) > 1:
            raise ValueError("duplicate manifest assets")
        if not matches:
            manifests[tag] = None
            continue
        asset_id = matches[0].get("id")
        if type(asset_id) is not int or asset_id <= 0:
            raise ValueError("invalid manifest asset ID")
        # SystemExit/API/JSON failure propagates. Never convert unknown to empty.
        manifests[tag] = bounded_json(sel.gh_json("api", f"repos/{repo}/releases/assets/{asset_id}", "-H", "Accept: application/octet-stream"))
    return releases, manifests
