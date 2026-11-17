from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]

class TraversalPolicy(unittest.TestCase):
    def test_input_focus_does_not_gate_traversal(self):
        source = (ROOT / 'src/StartApp.App/App.xaml.cs').read_text(encoding='utf-8')
        traversal, cleanup = source.split('                // Restore the originally selected tab even after input/focus changes.', 1)
        self.assertNotIn('.CanContinue', traversal)
        self.assertNotIn('focus-stop-', traversal)
        self.assertNotIn('tab-already-selected', traversal)
        self.assertGreaterEqual(traversal.count('!selected.Current.IsSelected'), 2)
        self.assertIn('if (originallySelected != null && !cancellationToken.IsCancellationRequested)', cleanup)
        self.assertNotIn('.CanContinue', cleanup)
        self.assertIn('if (!cancellationToken.IsCancellationRequested) restore.Select();', cleanup)

    def test_explicit_cancel_and_lifecycle_guards_remain(self):
        source = (ROOT / 'src/StartApp.App/App.xaml.cs').read_text(encoding='utf-8')
        self.assertGreaterEqual(source.count('cancellationToken.ThrowIfCancellationRequested();'), 5)
        self.assertIn('if (!ScanGate.Wait(0))', source)
        self.assertIn('finally { ScanGate.Release(); }', source)
        vm = (ROOT / 'src/StartApp.Presentation/MainViewModel.cs').read_text(encoding='utf-8')
        self.assertIn('if (gen != _scanGen || cts.IsCancellationRequested) return;', vm)
        self.assertIn('_scanGen++; try { _scanCts?.Cancel(); }', vm)
        self.assertIn('CaptureScanning.BoundedAsync', vm)

if __name__ == '__main__':
    unittest.main()
