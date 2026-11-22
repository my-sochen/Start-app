using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StartApp.Presentation;
namespace StartApp.App;
public partial class ItemBatchEditWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly MainViewModel.AllItemRow _row;
    private readonly IReadOnlyList<MainViewModel.ItemActionTarget> _targets;
    private readonly string _name, _description, _target, _program;
    private bool _ready, _allowClose;
    public ItemBatchEditWindow(MainViewModel vm, MainViewModel.AllItemRow row, IReadOnlyList<MainViewModel.ItemActionTarget> targets)
    {
        _vm = vm; _row = row; _targets = targets;
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        var names = targets.Select(t => t.Name).Distinct().ToArray(); var descriptions = targets.Select(t => t.Description).Distinct().ToArray();
        _name = names.Length == 1 ? names[0] : ""; _description = descriptions.Length == 1 ? descriptions[0] : ""; _target = row.Target;
        var programs = targets.Select(t => t.Launch?.Browser ?? t.Launch?.OpenWith ?? "").Distinct().ToArray(); _program = programs.Length == 1 ? programs[0] : "";
        if (row.Type == "Folder")
        {
            OpenWithPicker.SetDefaultLabel(OpenWithInput, "ללא");
            ClearLaunch.Content = "ללא תוכנה חלופית בכל הסביבות שנבחרו";
        }
        if (row.Type == "Program") { OpenWithField.Visibility = Visibility.Collapsed; Grid.SetColumn(NameField, 0); Grid.SetColumnSpan(NameField, 3); }
        OpenWithInput.IsEnabled = ClearLaunch.IsEnabled = row.Type != "Program"; OpenWithPicker.SetPath(OpenWithInput, _program);
        OpenWithInput.ToolTip = row.Type == "Program" ? "לא חל על אייטם תוכנה: האייטם הוא התוכנה עצמה" : "בחר תוכנה מותקנת או תוכנה אחרת. ברירת המחדל של Windows אינה משנה את הגדרות המחשב.";
        if (row.Type == "Folder") OpenWithInput.ToolTip = "ללא תוכנה חלופית: התיקייה נפתחת כרגיל דרך Windows. אפשר לבחור תוכנה אחרת.";
        ClearLaunch.Visibility = programs.Length > 1 && row.Type != "Program" ? Visibility.Visible : Visibility.Collapsed;
        ClearDescription.Visibility = descriptions.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        TargetLabel.Text = row.Type == "Link" ? "כתובת האתר" : "נתיב";
        TitleLabel.Text = "עריכת אייטם: " + row.Name; CaptionLabel.Text = "אייטמים · " + row.Name + " · עריכה";
        NameLabel.Text = names.Length > 1 ? "שם האייטם (ערכים שונים)" : "שם האייטם";
        DescriptionLabel.Text = descriptions.Length > 1 ? "תיאור (ערכים שונים)" : "תיאור";
        Scope.Inlines.Add(new System.Windows.Documents.Run("טופס אחד לכל הסביבות שנבחרו: "));
        var affectedWorkspaces = row.Workspaces.Where(w => targets.Any(t => t.WorkspaceId == w.Id)).ToArray();
        foreach (var workspace in affectedWorkspaces)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = ColorBrushConverter.BrushFrom(workspace.Color ?? "#A4ADBD"), Margin = new Thickness(5, 0, 3, 0) };
            Scope.Inlines.Add(new System.Windows.Documents.InlineUIContainer(dot) { BaselineAlignment = BaselineAlignment.Center });
            Scope.Inlines.Add(new System.Windows.Documents.Run(workspace.Name + (workspace == affectedWorkspaces[^1] ? ". " : " · ")));
        }
        Scope.Inlines.Add(new System.Windows.Documents.Run("העריכה תחול רק על העותקים הזהים לחלוטין בסביבות שנבחרו; עותק ששונה בו אפילו פרט אחד לא ישתנה. רק שדות שתשנה יוחלו - מה שלא תיגע בו יישאר כמו שהוא בכל סביבה."));
        if (targets.Select(t => t.WorkspaceId).Distinct().Count() < 2) Scope.Visibility = Visibility.Collapsed;
        if (row.Type == "Link") OpenWithPicker.SetWebsiteTarget(OpenWithInput, row.Target);
        NameInput.Text = _name; DescriptionInput.Text = _description; TargetInput.Text = _target; _ready = true;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (OpenWithInput.IsDropDownOpen) { OpenWithInput.IsDropDownOpen = false; return; }
            if (UnsavedShade.Visibility == Visibility.Visible) { UnsavedShade.Visibility = Visibility.Collapsed; NameInput.Focus(); return; }
            Close();
        };
    }
    private bool Dirty => _ready && (NameInput.Text != _name || DescriptionInput.Text != _description || TargetInput.Text != _target || ClearDescription.IsChecked == true || OpenWithPicker.SelectedPath(OpenWithInput) != _program || ClearLaunch.IsChecked == true);
    private void Launch_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) SaveButton.IsEnabled = Dirty; }
    private void Input_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        if (_row.Type == "Link" && ReferenceEquals(sender, TargetInput)) OpenWithPicker.SetWebsiteTarget(OpenWithInput, TargetInput.Text);
        SaveButton.IsEnabled = Dirty;
    }
    private void ClearDescription_Changed(object sender, RoutedEventArgs e) { if (!_ready) return; DescriptionInput.IsEnabled = ClearDescription.IsChecked != true; SaveButton.IsEnabled = Dirty; }
    private void ClearLaunch_Changed(object sender, RoutedEventArgs e) { if (!_ready) return; OpenWithInput.IsEnabled = ClearLaunch.IsChecked != true && _row.Type != "Program"; SaveButton.IsEnabled = Dirty; }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.SaveSelectedItemEdits(_row, _targets, NameInput.Text == _name ? null : NameInput.Text, ClearDescription.IsChecked == true ? "" : DescriptionInput.Text == _description ? null : DescriptionInput.Text, TargetInput.Text == _target ? null : TargetInput.Text, ClearLaunch.IsChecked == true || OpenWithPicker.SelectedPath(OpenWithInput) != _program, ClearLaunch.IsChecked == true ? "" : OpenWithPicker.SelectedPath(OpenWithInput)))
        { Error.Text = _vm.Status; UnsavedShade.Visibility = Visibility.Collapsed; return; }
        _allowClose = true; DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, CancelEventArgs e) { if (!_allowClose && Dirty) { e.Cancel = true; UnsavedShade.Visibility = Visibility.Visible; KeepEditingButton.Focus(); } }
    private void KeepEditing_Click(object sender, RoutedEventArgs e) { UnsavedShade.Visibility = Visibility.Collapsed; NameInput.Focus(); }
    private void Discard_Click(object sender, RoutedEventArgs e) { _allowClose = true; Close(); }
}
