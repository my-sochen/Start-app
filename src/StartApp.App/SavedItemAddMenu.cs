using System.Windows;
using System.Windows.Controls;
using StartApp.Core;
using StartApp.Presentation;
namespace StartApp.App;

public partial class MainWindow
{
    private void SavedCaptureContextOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MainViewModel.CaptureExistingRow row } host && DataContext is MainViewModel vm)
        { e.Handled = true; ShowSavedTransferActions(host, vm.SavedTransferFromCapture(row)); }
    }
    internal void ShowSavedTransferActions(FrameworkElement anchor, SavedItemTransfer? source)
    {
        if (DataContext is not MainViewModel vm) return;
        var menu = NewAnchoredMenu(anchor); menu.MinWidth = 190; menu.FlowDirection = FlowDirection.RightToLeft;
        menu.Items.Add(BuildSavedItemAddMenu(vm, source)); menu.IsOpen = true;
    }
    private MenuItem BuildSavedItemAddMenu(MainViewModel vm, SavedItemTransfer? source)
    {
        var menu = new MenuItem { Header = "הוסף ל...", IsEnabled = source is not null && vm.SavedItemTransferAllowed };
        if (source is null) return menu;
        menu.Items.Add(ActionItem("סביבה חדשה", () => ShowItemDialog(new NewWorkspaceFromItemWindow(vm, source))));
        menu.Items.Add(ActionItem("סביבה נבחרת", () =>
        {
            var destinations = vm.SavedItemDestinations(source);
            if (destinations.Count == 0) { ShowItemDialog(SavedItemPromptWindow.Notice("אין סביבות זמינות", "כל הסביבות כבר מכילות אייטם באותו סוג וכתובת. אפשר ליצור סביבה חדשה.")); return; }
            var picker = new ItemWorkspacePickerWindow(destinations, source.Name, "share");
            if (ShowItemDialog(picker) == true) vm.AddSavedItemToWorkspaces(source, picker.ChosenWorkspaceIds);
        }));
        void Add(GlobalListKind kind)
        {
            if (vm.AddSavedItemToList(source, kind)) return;
            var conflict = vm.SavedItemListConflict;
            if (conflict.Length != 0 && ShowItemDialog(SavedItemPromptWindow.Conflict(conflict)) == true)
                vm.AddSavedItemToList(source, kind, confirmedMove: true);
        }
        menu.Items.Add(ActionItem("רשימה שחורה", () => Add(GlobalListKind.Blacklist)));
        menu.Items.Add(ActionItem("רשימה לבנה", () => Add(GlobalListKind.Whitelist)));
        menu.Items.Add(ActionItem("סביבה גלובלית", () => Add(GlobalListKind.GlobalItems)));
        return menu;
    }
}
