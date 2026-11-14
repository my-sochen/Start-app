using StartApp.Core;
using Xunit;

namespace StartApp.Core.Tests;

/// <summary>Scan identity and browser naming for captured sites. The identity rule is the owner decision of Nov 14:
/// the same URL open in two browsers stays TWO choices, one row per browser with the browser named, so the user picks
/// what to keep; the software never picks a browser by scan order. Within one browser, or when the browser could not
/// be identified, the first row still wins.</summary>
public class CaptureBrowserIdentityTests
{
    private const string Edge = "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe";
    private const string Chrome = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";

    private static CapturedItem Site(string target, string? browser) =>
        new(ItemType.Link, "name", target, browser);

    [Fact] public void Dedupe_same_url_from_two_browsers_keeps_both_rows_each_with_its_browser()
    {
        var rows = CaptureRules.Dedupe(new[] { Site("https://example.com/", Edge), Site("https://example.com/", Chrome) });
        Assert.Equal(2, rows.Count);
        Assert.Equal(Edge, rows[0].Browser);
        Assert.Equal(Chrome, rows[1].Browser);
    }

    [Fact] public void Dedupe_same_url_in_the_same_browser_still_keeps_only_the_first_row()
    {
        var rows = CaptureRules.Dedupe(new[] { Site("https://example.com/", Chrome), Site("https://example.com/", Chrome) });
        var one = Assert.Single(rows);
        Assert.Equal(Chrome, one.Browser);
    }

    [Fact] public void Dedupe_same_url_with_no_identified_browser_still_keeps_only_the_first_row()
    {
        var rows = CaptureRules.Dedupe(new[] { Site("https://example.com/", null), Site("https://example.com/", null) });
        Assert.Single(rows);
    }

    [Fact] public void Dedupe_treats_an_identified_and_an_unidentified_capture_as_two_choices()
    {
        // The identified row carries its browser; the unidentified one is a genuinely different capture provenance,
        // so collapsing them would silently drop one. First-wins applies only inside the same identity.
        var rows = CaptureRules.Dedupe(new[] { Site("https://example.com/", Edge), Site("https://example.com/", null) });
        Assert.Equal(2, rows.Count);
    }

    [Fact] public void Dedupe_keeps_the_no_browser_behavior_for_non_site_types()
    {
        var rows = CaptureRules.Dedupe(new[]
        {
            new CapturedItem(ItemType.Program, "A", "C:\\a.exe"), new CapturedItem(ItemType.Program, "A again", "C:\\a.exe"),
            new CapturedItem(ItemType.Folder, "f", "C:\\a.exe"),
        });
        Assert.Equal(2, rows.Count); Assert.Equal("A", rows[0].Name);
    }

    [Theory]
    [InlineData(Edge, "Edge")]
    [InlineData(Chrome, "Chrome")]
    [InlineData("C:\\Firefox\\firefox.exe", "Firefox")]
    [InlineData("C:\\Brave\\brave.exe", "Brave")]
    [InlineData("C:\\Opera\\opera.exe", "Opera")]
    [InlineData("D:\\portable\\MyBrowser.exe", "MyBrowser")] // unknown executables keep their file stem as-is
    [InlineData("msedge.exe", "Edge")]                       // bare file name still maps
    public void BrowserDisplayName_maps_known_executables_and_keeps_unknown_stems(string path, string expected)
        => Assert.Equal(expected, CaptureRules.BrowserDisplayName(path));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void BrowserDisplayName_is_null_when_no_browser_was_identified(string? path)
        => Assert.Null(CaptureRules.BrowserDisplayName(path));
}
