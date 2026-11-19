#!/usr/bin/env python3
"""Static checks for the ACTIVATED (owner-gated) release-pointer-update workflow,
trampolined redesign (option A candidate). Offline: parses the YAML file only.
Mirrors the repo's test_release_workflow_static.py convention (unittest + PyYAML).
These checks prove the file's declared shape; they are not workflow runs and
prove nothing about GitHub-side state.
Run: python3 -m unittest discover -s .github/release/tests -v
Note: YAML 1.1 parses the bare key `on` as boolean True; both forms handled."""
import unittest
from pathlib import Path

import yaml

WF = Path(__file__).resolve().parents[2] / "workflows" / "release-pointer-update.yml"
RAW = WF.read_text(encoding="utf-8")
DOC = yaml.safe_load(RAW)
TRIGGERS = DOC.get("on", DOC.get(True))  # YAML 1.1: 'on' -> True
DISPATCH = DOC["jobs"]["dispatch"]
WRITER = DOC["jobs"]["update-pointer"]


class TriggerTests(unittest.TestCase):
    def test_lifecycle_plus_manual_only(self):
        self.assertEqual(set(TRIGGERS["release"]["types"]),
                         {"published", "unpublished", "edited", "deleted"})
        self.assertIn("workflow_dispatch", TRIGGERS)
        for banned in ("push", "pull_request", "pull_request_target", "schedule", "workflow_run"):
            self.assertNotIn(banned, TRIGGERS, banned)

    def test_dispatch_accepts_correlation_input(self):
        inputs = TRIGGERS["workflow_dispatch"].get("inputs") or {}
        self.assertIn("correlation", inputs)
        self.assertFalse(inputs["correlation"].get("required", True))

    def test_guards_are_enable_variable_plus_own_event(self):
        # Enablement guard retained verbatim on BOTH jobs; each job is pinned
        # to its own event so release events never deploy directly and the
        # writer runs only on dispatch (manual or trampoline child).
        self.assertEqual(DISPATCH["if"].strip(),
                         "vars.PROTOTYPE_RELEASE_ENABLE == 'true' && github.event_name == 'release'")
        self.assertEqual(WRITER["if"].strip(),
                         "vars.PROTOTYPE_RELEASE_ENABLE == 'true' && github.event_name == 'workflow_dispatch'")


class TrampolineTests(unittest.TestCase):
    def test_two_jobs_dispatch_and_writer(self):
        self.assertEqual(set(DOC["jobs"]), {"dispatch", "update-pointer"})

    def test_no_workflow_level_pages_lock(self):
        # A release-event parent waiting for its child must NOT hold the pages
        # group; the lock lives on the writer job only.
        self.assertNotIn("concurrency", DOC)

    def test_writer_holds_pages_lock_through_verification(self):
        self.assertEqual(WRITER["concurrency"], {"group": "pages", "cancel-in-progress": False})

    def test_dispatcher_least_privilege_no_checkout(self):
        self.assertEqual(DISPATCH["permissions"], {"actions": "write"})
        self.assertNotIn("environment", DISPATCH)
        uses = [str(s.get("uses", "")) for s in DISPATCH["steps"]]
        self.assertFalse(any("checkout" in u for u in uses))
        run_body = "\n".join(s.get("run", "") for s in DISPATCH["steps"])
        self.assertIn("dispatches", run_body)  # fixed workflow dispatch call
        self.assertIn("correlation", run_body)

    def test_dispatch_target_fixed_default_branch(self):
        run_body = "\n".join(s.get("run", "") for s in DISPATCH["steps"])
        self.assertIn('ref="$DEFAULT_BRANCH"', run_body)
        self.assertIn('WF="release-pointer-update.yml"', run_body)


class SourceProvenanceTests(unittest.TestCase):
    def test_checkout_pinned_to_default_branch_not_event_ref(self):
        checkouts = [s for s in WRITER["steps"] if str(s.get("uses", "")).startswith("actions/checkout")]
        self.assertEqual(len(checkouts), 1)
        self.assertEqual(checkouts[0]["with"]["ref"], "${{ github.event.repository.default_branch }}")

    def test_no_gh_pages_branch_or_second_writer(self):
        body = RAW.replace("# gh-pages branch", "")
        self.assertNotIn("gh-pages", body)
        uses = [str(s.get("uses", "")) for s in WRITER["steps"]]
        self.assertFalse(any("peaceiris" in u or "JamesIves" in u for u in uses))


class DeployShapeTests(unittest.TestCase):
    def test_environment_and_writer_permissions_least(self):
        self.assertEqual(WRITER["environment"], "github-pages")
        self.assertEqual(WRITER["permissions"],
                         {"contents": "read", "pages": "write", "id-token": "write"})
        self.assertEqual(DOC["permissions"], {})

    def test_artifact_built_before_deploy_and_pointer_driver_used(self):
        steps = WRITER["steps"]
        uses = [str(s.get("uses", "")) for s in steps]
        run_steps = [s["run"] for s in steps if "run" in s]
        self.assertTrue(any("update_pointer.py" in r and "--website .website-source" in r
                            and "--out _site" in r for r in run_steps))
        i_upload = next(i for i, u in enumerate(uses) if "upload-pages-artifact" in u)
        i_deploy = next(i for i, u in enumerate(uses) if "deploy-pages" in u)
        self.assertLess(i_upload, i_deploy)
        self.assertEqual(steps[i_upload]["with"]["path"], "_site")

    def test_staged_identity_frozen_before_upload(self):
        steps = WRITER["steps"]
        run_steps = [s["run"] for s in steps if "run" in s]
        i_freeze = next(i for i, r in enumerate(run_steps) if ".served-expected" in r)
        uses = [str(s.get("uses", "")) for s in steps]
        i_upload = next(i for i, u in enumerate(uses) if "upload-pages-artifact" in u)
        # the freeze step runs before the artifact is uploaded
        freeze_step = [s for s in steps if s.get("run") == run_steps[i_freeze]]
        self.assertTrue(freeze_step)
        self.assertLess(steps.index(freeze_step[0]), i_upload)

    def test_verification_after_deploy_no_redeploy(self):
        steps = WRITER["steps"]
        uses = [str(s.get("uses", "")) for s in steps]
        i_deploy = next(i for i, u in enumerate(uses) if "deploy-pages" in u)
        verify_steps = [s for s in steps if "verify_served.py" in s.get("run", "")]
        self.assertEqual(len(verify_steps), 1)
        self.assertGreater(steps.index(verify_steps[0]), i_deploy)
        # verification is the LAST step: nothing after it can redeploy
        self.assertEqual(steps[-1], verify_steps[0])


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
        self.assertEqual(str(WRITER["runs-on"]), "ubuntu-latest")


def _dispatch_script():
    return "\n".join(s.get("run", "") for s in DISPATCH["steps"])


class CorrelationV2Tests(unittest.TestCase):
    """Blocker 1: exact, unique, fully bound child correlation (v2)."""

    def test_matches_display_title_exactly_not_name_or_contains(self):
        body = _dispatch_script()
        self.assertIn(".display_title == $title", body)
        self.assertNotIn("contains(", body)
        self.assertNotIn(".name | contains", body)
        self.assertNotIn("head -n1", body)

    def test_expected_title_mirrors_run_name(self):
        self.assertEqual(DOC["run-name"], "pointer-update ${{ github.event_name }} corr=${{ inputs.correlation }}")
        self.assertIn('EXPECTED_TITLE="pointer-update workflow_dispatch corr=$CORRELATION"', _dispatch_script())

    def test_full_binding_fields(self):
        body = _dispatch_script()
        for needle in (".repository.full_name == $repo", ".workflow_id == ($wfid | tonumber)",
                       '.event == "workflow_dispatch"', ".head_branch == $branch", ".run_attempt == 1",
                       "fromdateiso8601", "type == \"number\""):
            self.assertIn(needle, body, needle)

    def test_numeric_id_validation_and_uniqueness(self):
        body = _dispatch_script()
        self.assertIn("[!0-9]", body)
        self.assertIn("ambiguous", body)
        self.assertIn(".total_count", body)

    def test_single_post_and_no_silent_dispatch_fallback(self):
        body = _dispatch_script()
        self.assertEqual(body.count("-X POST"), 1)
        self.assertNotIn('|| RESP=""', body)
        self.assertIn("NOT retried", body)


class OutcomeOwnershipV2Tests(unittest.TestCase):
    """Blocker 2: writer job + deploy + verify evidence, not run conclusion alone."""

    def test_requires_writer_job_and_step_evidence(self):
        body = _dispatch_script()
        # Evidence is pinned to the exact bound attempt, never the latest:
        # a rerun's jobs must not stand in for the original delivery's jobs.
        self.assertIn("/attempts/$BOUND_ATTEMPT/jobs", body)
        self.assertNotIn("filter=latest", body)
        self.assertIn('.name == "update-pointer"', body)
        self.assertIn(".conclusion == \"success\"", body)

    def test_every_status_read_revalidates_the_frozen_binding(self):
        # Rerun-masquerade revision: the complete identity frozen at binding
        # (attempt, head_sha, created_at plus repo/workflow/event/ref/title)
        # is rechecked on every status response, with visible failure on drift.
        body = _dispatch_script()
        for needle in ("BOUND_ATTEMPT", "BOUND_SHA", "BOUND_CREATED", "identity changed"):
            self.assertIn(needle, body, needle)

    def test_step_names_match_the_writer_steps(self):
        env = DISPATCH["env"]
        names = [s.get("name") for s in WRITER["steps"]]
        self.assertEqual(names.count(env["DEPLOY_STEP_NAME"]), 1)
        self.assertEqual(names.count(env["VERIFY_STEP_NAME"]), 1)
        deploy = next(s for s in WRITER["steps"] if s.get("id") == "deploy")
        self.assertEqual(deploy["name"], env["DEPLOY_STEP_NAME"])


class ParentTimingV2Tests(unittest.TestCase):
    """Blocker 4: hosted 360 min cap, setup accounted, short fail-visible PENDING, no cancel."""

    def test_parent_timeout_within_hosted_cap_with_setup(self):
        env = DISPATCH["env"]
        work = (int(env["LOOKUP_BOUND_SECONDS"]) + int(env["PARENT_WAIT_SECONDS"])
                + 4 * int(env["API_TIMEOUT_SECONDS"]))
        cap = DISPATCH["timeout-minutes"] * 60
        self.assertLessEqual(DISPATCH["timeout-minutes"], 360)
        self.assertGreaterEqual(cap - work, 5 * 60, "need >= 5 min slack for runner setup")
        self.assertLessEqual(DISPATCH["timeout-minutes"], 60, "wait is short, not long")

    def test_pending_is_fail_visible_and_never_cancels(self):
        body = _dispatch_script()
        self.assertIn("PENDING", body)
        self.assertIn("did NOT cancel", body)
        for banned in ("/cancel", "gh run cancel", "force-cancel"):
            self.assertNotIn(banned, body)
        self.assertEqual(DISPATCH["permissions"], {"actions": "write"})

    def test_deadlines_use_absolute_shell_clock_and_bounded_calls(self):
        body = _dispatch_script()
        self.assertIn("WAIT_END=$(( SECONDS + PARENT_WAIT_SECONDS ))", body)
        self.assertIn('timeout "$API_TIMEOUT_SECONDS" gh api', body)

    def test_writer_has_finite_total_timeout(self):
        self.assertEqual(WRITER["timeout-minutes"], 35)
        self.assertLess(WRITER["timeout-minutes"], 360)

    def test_verify_step_bounded_by_deadline_and_step_timeout(self):
        step = WRITER["steps"][-1]
        self.assertIn("--deadline", step["run"])
        self.assertNotIn("--attempts", step["run"])
        deadline = int(step["env"]["VERIFY_DEADLINE_SECONDS"])
        self.assertGreater(step["timeout-minutes"] * 60, deadline + 30 + 10)


if __name__ == "__main__":
    unittest.main()
