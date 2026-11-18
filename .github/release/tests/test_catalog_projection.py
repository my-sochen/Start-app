"""Synthetic offline tests only. No release, website, account or network writes."""
import copy
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import catalog_projection as c
import update_pointer as up


def fixture(build=1, line="0.1.0", layout="portable-v1"):
    version = f"{line}-proto.{build}"
    tag = "v" + version
    url = f"https://github.com/{c.REPO}/releases/download/{tag}/{c.sel.ASSET}"
    rel = {"tag_name": tag, "draft": False, "prerelease": True,
           "assets": [{"name": c.sel.ASSET, "browser_download_url": url}, {"name": "manifest.json", "id": build}]}
    manifest = {"version": version, "channel": "prototype", "prerelease": True,
                "commit": "a" * 40, "asset": c.sel.ASSET, "size": 123, "sha256": "b" * 64,
                "unit_tests": "passed", "ui_gate": "passed"}
    if layout is not None:
        manifest["layout"] = layout
    return rel, manifest


class ProjectionTests(unittest.TestCase):
    def project(self, pairs):
        return c.project([r for r, m in pairs], {r["tag_name"]: m for r, m in pairs})

    def test_selected_identity_order_and_pointer_compatibility(self):
        p, catalog = self.project([fixture(9), fixture(1, "0.2.0")])
        self.assertEqual(p["version"], "0.1.0-proto.9")
        self.assertEqual([e["version"] for e in catalog["entries"]], ["0.1.0-proto.9", "0.2.0-proto.1"])
        self.assertEqual(p["layout"], "portable-v1")
        self.assertTrue(all(p[k] == catalog["entries"][0][k] for k in c.IDENTITY))
        old = c.sel.pointer_document(c.sel.select_pointer([fixture(9)[0]], {fixture(9)[0]["tag_name"]: fixture(9)[1]})[0])
        self.assertTrue(all(p[k] == v for k, v in old.items() if k != "generated_utc"))

    def test_none(self):
        p, catalog = self.project([])
        self.assertIsNone(p["candidate"])
        self.assertEqual(catalog["entries"], [])
        self.assertIsNone(catalog["selected_tag"])
        self.assertIs(catalog["complete"], True)

    def test_rejections_and_withdrawal_match_existing_selection(self):
        for mutation in ("draft", "prerelease", "ui_gate", "unit_tests", "channel"):
            rel, man = fixture()
            if mutation == "draft": rel[mutation] = True
            elif mutation == "prerelease": rel[mutation] = False
            elif mutation == "channel": man[mutation] = "beta"
            else: man[mutation] = "failed"
            self.assertIsNone(self.project([(rel, man)])[0]["candidate"])

    def test_old_missing_and_unrecognized_layout_unknown(self):
        for layout in (None, "future-v2", 7):
            self.assertEqual(self.project([fixture(layout=layout)])[0]["layout"], "unknown")

    def test_only_full_legacy_pin_maps_legacy(self):
        rel, man = fixture(layout=None)
        man.update(commit=c.LEGACY[1], sha256=c.LEGACY[2], size=c.LEGACY[3])
        self.assertEqual(self.project([(rel, man)])[0]["layout"], "legacy-flat-v1")
        for field in ("commit", "sha256", "size"):
            changed = copy.deepcopy(man)
            changed[field] = 123 if field == "size" else "a" * len(changed[field])
            self.assertEqual(self.project([(rel, changed)])[0]["layout"], "unknown")

    def test_explicit_null_layout_on_legacy_pin_stays_unknown(self):
        rel, man = fixture(layout=None)
        man.update(commit=c.LEGACY[1], sha256=c.LEGACY[2], size=c.LEGACY[3])
        man["layout"] = None
        pointer, catalog = self.project([(rel, man)])
        self.assertEqual(pointer["layout"], "unknown")
        self.assertEqual(catalog["entries"][0]["layout"], "unknown")
        self.assertTrue(all(pointer[k] == catalog["entries"][0][k] for k in c.IDENTITY))

    def test_duplicates_and_conflicts_abort(self):
        for pairs in ([fixture(), fixture()], [fixture(), fixture(1, "0.2.0")], [fixture(9), fixture(1), fixture(1, "0.2.0")]):
            with self.assertRaises(ValueError): self.project(pairs)
        r, m = fixture(); r["assets"].append(copy.deepcopy(r["assets"][0]))
        with self.assertRaises(ValueError): self.project([(r, m)])

    def test_invalid_identity_cannot_enter_catalog(self):
        for field, value in (("size", True), ("size", 2**53), ("sha256", "BAD")):
            r, m = fixture(); m[field] = value
            with self.assertRaises(ValueError): self.project([(r, m)])
        r, m = fixture(); r["assets"][0]["browser_download_url"] = "https://example.invalid/file.zip"
        with self.assertRaises(ValueError): self.project([(r, m)])

    def test_missing_manifest_result_not_silently_complete(self):
        with self.assertRaises(ValueError): c.project([fixture()[0]], {})

    def test_bounds(self):
        with mock.patch.object(c, "MAX_RELEASES", 0):
            with self.assertRaises(ValueError): self.project([fixture()])
        with mock.patch.object(c, "MAX_CATALOG_BYTES", 1):
            with self.assertRaises(ValueError): self.project([])
        with mock.patch.object(c, "MAX_JSON_BYTES", 1):
            with self.assertRaises(ValueError): c.bounded_json({"x": 1})


class CollectionTests(unittest.TestCase):
    def test_complete_pagination_not_only_first_hundred(self):
        first = [fixture(i + 1)[0] for i in range(100)]
        last = [fixture(101)[0]]
        def api(*args):
            path = args[1]
            if path.endswith("&page=1"): return first
            if path.endswith("&page=2"): return last
            build = int(path.rsplit("/", 1)[-1]); return fixture(build)[1]
        with mock.patch.object(c.sel, "gh_json", side_effect=api) as mocked:
            releases, manifests = c.collect_complete(c.REPO)
        self.assertEqual(len(releases), 101)
        self.assertEqual(len(manifests), 101)
        self.assertEqual(mocked.call_count, 103)
        self.assertEqual(c.project(releases, manifests)[0]["version"], "0.1.0-proto.101")

    def test_page_limit_never_claims_complete(self):
        with mock.patch.object(c, "MAX_PAGES", 1), mock.patch.object(c.sel, "gh_json", return_value=[fixture()[0]] * 100):
            with self.assertRaises(ValueError): c.collect_complete(c.REPO)

    def test_api_failure_propagates(self):
        with mock.patch.object(c.sel, "gh_json", side_effect=SystemExit("synthetic error")):
            with self.assertRaises(SystemExit): c.collect_complete(c.REPO)
        def api(*args):
            if "releases?" in args[1]: return [fixture()[0]]
            raise SystemExit("manifest unavailable")
        with mock.patch.object(c.sel, "gh_json", side_effect=api):
            with self.assertRaises(SystemExit): c.collect_complete(c.REPO)

    def test_no_manifest_asset_known_ineligible_and_fixed_repo(self):
        r, _ = fixture(); r["assets"] = r["assets"][:1]
        with mock.patch.object(c.sel, "gh_json", return_value=[r]):
            releases, manifests = c.collect_complete(c.REPO)
        self.assertIsNone(manifests[r["tag_name"]])
        self.assertIsNone(c.project(releases, manifests)[0]["candidate"])
        with self.assertRaises(ValueError): c.collect_complete("other/repo")


class AssemblyTests(unittest.TestCase):
    def test_opt_in_only_and_whole_artifact(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td); site = root / "site"; site.mkdir(); (site / "index.html").write_text("site")
            rf = root / "releases.json"; mf = root / "manifests.json"; r, m = fixture()
            rf.write_text(json.dumps([r])); mf.write_text(json.dumps({r["tag_name"]: m}))
            command = [sys.executable, str(Path(up.__file__)), "--releases", str(rf), "--manifests", str(mf), "--website", str(site), "--out", str(root / "out")]
            subprocess.run(command, check=True, capture_output=True)
            self.assertFalse((root / "out/downloads.json").exists())
            self.assertNotIn("layout", json.loads((root / "out/pointer.json").read_text()))
            subprocess.run(command + ["--historical-catalog"], check=True, capture_output=True)
            catalog = json.loads((root / "out/downloads.json").read_text())
            pointer = json.loads((root / "out/pointer.json").read_text())
            self.assertTrue(all(pointer[k] == catalog["entries"][0][k] for k in c.IDENTITY))
            self.assertEqual((root / "out/index.html").read_text(), "site")
            before = (root / "out/pointer.json").read_bytes()
            mf.write_text("{invalid")
            failed = subprocess.run(command + ["--historical-catalog"], capture_output=True)
            self.assertNotEqual(failed.returncode, 0)
            self.assertEqual((root / "out/pointer.json").read_bytes(), before)


if __name__ == "__main__": unittest.main()
