using System.Xml.Linq;
using Xunit;

namespace StartApp.Presentation.Tests;

/// <summary>MainWindow.xaml is written by humans and scripts alike, and an unsubstituted generator token (the CR
/// finding that produced this fixture: a literal ''' + palette + ''' line) still compiles to BAML without error,
/// because the XAML compiler does not validate panel content models - the app would have failed only at runtime
/// startup. Pin the view source instead: well-formed XML, no generator artifacts, both create palettes really present.</summary>
public class ViewSourceIntegrityTests
{
    private static string ViewSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "StartApp.App", "MainWindow.xaml");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException("src/StartApp.App/MainWindow.xaml not found above " + AppContext.BaseDirectory);
    }

    [Fact] public void MainWindow_xaml_is_well_formed_xml() => XDocument.Parse(ViewSource());

    [Fact] public void MainWindow_xaml_has_no_unsubstituted_generator_tokens()
    {
        var xaml = ViewSource();
        Assert.DoesNotContain("'''", xaml);
        Assert.DoesNotContain("+ palette +", xaml);
    }

    [Fact] public void Both_create_forms_have_a_real_palette_control()
    {
        var hits = System.Text.RegularExpressions.Regex.Matches(ViewSource(), "ApplyCreatePaletteColorCommand").Count;
        Assert.True(hits >= 2, $"expected the ordinary and the capture create palettes in the view, found {hits}");
    }
}
