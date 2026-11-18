using System.Windows;
using System.Windows.Input;
using StartApp.Presentation;

namespace StartApp.App;

public partial class UpdateSettingsWindow : Window
{
    public UpdateSettingsWindow(UpdateViewModel updates)
    {
        InitializeComponent();
        DataContext = updates;
        // MainWindow owns this updater. Closing settings must not cancel or
        // dispose a shared check or affect workspace data/unsaved changes.
    }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
