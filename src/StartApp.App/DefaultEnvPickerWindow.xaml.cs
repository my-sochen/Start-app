using System.Collections.Generic;
using System.Linq;
using System.Windows;
using StartApp.Presentation;

namespace StartApp.App;

/// <summary>אופציות > הגדר סביבה כברירת מחדל picker (options-default-env-f1, approved 22.11 03:03): one radio row per
/// workspace (color dot, name, category) plus "ללא סביבת ברירת מחדל" first. The current default starts selected;
/// choosing another row only stages it — אישור commits (the caller persists), ביטול / ✕ close without saving.</summary>
public partial class DefaultEnvPickerWindow : Window
{
    public sealed class Row
    {
        public string? WorkspaceId { get; init; }
        public string Name { get; init; } = "";
        public string Category { get; init; } = "";
        public string? ColorHex { get; init; }
        public bool HasColor => ColorHex is { Length: > 0 };
    }

    /// <summary>The staged choice when the dialog closes with אישור: the workspace id, or null for "ללא סביבת ברירת מחדל".</summary>
    public string? ChosenWorkspaceId { get; private set; }

    public DefaultEnvPickerWindow(MainViewModel vm)
    {
        InitializeComponent();
        var rows = new[] { new Row { WorkspaceId = null, Name = "ללא ברירת מחדל" } }
            .Concat(vm.Workspaces.Select(w => new Row { WorkspaceId = w.Id, Name = w.Name, Category = w.EffectiveCategory, ColorHex = w.Color }))
            .ToList();
        Rows.ItemsSource = rows;
        Rows.SelectedItem = rows.FirstOrDefault(r => r.WorkspaceId == vm.DefaultWorkspaceId) ?? rows[0];
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        ChosenWorkspaceId = (Rows.SelectedItem as Row)?.WorkspaceId;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
