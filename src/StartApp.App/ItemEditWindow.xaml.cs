using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using StartApp.Presentation;

namespace StartApp.App;

public partial class ItemEditWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly string _name, _description, _target, _program;
    private bool _allowClose;

    public ItemEditWindow(MainViewModel vm)
    {
        _vm = vm;
        _name = vm.EditItemName;
        _description = vm.EditItemDescription;
        _target = vm.EditItemTarget;
        _program = vm.EditItemOpenWith;
        InitializeComponent();
        DataContext = vm;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        CaptionLabel.Text = "פרטי הסביבה · " + _name + " · עריכה";
        TitleLabel.Text = "עריכת אייטם: " + _name;
        ContentRendered += (_, _) => NameInput.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (OpenWithInput.IsDropDownOpen) { OpenWithInput.IsDropDownOpen = false; return; }
            if (UnsavedShade.Visibility == Visibility.Visible) { UnsavedShade.Visibility = Visibility.Collapsed; NameInput.Focus(); return; }
            Close();
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        UnsavedShade.Visibility = Visibility.Collapsed;
        if (_vm.SaveItemEditCommand.CanExecute(null)) _vm.SaveItemEditCommand.Execute(null);
        if (_vm.ItemEditOpen) return;
        _allowClose = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose && _vm.ItemEditDirty)
        {
            e.Cancel = true;
            UnsavedShade.Visibility = Visibility.Visible;
            KeepEditingButton.Focus();
            return;
        }
        if (_vm.CancelItemEditCommand.CanExecute(null)) _vm.CancelItemEditCommand.Execute(null);
    }

    private void KeepEditing_Click(object sender, RoutedEventArgs e) { UnsavedShade.Visibility = Visibility.Collapsed; NameInput.Focus(); }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        // Discard only this dialog's draft. The existing command closes the captured editor identity.
        _vm.EditItemName = _name;
        _vm.EditItemDescription = _description;
        _vm.EditItemTarget = _target;
        _vm.EditItemOpenWith = _program;
        _allowClose = true;
        Close();
    }
}
