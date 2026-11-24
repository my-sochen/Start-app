using System.Windows;
using System.Windows.Controls;
using StartApp.Presentation;

namespace StartApp.App;

/// <summary>Capture-only drag/drop and staged editor focus. DataContext stays the shared workspace session.</summary>
public partial class CaptureView : UserControl
{
    public CaptureView() => InitializeComponent();
    private void RowContextMenu_Opening(object sender, ContextMenuEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow owner) owner.RowContextMenu_Opening(sender, e);
    }
    // Drag from the capture list onto the workspace side. Each drop is one item and goes through the per-drag duplicate warning in the view model.
    private Point _dragStart;
    private bool _captureDragActive;
    private void CaptureList_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => _dragStart = e.GetPosition(null);
    private void CaptureList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // DoDragDrop pumps mouse messages in its modal loop, so PreviewMouseMove re-enters while a drag
        // is active; without the guard a single physical drag starts a second session whose extra Drop
        // stages the item and then flags it as its own duplicate (CI v37/v38 evidence).
        if (_captureDragActive) return;
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (e.OriginalSource is DependencyObject src && ItemsControl.ContainerFromElement(CaptureList, src) is ListBoxItem { DataContext: MainViewModel.CaptureRow row })
        {
            _captureDragActive = true;
            try { DragDrop.DoDragDrop(CaptureList, new DataObject(typeof(MainViewModel.CaptureRow), row), DragDropEffects.Copy); }
            finally { _captureDragActive = false; }
        }
    }
    private void CaptureDropZone_DragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(typeof(MainViewModel.CaptureRow)) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void CaptureDropZone_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel vm && e.Data.GetData(typeof(MainViewModel.CaptureRow)) is MainViewModel.CaptureRow row) vm.DropCaptured(row);
        e.Handled = true;
    }
    private void ToggleStaged_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MainViewModel.StagedRow row }) RevealStagedRow(sender, editing: !row.IsEditing);
    }
    private void RevealStagedRow(object sender, bool editing)
    {
        if (sender is not Button { DataContext: MainViewModel.StagedRow row } button) return;
        DependencyObject? ancestor = button;
        while (ancestor is not null && ancestor is not StackPanel { ContextMenu: not null, DataContext: MainViewModel.StagedRow })
            ancestor = System.Windows.Media.VisualTreeHelper.GetParent(ancestor);
        if (ancestor is not StackPanel host || !ReferenceEquals(host.DataContext, row)) return;
        // Click precedes the command. Wait for its state and layout, then reveal only this row's input or summary button.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (DataContext is not MainViewModel vm || !vm.IsCaptureSide || !vm.StagedRows.Contains(row) ||
                row.IsEditing != editing || !host.IsVisible || !ReferenceEquals(host.DataContext, row)) return;
            var target = FindStagedRevealTarget(host, row, editing);
            if (target is null) return;
            target.BringIntoView(); target.Focus();
        }));
    }
    private static FrameworkElement? FindStagedRevealTarget(DependencyObject root, MainViewModel.StagedRow row, bool editing)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element && element.IsVisible && ReferenceEquals(element.DataContext, row))
            {
                if (editing && element is TextBox input && System.Windows.Automation.AutomationProperties.GetName(input) == "Added item name") return input;
                if (!editing && element is Button edit && System.Windows.Automation.AutomationProperties.GetName(edit) == "Toggle added " + row.OriginalName) return edit;
            }
            var found = FindStagedRevealTarget(child, row, editing); if (found is not null) return found;
        }
        return null;
    }
}
