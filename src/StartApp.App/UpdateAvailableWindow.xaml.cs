using System.Windows;
using System.Windows.Input;
using StartApp.Presentation;

namespace StartApp.App;

public partial class UpdateAvailableWindow : Window
{
    public UpdateAvailableWindow(UpdateViewModel updates)
    {
        InitializeComponent();
        DataContext = updates;
        // MainWindow owns the updater. Dismissal does not dispose or install.
    }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
