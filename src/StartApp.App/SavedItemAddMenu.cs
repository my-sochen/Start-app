using System.Windows;
using System.Windows.Controls;
using StartApp.Core;
using StartApp.Presentation;
namespace StartApp.App;

public partial class MainWindow
{
    internal void ShowSavedTransferActions(FrameworkElement anchor, SavedItemTransfer? source)
    {
        if (DataContext is not MainViewModel vm) return;
        var menu = NewAnchoredMenu(anchor); menu.MinWidth = 190; menu.FlowDirection = FlowDirection.RightToLeft;
        menu.Items.Add(BuildSavedItemAddMenu(vm, source)); menu.IsOpen = true;
    }
    private MenuItem BuildSavedItemAddMenu(MainViewModel vm, SavedItemTransfer? source)
    {
        var menu = new MenuItem { Header = "הוסף ל...", Tag = "saved-add-submenu", IsEnabled = source is not null && vm.SavedItemTransferAllowed };
        if (source is null) return menu;
        menu.Items.Add(ActionItem("סביבה חדשה", () => ShowItemDialog(new NewWorkspaceFromItemWindow(vm, source))));
        var selectedEnvironment = ActionItem("סביבה נבחרת", () =>
        {
            var destinations = vm.SavedItemDestinations(source);
            if (destinations.Count == 0) { ShowItemDialog(SavedItemPromptWindow.Notice("אין סביבות זמינות", "אין סביבות קיימות. אפשר ליצור סביבה חדשה.")); return; }
            var picker = new ItemWorkspacePickerWindow(destinations, source.Name, "share");
            if (ShowItemDialog(picker) == true) vm.AddSavedItemToWorkspaces(source, picker.ChosenWorkspaceIds);
        });
        selectedEnvironment.IsEnabled = vm.SavedItemDestinations(source).Count > 0;
        menu.Items.Add(selectedEnvironment);
        void Add(GlobalListKind kind)
        {
            if (vm.AddSavedItemToList(source, kind)) return;
            var conflict = vm.SavedItemListConflict;
            if (conflict.Length != 0 && ShowItemDialog(SavedItemPromptWindow.Conflict(conflict)) == true)
                vm.AddSavedItemToList(source, kind, confirmedMove: true);
        }
        void AddDestination(string label, GlobalListKind kind)
        {
            var destination = ActionItem(label, () => Add(kind));
            destination.IsEnabled = !vm.SavedItemIsSourceList(source, kind);
            menu.Items.Add(destination);
        }
        AddDestination("רשימה שחורה", GlobalListKind.Blacklist);
        AddDestination("רשימה לבנה", GlobalListKind.Whitelist);
        AddDestination("סביבה גלובלית", GlobalListKind.GlobalItems);
        return menu;
    }
}
