using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace StartApp.App;

/// <summary>One owner-bound action-menu component for caption dropdowns and contextual rows.
/// Existing WPF styles own arrows/checkmarks; callers supply entries and actions.
/// Row and button anchoring remain distinct policies to preserve existing behavior.</summary>
internal sealed class ActionMenu
{
    private readonly Window _owner;
    internal ActionMenu(Window owner) => _owner=owner;
    /// <summary>Dropdown anchored at the button it came from (approved 22.11): top-left under the button's
    /// bottom-left like the approved menu mocks, flipping above the button or clamping inside the window when
    /// it would otherwise overflow. Programmatic IsOpen opens used the MousePoint default before, so a
    /// keyboard/UIA open detached the menu to the cursor instead of the button.</summary>
    internal ContextMenu Anchor(FrameworkElement target, ContextMenu? existing = null)
    {
        var menu = existing ?? new ContextMenu();
        menu.PlacementTarget = target;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
        menu.RemoveHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(OnAnchoredSubmenuOpened));
        menu.AddHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(OnAnchoredSubmenuOpened));
        menu.MaxHeight = Math.Max(120, _owner.ActualHeight - 16);
        menu.MaxWidth = Math.Max(120, _owner.ActualWidth - 16);
        menu.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
        {
            var origin = target.TransformToAncestor(_owner).Transform(new Point(0, 0));
            var x = Math.Clamp(origin.X, 8, Math.Max(8, _owner.ActualWidth - popupSize.Width - 8));
            var y = origin.Y + targetSize.Height + 2;
            if (y + popupSize.Height > _owner.ActualHeight - 8) y = Math.Max(8, origin.Y - popupSize.Height - 2);
            y = Math.Min(y, Math.Max(8, _owner.ActualHeight - 8 - popupSize.Height));
            return new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(x - origin.X, y - origin.Y), System.Windows.Controls.Primitives.PopupPrimaryAxis.None) };
        };
        return menu;
    }

    private void OnAnchoredSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not MenuItem item || item.Template.FindName("PART_Popup", item) is not System.Windows.Controls.Primitives.Popup popup) return;
        if (item.Tag as string == "saved-add-submenu")
        {
            popup.CustomPopupPlacementCallback = null;
            popup.PlacementTarget = item;
            popup.Placement = System.Windows.Controls.Primitives.PlacementMode.Left;
            popup.HorizontalOffset = 0; popup.VerticalOffset = 0;
            return;
        }
        popup.MaxWidth = Math.Max(120, _owner.ActualWidth - 16);
        popup.MaxHeight = Math.Max(120, _owner.ActualHeight - 16);
        popup.PlacementTarget = item;
        popup.CustomPopupPlacementCallback = (size, targetSize, _) =>
        {
            var origin = _owner.PointFromScreen(item.PointToScreen(new Point(0, 0)));
            var x = origin.X + targetSize.Width - 2;
            if (x + size.Width > _owner.ActualWidth - 8) x = origin.X - size.Width + 2;
            x = Math.Clamp(x, 8, Math.Max(8, _owner.ActualWidth - size.Width - 8));
            var y = Math.Clamp(origin.Y - 5, 8, Math.Max(8, _owner.ActualHeight - size.Height - 8));
            return new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(x - origin.X, y - origin.Y), System.Windows.Controls.Primitives.PopupPrimaryAxis.None) };
        };
        popup.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
    }

    internal void AnchorRow(FrameworkElement host,ContextMenu menu)
    {
        // Keep row identity separate from window commands across the popup boundary.
        menu.PlacementTarget = host;
        menu.DataContext = host.DataContext;
        // Do not anchor below the whole expanded editor or a partly scrolled-out row.
        // Compute the final popup origin inside the owner, then express it relative to the host.
        var origin = host.TransformToAncestor(_owner).Transform(new Point(0, 0));
        // A just-materialized (virtualized) row can report ActualHeight 0 here; fall back to the 32 dip offset.
        var anchorY = WindowPopupPlacement.AnchorY(origin.Y, host.ActualHeight, _owner.ActualHeight);
        menu.MaxHeight = Math.Max(0, _owner.ActualHeight - 16);
        menu.MaxWidth = Math.Max(0, _owner.ActualWidth - 16);
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
            var offset = WindowPopupPlacement.Offset(origin.X, origin.Y, targetSize.Width,
                popupSize.Width, popupSize.Height, anchorY, _owner.ActualWidth, _owner.ActualHeight);
            return new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(offset.X, offset.Y),
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
    internal static MenuItem Command(string header,ICommand command)=>new(){Header=header,Command=command};
    internal static MenuItem Action(string header,Action action)
    {
        var item=new MenuItem{Header=header};item.Click+=(_,_)=>action();return item;
    }
    internal static MenuItem Choice(string header,bool selected,Action action,bool staysOpen=false)
    {
        var item=new MenuItem{Header=header,IsCheckable=true,IsChecked=selected,StaysOpenOnClick=staysOpen};
        item.Click+=(_,_)=>action();return item;
    }
}
