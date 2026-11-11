using StartApp.Core;
using StartApp.Platform.Windows;
using Xunit;

public class PlatformTests
{
    private sealed class Rec : IProcessStarter { public List<StartRequest> Calls = new(); public Exception? Throw; public void Start(StartRequest r) { if (Throw != null) throw Throw; Calls.Add(r); } }
    private sealed class FakeProbe(Func<string, ProbeResult> f) : IFileSystemProbe { public ProbeResult Probe(string p) => f(p); }
    private static Item It(ItemType t, string target) => new("it_1", t, "n", target);

    [Fact] public void Program_StartsExactPath_NoArgs_NoShell()
    {
        var r = new Rec(); var res = new WindowsLauncher(r).Launch(It(ItemType.Program, "C:\\Apps\\a b & c.exe"), null);
        Assert.Equal(new StartRequest("C:\\Apps\\a b & c.exe", [], false), r.Calls[0]); Assert.Equal(Ownership.Unknown, res.Ownership); Assert.Null(res.OpaqueHandle);
    }
    [Fact] public void File_NoOpenWith_UsesShellWithSinglePath()
    {
        var r = new Rec(); new WindowsLauncher(r).Launch(It(ItemType.File, "C:\\d\\x y.txt"), null);
        Assert.Equal(new StartRequest("C:\\d\\x y.txt", [], true), r.Calls[0]);
    }
    [Fact] public void File_OpenWith_PassesPathAsOneArgument_NoShell()
    {
        var r = new Rec(); new WindowsLauncher(r).Launch(It(ItemType.File, "C:\\d\\x y&z.txt"), new LaunchConfig(OpenWith: "C:\\Apps\\ed.exe"));
        Assert.Equal("C:\\Apps\\ed.exe", r.Calls[0].FileName); Assert.Equal(new[] { "C:\\d\\x y&z.txt" }, r.Calls[0].Arguments); Assert.False(r.Calls[0].UseShell);
    }
    [Fact] public void Link_Browser_PassesUrlAsOneArgument()
    {
        var r = new Rec(); new WindowsLauncher(r).Launch(It(ItemType.Link, "https://e.com/?a=1&b=2"), new LaunchConfig(Browser: "C:\\B\\b.exe"));
        Assert.Equal(new[] { "https://e.com/?a=1&b=2" }, r.Calls[0].Arguments); Assert.False(r.Calls[0].UseShell);
    }
    [Theory] [InlineData(ItemType.Link, "file:///C:/x.exe")] [InlineData(ItemType.Program, "calc")] [InlineData(ItemType.Link, "ms-settings:")]
    public void NeverStartsUnsupported(ItemType t, string target)
    {
        var r = new Rec(); var res = new WindowsLauncher(r).Launch(It(t, target), null);
        Assert.Empty(r.Calls); Assert.Equal(LaunchStatus.Unavailable, res.Status);
    }
    [Fact] public void StartFailure_IsFailedWithReason_NotStarted()
    {
        var r = new Rec { Throw = new System.ComponentModel.Win32Exception("not found") };
        var res = new WindowsLauncher(r).Launch(It(ItemType.Program, "C:\\a.exe"), null);
        Assert.Equal(LaunchStatus.Failed, res.Status); Assert.Contains("Win32Exception", res.Detail);
    }
    [Fact] public void ProgrammingErrors_StayLoud()
    {
        var r = new Rec { Throw = new NullReferenceException() };
        Assert.Throws<NullReferenceException>(() => new WindowsLauncher(r).Launch(It(ItemType.Program, "C:\\a.exe"), null));
    }
    [Theory]
    [InlineData(typeof(FileNotFoundException), ProbeKind.NotFound)] [InlineData(typeof(DirectoryNotFoundException), ProbeKind.NotFound)]
    [InlineData(typeof(UnauthorizedAccessException), ProbeKind.Error)] [InlineData(typeof(IOException), ProbeKind.Error)]
    [InlineData(typeof(PathTooLongException), ProbeKind.Error)] [InlineData(typeof(NotSupportedException), ProbeKind.Error)] [InlineData(typeof(ArgumentException), ProbeKind.Error)]
    public void ExceptionMapping_DistinguishesAbsentFromUnreadable(Type e, ProbeKind kind) => Assert.Equal(kind, ProbeMapping.FromException((Exception)Activator.CreateInstance(e)!).Kind);
    [Fact] public void Preflight_MapsProbeResults()
    {
        PreflightFinding Run(ProbeResult pr, ItemType t) => new FileSystemPreflight(new FakeProbe(_ => pr)).Check(It(t, "C:\\x"), null);
        Assert.Equal(PreflightState.Missing, Run(new(ProbeKind.NotFound), ItemType.File).State);
        Assert.Equal(PreflightState.Unavailable, Run(new(ProbeKind.Error, "UnauthorizedAccessException"), ItemType.File).State);
        Assert.Equal(PreflightState.Unsupported, Run(new(ProbeKind.Directory), ItemType.File).State);
        Assert.Equal(PreflightState.Unsupported, Run(new(ProbeKind.File), ItemType.Folder).State);
        Assert.Equal(PreflightState.Ready, Run(new(ProbeKind.Directory), ItemType.Folder).State);
        Assert.Equal(PreflightState.Ready, Run(new(ProbeKind.File), ItemType.Program).State);
    }
    [Fact] public void Preflight_ChecksExplicitLaunchProgramToo_AndNeverProbesLinks()
    {
        var probed = new List<string>(); var pf = new FileSystemPreflight(new FakeProbe(p => { probed.Add(p); return p.Contains("ed.exe") ? new(ProbeKind.NotFound) : new(ProbeKind.File); }));
        var f = pf.Check(It(ItemType.File, "C:\\a.txt"), new LaunchConfig(OpenWith: "C:\\Apps\\ed.exe"));
        Assert.Equal(PreflightState.Missing, f.State); Assert.Contains("launch program", f.Detail);
        probed.Clear(); pf.Check(It(ItemType.Link, "https://e.com"), null); Assert.Empty(probed);
    }
    [Fact] public void RealProbe_OnLocalTempTree()
    {
        var d = Directory.CreateTempSubdirectory().FullName; var f = Path.Combine(d, "a.txt"); File.WriteAllText(f, "x");
        var p = new FileSystemProbe();
        Assert.Equal(ProbeKind.File, p.Probe(f).Kind); Assert.Equal(ProbeKind.Directory, p.Probe(d).Kind); Assert.Equal(ProbeKind.NotFound, p.Probe(Path.Combine(d, "nope")).Kind);
        Directory.Delete(d, true);
    }
    [Fact] public void Probe_Timeout_IsUnavailable_AndSlotsAreBounded()
    {
        var gate = new ManualResetEventSlim(false); var p = new FileSystemProbe(TimeSpan.FromMilliseconds(30));
        var results = new List<ProbeResult>();
        for (int i = 0; i < FileSystemProbe.MaxInFlight + 3; i++) results.Add(p.Probe("C:\\x", _ => { gate.Wait(); return new(ProbeKind.File); }));
        Assert.Equal(FileSystemProbe.MaxInFlight, results.Count(r => r.Reason == "check timed out"));
        Assert.Equal(3, results.Count(r => r.Reason == "too many unfinished checks"));
        gate.Set(); Thread.Sleep(300);
        Assert.Equal(ProbeKind.File, new FileSystemProbe().Probe("C:\\y", _ => new(ProbeKind.File)).Kind); Assert.NotEqual("too many unfinished checks", p.Probe("C:\\y", _ => new(ProbeKind.File)).Reason);
    }
    [Theory]
    [InlineData("C:\\d\\a.exe")] [InlineData("C:\\d\\a.BAT")] [InlineData("C:\\d\\a.cmd")] [InlineData("C:\\d\\a.ps1")] [InlineData("C:\\d\\a.vbs")] [InlineData("C:\\d\\a.js")]
    [InlineData("C:\\d\\a.hta")] [InlineData("C:\\d\\a.lnk")] [InlineData("C:\\d\\a.url")] [InlineData("C:\\d\\a.msi")] [InlineData("C:\\d\\a.scr")] [InlineData("C:\\d\\a.pif")]
    [InlineData("C:\\d\\a.exe.")] [InlineData("C:\\d\\a.exe ")] [InlineData("C:\\d\\a.txt:evil.exe")] [InlineData("\\\\srv\\share\\a.cmd")]
    public void File_ExecutableOrScriptCapable_NeverReachesTheShell(string path)
    {
        foreach (var t in new[] { ItemType.File, ItemType.Folder })
        {
            var r = new Rec(); var res = new WindowsLauncher(r).Launch(It(t, path), null);
            Assert.Empty(r.Calls); Assert.Equal(LaunchStatus.Unavailable, res.Status);
        }
    }
    [Fact] public void File_Document_StillReachesShell_ButOpenWithDoesNotExemptCodeCapableTargetsOrInterpreters()
    {
        var r = new Rec(); var l = new WindowsLauncher(r);
        l.Launch(It(ItemType.File, "C:\\d\\a.docx"), null);
        Assert.Single(r.Calls); Assert.True(r.Calls[0].UseShell);
        l.Launch(It(ItemType.File, "C:\\d\\a.ps1"), new LaunchConfig(OpenWith: "C:\\Apps\\ed.exe"));
        l.Launch(It(ItemType.File, "C:\\d\\a.txt"), new LaunchConfig(OpenWith: "C:\\Windows\\System32\\cmd.exe"));
        l.Launch(It(ItemType.File, "C:\\d\\a.txt"), new LaunchConfig(OpenWith: "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\PowerShell.EXE"));
        Assert.Single(r.Calls);
    }
    [Theory]
    [InlineData("https://user@evil.example/")] [InlineData("https://user:pw@evil.example/")] [InlineData("http://exa%6dple.com/")] [InlineData("https://example.com\\@evil.example/")]
    [InlineData("https://\u00e9xample.com/")] [InlineData("https:///nohost")] [InlineData("http://exa mple.com/")] [InlineData("https://example.com/a\tb")]
    public void AmbiguousHttpForms_AreRefusedAsNeedsDecision_AndNotRewritten(string link)
    {
        var f = PreflightRules.CheckForm(It(ItemType.Link, link), null);
        Assert.Equal(PreflightState.NeedsDecision, f!.State);
        var r = new Rec(); Assert.Equal(LaunchStatus.Unavailable, new WindowsLauncher(r).Launch(It(ItemType.Link, link), null).Status); Assert.Empty(r.Calls);
    }
    [Theory] [InlineData("http://example.com")] [InlineData("HTTPS://Example.com:8443/a/b?x=1&y=2#frag")] [InlineData("https://192.168.0.1/")] [InlineData("https://example.com/caf\u00e9")]
    public void PlainHttpForms_AreAccepted_AndLaunchedWithTheSavedBytes(string link)
    {
        Assert.Null(PreflightRules.CheckForm(It(ItemType.Link, link), null));
        var r = new Rec(); new WindowsLauncher(r).Launch(It(ItemType.Link, link), null); Assert.Equal(link, r.Calls[0].FileName);
    }
    [Fact] public void ReparsePoints_AndLinkedLaunchPrograms_NeedADecision()
    {
        PreflightFinding Run(ProbeResult pr, ItemType t, LaunchConfig? l = null) => new FileSystemPreflight(new FakeProbe(_ => pr)).Check(It(t, "C:\\x"), l);
        Assert.Equal(PreflightState.NeedsDecision, Run(new(ProbeKind.File, null, true), ItemType.File).State);
        Assert.Equal(PreflightState.NeedsDecision, Run(new(ProbeKind.Directory, null, true), ItemType.Folder).State);
        Assert.Equal(PreflightState.NeedsDecision, Run(new(ProbeKind.File, null, true), ItemType.Program).State);
        var pf = new FileSystemPreflight(new FakeProbe(p => p.Contains("ed.exe") ? new(ProbeKind.File, null, true) : new(ProbeKind.File)));
        Assert.Equal(PreflightState.NeedsDecision, pf.Check(It(ItemType.File, "C:\\a.txt"), new LaunchConfig(OpenWith: "C:\\Apps\\ed.exe")).State);
    }

    [Fact] public void NormalComposition_ChecksBeforeLaunching_AndBlockedClassNeverReachesTheStarter()
    {
        if (!OperatingSystem.IsWindows()) return; // the real probe/preflight use Windows path rules; this runs on a Windows runner only (temp files, recording starter, no real launch)
        var dir = Path.Combine(Path.GetTempPath(), "sa-comp-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var ok = Path.Combine(dir, "ok.txt"); File.WriteAllText(ok, "x"); var ps = Path.Combine(dir, "run.ps1"); File.WriteAllText(ps, "x");
            var rec = new Rec(); var (launcher, pre) = WindowsComposition.Create(rec);
            Assert.Equal(PreflightState.Ready, pre.Check(new Item("a", ItemType.File, "ok", ok), null).State);
            Assert.Equal(PreflightState.NeedsDecision, pre.Check(new Item("b", ItemType.File, "ps", ps), null).State);
            Assert.Equal(PreflightState.Missing, pre.Check(new Item("c", ItemType.File, "no", Path.Combine(dir, "none.txt")), null).State);
            Assert.Empty(rec.Calls);                                // checking starts nothing
            launcher.Launch(new Item("a", ItemType.File, "ok", ok), null);
            Assert.Single(rec.Calls); Assert.Equal(ok, rec.Calls[0].FileName); Assert.True(rec.Calls[0].UseShell);   // recorded only: the real shell was not used in this test
            var blocked = launcher.Launch(new Item("b", ItemType.File, "ps", ps), null);
            Assert.NotEqual(LaunchStatus.Started, blocked.Status); Assert.Single(rec.Calls);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact] public void NormalComposition_DefaultStarter_IsCreatedWithoutStartingAnything()
    {
        var (l, p) = WindowsComposition.Create(); Assert.IsType<WindowsLauncher>(l); Assert.IsType<FileSystemPreflight>(p);
    }
}
