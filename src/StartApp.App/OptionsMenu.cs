using System;
using System.Windows;
using System.Windows.Controls;
using StartApp.Presentation;

namespace StartApp.App;

/// <summary>Workspace creation, capture, default selection, global environment and exit.</summary>
public partial class MainWindow
{
    private void OptionsMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not MainViewModel vm) return;
        var menu = NewAnchoredMenu((Button)sender);

        menu.MinWidth = 244;
        menu.Padding = new Thickness(6);
        menu.Resources["MenuIconColumnWidth"] = new GridLength(12);
        var optionItemStyle = new Style(typeof(MenuItem), (Style)FindResource(typeof(MenuItem)));
        optionItemStyle.Setters.Add(new Setter(MinHeightProperty, 36d));
        menu.Resources[typeof(MenuItem)] = optionItemStyle;

        var capture = new MenuItem { Header = "לכידה" };
        capture.Items.Add(CommandItem("סביבה קיימת", vm.CaptureExistingCommand));
        capture.Items.Add(CommandItem("סביבה חדשה", vm.CaptureNewCommand));
        menu.Items.Add(capture);
        menu.Items.Add(ActionItem("סביבה חדשה", () => NewWorkspaceFocus_Click(sender, e)));

        menu.Items.Add(ActionItem("הגדר סביבה כברירת מחדל", () => OpenDefaultEnvPicker(vm)));
        menu.Items.Add(CommandItem("אייטמים", vm.OpenAllItemsCommand));
        menu.Items.Add(CommandItem("סביבה גלובלית", vm.OpenGlobalEnvCommand));
        // Design-only until the results screen exists (designer note 21.11 19:14: the new-feature rows are placeholders).
        menu.Items.Add(new MenuItem { Header = "תוצאות פתיחה אחרונה", IsEnabled = false });
        menu.Items.Add(new Separator());
        menu.Items.Add(ActionItem("יציאה", RequestTrayExit));

        menu.IsOpen = true;
    }

    /// <summary>The picker chooses on row activation. The view model confirms replacement or removal.</summary>
    private void OpenDefaultEnvPicker(MainViewModel vm)
    {
        var picker = new DefaultEnvPickerWindow(vm) { Owner = this };
        picker.ContentRendered += (_, _) => OptionsCaptionButton.FocusVisualStyle = null;
        bool? chosen;
        DefaultPickerShade.Visibility = Visibility.Visible;
        try { chosen = picker.ShowDialog(); }
        finally
        {
            DefaultPickerShade.Visibility = Visibility.Collapsed;
            OptionsCaptionButton.ClearValue(Control.FocusVisualStyleProperty);
        }
        if (chosen == true) vm.SetDefaultWorkspace(picker.ChosenWorkspaceId);
    }

    private static MenuItem CommandItem(string header,System.Windows.Input.ICommand command)=>ActionMenu.Command(header,command);
    private static MenuItem ActionItem(string header,Action action)=>ActionMenu.Action(header,action);
}
