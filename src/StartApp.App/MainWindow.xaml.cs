using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using StartApp.Presentation;
namespace StartApp.App;
public partial class MainWindow : Window
{
    public UpdateViewModel Updates { get; }
    private readonly WindowUpdateSession<UpdateAvailableWindow> _updateWindow = new();
    private readonly WindowSessionSubscriptions _sessionSubscriptions;
    private readonly DialogFocusSession<System.Windows.IInputElement> _defaultDialogFocus;
    private MainViewModel? _hooked => _sessionSubscriptions.Current;
    public MainWindow() : this(UpdateComposition.Create()) { }
    internal MainWindow(UpdateViewModel updates)
    {
        Updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _sessionSubscriptions = new(OnFocusAddRequested, OnCopyAddressRequested, OnAppCloseApproved, OnPickImageRequested, OnPickCreateImageRequested, OnViewOptionChanged);
        _defaultDialogFocus = new(
            () => System.Windows.Input.Keyboard.FocusedElement,
            () => System.Windows.Input.InputManager.Current.MostRecentInputDevice is System.Windows.Input.KeyboardDevice,
            keyboard => { if (keyboard) DefaultConfirmPrimaryButton.Focus(); else DefaultConfirmOverlay.Focus(); },
            (element, keyboard) =>
            {
                if (element is not UIElement { IsVisible: true, IsEnabled: true } target) return;
                if (!keyboard && target is Control control) control.FocusVisualStyle = null;
                target.Focus();
            });
        InitializeComponent();
        Updates.StartupUpdateAvailable += ShowStartupUpdate;
        Updates.StagedUpdateReady += OnStagedUpdateReady;
        DataContextChanged += (_, _) => Hook();
        Loaded += async (_, _) => { Hook(); InitializeTray(); await Updates.CheckOnStartupOnceAsync(); };
        Closed += (_, _) => { _tray?.Dispose(); _tray = null; Detach(); Updates.StartupUpdateAvailable -= ShowStartupUpdate; Updates.StagedUpdateReady -= OnStagedUpdateReady; Updates.Dispose(); };
    }
    // Phase 2: the verified package is staged - launch the apply helper and close for the swap.
    private void OnStagedUpdateReady(StagedUpdate staged)
    {
        try { UpdateApplyLauncher.Launch(staged); _updateWindow.ApproveClose(); Close(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { Updates.NotifyApplyFailed(); }
    }
    private void ShowStartupUpdate()
    {
        // Non-modal: dismissing or ignoring an optional update never blocks work.
        _updateWindow.ShowStartup(IsLoaded,
            () => new UpdateAvailableWindow(Updates) { Owner = this },
            (window, closed) => window.Closed += (_, _) => closed(),
            window => window.Show());
    }
    // Attach at most once per view model: DataContext assignment and Loaded both reach here, and a
    // re-assigned DataContext must not keep the old view model's subscriptions alive (double subscription
    // made one upload click queue two sequential OpenFileDialogs).
    private void Hook() => _sessionSubscriptions.Replace(DataContext as MainViewModel);
    private void Detach() => _sessionSubscriptions.Dispose();
    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void ToggleMaximizeWindow_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (MaximizeWindowButton is not null)
        {
            MaximizeWindowButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
            System.Windows.Automation.AutomationProperties.SetName(MaximizeWindowButton,
                WindowState == WindowState.Maximized ? "Restore window" : "Maximize window");
        }
    }
    private void OnFocusAddRequested()
    {
        if (DataContext is MainViewModel vm && vm.CardAddSession is { } session)
        {
            try { new CardAddWindow(session) { Owner = this }.ShowDialog(); }
            finally { vm.FinishCardAdd(); }
        }
        else DetailsView.FocusAddForm();
    }
    private void OnCopyAddressRequested(string a) => Clipboard.SetText(a);
    private void OnAppCloseApproved() { _updateWindow.ApproveClose(); Close(); }
    private void OnPickImageRequested()
    {
        var dlg = new OpenFileDialog { Title = "העלה תמונה", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*" };
        if (dlg.ShowDialog(this) == true && DataContext is MainViewModel vm) vm.DesignImagePath = dlg.FileName;
    }
    private void OnPickCreateImageRequested()
    {
        var dlg = new OpenFileDialog { Title = "העלה תמונה", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*" };
        if (dlg.ShowDialog(this) == true && DataContext is MainViewModel vm) vm.NewWorkspaceImagePath = dlg.FileName;
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_updateWindow.CloseApproved && !_trayExitRequested && _tray is { CanHide: true } && DataContext is MainViewModel { KeepRunningInTray: true })
        {
            e.Cancel = true; Hide(); // Preserve all drafts and running work. Explicit tray Exit still uses the normal guard.
        }
        else if (!_updateWindow.CloseApproved && DataContext is MainViewModel vm && !vm.RequestAppClose()) e.Cancel = true;
        base.OnClosing(e);
    }
    // Capture dropdown (לכידה): anchored under its button at the window's left edge, so it opens downward and stays inside the window.
    private void HelpMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.ContextMenu is { } m)
            {
                NewAnchoredMenu(b, m).IsOpen = true;
            }
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e) =>
            System.Windows.MessageBox.Show(this, "Start-app\n" + Updates?.Status, "אודות Start-app", MessageBoxButton.OK, MessageBoxImage.Information);

        private void NewWorkspaceFocus_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            vm.ShowCreateScreen();
            if (vm.InCreate) Dispatcher.BeginInvoke(new Action(() => CreateView.FocusName()));
        }

        private void CaptureMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            NewAnchoredMenu(button, menu).IsOpen = true;
        }
    }
    internal void CaptionTab_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    { if (sender is Control tab) tab.FocusVisualStyle = null; }
    internal void CaptionTab_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    { if (sender is Control tab) tab.ClearValue(Control.FocusVisualStyleProperty); }
    internal void CaptionTab_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    { if (sender is Control tab) tab.ClearValue(Control.FocusVisualStyleProperty); }
    private void UpdateDefaultDialogFocus() => _defaultDialogFocus.Update(_hooked?.DefaultConfirmOpen == true);
    private void DefaultConfirm_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape || _hooked?.DefaultConfirmOpen != true) return;
        _hooked.CancelDefaultReplaceCommand.Execute(null); e.Handled = true;
    }
    internal void RowContextMenu_Opening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { ContextMenu: { } menu } host) return;
        // TextBox keeps WPF's ordinary text-edit context menu, including Apps and Shift+F10.
        // The separately focusable row is the action-menu anchor for staged items.
        for (DependencyObject? source = e.OriginalSource as DependencyObject; source is not null && !ReferenceEquals(source, host);)
        {
            if (source is TextBox) return;
            source = source is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        if (host.DataContext is ItemRow detail && DataContext is MainViewModel viewModel && viewModel.AllItemFromDetail(detail) is { } allRow && FindItemActionButton(host) is { } anchor)
        { e.Handled = true; ShowItemActions(anchor, allRow, detail); return; }
        Menus.AnchorRow(host,menu);

    }
}
