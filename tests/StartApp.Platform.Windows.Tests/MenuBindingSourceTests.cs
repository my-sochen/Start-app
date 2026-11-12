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
            if (File.Exists(p)) return File.ReadAllText(p);
        }
        throw new FileNotFoundException("src/StartApp.App/MainWindow.xaml not found above " + AppContext.BaseDirectory);
    }

    [Fact] public void ContextMenu_bindings_reach_the_view_model_through_the_button_Tag()
    {
        var xaml = Xaml();
        var menus = Regex.Matches(xaml, "<ContextMenu>.*?</ContextMenu>", RegexOptions.Singleline);
        Assert.Equal(3, menus.Count); // workspace card menu + item actions menu + capture dropdown
        foreach (Match m in menus)
        {
            Assert.DoesNotContain("AncestorType=Window", m.Value);
            Assert.DoesNotContain("PlacementTarget.DataContext", m.Value);
            foreach (Match cmd in Regex.Matches(m.Value, "Command=\"\\{Binding ([^,}]+)"))
                Assert.StartsWith("PlacementTarget.Tag.", cmd.Groups[1].Value);
        }
    }

    [Fact] public void All_menu_buttons_carry_the_window_view_model_in_their_Tag()
    {
        var xaml = Xaml();
        const string tag = "Tag=\"{Binding DataContext, RelativeSource={RelativeSource AncestorType=Window}}\"";
        foreach (var button in new[] { "AutomationProperties.Name=\"Item actions\"", "AutomationProperties.Name=\"Workspace actions\"", "AutomationProperties.Name=\"Capture\"" })
        {
            var i = xaml.IndexOf(button, StringComparison.Ordinal);
            Assert.True(i >= 0, button + " not found");
            var open = xaml.LastIndexOf("<Button", i, StringComparison.Ordinal);
            var close = xaml.IndexOf('>', i);
            Assert.Contains(tag, xaml.Substring(open, close - open));
        }
    }
}
