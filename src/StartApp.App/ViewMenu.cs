using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using StartApp.Core;
using StartApp.Presentation;

namespace StartApp.App;

/// <summary>The תצוגה menu (approved 22.11 handoff): four rows, each with a hover submenu in the Windows
/// style — גודל כרטיסים, מיון (מסדר, לא מסתיר), סינון (combines across dimensions) and תבנית תצוגה. Built
/// fresh on every open because the filter dimensions are dynamic (colors in use, categories, real items).
/// Selecting an item applies immediately; checkmarks mark the active choice in every dimension.</summary>
public partial class MainWindow
{
    /// <summary>Dropdown anchored at the button it came from (approved 22.11): top-left under the button's
    /// bottom-left like the approved menu mocks, flipping above the button or clamping inside the window when
    /// it would otherwise overflow. Programmatic IsOpen opens used the MousePoint default before, so a
    /// keyboard/UIA open detached the menu to the cursor instead of the button.</summary>
    private ContextMenu NewAnchoredMenu(Button target, ContextMenu? existing = null)
    {
        var menu = existing ?? new ContextMenu();
        menu.PlacementTarget = target;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
        menu.RemoveHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(OnAnchoredSubmenuOpened));
        menu.AddHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(OnAnchoredSubmenuOpened));
        menu.MaxHeight = Math.Max(120, ActualHeight - 16);
        menu.MaxWidth = Math.Max(120, ActualWidth - 16);
        menu.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
        {
            var origin = target.TransformToAncestor(this).Transform(new Point(0, 0));
            var x = Math.Clamp(origin.X, 8, Math.Max(8, ActualWidth - popupSize.Width - 8));
            var y = origin.Y + targetSize.Height + 2;
            if (y + popupSize.Height > ActualHeight - 8) y = Math.Max(8, origin.Y - popupSize.Height - 2);
            y = Math.Min(y, Math.Max(8, ActualHeight - 8 - popupSize.Height));
            return new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(x - origin.X, y - origin.Y), System.Windows.Controls.Primitives.PopupPrimaryAxis.None) };
        };
        return menu;
    }

    private void OnAnchoredSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not MenuItem item || item.Template.FindName("PART_Popup", item) is not System.Windows.Controls.Primitives.Popup popup) return;
        popup.MaxWidth = Math.Max(120, ActualWidth - 16);
        popup.MaxHeight = Math.Max(120, ActualHeight - 16);
        popup.PlacementTarget = item;
        popup.CustomPopupPlacementCallback = (size, targetSize, _) =>
        {
            var origin = PointFromScreen(item.PointToScreen(new Point(0, 0)));
            var x = origin.X + targetSize.Width - 2;
            if (x + size.Width > ActualWidth - 8) x = origin.X - size.Width + 2;
            x = Math.Clamp(x, 8, Math.Max(8, ActualWidth - size.Width - 8));
            var y = Math.Clamp(origin.Y - 5, 8, Math.Max(8, ActualHeight - size.Height - 8));
            return new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(x - origin.X, y - origin.Y), System.Windows.Controls.Primitives.PopupPrimaryAxis.None) };
        };
        popup.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
    }

    private void ViewMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not MainViewModel vm) return;
        var menu = NewAnchoredMenu((Button)sender);

        var size = new MenuItem { Header = "גודל כרטיסים" };
        size.Items.Add(Choice("קטן", vm.CurrentCardSize == CardSizeOption.Small, () => vm.CurrentCardSize = CardSizeOption.Small));
        size.Items.Add(Choice("בינוני", vm.CurrentCardSize == CardSizeOption.Medium, () => vm.CurrentCardSize = CardSizeOption.Medium));
        size.Items.Add(Choice("גדול", vm.CurrentCardSize == CardSizeOption.Large, () => vm.CurrentCardSize = CardSizeOption.Large));
        menu.Items.Add(size);

        var sort = new MenuItem { Header = "מיון" };
        sort.Items.Add(Choice("כניסות אחרונות", vm.CurrentSort == ViewSort.RecentEntries, () => vm.CurrentSort = ViewSort.RecentEntries));
        sort.Items.Add(Choice("לפי תאריך יצירה", vm.CurrentSort == ViewSort.CreationDate, () => vm.CurrentSort = ViewSort.CreationDate));
        sort.Items.Add(Choice("לפי כמות אייטמים", vm.CurrentSort == ViewSort.ItemCount, () => vm.CurrentSort = ViewSort.ItemCount));
        sort.Items.Add(Choice("לפי כמות שיגורים", vm.CurrentSort == ViewSort.LaunchCount, () => vm.CurrentSort = ViewSort.LaunchCount));
        sort.Items.Add(Choice("לפי הא' ב'", vm.CurrentSort == ViewSort.Alphabetical, () => vm.CurrentSort = ViewSort.Alphabetical));
        menu.Items.Add(sort);

        var filter = new MenuItem { Header = "סינון", StaysOpenOnClick = true };
        // לפי צבע ▸ (owner correction 09:40:56 + structure 09:45:24): a regular row with an arrow opening a nested
        // flyout - base palette first, separator, then the custom colors in use. Swatch 13px + hex in LTR, no note.
        var colors = new MenuItem { Header = "לפי צבע" };
        foreach (var c in vm.DesignPalette)
            colors.Items.Add(ColorChoice(vm, c));
        var customs = vm.FilterColorOptions.Where(c => !vm.DesignPalette.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
        if (customs.Count > 0)
        {
            colors.Items.Add(new Separator());
            foreach (var c in customs)
                colors.Items.Add(ColorChoice(vm, c));
        }
        filter.Items.Add(colors);
        var cats = new MenuItem { Header = "לפי קטגוריה" };
        foreach (var cat in vm.FilterCategoryOptions)
            cats.Items.Add(Choice(cat, vm.IsCategoryFiltered(cat), () => vm.ToggleCategoryFilter(cat), staysOpen: true));
        filter.Items.Add(cats);
        var types = new MenuItem { Header = "לפי סוג תוכן" };
        foreach (var t in new[] { ItemType.Link, ItemType.Program, ItemType.File, ItemType.Folder })
        {
            var typeItem = new MenuItem { Header = MainViewModel.ContentTypeLabel(t) };
            var items = vm.ContentItemsOfType(t);
            if (items.Count == 0) { typeItem.IsEnabled = false; }
            foreach (var it in items)
                typeItem.Items.Add(Choice(it.Name, vm.CurrentContentItemId == it.Id,
                    () => vm.SetContentItemFilter(vm.CurrentContentItemId == it.Id ? null : it)));
            types.Items.Add(typeItem);
        }
        filter.Items.Add(types);
        var containsBox = new TextBox { Width = 104, Height = 28, Text = vm.FilterContainsText, Padding = new Thickness(8, 3, 8, 3), BorderBrush = System.Windows.Media.Brushes.LightSteelBlue, BorderThickness = new Thickness(1), FlowDirection = FlowDirection.LeftToRight, VerticalContentAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(containsBox, "מכיל...");
        containsBox.TextChanged += (_, _) => vm.FilterContainsText = containsBox.Text;
        var containsRow = new StackPanel { Orientation = Orientation.Horizontal };
        containsRow.Children.Add(new TextBlock { Text = "מכיל...", Width = 54, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        containsRow.Children.Add(containsBox);
        filter.Items.Add(new MenuItem { Header = containsRow, StaysOpenOnClick = true, ToolTip = "מכיל..." });
        menu.Items.Add(filter);

        var template = new MenuItem { Header = "תבנית תצוגה" };
        template.Items.Add(Choice("כרטיסים", vm.CurrentTemplate == ViewTemplate.Cards, () => vm.CurrentTemplate = ViewTemplate.Cards));
        template.Items.Add(Choice("רשימה", vm.CurrentTemplate == ViewTemplate.List, () => vm.CurrentTemplate = ViewTemplate.List));
        template.Items.Add(Choice("אייקונים", vm.CurrentTemplate == ViewTemplate.Icons, () => vm.CurrentTemplate = ViewTemplate.Icons));
        menu.Items.Add(template);

        menu.IsOpen = true;
    }

    private static MenuItem ColorChoice(MainViewModel vm, string hex)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 13, Height = 13, VerticalAlignment = VerticalAlignment.Center,
            Fill = ColorBrushConverter.BrushFrom(hex) ?? System.Windows.Media.Brushes.Transparent,
            Stroke = TopBarBrushConverter.Frozen("#C9D0DC"), StrokeThickness = 1
        });
        head.Children.Add(new TextBlock
        {
            Text = hex.ToLowerInvariant(), FlowDirection = FlowDirection.LeftToRight, FontSize = 12,
            Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
        });
        var item = new MenuItem { Header = head, IsCheckable = true, IsChecked = vm.IsColorFiltered(hex), StaysOpenOnClick = true };
        item.Click += (_, _) => vm.ToggleColorFilter(hex);
        return item;
    }

    private static MenuItem Choice(string header, bool on, Action apply, bool staysOpen = false)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = on, StaysOpenOnClick = staysOpen };
        item.Click += (_, _) => apply();
        return item;
    }

    /// <summary>Template and size changes re-template the already-generated cards.</summary>
    private void OnViewOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.DefaultConfirmOpen))
            Dispatcher.BeginInvoke(new Action(UpdateDefaultDialogFocus));
        if (e.PropertyName == nameof(MainViewModel.UnsavedOpen) && _hooked?.UnsavedOpen == true)
            Dispatcher.BeginInvoke(new Action(() => UnsavedCancelButton.Focus()));
        if (e.PropertyName is nameof(MainViewModel.CurrentTemplate) or nameof(MainViewModel.CurrentCardSize))
        {
            WorkspaceList.ItemTemplate = (DataTemplate)Resources[_hooked!.CurrentTemplate switch
            { ViewTemplate.List => "WorkspaceListTemplate", ViewTemplate.Icons => "WorkspaceIconsTemplate", _ => "WorkspaceCardTemplate" }];
            WorkspaceList.ItemsPanel = _hooked!.CurrentTemplate switch
            {
                ViewTemplate.List => FindResource("ListRowsPanel") as ItemsPanelTemplate,
                ViewTemplate.Icons => FindResource("IconTilesPanel") as ItemsPanelTemplate,
                _ => FindResource("CardGridPanel") as ItemsPanelTemplate
            };
        }
    }
}
