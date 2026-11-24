using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StartApp.Presentation;
namespace StartApp.App;
public partial class AllItemsView : UserControl
{
    public AllItemsView() { InitializeComponent(); }
    private void ItemContextOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MainViewModel.AllItemRow row } element && Window.GetWindow(this) is MainWindow window && FindButton(element) is { } button)
        { e.Handled = true; window.ShowAllItemActions(button, row); }
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
    internal void ShowAllItemActions(Button target, MainViewModel.AllItemRow row) => ShowItemActions(target, row, null);
    private bool? ShowItemDialog(Window dialog)
    {
        dialog.Owner = this;
        var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer((UIElement)Content);
        var shade = new ItemPickerShade((UIElement)Content); layer?.Add(shade);
        try { return dialog.ShowDialog(); } finally { layer?.Remove(shade); }
    }
    private static Button? FindItemActionButton(DependencyObject root)
    {
        if (root is Button button) return button;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) if (FindItemActionButton(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }
    private void ShowItemActions(Button target, MainViewModel.AllItemRow row, ItemRow? detail)
    {
        if (DataContext is not MainViewModel vm) return;
        var menu = NewAnchoredMenu(target); menu.MinWidth = 190; menu.FlowDirection = FlowDirection.RightToLeft;
        menu.Items.Add(new MenuItem { Header = "פתח", Command = detail is null ? vm.OpenAllItemCommand : vm.OpenItemCommand, CommandParameter = detail is null ? row : detail });
        menu.Items.Add(ActionItem("העתק כתובת", () => Clipboard.SetText(row.Target)));
        void Act(string action)
        {
            if (action == "edit" && detail is not null)
            {
                if (vm.EditItemCommand.CanExecute(detail)) vm.EditItemCommand.Execute(detail);
                return;
            }
            var workspaces = vm.ItemActionWorkspaces(row, action == "share");
            if (workspaces.Count == 0) return;
            IReadOnlyList<string> ids = workspaces.Select(w => w.Id).ToArray();
            if (workspaces.Count > 1 || action is "delete" or "share")
            {
                var counts = vm.ItemActionTargets(row, workspaces.Select(w => w.Id).ToArray()).GroupBy(t => t.WorkspaceId).ToDictionary(g => g.Key, g => g.Count());
                var picker = new ItemWorkspacePickerWindow(workspaces, row.Name, action, counts);
                if (ShowItemDialog(picker) != true) return;
                ids = picker.ChosenWorkspaceIds;
            }
            if (action == "share") { vm.AddItemToEnvironments(row, ids); return; }
            var targets = vm.ItemActionTargets(row, ids);
            if (targets.Count == 0) return;
            if (action == "delete") { vm.DeleteSelectedItem(row, targets); return; }
            if (action == "openwith") { vm.OpenItemWithInEnvironment(row, ids[0]); return; }
            ShowItemDialog(new ItemBatchEditWindow(vm, row, targets));
        }
        var edit = ActionItem("ערוך", () => Act("edit")); edit.IsEnabled = !vm.ReadOnly; menu.Items.Add(edit);
        if (detail is not null) menu.Items.Add(new MenuItem { Header = "עריכת כתובת", Command = vm.EditAddressCommand, CommandParameter = detail });
        if (row.Type != "Program" && !row.MixedTypes)
        {
            if (detail is not null) menu.Items.Add(new MenuItem { Header = "פתח באמצעות", Command = vm.OpenWithCommand, CommandParameter = detail });
            else {
                var openWith = ActionItem("פתח באמצעות", () => Act("openwith"));
                openWith.IsEnabled = row.Type != "Folder" && !vm.ReadOnly;
                menu.Items.Add(openWith);
            }
        }
        var source = detail is null ? vm.SavedTransferFromAll(row) : vm.SavedTransferFromDetail(detail);
        menu.Items.Add(BuildSavedItemAddMenu(vm, source));
        if (detail is not null)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "הזז למעלה", Command = vm.MoveItemUpCommand, CommandParameter = detail });
            menu.Items.Add(new MenuItem { Header = "הזז למטה", Command = vm.MoveItemDownCommand, CommandParameter = detail });
        }
        menu.Items.Add(new Separator()); var remove = ActionItem("מחק", () => Act("delete")); remove.IsEnabled = !vm.ReadOnly; menu.Items.Add(remove);
        menu.IsOpen = true;
    }

}

internal sealed class ItemPickerShade : System.Windows.Documents.Adorner
{
    public ItemPickerShade(UIElement element) : base(element) { IsHitTestVisible = false; }
    protected override void OnRender(DrawingContext drawingContext) => drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(80, 44, 58, 82)), null, new Rect(AdornedElement.RenderSize));
}
