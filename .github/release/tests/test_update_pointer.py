#!/usr/bin/env python3
"""Offline tests for update_pointer.py. No network, no gh, no GitHub state.
Run: python3 -m unittest discover -s .github/release/tests -v
(also pytest-compatible). Fixtures mirror the gh api shapes consumed by
pick_release_pointer. These tests exercise selection/assembly logic only;
they are not release runs and prove nothing about GitHub-side state.
Requires PyYAML only for the sibling static-workflow tests."""
import json, os, shutil, subprocess, sys, tempfile, unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import update_pointer as up  # noqa: E402

SHA = "a" * 40
TAG = "v0.1.0-proto.3"


def make_release(tag=TAG, draft=False, prerelease=True,
                 url="https://github.com/o/r/releases/download/x/StartApp-win-x64.zip",
                 with_manifest_asset=True):
    assets = [{"name": "StartApp-win-x64.zip", "browser_download_url": url}]
    if with_manifest_asset:
        assets.append({"name": "manifest.json", "id": 1})
    return {"draft": draft, "prerelease": prerelease, "tag_name": tag, "assets": assets}


def make_manifest(version="0.1.0-proto.3", commit=SHA, ui="passed"):
    return {"channel": "prototype", "prerelease": True, "version": version,
            "commit": commit, "unit_tests": "passed", "ui_gate": ui,
            "asset": "StartApp-win-x64.zip", "sha256": "b" * 64, "size": 123}


class ClassifyTests(unittest.TestCase):
    def test_candidate_selected(self):
        action, doc = up.classify([make_release()], {TAG: make_manifest()})
        self.assertEqual(action, "candidate")
        self.assertEqual(doc["tag"], TAG)
        self.assertEqual(doc["schema"], 1)

    def test_no_candidate_is_safe_none_document(self):
        action, doc = up.classify([], {})
        self.assertEqual(action, "none")
        self.assertIsNone(doc["candidate"])
        self.assertIn("no published prerelease", doc["reason"])

    def test_withdrawal_becomes_none_not_stale_last_good(self):
        action, doc = up.classify([make_release(draft=True)], {TAG: make_manifest()})
        self.assertEqual(action, "none")
        self.assertIsNone(doc["candidate"])

    def test_release_edited_out_of_channel_reconciles(self):
        action, doc = up.classify([make_release(prerelease=False)], {TAG: make_manifest()})
        self.assertEqual(action, "none")
        action, doc = up.classify([make_release(tag="v0.1.0")], {"v0.1.0": None})
        self.assertEqual(action, "none")
        self.assertIsNone(doc["candidate"])

    def test_ui_gate_failure_becomes_none(self):
        action, doc = up.classify([make_release()], {TAG: make_manifest(ui="failed")})
        self.assertEqual(action, "none")

    def test_rollback_selects_next_highest_qualifying(self):
        older = make_release(tag="v0.1.0-proto.1")
        newer_draft = make_release(tag="v0.1.0-proto.3", draft=True)
        mans = {"v0.1.0-proto.1": make_manifest(version="0.1.0-proto.1"),
                "v0.1.0-proto.3": make_manifest()}
        action, doc = up.classify([older, newer_draft], mans)
        self.assertEqual(action, "candidate")
        self.assertEqual(doc["tag"], "v0.1.0-proto.1")

    def test_tie_aborts_loudly(self):
        r1 = make_release(tag="v0.1.0-proto.3")
        r2 = make_release(tag="v0.2.0-proto.3")
        mans = {"v0.1.0-proto.3": make_manifest(),
                "v0.2.0-proto.3": make_manifest(version="0.2.0-proto.3")}
        with self.assertRaises(ValueError):
            up.classify([r1, r2], mans)


class CollectLiveTests(unittest.TestCase):
    """F3: a manifest FETCH failure is an abort, never a 'none' deploy."""

    def test_manifest_fetch_failure_aborts(self):
        releases = [make_release()]
        def fake_gh(*args):
            if "releases?" in args[1]:
                return releases
            raise SystemExit("gh api failed: boom")
        with mock.patch.object(up.sel, "gh_json", side_effect=fake_gh):
            with self.assertRaises(SystemExit):
                up.collect_live("o/r")

    def test_missing_manifest_asset_is_none_not_error(self):
        releases = [make_release(with_manifest_asset=False)]
        with mock.patch.object(up.sel, "gh_json", return_value=releases):
            got_releases, manifests = up.collect_live("o/r")
        self.assertEqual(got_releases, releases)
        self.assertEqual(manifests, {TAG: None})
        # ...and a missing manifest is an ordinary candidate rejection (DATA)
        action, doc = up.classify(got_releases, manifests)
        self.assertEqual(action, "none")


class BuildArtifactTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp, True)

    def make_site(self, with_index=True):
        site = self.tmp / "website"
        site.mkdir(exist_ok=True)
        if with_index:
            (site / "index.html").write_text("<html></html>", encoding="utf-8")
        (site / "docs.html").write_text("<html>docs</html>", encoding="utf-8")
        return site

    def test_artifact_contains_full_site_and_pointer(self):
        site = self.make_site()
        _, doc = up.classify([make_release()], {TAG: make_manifest()})
        up.build_artifact(str(site), doc, str(self.tmp / "_site"))
        self.assertTrue((self.tmp / "_site" / "pointer.json").is_file())
        self.assertTrue((self.tmp / "_site" / "index.html").is_file())
        self.assertTrue((self.tmp / "_site" / "docs.html").is_file())
        written = json.loads((self.tmp / "_site" / "pointer.json").read_text(encoding="utf-8"))
        self.assertEqual(written["tag"], TAG)

    def test_missing_website_dir_refuses_pointer_only(self):
        with self.assertRaises(SystemExit):
            up.build_artifact(str(self.tmp / "nope"), {"schema": 1}, str(self.tmp / "_site"))
        self.assertFalse((self.tmp / "_site").exists())

    def test_incomplete_website_refuses_partial(self):
        site = self.make_site(with_index=False)
        with self.assertRaises(SystemExit):
            up.build_artifact(str(site), {"schema": 1}, str(self.tmp / "_site"))
        self.assertFalse((self.tmp / "_site").exists())

    def test_site_tests_dir_never_published(self):
        site = self.make_site()
        (site / "tests").mkdir()
        (site / "tests" / "test-download-pointer.mjs").write_text("// test", encoding="utf-8")
        up.build_artifact(str(site), {"schema": 1, "channel": "prototype", "candidate": None},
                          str(self.tmp / "_site"))
        self.assertFalse((self.tmp / "_site" / "tests").exists())

    def test_redeploy_replaces_wholesale_no_stale_leftovers(self):
        site = self.make_site()
        out = self.tmp / "_site"
        out.mkdir()
        (out / "stale-old-pointer.json").write_text("{}", encoding="utf-8")
        up.build_artifact(str(site), {"schema": 1, "channel": "prototype", "candidate": None}, str(out))
        self.assertFalse((out / "stale-old-pointer.json").exists())
        self.assertTrue((out / "pointer.json").is_file())

    def test_failed_build_leaves_prior_artifact_untouched(self):
        site = self.make_site()
        out = self.tmp / "_site"
        up.build_artifact(str(site), {"schema": 1, "x": "old"}, str(out))
        before = (out / "pointer.json").read_text(encoding="utf-8")
        with self.assertRaises(SystemExit):
            up.build_artifact(str(self.tmp / "nope"), {"schema": 1, "x": "new"}, str(out))
        self.assertEqual((out / "pointer.json").read_text(encoding="utf-8"), before)


class CliTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tmp, True)

    def run_cli(self, releases, manifests):
        rf = self.tmp / "releases.json"; mf = self.tmp / "manifests.json"
        rf.write_text(json.dumps(releases), encoding="utf-8")
        mf.write_text(json.dumps(manifests), encoding="utf-8")
        site = self.tmp / "website"; site.mkdir()
        (site / "index.html").write_text("<html></html>", encoding="utf-8")
        script = str(Path(__file__).resolve().parents[1] / "update_pointer.py")
        return subprocess.run([sys.executable, script,
                               "--releases", str(rf), "--manifests", str(mf),
                               "--website", str(site), "--out", str(self.tmp / "_site")],
                              capture_output=True, text=True)

    def test_cli_candidate_exit0(self):
        r = self.run_cli([make_release()], {TAG: make_manifest()})
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(json.loads(r.stdout)["action"], "deploy-candidate")

    def test_cli_none_still_deploys_safe_document(self):
        r = self.run_cli([], {})
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(json.loads(r.stdout)["action"], "deploy-none")
        doc = json.loads((self.tmp / "_site" / "pointer.json").read_text(encoding="utf-8"))
        self.assertIsNone(doc["candidate"])

    def test_cli_missing_inputs_aborts(self):
        script = str(Path(__file__).resolve().parents[1] / "update_pointer.py")
        site = self.tmp / "website"; site.mkdir()
        (site / "index.html").write_text("<html></html>", encoding="utf-8")
        r = subprocess.run([sys.executable, script, "--website", str(site),
                            "--out", str(self.tmp / "_site")], capture_output=True, text=True)
        self.assertNotEqual(r.returncode, 0)
        self.assertFalse((self.tmp / "_site").exists())


if __name__ == "__main__":
    unittest.main()
