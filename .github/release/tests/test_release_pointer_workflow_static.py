#!/usr/bin/env python3
"""Static checks for the ACTIVATED (owner-gated) release-pointer-update workflow. Offline:
parses the YAML file only. Mirrors the repo's test_release_workflow_static.py
convention (unittest + PyYAML). These checks prove the file's declared shape;
they are not workflow runs and prove nothing about GitHub-side state.
Run: python3 -m unittest discover -s .github/release/tests -v
Note: YAML 1.1 parses the bare key `on` as boolean True; both forms handled."""
import unittest
from pathlib import Path

import yaml

WF = Path(__file__).resolve().parents[2] / "workflows" / "release-pointer-update.yml"
RAW = WF.read_text(encoding="utf-8")
DOC = yaml.safe_load(RAW)
TRIGGERS = DOC.get("on", DOC.get(True))  # YAML 1.1: 'on' -> True


def job():
    assert set(DOC["jobs"]) == {"update-pointer"}
    return DOC["jobs"]["update-pointer"]


class TriggerTests(unittest.TestCase):
    def test_lifecycle_plus_manual_only(self):
        self.assertEqual(set(TRIGGERS["release"]["types"]),
                         {"published", "unpublished", "edited", "deleted"})
        self.assertIn("workflow_dispatch", TRIGGERS)
        for banned in ("push", "pull_request", "pull_request_target", "schedule", "workflow_run"):
            self.assertNotIn(banned, TRIGGERS, banned)

    def test_guard_is_enable_variable_only(self):
        # A release EDITED out of channel (prerelease cleared / retagged) must
        # still reconcile; filtering the guard on the changed release's shape
        # would skip that run and strand a stale pointer.
        self.assertEqual(job()["if"].strip(), "vars.PROTOTYPE_RELEASE_ENABLE == 'true'")


class SourceProvenanceTests(unittest.TestCase):
    def test_checkout_pinned_to_default_branch_not_event_ref(self):
        # Release-event GITHUB_SHA is the tagged commit: a bare checkout could
        # roll the site back with an old release, and a deleted tag would fail
        # checkout and block withdrawal reconciliation.
        checkouts = [s for s in job()["steps"] if str(s.get("uses", "")).startswith("actions/checkout")]
        self.assertEqual(len(checkouts), 1)
        self.assertEqual(checkouts[0]["with"]["ref"], "${{ github.event.repository.default_branch }}")

    def test_no_gh_pages_branch_or_second_writer(self):
        body = RAW.replace("# gh-pages branch", "")
        self.assertNotIn("gh-pages", body)
        uses = [str(s.get("uses", "")) for s in job()["steps"]]
        self.assertFalse(any("peaceiris" in u or "JamesIves" in u for u in uses))


class DeployShapeTests(unittest.TestCase):
    def test_environment_and_concurrency(self):
        self.assertEqual(job()["environment"], "github-pages")
        self.assertEqual(DOC["concurrency"], {"group": "pages", "cancel-in-progress": False})
        self.assertEqual(DOC["permissions"], {})

    def test_job_permissions_least(self):
        self.assertEqual(job()["permissions"],
                         {"contents": "read", "pages": "write", "id-token": "write"})

    def test_artifact_built_before_deploy_and_pointer_driver_used(self):
        steps = job()["steps"]
        uses = [str(s.get("uses", "")) for s in steps]
        run_steps = [s["run"] for s in steps if "run" in s]
        self.assertTrue(any("update_pointer.py" in r and "--website website" in r
                            and "--out _site" in r for r in run_steps))
        i_upload = next(i for i, u in enumerate(uses) if "upload-pages-artifact" in u)
        i_deploy = next(i for i, u in enumerate(uses) if "deploy-pages" in u)
        self.assertLess(i_upload, i_deploy)
        self.assertEqual(steps[i_upload]["with"]["path"], "_site")


if __name__ == "__main__":
    unittest.main()


class JobShapeTests(unittest.TestCase):
    """Regression for the live parse failure on first push (run 43661392190):
    a job without runs-on is rejected by GitHub before any step runs."""

    def test_every_job_has_runs_on(self):
        for name, job in DOC["jobs"].items():
            self.assertIn("runs-on", job, f"job {name} lacks runs-on")
            self.assertTrue(str(job["runs-on"]).strip(), f"job {name} has empty runs-on")

    def test_every_job_has_nonempty_steps(self):
        for name, job in DOC["jobs"].items():
            steps = job.get("steps")
            self.assertIsInstance(steps, list, f"job {name} lacks steps")
            self.assertGreater(len(steps), 0, f"job {name} has empty steps")

    def test_update_pointer_runs_on_ubuntu(self):
        # configure-pages / upload-pages-artifact / deploy-pages are Linux actions.
        self.assertEqual(str(DOC["jobs"]["update-pointer"]["runs-on"]), "ubuntu-latest")
