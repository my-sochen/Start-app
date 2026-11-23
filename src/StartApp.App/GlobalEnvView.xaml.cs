using System.Windows.Controls;
using System.Windows;
using StartApp.Presentation;

namespace StartApp.App;

public partial class GlobalEnvView : UserControl
{
    public GlobalEnvView() { InitializeComponent(); }
    private void SavedGlobalContextOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MainViewModel.GlobalEntryRow row } host && Window.GetWindow(this) is MainWindow window && DataContext is MainViewModel vm)
        { e.Handled = true; window.ShowSavedTransferActions(host, vm.SavedTransferFromGlobal(row)); }
    }
}
