using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StartApp.Presentation;
namespace StartApp.App;
public partial class AllItemsView : UserControl
{
    public AllItemsView() { InitializeComponent(); }
    private void RowActions(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MainViewModel.AllItemRow row } element && Window.GetWindow(this) is MainWindow window)
        {
            var button = FindButton(element);
            if (button is not null) { window.ShowAllItemActions(button, row); e.Handled = true; }
        }
    }
    private void RowKeys(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Apps && !(e.Key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))) return;
        if (sender is FrameworkElement { DataContext: MainViewModel.AllItemRow row } element && Window.GetWindow(this) is MainWindow window && FindButton(element) is { } button)
        { window.ShowAllItemActions(button, row); e.Handled = true; }
    }
    private static Button? FindButton(DependencyObject root)
    {
        if (root is Button b) return b;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindButton(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }
}
public partial class MainWindow
{
    internal void ShowAllItemActions(Button target, MainViewModel.AllItemRow row)
    {
        if (DataContext is not MainViewModel vm) return;
        var menu = NewAnchoredMenu(target); menu.MinWidth = 170;
        menu.Items.Add(new MenuItem { Header = "פתח", Command = vm.OpenAllItemCommand, CommandParameter = row });
        void Act(bool delete)
        {
            var workspaces = vm.AllItemWorkspaces(row);
            if (workspaces.Count == 0) return;
            var workspace = workspaces[0];
            if (workspaces.Count > 1)
            {
                var picker = new ItemWorkspacePickerWindow(workspaces, row.Name, delete) { Owner = this };
                var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer((UIElement)Content);
                var shade = new ItemPickerShade((UIElement)Content);
                layer?.Add(shade);
                bool? confirmed;
                try { confirmed = picker.ShowDialog(); } finally { layer?.Remove(shade); }
                if (confirmed != true) return;
                workspace = workspaces.FirstOrDefault(w => w.Id == picker.ChosenWorkspaceId);
                if (workspace is null) return;
            }
            vm.ActOnAllItem(row, workspace, delete);
        }
        var edit = ActionItem("ערוך", () => Act(false)); edit.IsEnabled = !vm.ReadOnly; menu.Items.Add(edit);
        menu.Items.Add(new Separator());
        var remove = ActionItem("מחק", () => Act(true)); remove.IsEnabled = !vm.ReadOnly; menu.Items.Add(remove);
        menu.IsOpen = true;
    }
}

internal sealed class ItemPickerShade : System.Windows.Documents.Adorner
{
    public ItemPickerShade(UIElement element) : base(element) { IsHitTestVisible = false; }
    protected override void OnRender(DrawingContext drawingContext) => drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(80, 44, 58, 82)), null, new Rect(AdornedElement.RenderSize));
}
