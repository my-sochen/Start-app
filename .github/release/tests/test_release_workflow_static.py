#!/usr/bin/env python3
"""Static checks for the ACTIVATED release-prototype workflow. Offline: parses
the YAML file only. These checks prove the file's declared shape (manual
trigger, read-only top-level permissions, gated write job); they are not
release runs and prove nothing about GitHub-side state (the Environment's
protection and the variable's existence are owner-configured settings).
Requires PyYAML.
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

    def test_no_environment_outside_draft_job(self):
        for name, job in DOC["jobs"].items():
            if name != "draft-prerelease":
                self.assertNotIn("environment", job, name)


class ActivatedJobTests(unittest.TestCase):
    EXPECTED_IF = ("${{ needs.resolve.outputs.on_main == 'true' && "
                   "needs.tests.result == 'success' && "
                   "needs.ui-gate.result == 'success' && "
                   "needs.package.result == 'success' && "
                   "vars.PROTOTYPE_RELEASE_ENABLE == 'true' }}")

    def setUp(self):
        self.job = DOC["jobs"]["draft-prerelease"]

    def test_gate_condition_is_exact_design(self):
        self.assertEqual(str(self.job.get("if")).strip(), self.EXPECTED_IF,
                         "draft-prerelease gate must equal the reviewed design condition")

    def test_write_permission_scoped_to_contents(self):
        self.assertEqual(self.job.get("permissions"), {"contents": "write"},
                         "only contents: write may be granted to the draft job")

    def test_protected_environment_named(self):
        self.assertEqual(self.job.get("environment"), "prototype-release",
                         "draft job must run in the owner-protected Environment")

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

    def test_enable_variable_only_in_draft_gate(self):
        # Outside comments, vars.* may appear only inside the draft job's gate
        # line, and only the enable variable.
        for i, line in enumerate(WF.read_text(encoding="utf-8").splitlines(), 1):
            if "vars." in line and not line.lstrip().startswith("#"):
                self.assertIn("if:", line,
                              f"line {i}: vars reference outside the draft gate")
                self.assertIn("vars.PROTOTYPE_RELEASE_ENABLE == 'true'", line,
                              f"line {i}: unexpected variable reference")


if __name__ == "__main__":
    unittest.main()


class JobShapeTests(unittest.TestCase):
    """Same latent-requirement sweep applied to the release workflow:
    every job must declare runs-on and non-empty steps."""

    def test_every_job_has_runs_on_and_steps(self):
        for name, job in DOC["jobs"].items():
            self.assertIn("runs-on", job, f"job {name} lacks runs-on")
            self.assertTrue(str(job["runs-on"]).strip(), f"job {name} has empty runs-on")
            steps = job.get("steps")
            self.assertIsInstance(steps, list, f"job {name} lacks steps")
            self.assertGreater(len(steps), 0, f"job {name} has empty steps")
