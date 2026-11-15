using StartApp.Core;
using StartApp.Platform.Windows;
using Xunit;

/// <summary>A Link has no file target, but an explicit Browser program must get the same existence and reparse checks as OpenWith (it was skipped by an early return).</summary>
public class BrowserPreflightTests
{
    private sealed class FakeProbe(Func<string, ProbeResult> f) : IFileSystemProbe { public List<string> Seen = new(); public ProbeResult Probe(string p) { Seen.Add(p); return f(p); } }
    private static Item Link => new("l", ItemType.Link, "n", "https://example.com/");
    [Fact] public void Link_WithoutBrowser_ProbesNothing_IsReady()
    { var p = new FakeProbe(_ => new(ProbeKind.NotFound)); Assert.Equal(PreflightState.Ready, new FileSystemPreflight(p).Check(Link, null).State); Assert.Empty(p.Seen); }
    [Fact] public void Link_BrowserMissing_IsMissing()
    { var p = new FakeProbe(_ => new(ProbeKind.NotFound)); var f = new FileSystemPreflight(p).Check(Link, new LaunchConfig(Browser: "C:\\B\\b.exe")); Assert.Equal(PreflightState.Missing, f.State); Assert.Equal(new[] { "C:\\B\\b.exe" }, p.Seen); }
    [Fact] public void Link_BrowserIsReparsePoint_NeedsDecision()
    { var p = new FakeProbe(_ => new(ProbeKind.File, null, true)); Assert.Equal(PreflightState.NeedsDecision, new FileSystemPreflight(p).Check(Link, new LaunchConfig(Browser: "C:\\B\\b.exe")).State); }
    [Fact] public void Link_BrowserCheckFails_IsUnavailable_NotReady()
    { var p = new FakeProbe(_ => new(ProbeKind.Error, "timed out")); Assert.Equal(PreflightState.Unavailable, new FileSystemPreflight(p).Check(Link, new LaunchConfig(Browser: "C:\\B\\b.exe")).State); }
    [Fact] public void Link_BrowserIsFolder_IsUnsupported()
    { var p = new FakeProbe(_ => new(ProbeKind.Directory)); Assert.Equal(PreflightState.Unsupported, new FileSystemPreflight(p).Check(Link, new LaunchConfig(Browser: "C:\\B\\b.exe")).State); }
    [Fact] public void Link_BrowserOk_IsReady()
    { var p = new FakeProbe(_ => new(ProbeKind.File)); Assert.Equal(PreflightState.Ready, new FileSystemPreflight(p).Check(Link, new LaunchConfig(Browser: "C:\\B\\b.exe")).State); }
}
