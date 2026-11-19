import json
from pathlib import Path
import unittest
import yaml

ROOT = Path(__file__).resolve().parents[3]


class WebsiteLifecycleTests(unittest.TestCase):
    def test_ci_targets_only_website_lifecycle(self):
        doc = yaml.safe_load((ROOT / '.github/workflows/website-pr-tests.yml').read_text())
        triggers = doc.get('on', doc.get(True))
        self.assertEqual(set(triggers), {'pull_request'})
        self.assertEqual(triggers['pull_request']['branches'], ['website/dev', 'website/test', 'website/main'])
        self.assertEqual(doc['permissions'], {'contents': 'read'})
        self.assertEqual(set(doc['jobs']), {'website-tests'})
        self.assertNotIn('environment', doc['jobs']['website-tests'])
        self.assertNotIn('deploy-pages', str(doc))
        self.assertNotIn('dotnet', str(doc))

    def test_writer_stages_pinned_data_before_build(self):
        doc = yaml.safe_load((ROOT / '.github/workflows/release-pointer-update.yml').read_text())
        steps = doc['jobs']['update-pointer']['steps']
        commands = [s.get('run', '') for s in steps]
        stage = next(i for i, s in enumerate(commands) if 'stage_website.py' in s)
        build = next(i for i, s in enumerate(commands) if 'update_pointer.py' in s)
        self.assertLess(stage, build)
        self.assertIn('--website .website-source', commands[build])
        self.assertIn('refs/heads/website/main:refs/remotes/origin/website/main', '\n'.join(commands))
        self.assertNotIn('--website website ', '\n'.join(commands))

    def test_bootstrap_pin_is_exact_and_not_moving_test(self):
        pin = json.loads((ROOT / '.github/release/website-live.json').read_text())
        self.assertEqual(pin, {'schema': 1, 'branch': 'website/main', 'commit': '3779d174923a592d7c56a22671624cbe041404b1'})

    def test_ci_guard_rejects_app_and_unrelated_infrastructure(self):
        doc = yaml.safe_load((ROOT / '.github/workflows/website-pr-tests.yml').read_text())
        step = next(s for s in doc['jobs']['website-tests']['steps'] if s['name'] == 'Reject software changes in website lifecycle')
        script = step['run']
        # Inspect and execute only the policy expressions without git/environment reads.
        policy = script[script.index('infra = '):script.index('rejected = ')]
        scope = {}
        exec(policy, scope)
        for path in ('src/App.cs', 'tests/App.cs', '.github/release/make_manifest.py', '.github/workflows/release-prototype.yml'):
            self.assertFalse(scope['allowed'](path), path)
        self.assertTrue(scope['allowed']('website/index.html'))
        self.assertTrue(scope['allowed']('.github/release/stage_website.py'))


if __name__ == '__main__':
    unittest.main()
