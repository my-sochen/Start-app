"""Protect the author's minimal app-only contrast treatment, not a new geometry."""
import hashlib,json,unittest,xml.etree.ElementTree as ET
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
class ContrastProvenanceTests(unittest.TestCase):
 def test_exact_author_source_and_derived_hashes(self):
  m=json.loads((ROOT/'tools/portable-entry/provenance/contrast-integration.json').read_text())
  self.assertEqual(m['author_svg_sha256'],'1a5b1ab78ed15a7eb905936b370adb043555ceab0e97c86b3d874412fdb3325b')
  for name,digest in m['files'].items(): self.assertEqual(hashlib.sha256((ROOT/name).read_bytes()).hexdigest(),digest,name)
 def test_geometry_and_foreground_unchanged(self):
  original=ET.parse(ROOT/'tools/portable-entry/provenance/pre-contrast-app.svg').getroot();current=ET.parse(ROOT/'src/StartApp.App/Assets/start-app-ufo.svg').getroot()
  self.assertEqual(original.attrib,current.attrib)
  foreground=list(current)[1:];backing=list(current)[0]
  self.assertEqual([p.attrib for p in original],[p.attrib for p in foreground]);self.assertEqual(len(foreground),6)
  self.assertEqual(backing.attrib,{'stroke':'#ffffff','stroke-width':'2.1'})
  self.assertEqual([p.attrib['d'] for p in original],[p.attrib['d'] for p in backing])
  for path in backing:self.assertEqual(set(path.attrib),{'d','stroke','stroke-width'});self.assertEqual(path.attrib['stroke'],'#ffffff');self.assertEqual(path.attrib['stroke-width'],'2.1')
if __name__=='__main__':unittest.main()
