using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
            vm.AppCloseApproved += () => { _closeApproved = true; Close(); };
        }
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closeApproved && DataContext is MainViewModel vm && !vm.RequestAppClose()) e.Cancel = true;
        base.OnClosing(e);
    }
    private void CardActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu }) { menu.PlacementTarget = (Button)sender; menu.IsOpen = true; }
    }
}
