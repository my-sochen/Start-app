using System.Windows.Controls;
namespace StartApp.App;
public partial class ManualAddForm : StackPanel
{
    public ManualAddForm() => InitializeComponent();
    public void FocusTarget() => TargetBox.Focus();
}
