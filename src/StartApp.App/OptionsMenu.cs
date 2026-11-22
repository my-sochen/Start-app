using System;
using System.Windows;
using System.Windows.Controls;
using StartApp.Presentation;

namespace StartApp.App;

/// <summary>The אופציות menu (approved 21.11 19:11:13, eight rows in the owner's exact order): סביבה חדשה,
/// לכידה ▸ (סביבה קיימת / סביבה חדשה), הגדר סביבה כברירת מחדל (opens the approved radio picker), רשימה לבנה,
/// רשימה שחורה, רשימת אייטמים כללית (all three land on the סביבה גלובלית tab, pending the noted merge decision),
/// תוצאות פתיחה אחרונה (design-only until its screen exists) and יציאה.</summary>
public partial class MainWindow
{
    private void OptionsMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not MainViewModel vm) return;
        var menu = NewAnchoredMenu((Button)sender);

        menu.Items.Add(ActionItem("סביבה חדשה", () => NewWorkspaceFocus_Click(sender, e)));

        var capture = new MenuItem { Header = "לכידה" };
        capture.Items.Add(CommandItem("סביבה קיימת", vm.CaptureExistingCommand));
        capture.Items.Add(CommandItem("סביבה חדשה", vm.CaptureNewCommand));
        menu.Items.Add(capture);

        menu.Items.Add(ActionItem("הגדר סביבה כברירת מחדל", () => OpenDefaultEnvPicker(vm)));
        menu.Items.Add(CommandItem("רשימה לבנה", vm.OpenGlobalEnvCommand));
        menu.Items.Add(CommandItem("רשימה שחורה", vm.OpenGlobalEnvCommand));
        menu.Items.Add(CommandItem("רשימת אייטמים כללית", vm.OpenGlobalEnvCommand));
        // Design-only until the results screen exists (designer note 21.11 19:14: the new-feature rows are placeholders).
        menu.Items.Add(new MenuItem { Header = "תוצאות פתיחה אחרונה", IsEnabled = false });
        menu.Items.Add(ActionItem("יציאה", Close));

        menu.IsOpen = true;
    }

    /// <summary>אופציות > הגדר סביבה כברירת מחדל: the approved radio picker (options-default-env mock). Confirming
    /// persists the choice and moves the green badge; ביטול / ✕ leave the current default untouched.</summary>
    private void OpenDefaultEnvPicker(MainViewModel vm)
    {
        var picker = new DefaultEnvPickerWindow(vm) { Owner = this };
        if (picker.ShowDialog() == true)
            vm.SetDefaultWorkspace(picker.ChosenWorkspaceId);
    }

    private static MenuItem CommandItem(string header, System.Windows.Input.ICommand command) =>
        new() { Header = header, Command = command };

    private static MenuItem ActionItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }
}
