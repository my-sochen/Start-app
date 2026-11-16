"""Static asset/resource wiring checks. Not Windows shell/runtime tests."""
import hashlib,struct,unittest,xml.etree.ElementTree as ET
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
class IconSourceTests(unittest.TestCase):
 def test_icon_nine_exact_sizes(self):
  b=(ROOT/'src/StartApp.App/Assets/start-app-ufo.ico').read_bytes();reserved,kind,count=struct.unpack_from('<HHH',b);self.assertEqual((reserved,kind,count),(0,1,9))
  sizes=[]
  for i in range(count):
   w,h,c,r,p,bits,n,offset=struct.unpack_from('<BBBBHHII',b,6+16*i);sizes.append((w or 256,h or 256));self.assertGreater(n,0);self.assertLessEqual(offset+n,len(b))
  self.assertEqual(sizes,[(n,n) for n in [16,20,24,32,40,48,64,128,256]])
 def test_apphost_and_wpf_same_icon(self):
  p=ET.parse(ROOT/'src/StartApp.App/StartApp.App.csproj').getroot();self.assertEqual(p.findtext('./PropertyGroup/ApplicationIcon'),'Assets/start-app-ufo.ico');self.assertIsNotNone(p.find("./ItemGroup/Resource[@Include='Assets/start-app-ufo.ico']"))
  w=ET.parse(ROOT/'src/StartApp.App/MainWindow.xaml').getroot();self.assertEqual(w.attrib['Icon'],'Assets/start-app-ufo.ico')
 def test_launcher_resource_same_icon(self):
  self.assertEqual((ROOT/'tools/portable-entry/launcher.rc').read_text(),'1 ICON "src/StartApp.App/Assets/start-app-ufo.ico"\n')
  s=(ROOT/'tools/portable-entry/build.cmd').read_text();self.assertIn('rc /nologo /fo portable-build\\launcher.res',s);self.assertIn('launcher.c portable-build\\launcher.res',s)
 def test_package_includes_opt_in_script(self):
  s=(ROOT/'tools/portable-entry/package.ps1').read_text();self.assertIn("Join-Path $PSScriptRoot 'Create-DesktopShortcut.ps1'",s);self.assertNotIn('Invoke-Expression',s)
 def test_shortcut_no_overwrite_or_elevation(self):
  s=(ROOT/'tools/portable-entry/Create-DesktopShortcut.ps1').read_text();self.assertIn('[IO.File]::Move($tempLink, $link)',s);self.assertIn("$shortcut.IconLocation = $target + ',0'",s);self.assertIn('$shortcut.WorkingDirectory = $root',s);self.assertNotIn('-Force',s);self.assertNotIn('RunAs',s);self.assertNotIn('ExecutionPolicy',s)
if __name__=='__main__':unittest.main()
