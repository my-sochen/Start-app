using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StartApp.Presentation;

namespace StartApp.App;

/// <summary>A row click chooses immediately; the caller handles any replacement or removal confirmation.</summary>
public partial class DefaultEnvPickerWindow : Window
{
    public sealed class Row
    {
        public string? WorkspaceId { get; init; }
        public string Name { get; init; } = "";
        public string Category { get; init; } = "";
        public string? ColorHex { get; init; }
        public bool IsLast { get; set; }
        public bool HasColor => ColorHex is { Length: > 0 };
    }

    public string? ChosenWorkspaceId { get; private set; }

    public DefaultEnvPickerWindow(MainViewModel vm)
    {
        InitializeComponent();
        var rows = new[] { new Row { Name = "ללא ברירת מחדל" } }
            .Concat(vm.Workspaces.Where(w => w.Id != vm.DefaultWorkspaceId).Select(w => new Row { WorkspaceId = w.Id, Name = w.Name, Category = w.EffectiveCategory, ColorHex = w.Color }))
            .ToList();
        rows[^1].IsLast = true;
        Rows.ItemsSource = rows;
        Rows.SelectedIndex = -1;
    }

    private void Choose(Row row)
    {
        ChosenWorkspaceId = row.WorkspaceId;
        DialogResult = true;
    }

    private void Rows_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(Rows, source) is ListBoxItem { DataContext: Row row })
        {
            e.Handled = true;
            Choose(row);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
        else if (Rows.IsKeyboardFocusWithin && (e.Key is Key.Enter or Key.Space))
        {
            var row = Rows.SelectedItem as Row;
            if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(Rows, source) is ListBoxItem { DataContext: Row focused }) row = focused;
            if (row is null) return;
            e.Handled = true;
            Choose(row);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
