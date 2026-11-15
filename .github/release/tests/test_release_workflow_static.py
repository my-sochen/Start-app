#!/usr/bin/env python3
"""Static checks for the DORMANT release-prototype workflow. Offline: parses the
YAML file only. These checks prove the file's declared shape (manual trigger,
read-only permissions, hard-disabled write job); they are not release runs and
prove nothing about GitHub-side state. Requires PyYAML.
Note: YAML 1.1 parses the bare key `on` as boolean True; both forms are handled."""
import unittest
from pathlib import Path

import yaml

WF = Path(__file__).resolve().parents[2] / "workflows" / "release-prototype.yml"
DOC = yaml.safe_load(WF.read_text(encoding="utf-8"))
TRIGGERS = DOC.get("on", DOC.get(True))  # YAML 1.1: 'on' -> True


class TriggerTests(unittest.TestCase):
    def test_only_manual_trigger(self):
        self.assertIsInstance(TRIGGERS, dict)
        self.assertEqual(set(TRIGGERS), {"workflow_dispatch"},
                         f"unexpected triggers: {sorted(TRIGGERS)}")

    def test_no_event_triggers_anywhere(self):
        for banned in ("push", "pull_request", "pull_request_target", "release",
                       "schedule", "workflow_run", "create", "tag"):
            self.assertNotIn(banned, TRIGGERS, banned)

    def test_required_inputs(self):
        inputs = TRIGGERS["workflow_dispatch"]["inputs"]
        for name in ("sha", "version"):
            self.assertIn(name, inputs)
            self.assertTrue(inputs[name]["required"])


class PermissionTests(unittest.TestCase):
    def test_top_level_read_only(self):
        self.assertEqual(DOC.get("permissions"), {"contents": "read"})

    def test_no_environment_outside_disabled_job(self):
        for name, job in DOC["jobs"].items():
            if name != "draft-prerelease":
                self.assertNotIn("environment", job, name)


class DormantJobTests(unittest.TestCase):
    def setUp(self):
        self.job = DOC["jobs"]["draft-prerelease"]

    def test_hard_disabled(self):
        self.assertEqual(str(self.job.get("if")).strip(), "${{ false }}",
                         "draft-prerelease must stay hard-disabled in this commit")

    def test_no_write_permission(self):
        perms = self.job.get("permissions", {})
        self.assertEqual(perms.get("contents"), "none")

    def test_no_environment_configured(self):
        self.assertNotIn("environment", self.job,
                         "activation adds the Environment; dormant file must not reference one")

    def test_draft_never_publishes(self):
        text = WF.read_text(encoding="utf-8")
        self.assertIn("--draft", text)
        self.assertIn("--prerelease", text)
        self.assertNotIn("gh release edit", text, "no publish path may exist")
        self.assertNotIn("--latest", text, "no latest-pointer manipulation")

    def test_depends_on_all_gates(self):
        self.assertEqual(set(self.job["needs"]), {"resolve", "tests", "ui-gate", "package"})


class GateShapeTests(unittest.TestCase):
    def test_all_expected_jobs_present(self):
        self.assertEqual(set(DOC["jobs"]),
                         {"resolve", "tests", "ui-gate", "package", "draft-prerelease"})

    def test_package_manifest_uses_actual_ui_gate_result(self):
        text = WF.read_text(encoding="utf-8")
        self.assertIn("needs.ui-gate.result == 'success' && 'passed' || 'failed'", text)

    def test_no_repository_variable_arms_anything(self):
        # vars.* may appear only inside the explanatory comment of the dormant job.
        for i, line in enumerate(WF.read_text(encoding="utf-8").splitlines(), 1):
            if "vars." in line:
                self.assertTrue(line.lstrip().startswith("#"),
                                f"line {i}: vars reference outside a comment")


if __name__ == "__main__":
    unittest.main()
