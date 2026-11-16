using StartApp.Core;
namespace StartApp.Core.Tests;
public class BrowserCapturePolicyTests
{
    private sealed class WindowsSource : IOpenItemCapturer
    {
        public int Calls;
        public CaptureScan Scan() { Calls++; return new(new[] { new CapturedItem(ItemType.Program, "Synthetic program", @"C:\dummy.exe"), new CapturedItem(ItemType.Folder, "Synthetic folder", @"C:\dummy") }, new[] { "Original Windows note" }); }
    }
    [Fact] public void Inert_browser_source_returns_no_links_and_honest_note()
    { var scan = new BrowserCaptureUnavailable().Scan(); Assert.Empty(scan.Items); Assert.Equal(new[] { BrowserCapturePolicy.UnavailableNote }, scan.Notes); }
    [Fact] public void Composition_preserves_Windows_items_and_notes_without_browser_source()
    { var windows = new WindowsSource(); var scan = BrowserCapturePolicy.Compose(windows).Scan(); Assert.Equal(1, windows.Calls); Assert.Equal(2, scan.Items.Count); Assert.Contains(scan.Items, x => x.Type == ItemType.Program); Assert.Contains(scan.Items, x => x.Type == ItemType.Folder); Assert.DoesNotContain(scan.Items, x => x.Type == ItemType.Link); Assert.Contains("Original Windows note", scan.Notes); Assert.Contains(BrowserCapturePolicy.UnavailableNote, scan.Notes); }
    [Fact] public void Addresses_are_fixed_exact_internal_addresses_not_launch_commands()
    { Assert.Equal("chrome://inspect/#remote-debugging", BrowserCapturePolicy.ChromeSettingsAddress); Assert.Equal("edge://inspect/#remote-debugging", BrowserCapturePolicy.EdgeSettingsAddress); }
}
