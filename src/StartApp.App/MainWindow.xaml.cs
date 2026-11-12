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
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
            menu.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
            {
                var x = targetSize.Width - popupSize.Width;
                var y = targetSize.Height;
                if (Window.GetWindow(button) is { } w &&
                    button.TransformToAncestor(w).Transform(new Point(0, targetSize.Height)).Y + popupSize.Height > w.ActualHeight)
                    y = -popupSize.Height;
                return new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(x, y), System.Windows.Controls.Primitives.PopupPrimaryAxis.None) };
            };
            menu.IsOpen = true;
        }
    }
}
