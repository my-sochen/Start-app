#!/usr/bin/env python3
"""Behavioral offline tests for the `dispatch` job script of
release-pointer-update.yml. The step's real `run` body is extracted from the
YAML and executed under bash with a FAKE `gh` on PATH (no network, no GitHub).
Needs bash, jq and coreutils `timeout`. Proves the correlation, binding,
ownership and PENDING logic actually executes as designed; it does not prove
GitHub-side behavior (200-vs-204 dispatch response, field names in live REST
payloads), which still needs integration confirmation.
Run: python3 -m unittest discover -s .github/release/tests -v"""
import json, os, shutil, stat, subprocess, tempfile, unittest
from pathlib import Path

import yaml

WF = Path(__file__).resolve().parents[2] / "workflows" / "release-pointer-update.yml"
DOC = yaml.safe_load(WF.read_text(encoding="utf-8"))
JOB = DOC["jobs"]["dispatch"]
STEP = JOB["steps"][0]
SCRIPT = STEP["run"]

REPO = "my-sochen/Start-app"
CORR = "555-1"
TITLE = "pointer-update workflow_dispatch corr=" + CORR
WF_PATH = ".github/workflows/release-pointer-update.yml"

FAKE_GH = r'''#!/usr/bin/env python3
import json, os, sys
sc = json.load(open(os.environ["FAKE_SCENARIO"]))
log = open(os.environ["FAKE_LOG"], "a")
args = sys.argv[1:]
assert args[0] == "api"
path = args[1]
post = "-X" in args and args[args.index("-X") + 1] == "POST"
log.write(json.dumps({"path": path, "post": post}) + "\n")
log.flush()

def out(obj):
    if obj is not None:
        sys.stdout.write(obj if isinstance(obj, str) else json.dumps(obj))

if post:
    if sc.get("post_fail"):
        sys.exit(1)
    out(sc.get("post_response"))
    sys.exit(0)
if path.endswith("/actions/workflows/release-pointer-update.yml"):
    out(sc["workflow"]); sys.exit(0)
if "/actions/workflows/release-pointer-update.yml/runs" in path:
    out(sc["list"]); sys.exit(0)
if "/attempts/" in path and path.endswith("/jobs?per_page=100"):
    attempt = path.split("/attempts/", 1)[1].split("/", 1)[0]
    jba = sc.get("jobs_by_attempt") or {}
    out(jba.get(attempt, sc["jobs"])); sys.exit(0)
if "filter=latest" in path:
    # the revised script must NEVER read latest-attempt evidence
    sys.exit(1)
if "/actions/runs/" in path:
    rid = path.rsplit("/", 1)[1]
    seq = sc["runs"][rid]
    item = seq.pop(0) if len(seq) > 1 else seq[0]
    json.dump(sc, open(os.environ["FAKE_SCENARIO"], "w"))
    out(item); sys.exit(0)
sys.exit(1)
'''


def now_iso(offset=0):
    import time
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(time.time() + offset))


def run_obj(rid=777, **kw):
    d = {"id": rid, "repository": {"full_name": REPO}, "workflow_id": 42, "event": "workflow_dispatch",
         "head_branch": "main", "display_title": TITLE, "run_attempt": 1, "created_at": now_iso(1),
         "head_sha": "b" * 40,
         "status": "completed", "conclusion": "success", "name": "release-pointer-update"}
    d.update(kw)
    return d


def jobs_ok():
    steps = [{"name": "Deploy to GitHub Pages", "conclusion": "success"},
             {"name": "Verify served identity matches staged bytes (bounded, no redeploy)", "conclusion": "success"}]
    return {"total_count": 2, "jobs": [{"name": "dispatch", "conclusion": "skipped", "steps": []},
                                       {"name": "update-pointer", "conclusion": "success", "steps": steps}]}


def base_scenario(**kw):
    sc = {"workflow": {"id": 42, "path": WF_PATH}, "post_response": {"workflow_run_id": 777},
          "runs": {"777": [run_obj(777)]}, "list": {"total_count": 0, "workflow_runs": []}, "jobs": jobs_ok()}
    sc.update(kw)
    return sc


class DispatchBehavior(unittest.TestCase):
    def run_script(self, scenario, **env_over):
        d = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, d, True)
        bindir = Path(d) / "bin"
        bindir.mkdir()
        gh = bindir / "gh"
        gh.write_text(FAKE_GH)
        gh.chmod(gh.stat().st_mode | stat.S_IEXEC)
        sc = Path(d) / "scenario.json"
        sc.write_text(json.dumps(scenario))
        log = Path(d) / "calls.log"
        summary = Path(d) / "summary.md"
        env = {k: str(v) for k, v in (JOB.get("env") or {}).items() if "${{" not in str(v)}
        env.update({"PATH": str(bindir) + os.pathsep + os.environ["PATH"], "CORRELATION": CORR,
                    "GITHUB_REPOSITORY": REPO, "GITHUB_RUN_ID": "555", "GITHUB_RUN_ATTEMPT": "1",
                    "GITHUB_SERVER_URL": "https://github.com", "GITHUB_STEP_SUMMARY": str(summary),
                    "GH_TOKEN": "x", "DEFAULT_BRANCH": "main", "EVENT_ACTION": "published",
                    "EVENT_TAG": "v0.1.0-proto.3", "FAKE_SCENARIO": str(sc), "FAKE_LOG": str(log),
                    "LOOKUP_SLEEP_SECONDS": "0", "POLL_SECONDS": "1", "LOOKUP_BOUND_SECONDS": "3",
                    "PARENT_WAIT_SECONDS": "3", "HOME": d})
        env.update({k: str(v) for k, v in env_over.items()})
        p = subprocess.run(["bash", "-c", SCRIPT], env=env, capture_output=True, text=True, timeout=60)
        calls = [json.loads(l) for l in log.read_text().splitlines()] if log.exists() else []
        text = summary.read_text() if summary.exists() else ""
        return p.returncode, p.stdout + p.stderr, calls, text

    def posts(self, calls):
        return [c for c in calls if c["post"]]

    # --- happy paths
    def test_200_response_id_bound_and_writer_evidence_succeeds(self):
        rc, out, calls, _ = self.run_script(base_scenario())
        self.assertEqual(rc, 0, out)
        self.assertEqual(len(self.posts(calls)), 1)
        self.assertIn("deploy step and served-identity verify step all succeeded", out)

    def test_204_empty_response_uses_exact_unique_display_title_lookup(self):
        sc = base_scenario(post_response=None,
                           list={"total_count": 3, "workflow_runs": [
                               run_obj(900, display_title=TITLE + "9"),           # contains-token decoy
                               run_obj(901, display_title="x " + TITLE),          # prefix decoy
                               run_obj(777)]})
        rc, out, calls, _ = self.run_script(sc)
        self.assertEqual(rc, 0, out)
        self.assertEqual(len(self.posts(calls)), 1)
        self.assertIn("bound child run: 777", out)

    # --- blocker 1 negatives
    def test_name_field_match_is_not_enough(self):
        decoy = run_obj(900, display_title="something else", name=TITLE)
        sc = base_scenario(post_response=None, list={"total_count": 1, "workflow_runs": [decoy]})
        rc, out, calls, _ = self.run_script(sc)
        self.assertEqual(rc, 1)
        self.assertIn("could not be uniquely correlated", out)
        self.assertEqual(len(self.posts(calls)), 1)          # never re-POSTed

    def test_two_exact_matches_is_ambiguous_and_refused(self):
        sc = base_scenario(post_response=None, list={"total_count": 2, "workflow_runs": [run_obj(777), run_obj(778)]})
        rc, out, _, _ = self.run_script(sc)
        self.assertEqual(rc, 1)
        self.assertIn("ambiguous", out)

    def test_truncated_listing_refused(self):
        sc = base_scenario(post_response=None, list={"total_count": 150, "workflow_runs": [run_obj(777)]})
        rc, out, _, _ = self.run_script(sc)
        self.assertEqual(rc, 1)
        self.assertIn("truncated", out)

    def test_binding_mismatches_are_not_correlated(self):
        for field, value in (("head_branch", "feature"), ("event", "release"), ("workflow_id", 43),
                             ("run_attempt", 2), ("created_at", now_iso(-3600)), ("created_at", now_iso(3600)),
                             ("repository", {"full_name": "evil/Start-app"}), ("id", "777"), ("id", 7.5)):
            sc = base_scenario(post_response=None, list={"total_count": 1, "workflow_runs": [run_obj(777, **{field: value})]})
            rc, out, _, _ = self.run_script(sc)
            self.assertEqual(rc, 1, (field, value, out))
            self.assertIn("could not be uniquely correlated", out, (field, value))

    def test_returned_id_not_numeric_is_refused(self):
        for bad in ("abc", "12x", "1; echo pwned", "0", "-5", "1.5", "12345678901234567890"):
            sc = base_scenario(post_response={"workflow_run_id": bad})
            rc, out, calls, _ = self.run_script(sc)
            self.assertEqual(rc, 1, (bad, out))
            self.assertIn("refusing", out, bad)
            self.assertEqual(len(self.posts(calls)), 1)

    def test_returned_id_must_bind_to_expected_run(self):
        sc = base_scenario(runs={"777": [run_obj(777, display_title="other title")]})
        rc, out, _, _ = self.run_script(sc)
        self.assertEqual(rc, 1)
        self.assertIn("does not match", out)

    def test_dispatch_post_failure_is_terminal_no_repeat_no_lookup(self):
        sc = base_scenario(post_fail=True, list={"total_count": 1, "workflow_runs": [run_obj(777)]})
        rc, out, calls, _ = self.run_script(sc)
        self.assertEqual(rc, 1)
        self.assertIn("NOT retried", out)
        self.assertEqual(len(self.posts(calls)), 1)
        self.assertFalse(any("/runs" in c["path"] and "workflows" in c["path"] for c in calls))

    def test_workflow_metadata_must_validate_before_any_dispatch(self):
        sc = base_scenario(workflow={"id": 42, "path": ".github/workflows/other.yml"})
        rc, out, calls, _ = self.run_script(sc)
        self.assertEqual(rc, 1)
        self.assertEqual(self.posts(calls), [])

    # --- blocker 2
    def test_success_conclusion_with_skipped_writer_is_not_success(self):
        j = jobs_ok()
        j["jobs"][1]["conclusion"] = "skipped"
        j["jobs"][1]["steps"] = []
        rc, out, _, summary = self.run_script(base_scenario(jobs=j))
        self.assertEqual(rc, 1)
        self.assertIn("evidence is missing", out)
        self.assertIn("NO writer/verify evidence", summary)

    def test_success_without_verify_step_success_is_not_success(self):
        for idx in (0, 1):
            j = jobs_ok()
            j["jobs"][1]["steps"][idx]["conclusion"] = "skipped"
            rc, out, _, _ = self.run_script(base_scenario(jobs=j))
            self.assertEqual(rc, 1, idx)

    def test_missing_or_duplicate_writer_job_is_not_success(self):
        j = jobs_ok()
        j["jobs"] = [j["jobs"][0]]
        j["total_count"] = 1
        self.assertEqual(self.run_script(base_scenario(jobs=j))[0], 1)
        j = jobs_ok()
        j["jobs"].append(copy_job(j["jobs"][1]))
        j["total_count"] = 3
        self.assertEqual(self.run_script(base_scenario(jobs=j))[0], 1)

    def test_failed_child_conclusion_is_loud(self):
        sc = base_scenario(runs={"777": [run_obj(777, conclusion="failure")]})
        rc, out, _, _ = self.run_script(sc)
        self.assertEqual(rc, 1)
        self.assertIn("concluded 'failure'", out)

    # --- blocker 4
    def test_pending_after_bounded_wait_is_fail_visible_and_never_cancels(self):
        sc = base_scenario(runs={"777": [run_obj(777, status="in_progress", conclusion=None)]})
        rc, out, calls, summary = self.run_script(sc, PARENT_WAIT_SECONDS=2)
        self.assertEqual(rc, 1)
        self.assertIn("PENDING", out)
        self.assertIn("did NOT cancel", out)
        self.assertIn("PENDING", summary)
        self.assertFalse(any("cancel" in c["path"] for c in calls))
        self.assertEqual(len(self.posts(calls)), 1)

    def test_child_completing_before_deadline_after_polling(self):
        ts = now_iso(0)   # one created_at for both reads: a real run keeps it across attempts
        sc = base_scenario(runs={"777": [run_obj(777, status="in_progress", conclusion=None, created_at=ts),
                                         run_obj(777, created_at=ts)]})
        rc, out, _, _ = self.run_script(sc)
        self.assertEqual(rc, 0, out)

    # --- rerun masquerade (attempt/identity drift after binding)
    def test_attempt_change_after_binding_fails_visibly(self):
        # Executor reproduction: bound at attempt 1 in-progress, a later
        # status read presents attempt 2 completed/success under the same id.
        # v2 exited 0 via latest-attempt jobs evidence; the revision must
        # fail visibly and never treat the rerun as the original delivery.
        ts = now_iso(0)
        sc = base_scenario(runs={"777": [run_obj(777, status="in_progress", conclusion=None, created_at=ts),
                                         run_obj(777, run_attempt=2, created_at=ts)]})
        rc, out, calls, summary = self.run_script(sc)
        self.assertEqual(rc, 1)
        self.assertIn("identity changed", out)
        self.assertIn("IDENTITY DRIFT", summary)
        self.assertEqual(len(self.posts(calls)), 1)
        self.assertFalse(any("cancel" in c["path"] for c in calls))

    def test_title_ref_or_head_change_after_binding_fails_visibly(self):
        ts = now_iso(0)
        for kw in ({"display_title": "pointer-update workflow_dispatch corr=999-1"},
                   {"head_branch": "other"},
                   {"head_sha": "c" * 40}):
            sc = base_scenario(runs={"777": [run_obj(777, status="in_progress", conclusion=None, created_at=ts),
                                             run_obj(777, created_at=ts, **kw)]})
            rc, out, _, _ = self.run_script(sc)
            self.assertEqual(rc, 1, kw)
            self.assertIn("identity changed", out, kw)

    def test_jobs_evidence_read_is_pinned_to_the_bound_attempt(self):
        rc, out, calls, _ = self.run_script(base_scenario())
        self.assertEqual(rc, 0, out)
        self.assertTrue(any("/attempts/1/jobs" in c["path"] for c in calls))
        self.assertFalse(any("filter=latest" in c["path"] for c in calls))

    def test_attempt_specific_job_association(self):
        # Success evidence on a LATER attempt must not launder the bound
        # attempt: attempt 1 lacks verify success while attempt 2 is clean.
        bad = jobs_ok()
        bad["jobs"][1]["steps"][1]["conclusion"] = "failure"
        sc = base_scenario(jobs=bad, jobs_by_attempt={"1": bad, "2": jobs_ok()})
        rc, out, calls, _ = self.run_script(sc)
        self.assertEqual(rc, 1)
        self.assertIn("evidence is missing", out)
        self.assertFalse(any("/attempts/2/" in c["path"] for c in calls))
        # And the bound attempt's own clean evidence stands on its own even
        # when a later attempt's jobs failed: attempt 2 is never consulted.
        ok1, bad2 = jobs_ok(), jobs_ok()
        bad2["jobs"][1]["conclusion"] = "failure"
        sc = base_scenario(jobs=ok1, jobs_by_attempt={"1": ok1, "2": bad2})
        rc, out, calls, _ = self.run_script(sc)
        self.assertEqual(rc, 0, out)
        self.assertFalse(any("/attempts/2/" in c["path"] for c in calls))

    def test_malformed_correlation_token_refused_before_any_call(self):
        rc, out, calls, _ = self.run_script(base_scenario(), CORRELATION="1; rm -rf /")
        self.assertEqual(rc, 1)
        self.assertEqual(calls, [])


def copy_job(j):
    return json.loads(json.dumps(j))


if __name__ == "__main__":
    unittest.main()
