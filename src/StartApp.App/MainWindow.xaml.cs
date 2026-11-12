using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using StartApp.Presentation;
namespace StartApp.App;
public partial class MainWindow : Window
{
    private bool _closeApproved;
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook();
        Loaded += (_, _) => Hook();
    }
    private void Hook()
    {
        if (DataContext is MainViewModel vm)
        {
            vm.FocusAddRequested += () => TargetBox.Focus();
            vm.CopyAddressRequested += a => Clipboard.SetText(a);
            vm.AppCloseApproved += () => { _closeApproved = true; Close(); };
            vm.PickImageRequested += () =>
            {
                var dlg = new OpenFileDialog { Title = "העלה תמונה", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*" };
                if (dlg.ShowDialog(this) == true) vm.DesignImagePath = dlg.FileName;
            };
        }
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closeApproved && DataContext is MainViewModel vm && !vm.RequestAppClose()) e.Cancel = true;
        base.OnClosing(e);
    }
    // Capture dropdown (לכידה): anchored under its button at the window's left edge, so it opens downward and stays inside the window.
    private void CaptureMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }
    // Drag from the capture list onto the workspace side. Each drop is one item and goes through the per-drag duplicate warning in the view model.
    private Point _dragStart;
    private void CaptureList_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => _dragStart = e.GetPosition(null);
    private void CaptureList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (e.OriginalSource is DependencyObject src && ItemsControl.ContainerFromElement(CaptureList, src) is ListBoxItem { DataContext: MainViewModel.CaptureRow row })
            DragDrop.DoDragDrop(CaptureList, new DataObject(typeof(MainViewModel.CaptureRow), row), DragDropEffects.Copy);
    }
    private void CaptureDropZone_DragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(typeof(MainViewModel.CaptureRow)) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void CaptureDropZone_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel vm && e.Data.GetData(typeof(MainViewModel.CaptureRow)) is MainViewModel.CaptureRow row) vm.DropCaptured(row);
        e.Handled = true;
    }
    private void CardActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu })
        {
            // Keep the whole menu inside the window (user report Nov 12: the item-row menu clipped off-screen).
            // Anchor it to the button itself (default placement tracks the mouse/work-area and clips off-window),
            // right-align it with the button so it grows leftward (both action buttons sit at the window's right
            // edge), and flip above the button when the space below it inside the window is too small.
            var button = (Button)sender;
            menu.PlacementTarget = button;
            // Settle the share entry's visibility BEFORE the popup measures, so its height is final at placement time.
            foreach (var mi in menu.Items.OfType<System.Windows.Controls.MenuItem>())
                if (mi.Header as string == "שיתוף לסביבה אחרת")
                    mi.Visibility = (button.Tag as MainViewModel)?.HasOtherWorkspaces == true ? Visibility.Visible : Visibility.Collapsed;
            // Decide up/down from the real measured space inside the window at click time, and hard-bound the menu
            // height to the larger side so it can never extend past the window. The placement callback's popupSize is
            // not reliable on a menu's first open (runs 43307032198/43310237708: last-row menu overflowed the window
            // bottom by 19px because the flip never engaged).
            var openAbove = false;
            if (Window.GetWindow(button) is { } host)
            {
                var topLeft = button.TransformToAncestor(host).Transform(new Point(0, 0));
                var spaceBelow = host.ActualHeight - (topLeft.Y + button.ActualHeight);
                var spaceAbove = topLeft.Y;
                openAbove = spaceBelow < spaceAbove;
                menu.MaxHeight = Math.Max(spaceBelow, spaceAbove);
            }
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
            menu.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
            {
                var x = targetSize.Width - popupSize.Width;
                var y = openAbove ? -popupSize.Height : targetSize.Height;
                return new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(x, y), System.Windows.Controls.Primitives.PopupPrimaryAxis.None) };
            };
            menu.IsOpen = true;
        }
    }
}
