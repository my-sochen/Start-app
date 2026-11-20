import json
from pathlib import Path
import unittest
import yaml

ROOT = Path(__file__).resolve().parents[3]


class WebsiteLifecycleTests(unittest.TestCase):
    def test_ci_targets_website_lifecycle_and_main_gate(self):
        doc = yaml.safe_load((ROOT / '.github/workflows/website-pr-tests.yml').read_text())
        triggers = doc.get('on', doc.get(True))
        self.assertEqual(set(triggers), {'pull_request'})
        self.assertEqual(triggers['pull_request']['branches'], ['main', 'website/dev', 'website/test', 'website/main'])
        # Always-present gate: no paths filter, so the check appears on every
        # pull request to these branches and is safe to make required.
        self.assertNotIn('paths', triggers['pull_request'])
        self.assertEqual(doc['permissions'], {'contents': 'read'})
        self.assertEqual(set(doc['jobs']), {'infrastructure-checks', 'website-browser-full'})
        # The gate job keeps this exact unique name: it is the status check an
        # owner may require on main. The browser job is intentionally
        # conditional and must never be a universally required check.
        gate = doc['jobs']['infrastructure-checks']
        self.assertEqual(gate['name'], 'Website infrastructure and static checks')
        self.assertEqual(doc['jobs']['website-browser-full']['name'], 'Website full Chrome DOM and eight locales')
        heavy = [s for s in gate['steps'] if str(s.get('name', '')).startswith(('Run offline', 'Set up Node'))]
        self.assertTrue(heavy)
        for step in heavy:
            self.assertIn("relevant == 'true'", str(step.get('if', '')), step['name'])
        self.assertNotIn('environment', gate)
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

    def test_live_pin_matches_approved_baseline(self):
        # Visibility tripwire, NOT authorization: the expected value records the
        # website commit the owner approved for promotion through the website
        # lifecycle and for publication on 2026-11-20, whose served bytes an
        # independent post-publication review verified byte-identical to the
        # pinned source. A later pin change must update this value in the same
        # pull request, making every pin move visible in review. Enforcement
        # comes only from the repository protection settings documented in
        # WEBSITE-LIFECYCLE.md, never from this test.
        pin = json.loads((ROOT / '.github/release/website-live.json').read_text())
        self.assertEqual(pin, {'schema': 1, 'branch': 'website/main', 'commit': 'f94789e0f94c6f8a30c544bc4d6beb61a8409d4b'})

    def test_ci_guard_rejects_app_and_unrelated_infrastructure(self):
        doc = yaml.safe_load((ROOT / '.github/workflows/website-pr-tests.yml').read_text())
        step = next(s for s in doc['jobs']['infrastructure-checks']['steps'] if s['name'] == 'Reject software changes in website lifecycle')
        script = step['run']
        # Inspect and execute only the policy expressions without git/environment reads.
        policy = script[script.index('infra = '):script.index('rejected = ')]
        scope = {}
        exec(policy, scope)
        for path in ('src/App.cs', 'tests/App.cs', '.github/release/make_manifest.py', '.github/workflows/release-prototype.yml'):
            self.assertFalse(scope['allowed'](path), path)
        self.assertTrue(scope['allowed']('website/index.html'))
        self.assertTrue(scope['allowed']('.github/release/stage_website.py'))

    def test_website_scope_detection_includes_nested_delete_and_rename_destinations(self):
        import os, tempfile
        from unittest import mock
        doc = yaml.safe_load((ROOT / '.github/workflows/website-pr-tests.yml').read_text())
        steps = doc['jobs']['infrastructure-checks']['steps']
        scope_step = next(s for s in steps if s.get('id') == 'scope')
        script = scope_step['run'].split("python3 - <<'PYGUARD'\n", 1)[1].rsplit('PYGUARD', 1)[0]
        cases = [(['website/index.html'], 'main', True, True),
                 (['website/en/deep/file.css'], 'website/dev', True, True),
                 (['website/tests/lock.json'], 'website/test', True, True),
                 (['website/removed.html', 'website/renamed.html'], 'website/main', True, True),
                 (['.github/workflows/website-pr-tests.yml', '.github/release/stage_website.py'], 'main', False, True),
                 ([], 'main', False, False),
                 (['src/App.cs', 'README.md'], 'main', False, False)]
        for paths, base_ref, expected_website, expected_relevant in cases:
            with self.subTest(paths=paths, base_ref=base_ref), tempfile.TemporaryDirectory() as t:
                output = Path(t) / 'outputs'
                env = {'BASE_SHA': 'a'*40, 'EXPECTED_HEAD': 'b'*40, 'GITHUB_OUTPUT': str(output), 'BASE_REF': base_ref}
                with mock.patch.dict(os.environ, env), mock.patch('subprocess.check_output', return_value=('\0'.join(paths)+'\0').encode()):
                    exec(script, {})
                lines = dict(line.split('=') for line in output.read_text().splitlines())
                self.assertEqual(lines['website_changed'], str(expected_website).lower())
                self.assertEqual(lines['relevant'], str(expected_relevant).lower())
        # The software-change rejection applies only inside website lifecycle
        # branches; on main the same paths are a trivial-pass gate result.
        with tempfile.TemporaryDirectory() as t:
            output = Path(t) / 'outputs'
            env = {'BASE_SHA': 'a'*40, 'EXPECTED_HEAD': 'b'*40, 'GITHUB_OUTPUT': str(output), 'BASE_REF': 'website/dev'}
            with mock.patch.dict(os.environ, env), mock.patch('subprocess.check_output', return_value=b'src/App.cs\0'):
                with self.assertRaises(SystemExit):
                    exec(script, {})
        browser = doc['jobs']['website-browser-full']
        self.assertEqual(browser['needs'], 'infrastructure-checks')
        self.assertIn("needs.infrastructure-checks.outputs.website_changed == 'true'", browser['if'])
        self.assertEqual(browser['name'], 'Website full Chrome DOM and eight locales')
        self.assertNotEqual(browser['name'], doc['jobs']['infrastructure-checks']['name'])
        self.assertEqual(doc['jobs']['infrastructure-checks']['outputs']['website_changed'], '${{ steps.scope.outputs.website_changed }}')
        for name in ('Install pinned harness dependencies (requires committed lockfile)', 'Run DOM behavior harness', 'Run locale harness (all 8 locales, default run)'):
            self.assertNotIn('if', next(s for s in browser['steps'] if s['name'] == name))
        for name in ('Run offline publisher regression tests (isolated Python, no deploy)', 'Run offline website tests (no dependencies, no server)'):
            self.assertIn("relevant == 'true'", next(s for s in steps if s['name'] == name)['if'])
        self.assertIn('NOT website runtime qualification', str(steps))

    def test_real_git_rename_source_cannot_bypass_scope_or_browser_coverage(self):
        import os, tempfile, subprocess
        from unittest import mock
        doc = yaml.safe_load((ROOT / '.github/workflows/website-pr-tests.yml').read_text())
        step = next(s for s in doc['jobs']['infrastructure-checks']['steps'] if s.get('id') == 'scope')
        script = step['run'].split("python3 - <<'PYGUARD'\n", 1)[1].rsplit('PYGUARD', 1)[0]
        cases = [('src/app.txt', 'website/app.txt', True, None),
                 ('website/source.txt', '.github/release/WEBSITE-LIFECYCLE.md', False, True),
                 ('website/deep/source.txt', 'website/new.txt', False, True),
                 ('.github/release/WEBSITE-LIFECYCLE.md', '.github/release/website-live.json', False, False)]
        for old, new, rejected, browser in cases:
            with self.subTest(old=old, new=new), tempfile.TemporaryDirectory() as t:
                repo = Path(t)
                def git(*args):
                    return subprocess.check_output(['git', '-C', t, *args], stderr=subprocess.STDOUT).decode().strip()
                git('init', '-q'); git('config', 'user.email', 'test@example.invalid'); git('config', 'user.name', 'Offline Test')
                git('config', 'diff.renames', 'true')
                src = repo / old; src.parent.mkdir(parents=True, exist_ok=True)
                src.write_text('same exact content\n' * 30)
                git('add', '.'); git('commit', '-qm', 'base'); base = git('rev-parse', 'HEAD')
                dst = repo / new; dst.parent.mkdir(parents=True, exist_ok=True)
                git('mv', old, new); git('commit', '-qm', 'rename'); head = git('rev-parse', 'HEAD')
                detected = git('diff', '--name-status', base, head)
                self.assertTrue(detected.startswith('R100'), detected)
                output = repo / 'out'
                # Lifecycle-branch scenario: the software-change rejection only
                # applies to website/* targets, so BASE_REF names one here.
                with mock.patch.dict(os.environ, {'BASE_SHA': base, 'EXPECTED_HEAD': head, 'GITHUB_OUTPUT': str(output), 'BASE_REF': 'website/test'}):
                    # Real subprocess/git and actual inline guard, no mocked diff list.
                    previous = Path.cwd()
                    try:
                        os.chdir(t)
                        if rejected:
                            with self.assertRaises(SystemExit): exec(script, {})
                            self.assertFalse(output.exists())
                        else:
                            exec(script, {})
                            lines = dict(line.split('=') for line in output.read_text().splitlines())
                            self.assertEqual(lines['website_changed'], str(browser).lower())
                            self.assertEqual(lines['relevant'], 'true')
                    finally: os.chdir(previous)


if __name__ == '__main__':
    unittest.main()
