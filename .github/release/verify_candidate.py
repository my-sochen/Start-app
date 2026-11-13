#!/usr/bin/env python3
"""Gate for a release candidate directory. Run from the TRUSTED workflow revision, never from the candidate tree.
Any failure -> exit 1. Usage: verify_candidate.py --dir DIR --sha FULL_SHA --version V
"""
import argparse, hashlib, json, re, sys, zipfile
from pathlib import Path

ASSET = "StartApp-win-x64.zip"
VERSION_RE = re.compile(r"[0-9]+\.[0-9]+\.[0-9]+-proto\.[0-9]+")
SHA_RE = re.compile(r"[0-9a-f]{40}")

def fields(text, key):
    return re.findall(rf"^{re.escape(key)}:[ \t]*(.*?)[ \t]*$", text, re.M)

def single(text, key, errs):
    v = fields(text, key)
    if len(v) != 1:
        errs.append(f"BUILDINFO must have exactly one '{key}:' line (found {len(v)})"); return None
    if v[0] == "":
        errs.append(f"BUILDINFO '{key}:' is empty"); return None
    return v[0]

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", required=True); ap.add_argument("--sha", required=True); ap.add_argument("--version", required=True)
    a = ap.parse_args()
    d = Path(a.dir); errs = []
    def need(c, m):
        if not c: errs.append(m)
    need(SHA_RE.fullmatch(a.sha) is not None, "--sha must be a full 40-char lowercase sha")
    need(VERSION_RE.fullmatch(a.version) is not None, "--version must look like 0.1.0-proto.1")
    for n in (ASSET, ASSET + ".sha256", "BUILDINFO.txt", "manifest.json"):
        need((d / n).is_file(), f"missing {n}")
    if errs: return fail(errs)

    real = hashlib.sha256((d / ASSET).read_bytes()).hexdigest()
    parts = (d / (ASSET + ".sha256")).read_text(encoding="ascii", errors="replace").split()
    need(len(parts) == 2, "sha256 file must be '<hash> <filename>'")
    if len(parts) == 2:
        need(parts[0].lower() == real, "sha256 file hash does not match ZIP bytes")
        need(parts[1].lstrip("*") == ASSET, "sha256 file names a different file")

    bi = (d / "BUILDINFO.txt").read_text(encoding="utf-8-sig")
    need(single(bi, "commit", errs) == a.sha, "BUILDINFO commit != requested sha")
    need(single(bi, "version", errs) == a.version, "BUILDINFO version != requested version")
    need(single(bi, "sha256", errs) == real, "BUILDINFO sha256 != real ZIP hash")

    try: mf = json.loads((d / "manifest.json").read_text(encoding="utf-8"))
    except Exception as e: return fail(errs + [f"manifest.json unreadable: {e}"])
    need(isinstance(mf, dict), "manifest must be an object")
    if not isinstance(mf, dict): return fail(errs)
    exp = {"schema": 1, "commit": a.sha, "sha256": real, "asset": ASSET, "version": a.version,
           "channel": "prototype", "prerelease": True, "size": (d / ASSET).stat().st_size, "unit_tests": "passed"}
    for k, v in exp.items():
        need(mf.get(k) == v and type(mf.get(k)) is type(v), f"manifest {k} wrong or missing")
    need(mf.get("ui_gate") in ("not-gated", "failed", "passed"), "manifest ui_gate must be 'not-gated', 'failed' or 'passed'")

    with zipfile.ZipFile(d / ASSET) as z:
        names = z.namelist()
        need(len(names) == len(set(names)), "ZIP has duplicate entries")
        for n in ("PROTOTYPE-NOTICE.txt", "KNOWN-LIMITS.txt"):
            need(n in names, f"ZIP lacks {n}")
        if "PROTOTYPE-NOTICE.txt" in names:
            t = z.read("PROTOTYPE-NOTICE.txt").decode("utf-8-sig")
            need(a.sha in t and a.version in t, "notice must name both sha and version")
        need(sum(n == "StartApp.App.exe" for n in names) == 1, "ZIP must contain StartApp.App.exe at its root")
    return fail(errs) if errs else ok(real)

def fail(errs):
    for e in errs: print("FAIL:", e)
    return 1
def ok(h):
    print("OK: candidate verified, sha256", h); return 0
if __name__ == "__main__": sys.exit(main())
