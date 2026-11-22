"""Protect modal coverage and the separate content/header layout contracts."""
from collections import Counter
from copy import deepcopy
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

NS = '{http://schemas.microsoft.com/winfx/2006/xaml/presentation}'
MODALS = {
    'CaptureDupSinglePending', 'CaptureDupGroupPending', 'EmptyWorkspaceOpen',
    'AddressEditOpen', 'OpenWithOpen', 'PickerOpen', 'DesignEditOpen',
    'CategoryEditOpen', 'UnsavedOpen', 'SelectionPending',
    'OpenDecisionPending', 'LastResultsOpen', 'InSettings', 'InGlobalEnv',
    'DefaultConfirmOpen',
}


def visibility(name):
    return '{Binding ' + name + ', Converter={StaticResource B2V}}'


class OverlayRows(unittest.TestCase):
    def setUp(self):
        self.root = ET.parse(Path(__file__).resolve().parents[2] /
                             'src/StartApp.App/MainWindow.xaml').getroot()

    def assert_layout(self, root):
        grid = root.find(NS + 'Grid')
        self.assertIsNotNone(grid)
        rows = grid.find(NS + 'Grid.RowDefinitions')
        self.assertIsNotNone(rows)
        # approved 22.11 title-bar tabs: 36px tab strip + update strip + content
        self.assertEqual(['36', 'Auto', '*'], [r.get('Height') for r in rows])
        # Exact identities catch missing, duplicate and unclassified root layers.
        layers = [e for e in grid if 'Visibility' in e.attrib]
        expected = MODALS | {'CaptureOpen', 'SaveBlocked', 'ToastVisible'}
        self.assertEqual(Counter({visibility(n): 1 for n in expected}),
                         Counter(e.get('Visibility') for e in layers))
        for layer in layers:
            binding = layer.get('Visibility')
            if binding in (visibility('CaptureOpen'), visibility('SaveBlocked')):
                # Capture is content, not a full-window modal: leave header visible.
                self.assertEqual('2' if binding == visibility('CaptureOpen') else '1',
                                 layer.get('Grid.Row'), binding)
                self.assertEqual('1', layer.get('Grid.RowSpan', '1'), binding)
            else:
                self.assertEqual('0', layer.get('Grid.Row', '0'), binding)
                self.assertEqual('3', layer.get('Grid.RowSpan'), binding)
            self.assertEqual(NS + ('Border' if binding in
                             (visibility('SaveBlocked'), visibility('ToastVisible'))
                             else 'Grid'), layer.tag, binding)
        self.assertEqual('720', root.get('MinWidth'))

    def test_root_layer_layout_contract(self):
        self.assert_layout(self.root)

    def test_contract_rejects_missing_duplicate_unknown_or_misplaced_layers(self):
        def layer(root, name):
            return next(e for e in root.find(NS + 'Grid')
                        if e.get('Visibility') == visibility(name))
        mutations = {
            'missing modal': lambda r: r.find(NS + 'Grid').remove(layer(r, 'SelectionPending')),
            'duplicate modal': lambda r: r.find(NS + 'Grid').append(deepcopy(layer(r, 'SelectionPending'))),
            'unclassified modal': lambda r: layer(r, 'SelectionPending').set('Visibility', visibility('NewModal')),
            'modal no longer spans header': lambda r: layer(r, 'LastResultsOpen').set('Grid.RowSpan', '1'),
            'capture hides header': lambda r: layer(r, 'CaptureOpen').set('Grid.RowSpan', '2'),
            'toast no longer spans header': lambda r: layer(r, 'ToastVisible').set('Grid.RowSpan', '1'),
            'save warning moved to content': lambda r: layer(r, 'SaveBlocked').set('Grid.Row', '2'),
        }
        for name, mutate in mutations.items():
            with self.subTest(name=name):
                changed = deepcopy(self.root)
                mutate(changed)
                with self.assertRaises(AssertionError):
                    self.assert_layout(changed)


if __name__ == '__main__':
    unittest.main()
