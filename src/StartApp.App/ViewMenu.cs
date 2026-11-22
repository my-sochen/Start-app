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
    private void ViewMenu_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not MainViewModel vm) return;
        var menu = new ContextMenu { PlacementTarget = (Button)sender };

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
        filter.Items.Add(new MenuItem { Header = "לפי צבע · נצבע דרך הבחירה המתקדמת", IsEnabled = false });
        foreach (var c in vm.FilterColorOptions)
            filter.Items.Add(Choice(c, vm.IsColorFiltered(c), () => vm.ToggleColorFilter(c), staysOpen: true));
        filter.Items.Add(new Separator());
        var cats = new MenuItem { Header = "לפי קטגוריה" };
        foreach (var cat in vm.FilterCategoryOptions)
            cats.Items.Add(Choice(cat, vm.IsCategoryFiltered(cat), () => vm.ToggleCategoryFilter(cat), staysOpen: true));
        filter.Items.Add(cats);
        var types = new MenuItem { Header = "לפי סוג תוכן" };
        foreach (var t in new[] { ItemType.Program, ItemType.Link, ItemType.Folder, ItemType.File })
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
        var containsBox = new TextBox { MinWidth = 140, Text = vm.FilterContainsText };
        containsBox.TextChanged += (_, _) => vm.FilterContainsText = containsBox.Text;
        filter.Items.Add(new MenuItem { Header = containsBox, StaysOpenOnClick = true, ToolTip = "מכיל..." });
        menu.Items.Add(filter);

        var template = new MenuItem { Header = "תבנית תצוגה" };
        template.Items.Add(Choice("כרטיסים", vm.CurrentTemplate == ViewTemplate.Cards, () => vm.CurrentTemplate = ViewTemplate.Cards));
        template.Items.Add(Choice("רשימה", vm.CurrentTemplate == ViewTemplate.List, () => vm.CurrentTemplate = ViewTemplate.List));
        template.Items.Add(Choice("אייקונים", vm.CurrentTemplate == ViewTemplate.Icons, () => vm.CurrentTemplate = ViewTemplate.Icons));
        menu.Items.Add(template);

        menu.IsOpen = true;
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
        if (e.PropertyName is nameof(MainViewModel.CurrentTemplate) or nameof(MainViewModel.CurrentCardSize))
        {
            WorkspaceList.ItemTemplate = (DataTemplate)Resources[_hooked!.CurrentTemplate switch
            { ViewTemplate.List => "WorkspaceListTemplate", ViewTemplate.Icons => "WorkspaceIconsTemplate", _ => "WorkspaceCardTemplate" }];
        }
    }
}
