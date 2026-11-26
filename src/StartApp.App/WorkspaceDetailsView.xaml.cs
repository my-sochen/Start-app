using System.Windows;
using System.Windows.Controls;

namespace StartApp.App;

/// <summary>Workspace detail layout and local input focus. The shared view model owns commands and drafts.</summary>
public partial class WorkspaceDetailsView : DockPanel
{
    public WorkspaceDetailsView() => InitializeComponent();

    public void FocusAddForm()
    {
        DetailsAddForm.Visibility = Visibility.Visible;
        DetailsAddForm.UpdateLayout();
        AddForm.BringIntoView();
        AddForm.FocusTarget();
    }

    private void ShowDetailsAddForm_Click(object sender, RoutedEventArgs e) => FocusAddForm();
    private void RowContextMenu_Opening(object sender, ContextMenuEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow owner) owner.RowContextMenu_Opening(sender, e);
    }
    private void CaptionTab_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow owner) owner.CaptionTab_MouseDown(sender, e);
    }
    private void CaptionTab_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow owner) owner.CaptionTab_KeyDown(sender, e);
    }
    private void CaptionTab_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow owner) owner.CaptionTab_LostKeyboardFocus(sender, e);
    }
}
