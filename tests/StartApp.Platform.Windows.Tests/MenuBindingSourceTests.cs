using System.Text.RegularExpressions;

/// <summary>Source guard for the v10 menu bug: a ContextMenu lives in a Popup outside the window's visual tree,
/// so {Binding ..., RelativeSource=AncestorType=Window} resolves to nothing inside it and PlacementTarget.DataContext
/// is the row/card item, not the window view-model. Every menu binding must reach the view-model through the
/// menu button's Tag, and each menu button must carry that Tag. Text-level regression for a binding failure no
/// view-model test can see.</summary>
public class MenuBindingSourceTests
{
    private static string Xaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, "src", "StartApp.App", "MainWindow.xaml");
            if (File.Exists(p)) return File.ReadAllText(p) + File.ReadAllText(Path.Combine(dir.FullName,"src","StartApp.App","CaptureView.xaml")) + File.ReadAllText(Path.Combine(dir.FullName,"src","StartApp.App","WorkspaceDetailsView.xaml"));
        }
        throw new FileNotFoundException("src/StartApp.App/MainWindow.xaml not found above " + AppContext.BaseDirectory);
    }

    [Fact] public void ContextMenu_bindings_reach_the_view_model_through_the_button_Tag()
    {
        var xaml = Xaml();
        var menus = Regex.Matches(xaml, "<ContextMenu(?:\\s[^>]*)?>.*?</ContextMenu>", RegexOptions.Singleline);
        Assert.Equal(5, menus.Count); // shared workspace actions menu resource (card/list/icons templates) + item actions menu + staged capture item menu + capture dropdown + עזרה tab menu (approved 22.11 title-bar tabs)
        foreach (Match m in menus)
        {
            Assert.DoesNotContain("AncestorType=Window", m.Value);
            Assert.DoesNotContain("PlacementTarget.DataContext", m.Value);
            foreach (Match cmd in Regex.Matches(m.Value, "Command=\"\\{Binding ([^,}]+)"))
                Assert.StartsWith("PlacementTarget.Tag.", cmd.Groups[1].Value);
        }
    }

    [Fact] public void All_menu_hosts_carry_the_window_view_model_in_their_Tag()
    {
        var xaml = Xaml();
        const string tag = "Tag=\"{Binding DataContext, RelativeSource={RelativeSource AncestorType=Window}}\"";
        foreach (var button in new[] { "AutomationProperties.Name=\"Item row\"", "AutomationProperties.Name=\"Workspace actions\"", "AutomationProperties.Name=\"Staged item row\"", "AutomationProperties.Name=\"Capture\"" })
        {
            var i = xaml.IndexOf(button, StringComparison.Ordinal);
            Assert.True(i >= 0, button + " not found");
            var open = xaml.LastIndexOf("<", i, StringComparison.Ordinal);
            var close = xaml.IndexOf('>', i);
            Assert.Contains(tag, xaml.Substring(open, close - open));
        }
    }
    [Fact] public void Contextual_actions_replace_ellipsis_and_open_the_full_item_dialog()
    {
        var xaml=Xaml(); Assert.DoesNotContain("Content=\"⋯\"",xaml);
        Assert.Equal(5,Regex.Matches(xaml,"ContextMenuOpening=\"RowContextMenu_Opening\"").Count); // card + list + icons workspace templates + item row + staged item row
        var start=xaml.IndexOf("<ListView.ItemTemplate>",StringComparison.Ordinal);
        var end=xaml.IndexOf("</ListView.ItemTemplate>",start,StringComparison.Ordinal);
        var row=xaml.Substring(start,end-start);
        Assert.Contains("Content=\"ערוך\"",row);
        Assert.Contains("EditItemCommand",row);
        var dialog=File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindViewPath())!,"ItemEditWindow.xaml"));
        var dialogCode=File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindViewPath())!,"ItemEditWindow.xaml.cs"));
        Assert.Contains("EditItemTarget",dialog); Assert.Contains("EditItemDescription",dialog);
        Assert.Contains("SaveItemEditCommand",dialogCode); Assert.Contains("CancelItemEditCommand",dialogCode);
        Assert.Equal(1,Regex.Matches(dialog,"AutomationProperties.Name=\"Edit item name\"").Count);
    }
    [Fact] public void Minimum_height_details_and_text_context_contracts_remain_explicit()
    {
        var xaml=Xaml();
        Assert.Contains("AutomationProperties.Name=\"Workspace details viewport\"",xaml);
        Assert.Contains("AutomationProperties.Name=\"Cancel item edit\"",File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindViewPath())!,"ItemEditWindow.xaml")));
        Assert.Contains("Focusable=\"True\" KeyboardNavigation.IsTabStop=\"True\"",xaml);
        Assert.DoesNotContain("Header=\"שיתוף לסביבה אחרת\" IsEnabled=\"False\"", xaml);
        var code=File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindViewPath())!,"MainWindow.xaml.cs"));
        Assert.Contains("if (source is TextBox) return;",code);
        Assert.Contains("ShowItemActions(anchor, allRow, detail)",code);
        var actions=File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindViewPath())!,"AllItemsView.xaml.cs"));
        Assert.Contains("vm.AddItemToEnvironments(row, ids)",actions);
        Assert.Contains("menu.Items.Add(BuildSavedItemAddMenu(vm, source))",actions);
        var submenu=File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindViewPath())!,"SavedItemAddMenu.cs"));
        Assert.Contains("selectedEnvironment.IsEnabled = vm.SavedItemDestinations(source).Count > 0",submenu);
        Assert.Contains("destination.IsEnabled = !vm.SavedItemIsSourceList(source, kind)",submenu);
        foreach(var destination in new[]{"GlobalListKind.Blacklist","GlobalListKind.Whitelist","GlobalListKind.GlobalItems"}) Assert.Contains(destination,submenu);
        code+=File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindViewPath())!,"Components","ActionMenu.cs"));
        // flip/clamp uses a padded height: the callback's popupSize under-measures the final rendered menu.
        Assert.Contains("WindowPopupPlacement.AnchorY(origin.Y, host.ActualHeight, _owner.ActualHeight)",code);
        Assert.Contains("WindowPopupPlacement.Offset(origin.X, origin.Y, targetSize.Width",code);
        var placement=File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindViewPath())!,"WindowPopupPlacement.cs"));
        Assert.Contains("popupHeight + 18",placement);
        Assert.Contains("Math.Max(8, anchorY - safeHeight)",placement);
        // Context menus are re-placed on the next dispatcher tick after any open: ContextMenuService
        // overrides the placement for keyboard/mouse opens, so the anchored/clamped Custom placement is
        // re-applied post-open (reopen) where it always wins.
        Assert.Contains("capturedMenu.Dispatcher.BeginInvoke",code);
        Assert.Contains("capturedMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom",code);
        Assert.Contains("capturedMenu.IsOpen = false;",code);
    }
    [Fact] public void Staged_fields_use_pixel_scrolling_when_row_is_taller_than_viewport()
    {
        var xaml=Xaml();
        var hosts=Regex.Matches(xaml,"<ListBox[^>]*ItemsSource=\"\\{Binding StagedRows}\"[^>]*>");
        Assert.Single(hosts.Cast<Match>());
        Assert.Contains("ScrollViewer.CanContentScroll=\"False\"",hosts[0].Value);
        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"",hosts[0].Value);
    }
    [Fact] public void Staged_editor_requires_explicit_row_command_and_summary_is_not_an_input()
    {
        var xaml=Xaml(); var start=xaml.IndexOf("ItemsSource=\"{Binding StagedRows}\"",StringComparison.Ordinal);
        var end=xaml.IndexOf("</ListBox>",start,StringComparison.Ordinal); var row=xaml.Substring(start,end-start);
        Assert.Contains("DataContext.EditStagedCommand",row); Assert.Contains("DataContext.CloseStagedEditCommand",row);
        Assert.Contains("Visibility=\"{Binding IsEditing, Converter={StaticResource B2V}}\"",row);
        Assert.Contains("AutomationProperties.AutomationId=\"StagedItemName\"",row);
        Assert.Contains("AutomationProperties.AutomationId=\"StagedItemAddress\"",row);
        var editorStart=row.IndexOf("AutomationProperties.Name=\"Staged item editor\"",StringComparison.Ordinal);
        Assert.True(editorStart>=0);
        Assert.DoesNotContain("<TextBox",row.Substring(0,editorStart));
        Assert.Equal(3,Regex.Matches(row.Substring(editorStart),"<TextBox").Count);
        Assert.Contains("<ComboBox",row.Substring(editorStart)); Assert.Contains("OpenWithPicker.Path=\"{Binding OpenWith",row.Substring(editorStart));
        Assert.DoesNotContain("SelectionChanged=",row); Assert.DoesNotContain("OpenItemCommand",row);
    }
    [Fact] public void Explicit_staged_toggle_requests_identity_bound_product_reveal()
    {
        var xaml=Xaml(); Assert.Contains("Click=\"ToggleStaged_Click\"",xaml);
        var code=File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindViewPath())!,"MainWindow.xaml.cs"));
        code += File.ReadAllText(Path.Combine(Path.GetDirectoryName(FindViewPath())!,"CaptureView.xaml.cs"));
        Assert.Contains("row.IsEditing != editing",code); Assert.Contains("!vm.StagedRows.Contains(row)",code);
        Assert.Contains("ReferenceEquals(host.DataContext, row)",code);
        Assert.Contains("FindStagedRevealTarget(host, row, editing)",code);
        Assert.Contains("target.BringIntoView(); target.Focus();",code);
    }
    private static string FindViewPath()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        for(var i=0;i<10&&dir is not null;i++,dir=dir.Parent){var path=Path.Combine(dir.FullName,"src","StartApp.App","MainWindow.xaml");if(File.Exists(path))return path;}
        throw new FileNotFoundException("MainWindow.xaml");
    }
}
