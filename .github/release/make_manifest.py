#!/usr/bin/env python3
"""Write manifest.json next to the ZIP. No URL inside: URLs come from the published release only."""
import argparse, hashlib, json
from pathlib import Path
ap = argparse.ArgumentParser()
ap.add_argument("--dir", required=True); ap.add_argument("--sha", required=True); ap.add_argument("--version", required=True)
ap.add_argument("--ui-gate", default="not-gated", choices=["passed", "failed", "not-gated"])
a = ap.parse_args()
z = Path(a.dir) / "StartApp-win-x64.zip"
m = {"schema": 1, "version": a.version, "channel": "prototype", "prerelease": True,
     "commit": a.sha, "asset": z.name, "size": z.stat().st_size,
     "sha256": hashlib.sha256(z.read_bytes()).hexdigest(),
     "unit_tests": "passed",      # package job runs only after the tests job succeeded on this sha
     "ui_gate": a.ui_gate}        # "passed" only when the ui-gate job of the SAME run succeeded for this sha; default fails safe
(Path(a.dir) / "manifest.json").write_text(json.dumps(m, indent=2) + "\n", encoding="utf-8")
print(json.dumps(m))
