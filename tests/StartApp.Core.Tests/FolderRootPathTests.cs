using StartApp.Core;

public class FolderRootPathTests
{
    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"z:\")]
    [InlineData(@"\\server\share")]
    [InlineData(@"\\server\share\")]
    [InlineData(@"\\שרת\תיקיה משותפת")]
    public void Explicit_folder_roots_pass_form_checks_without_changing_saved_text(string target)
    {
        var folder = new Item("i", ItemType.Folder, "root", target);
        Assert.Null(PreflightRules.CheckForm(folder, null));
        Assert.Equal(target, folder.Target);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"\\server\share")]
    [InlineData(@"\\server\share\")]
    public void Roots_do_not_become_valid_executable_or_file_paths(string target)
    {
        Assert.NotNull(Catalog.ValidateLaunch(ItemType.Link, new LaunchConfig(Browser: target)));
        Assert.NotNull(Catalog.ValidateLaunch(ItemType.File, new LaunchConfig(OpenWith: target)));
        Assert.Equal(PreflightState.Unsupported, PreflightRules.CheckForm(new Item("i", ItemType.Program, "p", target), null)?.State);
        Assert.Equal(PreflightState.Unsupported, PreflightRules.CheckForm(new Item("i", ItemType.File, "f", target), null)?.State);
    }

    [Theory]
    [InlineData(@"C:")]
    [InlineData(@"C:relative")]
    [InlineData(@"\rooted")]
    [InlineData(@"\\server")]
    [InlineData(@"\\server\")]
    [InlineData(@"\\?\share")]
    [InlineData(@"\\.\share")]
    [InlineData(@"\\server\..")]
    [InlineData(@"\\server\share:stream")]
    public void Ambiguous_or_device_roots_stay_unsupported(string target)
    {
        Assert.Equal(PreflightState.Unsupported, PreflightRules.CheckForm(new Item("i", ItemType.Folder, "f", target), null)?.State);
    }

    [Theory]
    [InlineData("file:///C:/", @"C:\")]
    [InlineData("file://server/share", @"\\server\share")]
    [InlineData("file://server/share/", @"\\server\share\")]
    public void Explorer_folder_root_urls_are_retained(string url, string expected)
    {
        Assert.Equal(expected, CaptureRules.FileUrlToPath(url));
    }
}
