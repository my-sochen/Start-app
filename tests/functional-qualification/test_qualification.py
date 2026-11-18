"""Bounded orchestrator source contracts, not Windows runtime evidence."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
path = ROOT / '.github/e2e/functional-qualification.py'
spec = importlib.util.spec_from_file_location('qualification', path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class Qualification(unittest.TestCase):
    def test_fixed_candidate_source_identity(self):
        self.assertEqual('72cece944eaef8d6057d702db3ae66c2bbf9290e', module.SHA)
        self.assertEqual('b93dd94396eb6db119723a8a411168c1c80ee688', module.TREE)
        self.assertEqual({'ux', 'capture-policy'}, set(module.LANES))

    def test_no_failure_or_missing_outcome_becomes_pass(self):
        with tempfile.TemporaryDirectory() as d:
            path = Path(d) / 'outcomes.json'
            with self.assertRaises(OSError):
                module.outcomes(path, {'actual scenario': 1})
            for payload in ([], {}, [{'name': 'case', 'ok': False}], [{'name': 'case', 'ok': 'true'}], [None], [{'ok': True}]):
                path.write_text(json.dumps(payload))
                with self.subTest(payload=payload), self.assertRaises(ValueError):
                    module.outcomes(path, {'actual scenario': 1})
            path.write_text(json.dumps([{'name': 'actual scenario', 'ok': True}]))
            self.assertEqual(1, len(module.outcomes(path, {'actual scenario': 1})))
            for rows in ([{'name': 'other', 'ok': True}], [{'name': 'actual scenario', 'ok': True}] * 2):
                path.write_text(json.dumps(rows))
                with self.assertRaisesRegex(ValueError, 'inventory mismatch'):
                    module.outcomes(path, {'actual scenario': 1})

    def test_only_feature_trigger_fixed_candidate_no_duplicate_full_suite(self):
        text = (ROOT / '.github/workflows/functional-qualification-72ce.yml').read_text(encoding='utf-8')
        self.assertIn('branches: [feat/functional-qualification-72ce]', text)
        self.assertIn('ref: ' + module.SHA, text)
        self.assertIn('contents: read', text)
        self.assertNotIn('workflow_dispatch:', text)
        self.assertNotIn('dotnet test StartApp.slnx', text)
        self.assertNotIn('cycle1-flow.ps1', text)
        self.assertNotIn('capture-flow.ps1', text)
        self.assertNotIn('dotnet publish', text)
        self.assertNotIn('secrets.', text)
        self.assertIn('fail-fast: false', text)
        self.assertIn('timeout-minutes: 12', text)
        self.assertIn('retention-days: 5', text)
        self.assertIn('if: always()', text)
        self.assertIn('STARTAPP_CAPTURE_TRACE%3BSTARTAPP_CAPTURE_TEST', text)
        self.assertIn("$p.Kill($true)", text)

    def test_helpers_are_workflow_source_but_tested_projects_are_candidate_source(self):
        text = (ROOT / '.github/workflows/functional-qualification-72ce.yml').read_text(encoding='utf-8')
        self.assertEqual(4, text.count('working-directory: candidate'))
        self.assertIn('python qualification/.github/e2e/functional-qualification.py', text)
        self.assertIn('--root candidate --output evidence', text)
        self.assertIn('git -C qualification rev-parse HEAD', text)
        self.assertIn('git -C candidate rev-parse HEAD', text)
        self.assertIn('--untracked-files=all', text)


    def test_process_hash_and_fresh_outcome_binding_fail_closed(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            for filename in ('Fixture.exe', 'Fixture.dll', 'StartApp.App.dll', 'checks.json'):
                (root / filename).write_bytes(filename.encode())
            data = {'exeSha256': module.digest(root / 'Fixture.exe'), 'dllSha256': module.digest(root / 'Fixture.dll'), 'appDllSha256': module.digest(root / 'StartApp.App.dll'), 'exitCode': 0, 'pid': 42, 'assertionsSha256': module.digest(root / 'checks.json')}
            record = root / 'process.json'
            record.write_text(json.dumps(data))
            self.assertEqual(42, module.process_evidence(record, root, 'Fixture', root / 'checks.json')['pid'])
            for field, value in [('exitCode', 1), ('exeSha256', 'wrong'), ('appDllSha256', 'wrong'), ('assertionsSha256', 'stale'), ('pid', 0)]:
                record.write_text(json.dumps({**data, field: value}))
                with self.subTest(field=field), self.assertRaises(ValueError):
                    module.process_evidence(record, root, 'Fixture', root / 'checks.json')

    def test_additive_harness_and_no_visible_chromium_guard(self):
        text = (ROOT / 'tools/FunctionalJourneys/Program.cs').read_text(encoding='utf-8')
        self.assertIn('Phase=="write" && (Directory.Exists(Output)||File.Exists(Output))', text)
        self.assertIn('write Done does not save', text)
        self.assertIn('write reopen retains changed bound fields', text)
        self.assertIn('reload visible rows contain persisted staged and saved edits', text)
        guard = (ROOT / 'tools/functional-qualification/Require-CleanChromium.ps1').read_text(encoding='utf-8')
        self.assertIn('EnumWindows', guard)
        self.assertIn('Chrome_WidgetWin_', guard)
        self.assertIn('$enumerated = [CleanChromium]::EnumWindows', guard)
        self.assertIn('$length -le 0 -or $length -ge 255', guard)
        self.assertIn('Require-CleanResult $enumerated $errors.Count $visible.Count', guard)
        self.assertIn('@($false,0,0),@($true,1,0),@($true,0,1)', guard)
        self.assertIn('-ContractTest', (ROOT / '.github/workflows/functional-qualification-72ce.yml').read_text(encoding='utf-8'))
        self.assertNotIn('Stop-Process', guard)
        self.assertNotIn('.Kill(', guard)
        driver = (ROOT / 'tools/functional-qualification/Run-CapturePolicy.ps1').read_text(encoding='utf-8')
        self.assertIn('Stale policy output', driver)
        self.assertIn('policy-process.json', driver)


if __name__ == '__main__':
    unittest.main()
