using System.Windows;
using System.Windows.Controls;

namespace StartApp.App;

public partial class CreateWorkspaceView : UserControl
{
    public CreateWorkspaceView() => InitializeComponent();
    public void FocusName() => NameInput.Focus();
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is StartApp.Presentation.MainViewModel vm) vm.LeaveCreateScreen();
    }
}
