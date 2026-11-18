"""Static local release wiring checks. No workflow execution or publication.
Policy baseline is the reviewed MAIN revision synchronized into DEV by PR61,
not the earlier dormant portable-preparation workflow. Baseline identity and
negative controls keep this a preservation check, not an activation grant.
"""
from pathlib import Path
import hashlib
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[2]
PATH = '.github/workflows/release-prototype.yml'
BASE = 'b627547ea873e383b2f6194091651838b92e515d'
BASE_SHA256 = '894f501326027265704a294835f5f39af1bcede07d6960179cb6187e433a6039'
DRAFT_GATE = "    if: ${{ needs.resolve.outputs.on_main == 'true' && needs.tests.result == 'success' && needs.ui-gate.result == 'success' && needs.package.result == 'success' && vars.PROTOTYPE_RELEASE_ENABLE == 'true' }}"


def assert_draft_policy(case, text, baseline):
    draft = text.split('  draft-prerelease:', 1)[1]
    case.assertEqual(draft, baseline.split('  draft-prerelease:', 1)[1])
    case.assertIn(DRAFT_GATE, draft)
    case.assertIn('    environment: prototype-release\n', draft)
    case.assertIn('      contents: write\n', draft)
    case.assertIn('    needs: [resolve, tests, ui-gate, package]\n', draft)
    case.assertIn('--draft --prerelease', draft)
    case.assertIn('--repo "$REPO"', draft)
    case.assertIn('--target "$CANDIDATE_SHA"', draft)
    case.assertNotIn('gh release edit', draft)


class ReleaseWiring(unittest.TestCase):
    def setUp(self):
        self.text = (ROOT / PATH).read_text(encoding='utf-8')
        raw = subprocess.check_output(['git', 'show', f'{BASE}:{PATH}'], cwd=ROOT)
        self.assertEqual(hashlib.sha256(raw).hexdigest(), BASE_SHA256)
        self.base = raw.decode('utf-8')

    def test_reviewed_draft_policy_byte_preserved(self):
        assert_draft_policy(self, self.text, self.base)

    def test_nonpackaging_jobs_preserved(self):
        # Header equality preserves triggers, permissions and dispatch inputs.
        self.assertEqual(self.text.split('  tests:', 1)[1].split('  package:', 1)[0], self.base.split('  tests:', 1)[1].split('  package:', 1)[0])
        self.assertEqual(self.text.split('jobs:', 1)[0], self.base.split('jobs:', 1)[0])

    def test_draft_policy_rejects_gate_and_destination_mutations(self):
        # Mutate in memory only. No workflow edit or release execution.
        mutations = {
            'on_main': ("needs.resolve.outputs.on_main == 'true'", 'true'),
            'tests': ("needs.tests.result == 'success'", 'true'),
            'ui': ("needs.ui-gate.result == 'success'", 'true'),
            'package': ("needs.package.result == 'success'", 'true'),
            'enable': ("vars.PROTOTYPE_RELEASE_ENABLE == 'true'", 'true'),
            'environment': ('    environment: prototype-release\n', ''),
            'permissions': ('      contents: write\n', '      contents: admin\n'),
            'dependency': ('    needs: [resolve, tests, ui-gate, package]\n', '    needs: [resolve]\n'),
            'draft': ('--draft --prerelease', '--prerelease'),
            'repository': ('--repo "$REPO"', '--repo other/repo'),
            'target': ('--target "$CANDIDATE_SHA"', '--target other'),
        }
        for name, (old, new) in mutations.items():
            with self.subTest(name=name):
                self.assertIn(old, self.text)
                mutated = self.text.replace(old, new)
                with self.assertRaises(AssertionError):
                    assert_draft_policy(self, mutated, self.base)

    def test_header_rejects_new_push_trigger(self):
        header = self.text.split('jobs:', 1)[0]
        self.assertIn('  workflow_dispatch:', header)
        mutated = header.replace('  workflow_dispatch:', '  push:\n  workflow_dispatch:', 1)
        with self.assertRaises(AssertionError):
            self.assertEqual(mutated, self.base.split('jobs:', 1)[0])

    def test_verify_before_extract_and_smoke(self):
        p = self.text.split('  package:', 1)[1].split('  draft-prerelease:', 1)[0]
        self.assertLess(p.index('      - name: Verify candidate consistency'), p.index('          Expand-Archive'))
        self.assertIn('--layout portable', p)
        self.assertIn('$env:GITHUB_SHA = $env:CANDIDATE_SHA', p)
        self.assertIn('-OwnershipHelper trusted/tests/portable-entry/OwnedTree.cs', p)
        self.assertIn('"workflow-revision: $env:WORKFLOW_SHA"', p)
        self.assertIn('name: StartApp-release-candidate', p)
        self.assertNotIn("-ExePath 'verify-ship\\StartApp.App.exe'", p)

    def test_separate_source_revision_checks_preserved(self):
        self.assertIn('(git -C trusted rev-parse HEAD) -ne $env:WORKFLOW_SHA', self.text)
        self.assertIn('(git -C src rev-parse HEAD) -ne $env:CANDIDATE_SHA', self.text)


if __name__ == '__main__':
    unittest.main()
