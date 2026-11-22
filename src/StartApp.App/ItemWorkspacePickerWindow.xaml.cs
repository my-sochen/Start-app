using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using StartApp.Core;
namespace StartApp.App;
public partial class ItemWorkspacePickerWindow : Window
{
    private sealed record Row(string WorkspaceId, string Name, string Category, string? ColorHex) { public bool HasColor => !string.IsNullOrEmpty(ColorHex); }
    public string? ChosenWorkspaceId { get; private set; }
    public ItemWorkspacePickerWindow(IReadOnlyList<Workspace> workspaces, string itemName, bool delete)
    {
        InitializeComponent();
        Heading.Text = delete ? "מאיזו סביבה למחוק?" : "באיזו סביבה לערוך?";
        Explanation.Text = itemName + (delete ? ": המחיקה מסירה את האייטם מהסביבה הנבחרת בלבד." : ": בחרו את הסביבה שאת האייטם שלה תרצו לערוך.");
        ConfirmButton.Content = delete ? "מחק מהסביבה הנבחרת" : "המשך לעריכה";
        Rows.ItemsSource = workspaces.Select(w => new Row(w.Id, w.Name, w.EffectiveCategory, w.Color)).ToArray();
        Rows.SelectedIndex = -1;
    }
    private void Rows_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (ConfirmButton is not null) ConfirmButton.IsEnabled = Rows.SelectedItem is Row; }
    private void Confirm_Click(object sender, RoutedEventArgs e) { if (Rows.SelectedItem is not Row row) return; ChosenWorkspaceId = row.WorkspaceId; DialogResult = true; }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
