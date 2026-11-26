using System.Runtime.InteropServices;
using System.Windows.Interop;
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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? windowName);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    internal bool CanHide => FindWindow("Shell_TrayWnd", null) != IntPtr.Zero;
    private readonly MainWindow _window;
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _art;
    private ContextMenu? _menu;
    private bool _disposed;
    public SystemTraySession(MainWindow window)
    {
        _window = window;
        _art = LoadIcon(Application.GetResourceStream(new Uri("pack://application:,,,/StartApp.App;component/Assets/start-app-ufo.ico")));
        _icon = new() { Icon = _art, Text = "Start App", Visible = true };
        _icon.MouseUp += MouseUp;
        _icon.DoubleClick += DoubleClick;
    }
    internal static System.Drawing.Icon LoadIcon(System.Windows.Resources.StreamResourceInfo? resource)
    {
        using var stream = (resource ?? throw new System.IO.IOException("Tray icon resource is missing")).Stream;
        return new System.Drawing.Icon(stream);
    }
    private void DoubleClick(object? sender, EventArgs e) => _window.Dispatcher.BeginInvoke(Restore);
    private void MouseUp(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (e.Button == System.Windows.Forms.MouseButtons.Left) _window.Dispatcher.BeginInvoke(Restore);
        else if (e.Button == System.Windows.Forms.MouseButtons.Right) _window.Dispatcher.BeginInvoke(ShowMenu);
    }
    internal void Restore()
    {
        if (_disposed) return; // Native callbacks already queued before a full exit must not reopen a closed Window.
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }
    internal ContextMenu BuildMenu()
    {
        var menu = new ContextMenu { FlowDirection = FlowDirection.RightToLeft, Placement = PlacementMode.MousePoint, MaxHeight = SystemParameters.WorkArea.Height - 16 };
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
        if (_disposed) return;
        if (_menu is not null) _menu.IsOpen = false;
        // Give a notification-area popup the foreground so an outside click dismisses it, even while hidden.
        var handle = new WindowInteropHelper(_window).Handle;
        SetForegroundWindow(handle);
        _menu = BuildMenu();
        _menu.Closed += (_, _) => PostMessage(handle, 0, IntPtr.Zero, IntPtr.Zero);
        _menu.IsOpen = true;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_menu is not null) _menu.IsOpen = false;
        _icon.MouseUp -= MouseUp; _icon.DoubleClick -= DoubleClick; _icon.Visible = false; _icon.Dispose(); _art.Dispose();
    }
}
