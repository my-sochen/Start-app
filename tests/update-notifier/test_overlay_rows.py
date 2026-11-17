"""Protect all real full-window overlays when root layout gains an update row."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

class OverlayRows(unittest.TestCase):
    def test_every_root_overlay_spans_both_rows(self):
        root = ET.parse(Path(__file__).resolve().parents[2] / 'src/StartApp.App/MainWindow.xaml').getroot()
        ns = '{http://schemas.microsoft.com/winfx/2006/xaml/presentation}'
        grid = root.find(ns + 'Grid')
        overlays = [e for e in grid if e.tag == ns + 'Grid' and 'Visibility' in e.attrib]
        self.assertEqual(10, len(overlays))
        for overlay in overlays:
            self.assertEqual('2', overlay.get('Grid.RowSpan'), overlay.get('Visibility'))
        self.assertEqual('720', root.get('MinWidth'))

if __name__ == '__main__': unittest.main()
