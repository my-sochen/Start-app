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
}
