using StartApp.Presentation;
using Xunit;

namespace StartApp.Presentation.Tests;

/// <summary>The icon + name display (settled Nov 14): the details row shows the program icon and a short name instead
/// of the full path; the full path stays in the tooltip / open-with popup, and a missing or unreadable exe falls back
/// to the full-path text. These tests cover the name resolution; the icon extraction and the visual fallback are
/// covered by the Windows e2e (icon element + automation name present, tooltip keeps the path, missing-exe row keeps
/// the path text).</summary>
public class ProgramDisplayNameTests
{
    [Theory]
    [InlineData(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", "Edge")]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", "Chrome")]
    [InlineData(@"C:\Program Files\Mozilla Firefox\firefox.exe", "Firefox")]
    [InlineData(@"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe", "Brave")]
    [InlineData(@"C:\Program Files\Opera\opera.exe", "Opera")]
    [InlineData(@"c:\program files\google\chrome\application\CHROME.EXE", "Chrome")] // case-insensitive file name
    public void Known_browser_exes_map_to_the_browser_name(string path, string expected)
    {
        Assert.Equal(expected, ProgramDisplayNames.Resolve(path));
    }

    [Fact]
    public void Unknown_exe_uses_the_version_info_description_when_present()
    {
        var original = ProgramDisplayNames.VersionInfoName;
        try
        {
            ProgramDisplayNames.VersionInfoName = _ => "Notepad";
            Assert.Equal("Notepad", ProgramDisplayNames.Resolve(@"C:\Windows\notepad.exe"));
        }
        finally { ProgramDisplayNames.VersionInfoName = original; }
    }

    [Fact]
    public void Unknown_exe_with_no_readable_version_info_falls_back_to_the_file_name()
    {
        var original = ProgramDisplayNames.VersionInfoName;
        try
        {
            ProgramDisplayNames.VersionInfoName = _ => null;
            Assert.Equal("my-tool", ProgramDisplayNames.Resolve(@"D:\tools\my-tool.exe"));
        }
        finally { ProgramDisplayNames.VersionInfoName = original; }
    }

    [Fact]
    public void Missing_exe_falls_back_to_the_file_name_without_version_info()
    {
        // the default provider cannot read a path that does not exist on any platform
        Assert.Equal("definitely-not-here", ProgramDisplayNames.Resolve(@"C:\no\such\definitely-not-here.exe"));
    }

    [Fact]
    public void Empty_paths_resolve_empty()
    {
        Assert.Equal("", ProgramDisplayNames.Resolve(null));
        Assert.Equal("", ProgramDisplayNames.Resolve(""));
        Assert.Equal("", ProgramDisplayNames.Resolve("   "));
    }

    [Fact]
    public void ItemRow_carries_the_display_name_and_keeps_the_full_path_untouched()
    {
        var row = new ItemRow("i1", "it1", "site", "https://x.example/", "Link", "Sites", null, @"C:\Program Files\Google\Chrome\Application\chrome.exe");
        Assert.Equal("Chrome", row.ProgramDisplayName);
        Assert.Equal(@"C:\Program Files\Google\Chrome\Application\chrome.exe", row.Program); // tooltip / help-text source unchanged
    }
}
