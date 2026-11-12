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
            // Anchor the popup to the button itself: default placement tracks the mouse/work-area, which
            // renders the menu clipped off-window when the pointer is parked at the screen corner.
            menu.PlacementTarget = (Button)sender;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }
}
