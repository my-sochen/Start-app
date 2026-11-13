#!/usr/bin/env python3
"""UI hard-gate evaluator. Mirrors the final aggregator step of windows-open-flow-feat.yml (live, feat/per-instance-items @ 9e681f1b),
as a trusted, offline-testable script. Exit 0 only when BOTH assertion files exist, are non-empty, contain no ok=false entry,
end with an ok FLOW-COMPLETE marker whose detail equals the expected sha, and diag/errors.txt does not exist.
Scope: proves the candidate's own UI scripts ran to their end marker in this job for this sha. Not a coverage or determinism claim."""
import argparse, json, sys
from pathlib import Path

def load(p):
    t = p.read_text(encoding="utf-8-sig").strip()
    if not t: return []
    d = json.loads(t)
    return [d] if isinstance(d, dict) else d          # PowerShell ConvertTo-Json turns a 1-item array into an object

def check(label, p, sha, errs):
    if not p.is_file(): errs.append(f"{label}: {p.name} missing (step failed before completing)"); return
    try: a = load(p)
    except Exception as e: errs.append(f"{label}: {p.name} unreadable: {e}"); return
    if not a or not all(isinstance(x, dict) for x in a): errs.append(f"{label}: {p.name} empty or malformed (no checks ran)"); return
    if any(x.get("ok") is False for x in a): errs.append(f"{label}: FAIL asserts present")
    last = a[-1]
    if last.get("name") != "FLOW-COMPLETE" or last.get("ok") is not True: errs.append(f"{label}: no ok FLOW-COMPLETE marker (partial run)")
    elif last.get("detail") != sha: errs.append(f"{label}: completion marker commit {last.get('detail')!r} != expected {sha}")

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--diag", required=True); ap.add_argument("--sha", required=True)
    a = ap.parse_args(); d = Path(a.diag); errs = []
    check("cycle1-flow", d / "assertions.json", a.sha, errs)
    check("capture-flow", d / "capture-assertions.json", a.sha, errs)
    if (d / "errors.txt").exists(): errs.append("diag/errors.txt exists (a step recorded an error)")
    for e in errs: print("UI GATE FAIL:", e)
    if not errs: print("UI GATE PASSED for", a.sha)
    return 1 if errs else 0
if __name__ == "__main__": sys.exit(main())
