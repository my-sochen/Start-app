#!/usr/bin/env python3
"""Offline tests for pick_release_pointer.py. No network, no gh, no GitHub state.
Run: python3 -m unittest discover -s .github/release/tests -v
These tests exercise SELECTION LOGIC ONLY on synthetic fixtures; they are not
release runs, not evidence that any release exists, and not tests of the product."""
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import pick_release_pointer as p  # noqa: E402

SHA = "a" * 40
SHA2 = "b" * 40


def rel(tag, *, draft=False, prerelease=True, assets=("StartApp-win-x64.zip", "manifest.json")):
    return {
        "tag_name": tag,
        "draft": draft,
        "prerelease": prerelease,
        "assets": [{"name": n, "browser_download_url": f"https://example.invalid/{tag}/{n}", "id": 1}
                   for n in assets],
    }


def man(version="0.1.0-proto.1", commit=SHA, ui_gate="passed", unit_tests="passed",
        channel="prototype", prerelease=True, asset="StartApp-win-x64.zip"):
    return {"schema": 1, "version": version, "channel": channel, "prerelease": prerelease,
            "commit": commit, "asset": asset, "size": 123,
            "sha256": "0" * 64, "unit_tests": unit_tests, "ui_gate": ui_gate}


class EvaluateTests(unittest.TestCase):
    def test_accepts_fully_qualified_prerelease(self):
        c = p.evaluate(rel("v0.1.0-proto.1"), man())
        self.assertEqual(c["build"], 1)
        self.assertEqual(c["version"], "0.1.0-proto.1")
        self.assertEqual(c["commit"], SHA)

    def test_rejects_draft(self):
        with self.assertRaises(p.Reject):
            p.evaluate(rel("v0.1.0-proto.1", draft=True), man())

    def test_rejects_stable_release(self):
        with self.assertRaises(p.Reject):
            p.evaluate(rel("v0.1.0-proto.1", prerelease=False), man())

    def test_rejects_non_prototype_tag(self):
        for tag in ("v1.0.0", "0.1.0-proto.1", "v0.1.0", "release-1", "v0.1.0-proto.1-extra"):
            with self.assertRaises(p.Reject, msg=tag):
                p.evaluate(rel(tag), man())

    def test_rejects_missing_zip_asset(self):
        with self.assertRaises(p.Reject):
            p.evaluate(rel("v0.1.0-proto.1", assets=("manifest.json",)), man())

    def test_rejects_missing_manifest_asset(self):
        with self.assertRaises(p.Reject):
            p.evaluate(rel("v0.1.0-proto.1", assets=("StartApp-win-x64.zip",)), None)

    def test_rejects_unreadable_manifest(self):
        with self.assertRaises(p.Reject):
            p.evaluate(rel("v0.1.0-proto.1"), None)

    def test_rejects_ui_gate_not_passed(self):
        for gate in ("failed", "not-gated"):
            with self.assertRaises(p.Reject, msg=gate):
                p.evaluate(rel("v0.1.0-proto.1"), man(ui_gate=gate))

    def test_rejects_unit_tests_not_passed(self):
        with self.assertRaises(p.Reject):
            p.evaluate(rel("v0.1.0-proto.1"), man(unit_tests="failed"))

    def test_rejects_manifest_version_mismatch(self):
        with self.assertRaises(p.Reject):
            p.evaluate(rel("v0.1.0-proto.2"), man(version="0.1.0-proto.1"))

    def test_rejects_bad_commit_shape(self):
        for bad in ("abc", "A" * 40, 123, None):
            with self.assertRaises(p.Reject, msg=repr(bad)):
                p.evaluate(rel("v0.1.0-proto.1"), man(commit=bad))

    def test_rejects_non_prototype_channel_manifest(self):
        with self.assertRaises(p.Reject):
            p.evaluate(rel("v0.1.0-proto.1"), man(channel="stable"))


class SelectTests(unittest.TestCase):
    def test_no_qualifying_release_returns_none(self):
        best, rejects = p.select_pointer([rel("v0.1.0-proto.1")], {"v0.1.0-proto.1": man(ui_gate="failed")})
        self.assertIsNone(best)
        self.assertEqual(len(rejects), 1)

    def test_empty_release_list_returns_none(self):
        best, rejects = p.select_pointer([], {})
        self.assertIsNone(best)
        self.assertEqual(rejects, [])

    def test_highest_proto_build_wins(self):
        releases = [rel("v0.1.0-proto.1"), rel("v0.1.0-proto.3"), rel("v0.1.0-proto.2")]
        mans = {t: man(version=t[1:]) for t in ("v0.1.0-proto.1", "v0.1.0-proto.2", "v0.1.0-proto.3")}
        best, _ = p.select_pointer(releases, mans)
        self.assertEqual(best["tag"], "v0.1.0-proto.3")

    def test_ordering_is_global_build_number_across_version_lines(self):
        # Documented behavior (picker docstring): ONLY the proto build number is
        # compared, so v0.1.0-proto.9 outranks v0.2.0-proto.1. If a product
        # decision changes this to version-line-aware ordering, change this test.
        releases = [rel("v0.2.0-proto.1"), rel("v0.1.0-proto.9")]
        mans = {"v0.2.0-proto.1": man(version="0.2.0-proto.1"),
                "v0.1.0-proto.9": man(version="0.1.0-proto.9")}
        best, _ = p.select_pointer(releases, mans)
        self.assertEqual(best["tag"], "v0.1.0-proto.9")

    def test_failed_gate_does_not_block_older_good_candidate(self):
        releases = [rel("v0.1.0-proto.1"), rel("v0.1.0-proto.2")]
        mans = {"v0.1.0-proto.1": man(version="0.1.0-proto.1"),
                "v0.1.0-proto.2": man(version="0.1.0-proto.2", ui_gate="failed")}
        best, rejects = p.select_pointer(releases, mans)
        self.assertEqual(best["tag"], "v0.1.0-proto.1")
        self.assertEqual(len(rejects), 1)

    def test_tie_is_an_error_never_a_guess(self):
        releases = [rel("v0.1.0-proto.1"), rel("v0.2.0-proto.1")]
        mans = {"v0.1.0-proto.1": man(version="0.1.0-proto.1"),
                "v0.2.0-proto.1": man(version="0.2.0-proto.1")}
        with self.assertRaises(ValueError):
            p.select_pointer(releases, mans)

    def test_pointer_document_carries_label_and_no_invented_url(self):
        best, _ = p.select_pointer([rel("v0.1.0-proto.1")], {"v0.1.0-proto.1": man()})
        doc = p.pointer_document(best)
        self.assertEqual(doc["channel"], "prototype")
        self.assertIn("אב-טיפוס", doc["label"])
        self.assertEqual(doc["url"], "https://example.invalid/v0.1.0-proto.1/StartApp-win-x64.zip")
        self.assertEqual(doc["commit"], SHA)


class CliFixtureTests(unittest.TestCase):
    def test_fixture_mode_exit_3_when_nothing_qualifies(self):
        with tempfile.TemporaryDirectory() as td:
            rp = Path(td, "releases.json"); mp = Path(td, "manifests.json")
            rp.write_text(json.dumps([rel("v0.1.0-proto.1")]), encoding="utf-8")
            mp.write_text(json.dumps({"v0.1.0-proto.1": man(ui_gate="not-gated")}), encoding="utf-8")
            r = subprocess.run([sys.executable, str(Path(p.__file__)),
                                "--releases", str(rp), "--manifests", str(mp)],
                               capture_output=True, text=True)
            self.assertEqual(r.returncode, 3, r.stderr)
            self.assertIn('"candidate": null', r.stdout)


if __name__ == "__main__":
    unittest.main()
