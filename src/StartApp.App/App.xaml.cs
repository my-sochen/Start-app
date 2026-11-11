using System.IO;
using System.Windows;
using StartApp.Presentation;
using StartApp.Storage.Json;

namespace StartApp.App;

public partial class App : Application
{
    // Composition root: the only place that wires concrete implementations to the Core ports.
    private JsonStore? _store;
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StartApp");
        _store = new JsonStore(Path.Combine(dir, "store.json"));
        var vm = new MainViewModel(_store, new NotImplementedLauncher());
        new MainWindow { DataContext = vm }.Show();
    }
    protected override void OnExit(ExitEventArgs e) { _store?.Dispose(); base.OnExit(e); }
}
