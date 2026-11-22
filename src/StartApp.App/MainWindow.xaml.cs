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
    private UpdateAvailableWindow? _updateAvailableWindow;
    private bool _closeApproved;
    private MainViewModel? _hooked;
    public MainWindow() : this(UpdateComposition.Create()) { }
    internal MainWindow(UpdateViewModel updates)
    {
        Updates = updates ?? throw new ArgumentNullException(nameof(updates));
        InitializeComponent();
        Updates.StartupUpdateAvailable += ShowStartupUpdate;
        Updates.StagedUpdateReady += OnStagedUpdateReady;
        DataContextChanged += (_, _) => Hook();
        Loaded += async (_, _) => { Hook(); await Updates.CheckOnStartupOnceAsync(); };
        Closed += (_, _) => { Detach(); Updates.StartupUpdateAvailable -= ShowStartupUpdate; Updates.StagedUpdateReady -= OnStagedUpdateReady; Updates.Dispose(); };
    }
    // Phase 2: the verified package is staged - launch the apply helper and close for the swap.
    private void OnStagedUpdateReady(StagedUpdate staged)
    {
        try { UpdateApplyLauncher.Launch(staged); _closeApproved = true; Close(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { Updates.NotifyApplyFailed(); }
    }
    private void ShowStartupUpdate()
    {
        if (_closeApproved || !IsLoaded || _updateAvailableWindow is not null) return;
        _updateAvailableWindow = new UpdateAvailableWindow(Updates) { Owner = this };
        _updateAvailableWindow.Closed += (_, _) => _updateAvailableWindow = null;
        // Non-modal: dismissing or ignoring an optional update never blocks work.
        _updateAvailableWindow.Show();
    }
    // Attach at most once per view model: DataContext assignment and Loaded both reach here, and a
    // re-assigned DataContext must not keep the old view model's subscriptions alive (double subscription
    // made one upload click queue two sequential OpenFileDialogs).
    private void Hook()
    {
        var vm = DataContext as MainViewModel;
        if (ReferenceEquals(vm, _hooked)) return;
        Detach();
        if (vm is null) return;
        vm.FocusAddRequested += OnFocusAddRequested;
        vm.CopyAddressRequested += OnCopyAddressRequested;
        vm.AppCloseApproved += OnAppCloseApproved;
        vm.PickImageRequested += OnPickImageRequested;
        vm.PickCreateImageRequested += OnPickCreateImageRequested;
        vm.PropertyChanged += OnViewOptionChanged;
        _hooked = vm;
    }
    private void Detach()
    {
        if (_hooked is null) return;
        _hooked.FocusAddRequested -= OnFocusAddRequested;
        _hooked.CopyAddressRequested -= OnCopyAddressRequested;
        _hooked.AppCloseApproved -= OnAppCloseApproved;
        _hooked.PickImageRequested -= OnPickImageRequested;
        _hooked.PickCreateImageRequested -= OnPickCreateImageRequested;
        _hooked.PropertyChanged -= OnViewOptionChanged;
        _hooked = null;
    }
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
        DetailsAddForm.Visibility = Visibility.Visible;
        DetailsAddForm.UpdateLayout();
        TargetBox.BringIntoView();
        TargetBox.Focus();
    }
    private void ShowDetailsAddForm_Click(object sender, RoutedEventArgs e) => OnFocusAddRequested();
    private void OnCopyAddressRequested(string a) => Clipboard.SetText(a);
    private void OnAppCloseApproved() { _closeApproved = true; Close(); }
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
        if (!_closeApproved && DataContext is MainViewModel vm && !vm.RequestAppClose()) e.Cancel = true;
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
    // Drag from the capture list onto the workspace side. Each drop is one item and goes through the per-drag duplicate warning in the view model.
    private Point _dragStart;
    private bool _captureDragActive;
    private void CaptureList_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => _dragStart = e.GetPosition(null);
    private void CaptureList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // DoDragDrop pumps mouse messages in its modal loop, so PreviewMouseMove re-enters while a drag
        // is active; without the guard a single physical drag starts a second session whose extra Drop
        // stages the item and then flags it as its own duplicate (CI v37/v38 evidence).
        if (_captureDragActive) return;
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (e.OriginalSource is DependencyObject src && ItemsControl.ContainerFromElement(CaptureList, src) is ListBoxItem { DataContext: MainViewModel.CaptureRow row })
        {
            _captureDragActive = true;
            try { DragDrop.DoDragDrop(CaptureList, new DataObject(typeof(MainViewModel.CaptureRow), row), DragDropEffects.Copy); }
            finally { _captureDragActive = false; }
        }
    }
    private void CaptureDropZone_DragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(typeof(MainViewModel.CaptureRow)) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void CaptureDropZone_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel vm && e.Data.GetData(typeof(MainViewModel.CaptureRow)) is MainViewModel.CaptureRow row) vm.DropCaptured(row);
        e.Handled = true;
    }
    private void ToggleStaged_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MainViewModel.StagedRow row }) RevealStagedRow(sender, editing: !row.IsEditing);
    }
    private void RevealStagedRow(object sender, bool editing)
    {
        if (sender is not Button { DataContext: MainViewModel.StagedRow row } button) return;
        DependencyObject? ancestor = button;
        while (ancestor is not null && ancestor is not StackPanel { ContextMenu: not null, DataContext: MainViewModel.StagedRow })
            ancestor = System.Windows.Media.VisualTreeHelper.GetParent(ancestor);
        if (ancestor is not StackPanel host || !ReferenceEquals(host.DataContext, row)) return;
        // Click precedes the command. Wait for its state and layout, then reveal only this row's input or summary button.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (DataContext is not MainViewModel vm || !vm.IsCaptureSide || !vm.StagedRows.Contains(row) ||
                row.IsEditing != editing || !host.IsVisible || !ReferenceEquals(host.DataContext, row)) return;
            var target = FindStagedRevealTarget(host, row, editing);
            if (target is null) return;
            target.BringIntoView(); target.Focus();
        }));
    }
    private static FrameworkElement? FindStagedRevealTarget(DependencyObject root, MainViewModel.StagedRow row, bool editing)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element && element.IsVisible && ReferenceEquals(element.DataContext, row))
            {
                if (editing && element is TextBox input && System.Windows.Automation.AutomationProperties.GetName(input) == "Added item name") return input;
                if (!editing && element is Button edit && System.Windows.Automation.AutomationProperties.GetName(edit) == "Toggle added " + row.OriginalName) return edit;
            }
            var found = FindStagedRevealTarget(child, row, editing); if (found is not null) return found;
        }
        return null;
    }
    private void CaptionTab_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    { if (sender is Control tab) tab.FocusVisualStyle = null; }
    private void CaptionTab_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    { if (sender is Control tab) tab.ClearValue(Control.FocusVisualStyleProperty); }
    private void CaptionTab_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    { if (sender is Control tab) tab.ClearValue(Control.FocusVisualStyleProperty); }
    private System.Windows.IInputElement? _defaultReturnFocus;
    private bool _defaultDialogActive, _defaultKeyboardEntry;
    private void UpdateDefaultDialogFocus()
    {
        if (_hooked?.DefaultConfirmOpen == true)
        {
            if (_defaultDialogActive) return;
            _defaultDialogActive = true;
            _defaultReturnFocus = System.Windows.Input.Keyboard.FocusedElement;
            _defaultKeyboardEntry = System.Windows.Input.InputManager.Current.MostRecentInputDevice is System.Windows.Input.KeyboardDevice;
            if (_defaultKeyboardEntry) DefaultConfirmPrimaryButton.Focus();
            else DefaultConfirmOverlay.Focus();
        }
        else if (_defaultDialogActive)
        {
            _defaultDialogActive = false;
            if (_defaultReturnFocus is UIElement { IsVisible: true, IsEnabled: true } target)
            {
                if (!_defaultKeyboardEntry && target is Control control) control.FocusVisualStyle = null;
                target.Focus();
            }
            _defaultReturnFocus = null;
        }
    }
    private void DefaultConfirm_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape || _hooked?.DefaultConfirmOpen != true) return;
        _hooked.CancelDefaultReplaceCommand.Execute(null); e.Handled = true;
    }
    private void RowContextMenu_Opening(object sender, ContextMenuEventArgs e)
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
        // Keep row identity separate from window commands across the popup boundary.
        menu.PlacementTarget = host;
        menu.DataContext = host.DataContext;
        // Do not anchor below the whole expanded editor or a partly scrolled-out row.
        // Compute the final popup origin inside the owner, then express it relative to the host.
        var origin = host.TransformToAncestor(this).Transform(new Point(0, 0));
        // A just-materialized (virtualized) row can report ActualHeight 0 here; fall back to the 32 dip offset.
        var anchorY = Math.Clamp(origin.Y + Math.Min(32, host.ActualHeight > 0 ? host.ActualHeight : 32), 8, Math.Max(8, ActualHeight - 8));
        menu.MaxHeight = Math.Max(0, ActualHeight - 16);
        menu.MaxWidth = Math.Max(0, ActualWidth - 16);
        // ContextMenuService overrides PlacementTarget/Placement for keyboard and mouse opens, and for the
        // keyboard route e.Handled here does not cancel its open (measured 22.11: identical placement with
        // and without handling). So let whoever opened it finish, then re-place on the next dispatcher tick:
        // reopening with our anchored clamped Custom placement wins regardless of the open path (approved
        // 22.11: anchored at the source, never overflowing the window).
        var capturedMenu = menu;
        var capturedCallback = (System.Windows.Controls.Primitives.CustomPopupPlacementCallback)((popupSize, targetSize, _) =>
        {
            // popupSize at callback time can under-measure the final rendered height (measured 22.11: 173 vs
            // a final 187 UIA rect), so flip/clamp against an 18px-padded height - under-flipping overflows.
            var safeH = popupSize.Height + 18;
            var x = Math.Clamp(origin.X + targetSize.Width - popupSize.Width, 8, Math.Max(8, ActualWidth - popupSize.Width - 8));
            var y = anchorY + safeH <= ActualHeight - 8 ? anchorY : Math.Max(8, anchorY - safeH);
            // Never overflow the window bottom (approved 22.11): even a popup taller than the space below
            // AND above the anchor clamps back inside the window.
            var maxY = Math.Max(8, ActualHeight - 8 - safeH);
            if (y > maxY) y = maxY;
            return new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(x - origin.X, y - origin.Y),
                System.Windows.Controls.Primitives.PopupPrimaryAxis.None) };
        });
        capturedMenu.Dispatcher.BeginInvoke(new System.Action(() =>
        {
            if (!capturedMenu.IsOpen) return;
            capturedMenu.PlacementTarget = host;
            capturedMenu.CustomPopupPlacementCallback = capturedCallback;
            capturedMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
            capturedMenu.IsOpen = false;
            capturedMenu.IsOpen = true;
        }), System.Windows.Threading.DispatcherPriority.Background);   // Input runs BEFORE the service's own posted open; Background runs after it
    }
}
