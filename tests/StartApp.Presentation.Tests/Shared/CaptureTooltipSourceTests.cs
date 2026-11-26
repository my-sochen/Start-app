using System.Xml.Linq;
using Xunit;
namespace StartApp.Presentation.Tests;
public class CaptureTooltipSourceTests
{
    [Fact] public void Capture_note_keeps_all_limitations_in_a_bounded_wrapped_tooltip()
    {
        var d=new DirectoryInfo(AppContext.BaseDirectory);
        while(d is not null && !File.Exists(Path.Combine(d.FullName,"src/StartApp.App/CaptureView.xaml")))d=d.Parent;
        Assert.NotNull(d);
        XNamespace p="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var doc=XDocument.Load(Path.Combine(d!.FullName,"src/StartApp.App/CaptureView.xaml"));
        var note=doc.Descendants(p+"TextBlock").Single(e=>((string?)e.Attribute("Text"))?.StartsWith("לא נמצא קובץ?")==true);
        var tip=note.Descendants(p+"ToolTip").Single();var text=tip.Element(p+"TextBlock")!;
        Assert.Equal("Top",(string?)tip.Attribute("Placement"));Assert.Equal("360",(string?)tip.Attribute("MaxWidth"));
        Assert.Equal("Wrap",(string?)text.Attribute("TextWrapping"));Assert.Equal("Right",(string?)text.Attribute("TextAlignment"));
        var body=(string)text.Attribute("Text")!;
        foreach(var detail in new[]{"חלונות ולשוניות נגישים","מסמכים פתוחים","חלונות מוגנים","פרופיל אינו נשמר","להוסיף פריטים ידנית"})Assert.Contains(detail,body);
    }
}
