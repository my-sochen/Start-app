using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using StartApp.Presentation;
namespace StartApp.App;
public partial class ItemBatchEditWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly MainViewModel.AllItemRow _row;
    private readonly IReadOnlyList<MainViewModel.ItemActionTarget> _targets;
    private readonly string _name, _description, _target;
    private bool _ready, _allowClose;
    public ItemBatchEditWindow(MainViewModel vm, MainViewModel.AllItemRow row, IReadOnlyList<MainViewModel.ItemActionTarget> targets)
    {
        _vm = vm; _row = row; _targets = targets;
        InitializeComponent();
        var names = targets.Select(t => t.Name).Distinct().ToArray(); var descriptions = targets.Select(t => t.Description).Distinct().ToArray();
        _name = names.Length == 1 ? names[0] : ""; _description = descriptions.Length == 1 ? descriptions[0] : ""; _target = row.Target;
        NameLabel.Text = names.Length > 1 ? "שם, ערכים שונים: ללא שינוי כל עוד השדה ריק" : "שם";
        DescriptionLabel.Text = descriptions.Length > 1 ? "תיאור, ערכים שונים: שדה שלא שונה יישאר כפי שהיה" : "תיאור";
        Scope.Text = "סביבות שנבחרו: " + string.Join(" · ", targets.Select(t => t.WorkspaceName).Distinct()) + " · " + targets.Count + " מופעים";
        NameInput.Text = _name; DescriptionInput.Text = _description; TargetInput.Text = _target; _ready = true;
    }
    private bool Dirty => _ready && (NameInput.Text != _name || DescriptionInput.Text != _description || TargetInput.Text != _target || ClearDescription.IsChecked == true);
    private void Input_Changed(object sender, TextChangedEventArgs e) { if (_ready) SaveButton.IsEnabled = Dirty; }
    private void ClearDescription_Changed(object sender, RoutedEventArgs e) { if (!_ready) return; DescriptionInput.IsEnabled = ClearDescription.IsChecked != true; SaveButton.IsEnabled = Dirty; }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.SaveSelectedItemEdits(_row, _targets, NameInput.Text == _name ? null : NameInput.Text, ClearDescription.IsChecked == true ? "" : DescriptionInput.Text == _description ? null : DescriptionInput.Text, TargetInput.Text == _target ? null : TargetInput.Text))
        { Error.Text = _vm.Status; UnsavedShade.Visibility = Visibility.Collapsed; return; }
        _allowClose = true; DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, CancelEventArgs e) { if (!_allowClose && Dirty) { e.Cancel = true; UnsavedShade.Visibility = Visibility.Visible; } }
    private void KeepEditing_Click(object sender, RoutedEventArgs e) => UnsavedShade.Visibility = Visibility.Collapsed;
    private void Discard_Click(object sender, RoutedEventArgs e) { _allowClose = true; Close(); }
}
