"""Static local release wiring checks. No workflow execution or publication."""
from pathlib import Path
import subprocess, unittest
ROOT=Path(__file__).resolve().parents[2]
PATH='.github/workflows/release-prototype.yml'
BASE='9b3b56ee8ce90066fc4fd2026847524d266deedd'
class ReleaseWiring(unittest.TestCase):
 def setUp(self):
  self.text=(ROOT/PATH).read_text()
  self.base=subprocess.check_output(['git','show',f'{BASE}:{PATH}'],cwd=ROOT,text=True)
 def test_draft_policy_byte_preserved(self):
  self.assertEqual(self.text.split('  draft-prerelease:',1)[1],self.base.split('  draft-prerelease:',1)[1])
  self.assertIn('if: ${{ false }}',self.text.split('  draft-prerelease:',1)[1])
 def test_nonpackaging_jobs_preserved(self):
  # Resolve changes are dependency lists only, not the release/on-main/tag gates.
  self.assertEqual(self.text.split('  tests:',1)[1].split('  package:',1)[0],self.base.split('  tests:',1)[1].split('  package:',1)[0])
  self.assertEqual(self.text.split('jobs:',1)[0],self.base.split('jobs:',1)[0])
 def test_verify_before_extract_and_smoke(self):
  p=self.text.split('  package:',1)[1].split('  draft-prerelease:',1)[0]
  self.assertLess(p.index('      - name: Verify candidate consistency'),p.index('          Expand-Archive'))
  self.assertIn('--layout portable',p)
  self.assertIn('$env:GITHUB_SHA = $env:CANDIDATE_SHA',p)
  self.assertIn('-OwnershipHelper trusted/tests/portable-entry/OwnedTree.cs',p)
  self.assertIn('"workflow-revision: $env:WORKFLOW_SHA"',p)
  self.assertIn('name: StartApp-release-candidate',p)
  self.assertNotIn("-ExePath 'verify-ship\\StartApp.App.exe'",p)
 def test_separate_source_revision_checks_preserved(self):
  self.assertIn("(git -C trusted rev-parse HEAD) -ne $env:WORKFLOW_SHA",self.text)
  self.assertIn("(git -C src rev-parse HEAD) -ne $env:CANDIDATE_SHA",self.text)
if __name__=='__main__':unittest.main()
