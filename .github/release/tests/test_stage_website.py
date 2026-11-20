import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import stage_website as sw


class StageWebsiteTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.repo = self.root / 'repo'
        self.repo.mkdir()
        self.git('init', '-q')
        self.git('config', 'user.email', 'test@example.invalid')
        self.git('config', 'user.name', 'Offline Test')
        for name in ('index.html', 'docs.html', 'styles.css', 'tests/private.txt', 'assets/tests/legitimate.txt'):
            f = self.repo / 'website' / name
            f.parent.mkdir(parents=True, exist_ok=True)
            f.write_text('approved ' + name)
        (self.repo / 'src').mkdir()
        (self.repo / 'src/private.txt').write_text('never publish')
        self.git('add', '.')
        self.git('commit', '-qm', 'approved')
        self.approved = self.git('rev-parse', 'HEAD').strip()
        self.git('update-ref', 'refs/remotes/origin/website/main', self.approved)
        self.pin = self.root / 'pin.json'
        self.save_pin()

    def tearDown(self):
        self.temp.cleanup()

    def git(self, *args):
        return subprocess.check_output(['git', '-C', str(self.repo), *args], stderr=subprocess.STDOUT).decode()

    def save_pin(self, **changes):
        d = {'schema': 1, 'branch': 'website/main', 'commit': self.approved}
        d.update(changes)
        self.pin.write_text(json.dumps(d))

    def stage(self):
        return sw.stage(self.repo, self.pin, self.root / 'out')

    def test_only_approved_website_no_software_or_tests(self):
        d = self.stage()
        self.assertEqual(d['commit'], self.approved)
        self.assertEqual(set(d['files']), {'index.html', 'docs.html', 'styles.css', 'assets/tests/legitimate.txt'})
        self.assertFalse((self.root / 'out/src').exists())
        self.assertFalse((self.root / 'out/tests').exists())
        self.assertTrue((self.root / 'out/website-source.json').is_file())

    def test_production_branch_advance_does_not_publish_new_content(self):
        (self.repo / 'website/index.html').write_text('not yet publication approved')
        self.git('add', '.'); self.git('commit', '-qm', 'await approval')
        self.git('update-ref', 'refs/remotes/origin/website/main', 'HEAD')
        self.stage()
        self.assertEqual((self.root / 'out/index.html').read_text(), 'approved index.html')

    def test_unmerged_pin_fails_closed(self):
        (self.repo / 'website/index.html').write_text('not merged')
        self.git('add', '.'); self.git('commit', '-qm', 'feature')
        self.save_pin(commit=self.git('rev-parse', 'HEAD').strip())
        with self.assertRaises(subprocess.CalledProcessError): self.stage()
        self.assertFalse((self.root / 'out').exists())

    def test_missing_production_branch_fails_closed(self):
        self.git('update-ref', '-d', 'refs/remotes/origin/website/main')
        with self.assertRaises(subprocess.CalledProcessError): self.stage()

    def test_invalid_pins(self):
        for change in ({'schema': True}, {'schema': 2}, {'branch': 'website/test'},
                       {'commit': 'main'}, {'commit': 'A' * 40}, {'extra': 'data'}):
            with self.subTest(change=change):
                self.save_pin(**change)
                with self.assertRaises(ValueError): self.stage()

    def test_existing_output_not_reused(self):
        self.stage()
        with self.assertRaises(ValueError): self.stage()

    def test_symlink_rejected_before_writing(self):
        (self.repo / 'website/link').symlink_to('../src/private.txt')
        self.git('add', '.'); self.git('commit', '-qm', 'unsafe link')
        self.approved = self.git('rev-parse', 'HEAD').strip()
        self.git('update-ref', 'refs/remotes/origin/website/main', self.approved)
        self.save_pin()
        with self.assertRaises(ValueError): self.stage()
        self.assertFalse((self.root / 'out').exists())

    def test_incomplete_site_rejected_before_writing(self):
        self.git('rm', 'website/docs.html'); self.git('commit', '-qm', 'incomplete')
        self.approved = self.git('rev-parse', 'HEAD').strip()
        self.git('update-ref', 'refs/remotes/origin/website/main', self.approved)
        self.save_pin()
        with self.assertRaises(ValueError): self.stage()
        self.assertFalse((self.root / 'out').exists())


if __name__ == '__main__':
    unittest.main()


class ArtifactExclusionTests(unittest.TestCase):
    def test_only_root_test_directory_is_excluded(self):
        import update_pointer
        with tempfile.TemporaryDirectory() as t:
            root = Path(t) / 'site'
            for name in update_pointer.REQUIRED_SITE_ENTRIES:
                f = root / name; f.parent.mkdir(parents=True, exist_ok=True); f.write_text('fixture')
            for name in ('tests/private.txt', 'assets/tests/keep.txt'):
                f = root / name; f.parent.mkdir(parents=True, exist_ok=True); f.write_text('fixture')
            out = Path(t) / 'out'
            update_pointer.build_artifact(str(root), {}, str(out))
            self.assertFalse((out / 'tests').exists())
            self.assertTrue((out / 'assets/tests/keep.txt').is_file())
