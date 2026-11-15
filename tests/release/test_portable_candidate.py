"""Synthetic ZIP structure tests only. No executable code or release produced."""
import hashlib,json,subprocess,sys,tempfile,unittest,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
SHA='a'*40
VERSION='0.1.0-proto.2'
class CandidateTests(unittest.TestCase):
 def check(self,layout,entries,expected,manifest_layout=True,reason=None):
  with tempfile.TemporaryDirectory() as tmp:
   d=Path(tmp);z=d/'StartApp-win-x64.zip'
   with zipfile.ZipFile(z,'w') as out:
    for name in entries:
     out.writestr(name, f'{SHA} {VERSION}' if name.endswith('PROTOTYPE-NOTICE.txt') else 'synthetic')
   digest=hashlib.sha256(z.read_bytes()).hexdigest()
   (d/(z.name+'.sha256')).write_text(f'{digest} {z.name}')
   (d/'BUILDINFO.txt').write_text(f'commit: {SHA}\nversion: {VERSION}\nsha256: {digest}\n')
   command=[sys.executable,str(ROOT/'.github/release/make_manifest.py'),'--dir',tmp,'--sha',SHA,'--version',VERSION,'--ui-gate','passed','--layout',layout]
   subprocess.run(command,check=True,stdout=subprocess.PIPE)
   if not manifest_layout:
    p=d/'manifest.json';m=json.loads(p.read_text());m.pop('layout',None);p.write_text(json.dumps(m))
   result=subprocess.run([sys.executable,str(ROOT/'.github/release/verify_candidate.py'),'--dir',tmp,'--sha',SHA,'--version',VERSION,'--layout',layout],capture_output=True,text=True)
   self.assertEqual(result.returncode,expected,result.stdout+result.stderr)
   if reason:self.assertIn(reason,result.stdout)
 def test_portable_pass(self):self.check('portable',self.portable(),0)
 def test_legacy_still_passes(self):self.check('legacy',['StartApp.App.exe','PROTOTYPE-NOTICE.txt','KNOWN-LIMITS.txt'],0)
 def test_no_launcher(self):self.check('portable',self.portable()[1:],1)
 def test_no_actual_app(self):self.check('portable',[n for n in self.portable() if n!='App/StartApp.App.exe'],1)
 def test_no_payload_notice(self):self.check('portable',[n for n in self.portable() if 'NOTICE'not in n],1)
 def test_no_layout_manifest(self):self.check('portable',self.portable(),1,False)
 def test_root_payload_rejected(self):self.check('portable',self.portable()+['StartApp.App.exe'],1)
 def test_traversal_rejected(self):self.check('portable',self.portable()+['App/../../escape'],1,reason='ZIP contains unsafe path')
 def test_nested_drive_rejected(self):self.check('portable',self.portable()+['App/C:/x'],1,reason='ZIP contains unsafe path')
 def test_ads_rejected(self):self.check('portable',self.portable()+['App/file:stream'],1,reason='ZIP contains unsafe path')
 def test_backslash_rejected(self):self.check('portable',self.portable()+['App/..\\escape'],1,reason='ZIP contains unsafe path')
 @staticmethod
 def portable():return ['Start-app.exe','START-HERE.txt','App/StartApp.App.exe','App/PROTOTYPE-NOTICE.txt','App/KNOWN-LIMITS.txt']
if __name__=='__main__':unittest.main()
