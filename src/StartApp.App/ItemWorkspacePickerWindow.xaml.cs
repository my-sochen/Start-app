using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using StartApp.Core;
namespace StartApp.App;
public partial class ItemWorkspacePickerWindow : Window
{
    private readonly bool _plainDelete;
    private sealed record Choice(string Id, string Name, string EffectiveCategory, string? Color, int Count, bool IsLast) { public string DisplayCategory => EffectiveCategory + " · " + (Count == 1 ? "מופע אחד" : Count + " מופעים"); }
    private readonly string _action;
    public IReadOnlyList<string> ChosenWorkspaceIds { get; private set; } = System.Array.Empty<string>();
    public ItemWorkspacePickerWindow(IReadOnlyList<Workspace> workspaces, string itemName, string action, IReadOnlyDictionary<string, int>? counts = null)
    {
        _action = action;
        InitializeComponent();
        _plainDelete = action == "delete" && workspaces.Count == 1;
        Breadcrumb.Text = "אייטמים · " + itemName + " · " + (action switch { "delete" => "מחיקה", "share" => "הוספה", "openwith" => "פתיחה", _ => "עריכה" });
        Heading.Text = action switch { "delete" => _plainDelete ? "למחוק את האייטם?" : "מאילו סביבות למחוק?", "share" => "לאילו סביבות להוסיף?", "openwith" => "באיזו סביבה לפתוח באמצעות?", _ => "באילו סביבות לערוך?" };
        Explanation.Text = itemName + (action switch { "delete" => _plainDelete ? ": מחיקה מהסביבה " + workspaces[0].Name + " · " + (counts?.GetValueOrDefault(workspaces[0].Id) ?? 1) + " מופעים." : ": סמנו את הסביבות שמהן יימחק האייטם. סביבות שלא סומנו לא ישתנו.", "share" => ": סמנו את הסביבות שאליהן יתווסף האייטם.", "openwith" => ": בחרו סביבה אחת לשימוש באפשרויות הפתיחה שלה.", _ => " שייך לכמה סביבות. בחרו את הסביבות שבהן תרצו לערוך אותו. השינוי יחול רק על הסביבות שנבחרו. אותו דיאלוג נפתח גם לפני מחיקה." });
        ConfirmButton.Content = action switch { "delete" => "מחק", "share" => "הוסף לסביבות שסומנו", "openwith" => "המשך", _ => "המשך לעריכה" };
        if (action == "openwith") Rows.SelectionMode = SelectionMode.Single;
        Rows.ItemsSource = workspaces.Select((w, index) => new Choice(w.Id, w.Name, w.EffectiveCategory, w.Color, counts?.GetValueOrDefault(w.Id) ?? 1, index == workspaces.Count - 1)).ToArray();
        Rows.SelectedIndex = -1;
        if (_plainDelete) { Rows.Visibility = Visibility.Collapsed; SelectionSummary.Visibility = Visibility.Collapsed; ConfirmButton.IsEnabled = true; ChosenWorkspaceIds = workspaces.Select(w => w.Id).ToArray(); }
    }
    private void Rows_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (ConfirmButton is not null) { ConfirmButton.IsEnabled = _plainDelete || Rows.SelectedItems.Count > 0; if (SelectionSummary is not null) SelectionSummary.Text = _action == "share" ? $"{Rows.SelectedItems.Count} סביבות נבחרו" : $"{Rows.SelectedItems.Count} סביבות · {Rows.SelectedItems.Cast<Choice>().Sum(r => r.Count)} מופעים נבחרו"; } }
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!_plainDelete) ChosenWorkspaceIds = Rows.SelectedItems.Cast<Choice>().Select(w => w.Id).ToArray();
        if (ChosenWorkspaceIds.Count > 0) DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
