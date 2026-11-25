using System.Xml.Linq;
using Xunit;

namespace StartApp.Presentation.Tests;

/// <summary>MainWindow.xaml is written by humans and scripts alike, and an unsubstituted generator token (the CR
/// finding that produced this fixture: a literal ''' + palette + ''' line) still compiles to BAML without error,
/// because the XAML compiler does not validate panel content models - the app would have failed only at runtime
/// startup. Pin the view source instead: well-formed XML, no generator artifacts, both create palettes really present.</summary>
public class ViewSourceIntegrityTests
{
    private static string ViewSource(string filename = "MainWindow.xaml")
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "StartApp.App", filename);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException("src/StartApp.App/MainWindow.xaml not found above " + AppContext.BaseDirectory);
    }

    [Theory]
    [InlineData("MainWindow.xaml")]
    [InlineData("CaptureView.xaml")]
    [InlineData("WorkspaceDetailsView.xaml")]
    [InlineData("AddressEditorView.xaml")]
    [InlineData("OpenWithEditorView.xaml")]
    [InlineData("ResourcePickerView.xaml")]
    [InlineData("CardDesignEditorView.xaml")]
    [InlineData("CategoryEditorView.xaml")]
    public void Product_views_are_well_formed_without_generator_tokens(string file)
    {
        var source = ViewSource(file);
        XDocument.Parse(source);
        Assert.DoesNotContain("'" + "''", source);
        Assert.DoesNotContain("+ palette +", source);
    }

    [Fact] public void MainWindow_xaml_has_no_unsubstituted_generator_tokens()
    {
        var xaml = ViewSource();
        Assert.DoesNotContain("'''", xaml);
        Assert.DoesNotContain("+ palette +", xaml);
    }

    [Fact] public void Both_create_forms_have_a_real_palette_control()
    {
        foreach (var file in new[] { "CaptureView.xaml", "CreateWorkspaceView.xaml", "CardDesignEditorView.xaml" })
        {
            var document = XDocument.Parse(ViewSource(file));
            var picker = Assert.Single(document.Descendants(), x => x.Name.LocalName == "WorkspaceColorPicker");
            Assert.Equal("{Binding DesignPalette}", picker.Attribute("Palette")?.Value);
            Assert.Equal(file == "CardDesignEditorView.xaml" ? "{Binding DesignColor}" : "{Binding NewWorkspaceColor}", picker.Attribute("SelectedColor")?.Value);
            Assert.Equal(file == "CardDesignEditorView.xaml" ? "design" : "create", picker.Attribute("Target")?.Value);
        }
        var component = ViewSource("Components/WorkspaceColorPicker.cs");
        Assert.Contains("new ItemsControl()", component); Assert.Contains("swatches.ItemTemplate", component);
        Assert.Contains("SetCurrentValue(SelectedColorProperty", component); Assert.Contains("new AdvancedColorWindow", component);
    }
    [Fact] public void Active_color_consumers_use_shared_picker_contract_not_removed_textboxes()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".github", "e2e"))) dir = dir.Parent;
        Assert.NotNull(dir);
        foreach (var name in new[] { "capture-flow.ps1", "cycle1-flow.ps1", "tabs-only-flow.ps1" })
        {
            var text = File.ReadAllText(Path.Combine(dir!.FullName, ".github", "e2e", name));
            Assert.Contains("workspace-colors.ps1", text);
            Assert.DoesNotContain("NamedEdit 'Card color'", text); Assert.DoesNotContain("NamedEdit 'Capture card color'", text);
            Assert.DoesNotContain("Create color gold", text);
        }
        var capture = File.ReadAllText(Path.Combine(dir!.FullName, ".github", "e2e", "capture-flow.ps1"));
        Assert.Contains("all 9 capture palette", capture); Assert.Contains("28.0 * $dpiX", capture);
    }
}
