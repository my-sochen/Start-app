using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using StartApp.Presentation;

namespace StartApp.App;

/// <summary>One native notification icon and WPF menu per main window. NotifyIcon restores itself after Explorer's
/// TaskbarCreated message. Workspace entries are rebuilt on demand, never retain deleted/renamed model objects.</summary>
internal sealed class SystemTraySession : IDisposable
{
    private readonly MainWindow _window;
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _art;
    private ContextMenu? _menu;
    public SystemTraySession(MainWindow window)
    {
        _window = window;
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/StartApp.App;component/Assets/start-app-ufo.ico")).Stream;
        _art = new System.Drawing.Icon(stream);
        _icon = new() { Icon = _art, Text = "Start App", Visible = true };
        _icon.MouseUp += MouseUp;
        _icon.DoubleClick += DoubleClick;
    }
    private void DoubleClick(object? sender, EventArgs e) => _window.Dispatcher.BeginInvoke(Restore);
    private void MouseUp(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (e.Button == System.Windows.Forms.MouseButtons.Left) _window.Dispatcher.BeginInvoke(Restore);
        else if (e.Button == System.Windows.Forms.MouseButtons.Right) _window.Dispatcher.BeginInvoke(ShowMenu);
    }
    internal void Restore()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }
    internal ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { FlowDirection = FlowDirection.RightToLeft, Placement = PlacementMode.MousePoint };
        menu.Items.Add(ActionMenu.Action("פתח את Start App", Restore));
        var environments = new MenuItem { Header = "שגר סביבה" };
        if (_window.DataContext is MainViewModel vm)
        {
            foreach (var workspace in vm.Workspaces)
            {
                var id = workspace.Id;
                var choice = new MenuItem { Header = workspace.Name.Replace("_", "__"), IsEnabled = CanLaunch(vm, workspace) };
                choice.Click += (_, _) =>
                {
                    Restore();
                    var current = vm.Workspaces.FirstOrDefault(w => w.Id == id);
                    if (current is not null && CanLaunch(vm, current)) vm.OpenWorkspaceForCommand.Execute(current);
                };
                environments.Items.Add(choice);
            }
        }
        if (environments.Items.Count == 0) environments.Items.Add(new MenuItem { Header = "אין סביבות שמורות", IsEnabled = false });
        menu.Items.Add(environments); menu.Items.Add(new Separator());
        menu.Items.Add(ActionMenu.Action("יציאה מלאה", () => { Restore(); _window.RequestTrayExit(); }));
        return menu;
    }
    private static bool CanLaunch(MainViewModel vm, StartApp.Core.Workspace workspace) =>
        !vm.ReadOnly && !vm.CreateDirty && !vm.DetailsDirty && !vm.CaptureOpen && !vm.ItemEditOpen &&
        !vm.AddressEditOpen && !vm.DesignEditOpen && !vm.UnsavedOpen && vm.OpenWorkspaceForCommand.CanExecute(workspace);
    internal void ShowMenu()
    {
        if (_menu is not null) _menu.IsOpen = false;
        _menu = BuildMenu(); _menu.IsOpen = true;
    }
    public void Dispose()
    {
        if (_menu is not null) _menu.IsOpen = false;
        _icon.MouseUp -= MouseUp; _icon.DoubleClick -= DoubleClick; _icon.Visible = false; _icon.Dispose(); _art.Dispose();
    }
}
