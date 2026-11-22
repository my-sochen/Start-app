"""Profile routing and fail-closed readiness contracts. Standard library only."""
import hashlib
import importlib.util
import itertools
from pathlib import Path
import re
import unittest
from unittest.mock import patch
import os
import tempfile

ROOT = Path(__file__).resolve().parents[2]
PATH = ROOT / '.github/e2e/validation-profile.py'
spec = importlib.util.spec_from_file_location('profiles', PATH)
profiles = importlib.util.module_from_spec(spec)
spec.loader.exec_module(profiles)


class Profiles(unittest.TestCase):
    def setUp(self):
        self.workflow = (ROOT / '.github/workflows/validation-feature-e2e.yml').read_text()

    def test_exact_dev_is_the_only_fast_ref(self):
        self.assertEqual('fast', profiles.profile_for('refs/heads/dev'))
        for ref in profiles.FULL_REFS:
            self.assertEqual('full', profiles.profile_for('refs/heads/' + ref))

    def test_unexpected_refs_fail_not_fast_or_silent_skip(self):
        for ref in (None, '', 'dev', 'main', 'refs/heads/main', 'refs/heads/DEV',
                    'refs/heads/dev/feature', 'refs/heads/develop', 'refs/tags/dev',
                    'refs/pull/57/merge', 'refs/heads/test', 'refs/heads/fix/new-impact',
                    'refs/heads/feat/unknown', 'refs/heads/dev\nprofile=fast'):
            with self.subTest(ref=ref), self.assertRaises(ValueError):
                profiles.profile_for(ref)

    def test_trigger_inventory_exactly_matches_reviewed_routes(self):
        branches = re.search(r'branches: \[([^]]+)\]', self.workflow).group(1).split(',')
        self.assertEqual(profiles.FULL_REFS | {'dev'}, {s.strip() for s in branches})
        # reviewed route (owner 22.11 targeted cadence): exactly one manual dispatch,
        # offering only the two reviewed UI scopes
        self.assertEqual(1, self.workflow.count('workflow_dispatch:'))
        self.assertIn("options: ['cycle1-only', 'full']", self.workflow)
        self.assertNotIn('pull_request:', self.workflow)

    def test_no_failed_or_skipped_required_stage_can_be_ready(self):
        states = ['success', 'failure', 'cancelled', 'skipped', '', None, 'stale']
        for profile in ['fast', 'full', '', None, 'unknown']:
            for resolve, tests, ui, package in itertools.product(states, repeat=4):
                allowed = ((resolve, tests, ui) == ('success',) * 3 and
                           ((profile == 'fast' and package == 'skipped') or
                            (profile == 'full' and package == 'success')))
                if allowed:
                    text = profiles.result_message(profile, resolve, tests, ui, package)
                    self.assertIn('QA', text)
                else:
                    with self.assertRaises(ValueError):
                        profiles.result_message(profile, resolve, tests, ui, package)

    def test_full_gates_and_shared_capture_are_wired(self):
        for name in ['Cycle-1 flow UI run', 'Capture readiness helper', 'Capture flow UI run']:
            step = self.workflow.split('      - name: ' + name, 1)[1].split('      - ', 1)[0]
            self.assertIn("if: needs.resolve.outputs.profile == 'full'", step)
        self.assertIn("always() && needs.resolve.outputs.profile == 'full'", self.workflow)
        self.assertIn("needs.resolve.result == 'success' && needs.resolve.outputs.profile == 'full' && needs.tests.result == 'success'", self.workflow)
        self.assertEqual(1, self.workflow.count('& ./.github/e2e/synthetic-update-capture.ps1'))
        self.assertIn('python .github/e2e/validation-profile.py --status', self.workflow)
        self.assertIn('needs: [resolve, tests, ui-gate, package]', self.workflow)
        self.assertIn("if: ${{ always() && github.event.inputs.ui_scope != 'cycle1-only' && !(github.event_name == 'push' && github.ref_name == 'feat/settings-tab') }}", self.workflow)
        # feat/settings-tab pushes run resolve+tests only; UI/package/status via dispatch
        self.assertIn("if: ${{ github.event_name == 'workflow_dispatch' || github.ref_name != 'feat/settings-tab' || contains(github.event.head_commit.message, '[ui]') }}", self.workflow)
        self.assertEqual(4, self.workflow.count("github.event.inputs.ui_scope != 'cycle1-only'"))
        self.assertNotIn('retention-days: 1', self.workflow)

    def test_synthetic_capture_guard_contracts_preserved(self):
        text = (ROOT / '.github/e2e/synthetic-update-capture.ps1').read_text()
        for required in ['$pngs.Count -ne 30', 'StartAppSourceCommit=$env:SHA',
                         'default-factory-source-selection', 'MainBetaNoticeSource',
                         '$width in 900,720', 'startupReads -ne 11', 'clickReads -ne 11',
                         'explicitBrowserSinkCalls -ne 1', 'WaitForExit(60000)',
                         'Synthetic capture requires the exact checked-out commit']:
            self.assertIn(required, text)

    def test_synthetic_body_is_exact_previous_qualified_definition(self):
        text = (ROOT / '.github/e2e/synthetic-update-capture.ps1').read_text().split('}\n', 1)[1]
        self.assertEqual('6339bad06f3ebcefc8b2c8e0c1998f224cd0a3a9e4b2ad7152ddac5478ed3da2',
                         hashlib.sha256(text.encode()).hexdigest())

    def test_evidence_rejects_wrong_sha_and_dirty_checkout(self):
        with patch.dict(os.environ, {'SHA': 'b' * 40}), patch.object(profiles, 'git', return_value='a' * 40):
            with self.assertRaisesRegex(ValueError, 'exact current checkout'):
                profiles.binding('refs/heads/dev')
        with patch.dict(os.environ, {'SHA': 'a' * 40}), patch.object(profiles, 'git', side_effect=['a' * 40, ' M check.py']):
            with self.assertRaisesRegex(ValueError, 'Tracked checkout changes'):
                profiles.binding('refs/heads/dev')

    def test_evidence_binds_exact_tree_run_attempt_and_definition_bytes(self):
        env = dict(SHA='a'*40, GITHUB_RUN_ID='123', GITHUB_RUN_ATTEMPT='2',
                   RUNNER_OS='Windows', ImageVersion='image-example', VERSION='0.1.0-proto.1')
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            file = root / 'global.json'
            file.write_text('example')
            with patch.dict(os.environ, env), patch.object(profiles, 'ROOT', root), \
                 patch.object(profiles, 'git', side_effect=['a'*40, '', 'global.json', 'b'*40]), \
                 patch.object(profiles.subprocess, 'check_output', return_value='10.0.404\n'):
                data = profiles.binding('refs/heads/test/validation')
            self.assertEqual('b'*40, data['sourceTree'])
            self.assertEqual(('full', '123', '2'), (data['profile'], data['runId'], data['runAttempt']))
            self.assertEqual(hashlib.sha256(b'example').hexdigest(), data['definitionSha256']['global.json'])
            self.assertEqual(5, data['artifactRetentionDays'])

    def test_package_smokes_exact_zip_not_payload(self):
        text = self.workflow.split('  package:', 1)[1].split('  validation-status:', 1)[0]
        self.assertLess(text.index('Verify candidate consistency'), text.index('Expand-Archive'))
        self.assertIn('Expand-Archive -Path cand/StartApp-win-x64.zip', text)
        self.assertIn('-Extracted verify-ship', text)
        self.assertIn('--evidence ../cand/validation-binding.json', text)
        self.assertIn('retention-days: 5', text)
        self.assertIn("UI_GATE: ${{ needs.ui-gate.result == 'success' && 'passed' || 'failed' }}", text)


if __name__ == '__main__':
    unittest.main()
