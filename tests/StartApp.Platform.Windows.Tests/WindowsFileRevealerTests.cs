using StartApp.Core;
using StartApp.Platform.Windows;

public class WindowsFileRevealerTests
{
    private sealed class StubProbe(ProbeResult result) : IFileSystemProbe
    {
        public int Calls;
        public ProbeResult Probe(string path) { Calls++; return result; }
    }
    [Theory]
    [InlineData(@"C:\Users\Example User\מסמך.txt")]
    [InlineData(@"C:\Tools\editor.exe")]
    [InlineData(@"\\server\share\folder\report.txt")]
    [InlineData(@"C:\a,b & c\report.lnk")]
    public void Selects_exact_saved_entry_without_launching_it(string path)
    {
        var probe=new StubProbe(new(ProbeKind.File));var selected=new List<string>();
        var result=new WindowsFileRevealer(probe,selected.Add).RevealAsync(path).GetAwaiter().GetResult();
        Assert.Equal(RevealStatus.Revealed,result.Status);Assert.Equal(new[]{path},selected);Assert.Equal(1,probe.Calls);
    }
    [Theory]
    [InlineData("")][InlineData("editor.exe")][InlineData("https://example.com/")]
    [InlineData(@"shell:AppsFolder")][InlineData(@"C:\a.txt:stream")]
    [InlineData(@"\\?\C:\a.txt")][InlineData(@"\\.\C:\a.txt")]
    [InlineData(@"C:\folder\..\a.txt")][InlineData(@"C:\folder\a.txt ")]
    [InlineData(@"C:\folder\a*.txt")][InlineData("C:\\folder\\a\n.txt")]
    public void Invalid_or_ambiguous_paths_never_reach_probe_or_shell(string path)
    {
        var probe=new StubProbe(new(ProbeKind.File));var selected=new List<string>();
        Assert.Equal(RevealStatus.Invalid,new WindowsFileRevealer(probe,selected.Add).RevealAsync(path).GetAwaiter().GetResult().Status);
        Assert.Equal(0,probe.Calls);Assert.Empty(selected);
    }
    [Theory]
    [InlineData(ProbeKind.NotFound,RevealStatus.Missing)]
    [InlineData(ProbeKind.Error,RevealStatus.Unavailable)]
    [InlineData(ProbeKind.Directory,RevealStatus.Invalid)]
    public void Failed_probe_is_honest_and_never_opens_another_location(ProbeKind kind,RevealStatus expected)
    {
        var selected=new List<string>();var r=new WindowsFileRevealer(new StubProbe(new(kind,"reason")),selected.Add).RevealAsync(@"C:\a.txt").GetAwaiter().GetResult();
        Assert.Equal(expected,r.Status);Assert.Empty(selected);
    }
    [Fact] public void Select_failure_returns_unavailable_without_retry()
    {
        var count=0;var r=new WindowsFileRevealer(new StubProbe(new(ProbeKind.File)),_=>{count++;throw new System.Runtime.InteropServices.COMException("fixture");}).RevealAsync(@"C:\a.txt").GetAwaiter().GetResult();
        Assert.Equal(RevealStatus.Unavailable,r.Status);Assert.Equal(1,count);Assert.Contains("COMException",r.Detail);
    }
    [Fact] public void Reparse_entry_is_selected_as_an_entry_not_followed_or_run()
    {
        string? selected=null;var r=new WindowsFileRevealer(new StubProbe(new(ProbeKind.File,Reparse:true)),p=>selected=p).RevealAsync(@"C:\link.lnk").GetAwaiter().GetResult();
        Assert.Equal(RevealStatus.Revealed,r.Status);Assert.Equal(@"C:\link.lnk",selected);
    }
}

public class WindowsFileRevealerWorkerTests
{
    private sealed class BlockingProbe(Action run) : IFileSystemProbe
    {
        public ProbeResult Probe(string path) { run(); return new(ProbeKind.File); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Blocking_probe_or_shell_never_blocks_caller_and_retains_gate(bool blockProbe)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var caller = Environment.CurrentManagedThreadId;
        var workerId = 0; var selectedId = 0; var calls = 0;
        void Block() { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); }
        var probe = new BlockingProbe(() => { workerId = Environment.CurrentManagedThreadId; if (blockProbe) Block(); });
        var adapter = new WindowsFileRevealer(probe, _ =>
        {
            selectedId = Environment.CurrentManagedThreadId;
            Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
            Assert.True(Thread.CurrentThread.IsBackground);
            Interlocked.Increment(ref calls);
            if (!blockProbe) Block();
        });
        var pending = adapter.RevealAsync(@"C:\fixture.txt");
        try
        {
            Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(5))));
            Assert.False(pending.IsCompleted);
            Assert.Equal(RevealStatus.Unavailable, (await adapter.RevealAsync(@"C:\second.txt")).Status);
            // Dropping/abandoning an await does not free the gate or spawn another shell worker.
            await Assert.ThrowsAsync<TimeoutException>(() => pending.WaitAsync(TimeSpan.FromMilliseconds(25)));
            Assert.Equal(RevealStatus.Unavailable, (await adapter.RevealAsync(@"C:\third.txt")).Status);
        }
        finally { release.Set(); }
        Assert.Equal(RevealStatus.Revealed, (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        Assert.NotEqual(caller, workerId); Assert.Equal(workerId, selectedId); Assert.Equal(1, calls);
        Assert.Equal(RevealStatus.Revealed, (await adapter.RevealAsync(@"C:\next.txt")).Status);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Confirmation_and_single_readiness_retry_stay_on_the_same_STA()
    {
        var ids = new List<int>(); var apartments = new List<ApartmentState>();
        var requests = 0; var polls = 0;
        void Record() { ids.Add(Environment.CurrentManagedThreadId); apartments.Add(Thread.CurrentThread.GetApartmentState()); }
        var adapter = new WindowsFileRevealer(new BlockingProbe(Record), _ => { Record(); requests++; }, _ =>
        { Record(); polls++; return (polls >= 3, true); });
        Assert.Equal(RevealStatus.Revealed, (await adapter.RevealAsync(@"C:\fixture.txt")).Status);
        Assert.Equal(2, requests); Assert.Equal(3, polls);
        Assert.Single(ids.Distinct()); Assert.All(apartments, a => Assert.Equal(ApartmentState.STA, a));
    }

    [Fact]
    public async Task Unconfirmed_selection_returns_failure_without_repeated_requests()
    {
        var requests = 0;
        var adapter = new WindowsFileRevealer(new BlockingProbe(() => {}), _ => requests++, _ => (false, true));
        Assert.Equal(RevealStatus.Unavailable, (await adapter.RevealAsync(@"C:\fixture.txt")).Status);
        Assert.Equal(2, requests);
    }
}
