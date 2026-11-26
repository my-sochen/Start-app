using System.Xml.Linq;
using Xunit;
namespace StartApp.Presentation.Tests;

public class SharedFieldStyleTests
{
    private static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static XDocument Read(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "src", "StartApp.App", file);
            if (File.Exists(path)) return XDocument.Load(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(file);
    }
    private static XElement Style(string file, string key) => Read(file).Descendants(P + "Style").Single(x => (string?)x.Attribute(X + "Key") == key);
    [Fact] public void Context_open_with_reuses_the_same_noneditable_dropdown()
    {
        var doc=Read("OpenWithEditorView.xaml");var combo=doc.Descendants(P+"ComboBox").Single();
        Assert.Equal("{StaticResource OpenWithDropdown}",(string?)combo.Attribute("Style"));
        Assert.Null(combo.Attribute("IsEditable"));Assert.Empty(doc.Descendants(P+"TextBox"));
        XNamespace local="clr-namespace:StartApp.App";
        Assert.Equal("{Binding OpenWithFileTarget}",(string?)combo.Attribute(local+"OpenWithPicker.FileTarget"));
        Assert.Equal("{Binding OpenWithTarget}",(string?)combo.Attribute(local+"OpenWithPicker.WebsiteTarget"));
    }
    [Theory]
    [InlineData("FieldInput", "DetailsInput")]
    [InlineData("FieldCombo", "DetailsCombo")]
    [InlineData("FieldLabel", "DetailsLabel")]
    public void Create_aliases_reuse_existing_styles_without_local_overrides(string alias, string existing)
    {
        var style = Style("CreateWorkspaceView.xaml", alias);
        Assert.Equal("{StaticResource " + existing + "}", (string?)style.Attribute("BasedOn"));
        Assert.Empty(style.Elements());
        Assert.NotNull(Style("SharedResources.xaml", existing));
    }
    [Theory]
    [InlineData("ItemEditWindow.xaml")]
    [InlineData("ItemBatchEditWindow.xaml")]
    public void Item_editors_use_one_explicit_compact_variant(string file)
    {
        var document = Read(file);
        Assert.DoesNotContain(document.Descendants(P + "Style"), x => (string?)x.Attribute(X + "Key") == "Input");
        var fields = document.Descendants(P + "TextBox").ToArray();
        Assert.Equal(3, fields.Length);
        Assert.All(fields, x => Assert.Equal("{StaticResource CompactItemInput}", (string?)x.Attribute("Style")));
    }
    [Fact]
    public void Compact_variant_keeps_every_original_override()
    {
        var style = Style("SharedResources.xaml", "CompactItemInput");
        Assert.Equal("{StaticResource DetailsInput}", (string?)style.Attribute("BasedOn"));
        var expected = new Dictionary<string, string> { ["Height"]="34", ["MinHeight"]="34", ["FontSize"]="14", ["BorderBrush"]="#C5CDDB", ["Padding"]="10,6", ["Margin"]="0,4,0,10", ["TextAlignment"]="Right" };
        var actual = style.Elements(P + "Setter").ToDictionary(x => (string)x.Attribute("Property")!, x => (string)x.Attribute("Value")!);
        Assert.Equal(expected.OrderBy(x=>x.Key), actual.OrderBy(x=>x.Key));
    }
    [Fact]
    public void Open_with_remains_distinct_from_plain_selector()
    {
        var plain=Style("SharedResources.xaml", "DetailsCombo");
        var open=Style("SharedResources.xaml", "OpenWithDropdown");
        Assert.DoesNotContain(plain.Descendants(P+"Popup"), x=>(string?)x.Attribute(X+"Name")=="PART_Popup");
        Assert.Contains(open.Descendants(P+"Popup"), x=>(string?)x.Attribute(X+"Name")=="PART_Popup");
        Assert.Contains(open.Elements(P+"Setter"), x=>(string?)x.Attribute("Property")=="FocusVisualStyle");
        Assert.Contains(open.Descendants(P+"Trigger"), x=>(string?)x.Attribute("Property")=="IsEnabled");
    }
}
