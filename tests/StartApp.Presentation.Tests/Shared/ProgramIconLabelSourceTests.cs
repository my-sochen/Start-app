using Xunit;
namespace StartApp.Presentation.Tests;
public class ProgramIconLabelSourceTests
{
    [Fact] public void Loading_success_and_missing_icon_share_the_Hebrew_prefix()
    {
        var d=new DirectoryInfo(AppContext.BaseDirectory);while(d is not null && !File.Exists(Path.Combine(d.FullName,"src/StartApp.App/ProgramIconConverter.cs")))d=d.Parent;
        Assert.NotNull(d);var s=File.ReadAllText(Path.Combine(d!.FullName,"src/StartApp.App/ProgramIconConverter.cs"));
        Assert.DoesNotContain("Opens with:",s);Assert.Equal(1,s.Split("נפתח באמצעות: ").Length-1);
        Assert.Contains("identity.Children.Add(_icon); identity.Children.Add(_label)",s);
        Assert.Contains("Children.Add(_prefix); Children.Add(identity)",s);
        Assert.Contains("_label.ToolTip = path",s);Assert.Contains("ProgramDisplayNames.Resolve(path)",s);Assert.Contains("gen != _generation",s);
    }
}
