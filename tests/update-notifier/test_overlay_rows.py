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
    'OpenDecisionPending', 'LastResultsOpen',
    'DefaultConfirmOpen',
}


CONTENT_SCREENS = {'CaptureOpen', 'InAllItems', 'InSettings', 'InGlobalEnv'}

def layout_grid(root):
    return root.find(NS + 'Border/' + NS + 'Grid')

def visibility(name):
    return '{Binding ' + name + ', Converter={StaticResource B2V}}'


class OverlayRows(unittest.TestCase):
    def setUp(self):
        self.root = ET.parse(Path(__file__).resolve().parents[2] /
                             'src/StartApp.App/MainWindow.xaml').getroot()

    def assert_layout(self, root):
        grid = layout_grid(root)
        self.assertIsNotNone(grid)
        rows = grid.find(NS + 'Grid.RowDefinitions')
        self.assertIsNotNone(rows)
        # Title strip, warning strip, content and footer in the redesigned window
        self.assertEqual(['38', 'Auto', '*', 'Auto'], [r.get('Height') for r in rows])
        # Exact identities catch missing, duplicate and unclassified root layers.
        layers = [e for e in grid if e.get('Visibility', '').startswith('{Binding') and e.get('Grid.Row') != '3']
        expected = MODALS | CONTENT_SCREENS | {'SaveBlocked', 'ToastVisible'}
        self.assertEqual(Counter({visibility(n): 1 for n in expected}),
                         Counter(e.get('Visibility') for e in layers))
        for layer in layers:
            binding = layer.get('Visibility')
            if binding in {visibility(n) for n in CONTENT_SCREENS | {'SaveBlocked'}}:
                # Capture is content, not a full-window modal: leave header visible.
                self.assertEqual('1' if binding == visibility('SaveBlocked') else '2',
                                 layer.get('Grid.Row'), binding)
                self.assertEqual('1', layer.get('Grid.RowSpan', '1'), binding)
            else:
                self.assertEqual('0', layer.get('Grid.Row', '0'), binding)
                self.assertEqual('4' if binding == visibility('UnsavedOpen') else '3', layer.get('Grid.RowSpan'), binding)
            self.assertEqual(NS + ('Border' if binding in
                             (visibility('SaveBlocked'), visibility('ToastVisible'))
                             else 'Grid'), layer.tag, binding)
        self.assertEqual('720', root.get('MinWidth'))

    def test_root_layer_layout_contract(self):
        self.assert_layout(self.root)

    def test_contract_rejects_missing_duplicate_unknown_or_misplaced_layers(self):
        def layer(root, name):
            return next(e for e in layout_grid(root)
                        if e.get('Visibility') == visibility(name))
        mutations = {
            'missing modal': lambda r: layout_grid(r).remove(layer(r, 'SelectionPending')),
            'duplicate modal': lambda r: layout_grid(r).append(deepcopy(layer(r, 'SelectionPending'))),
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
