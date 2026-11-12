using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

public class PreflightTests
{
    private sealed class FakeLauncher : IItemLauncher
    {
        public List<string> Launched = new();
        public LaunchResult Launch(Item item, LaunchConfig? l) { Launched.Add(item.Target); return item.Target.Contains("fail") ? new(LaunchStatus.Failed, Detail: "boom") : new(LaunchStatus.Started); }
    }
    private sealed class FakePreflight(Func<Item, PreflightFinding> f) : IItemPreflight
    {
        public int Calls; public PreflightFinding Check(Item i, LaunchConfig? l) { Calls++; return f(i); }
    }
    private static readonly Func<Item, PreflightFinding> Ready = _ => new(PreflightState.Ready);
    private static JsonStore Repo(params (ItemType t, string target)[] items)
    {
        var repo = new JsonStore(Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json"));
        repo.Mutate(c => { var w = c.CreateWorkspace("w"); foreach (var (t, tg) in items) c.AddToWorkspace(w.Id, c.GetOrAddItem(t, tg, tg).Id); });
        return repo;
    }
    private sealed class FixedDetector(OpenState s) : IOpenStateDetector { public OpenState Detect(Item i, LaunchConfig? l) => s; }
    private static MainViewModel Vm(JsonStore r, FakeLauncher l, Func<Item, PreflightFinding>? f = null, OpenState dup = OpenState.NotOpen) => new(r, l, new FakePreflight(f ?? Ready), new FixedDetector(dup));

    [Fact] public void AllReady_LaunchesWithoutAsking()
    {
        using var repo = Repo((ItemType.File, "C:\\a.txt")); var l = new FakeLauncher(); var vm = Vm(repo, l);
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait();
        Assert.False(vm.OpenDecisionPending); Assert.Single(l.Launched); Assert.Equal("Open requests sent for 1 item", vm.Status); Assert.Equal("C:\\a.txt: open request sent (not proof it loaded)", vm.OpenReport[0]);
    }
    [Fact] public void Missing_WarnsFirst_NothingLaunchedUntilUserDecides()
    {
        using var repo = Repo((ItemType.File, "C:\\a.txt"), (ItemType.File, "C:\\gone.txt")); var l = new FakeLauncher();
        var vm = Vm(repo, l, i => i.Target.EndsWith("gone.txt") ? new(PreflightState.Missing, "saved target") : new(PreflightState.Ready));
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait();
        Assert.True(vm.OpenDecisionPending); Assert.Empty(l.Launched);
        Assert.Contains("gone.txt", vm.OpenProblems[0]); Assert.Contains("not found", vm.OpenProblems[0]);
    }
    [Fact] public void OpenAnyway_AttemptsMissingAsSaved_ReportsEachItem_NoSubstitute()
    {
        using var repo = Repo((ItemType.File, "C:\\a.txt"), (ItemType.Program, "C:\\Apps\\gone-fail.exe")); var l = new FakeLauncher();
        var vm = Vm(repo, l, i => i.Type == ItemType.Program ? new(PreflightState.Missing) : new(PreflightState.Ready));
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait(); vm.ConfirmOpenAnywayCommand.Execute(null); vm.OpenTask.Wait();
        Assert.Equal(new[] { "C:\\a.txt", "C:\\Apps\\gone-fail.exe" }, l.Launched);
        Assert.Contains("1 of 2", vm.Status); Assert.Contains("C:\\Apps\\gone-fail.exe: failed (boom)", vm.OpenReport);
    }
    [Fact] public void Cancel_OpensNothing()
    {
        using var repo = Repo((ItemType.File, "C:\\gone.txt")); var l = new FakeLauncher(); var vm = Vm(repo, l, _ => new(PreflightState.Missing));
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait(); vm.CancelOpenCommand.Execute(null);
        Assert.Empty(l.Launched); Assert.Empty(vm.OpenProblems); Assert.False(vm.ConfirmOpenAnywayCommand.CanExecute(null));
    }
    [Theory]
    [InlineData(ItemType.Program, "notepad", "full path")]
    [InlineData(ItemType.Link, "file:///C:/Windows/System32/calc.exe", "http and https")]
    [InlineData(ItemType.Link, "myapp://do-thing", "http and https")]
    [InlineData(ItemType.Link, "javascript:alert(1)", "http and https")]
    [InlineData(ItemType.File, "relative\\a.txt", "full paths")]
    public void Unsupported_NeverLaunched_EvenAfterAnyway_AdapterNotConsulted(ItemType t, string target, string reason)
    {
        using var repo = Repo((t, target)); var l = new FakeLauncher(); var pf = new FakePreflight(Ready); var vm = new MainViewModel(repo, l, pf, new FixedDetector(OpenState.NotOpen));
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait();
        Assert.True(vm.OpenDecisionPending); Assert.Equal(0, pf.Calls); Assert.Contains(reason, vm.OpenProblems[0]);
        vm.ConfirmOpenAnywayCommand.Execute(null); vm.OpenTask.Wait();
        Assert.Empty(l.Launched); Assert.Contains("skipped by policy", vm.Status); Assert.Contains("skipped, not opened", vm.OpenReport[0]);
    }
    [Theory] [InlineData("http://example.com")] [InlineData("https://example.com/a?b=c")]
    public void HttpAndHttps_AreOpenable(string url)
    {
        using var repo = Repo((ItemType.Link, url)); var l = new FakeLauncher(); var vm = Vm(repo, l);
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait(); Assert.Single(l.Launched);
    }
    [Fact] public void Unavailable_IsWarned_ThenAttemptedOnlyAfterConfirm()
    {
        using var repo = Repo((ItemType.File, "C:\\a.txt")); var l = new FakeLauncher(); var vm = Vm(repo, l, _ => new(PreflightState.Unavailable, "UnauthorizedAccessException"));
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait(); Assert.Empty(l.Launched); Assert.Contains("UnauthorizedAccessException", vm.OpenProblems[0]);
        vm.ConfirmOpenAnywayCommand.Execute(null); vm.OpenTask.Wait(); Assert.Single(l.Launched);
    }
    [Fact] public void ChangingSelection_DropsPendingDecision()
    {
        using var repo = Repo((ItemType.File, "C:\\gone.txt")); var vm = Vm(repo, new FakeLauncher(), _ => new(PreflightState.Missing));
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait(); vm.Selected = null;
        Assert.False(vm.OpenDecisionPending); Assert.Empty(vm.OpenProblems);
    }

    [Fact] public void NoDetector_EveryItemIsUnknown_WarnedAsPossibleDuplicate_NeverClaimedDuplicate()
    {
        using var repo = Repo((ItemType.File, "C:\\a.txt")); var l = new FakeLauncher(); var vm = new MainViewModel(repo, l, new FakePreflight(Ready));
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait();
        Assert.True(vm.OpenDecisionPending); Assert.Empty(l.Launched);
        Assert.Contains("cannot tell whether it is already open", vm.OpenProblems[0]); Assert.DoesNotContain("confirmed", vm.OpenProblems[0]);
        vm.ConfirmOpenAnywayCommand.Execute(null); vm.OpenTask.Wait();
        Assert.Single(l.Launched); Assert.Contains("could not tell if it was already open", vm.OpenReport[0]);
    }
    [Fact] public void ConfirmedOpen_IsReportedAsConfirmed_AndStillUserChoice_NoSilentSkip()
    {
        using var repo = Repo((ItemType.File, "C:\\a.txt")); var l = new FakeLauncher(); var vm = Vm(repo, l, dup: OpenState.Open);
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait();
        Assert.Contains("already open (confirmed)", vm.OpenProblems[0]); Assert.Empty(l.Launched);
        vm.ConfirmOpenAnywayCommand.Execute(null); vm.OpenTask.Wait(); Assert.Single(l.Launched); Assert.Contains("was already open (confirmed)", vm.OpenReport[0]);
    }
    [Fact] public void MissingAndUnknown_AreBothShownForTheSameItem()
    {
        using var repo = Repo((ItemType.File, "C:\\a.txt")); var vm = Vm(repo, new FakeLauncher(), _ => new(PreflightState.Missing), OpenState.Unknown);
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait();
        Assert.Contains("not found", vm.OpenProblems[0]); Assert.Contains("may create a duplicate", vm.OpenProblems[0]);
    }

    private static (string, ItemType)[] Many(int n) => Enumerable.Range(0, n).Select(i => ($"C:\\d\\f{i}.txt", ItemType.File)).ToArray();
    [Fact] public void SlowChecks_DoNotBlockCaller_AndAggregateDeadlineApplies()
    {
        // The contract (MainViewModel.CheckThenOpenAsync): ONE aggregate deadline - Task.WhenAny(all workers, Task.Delay(OpenCheckTimeout)).
        // Checks still running at the deadline are ABANDONED, not awaited, and every unstarted/unfinished item is reported
        // "check did not finish in time". The proof below is deterministic: the checks stay blocked the whole time, so if
        // OpenTask completes at all before the gate opens, the wait cannot have depended on any check finishing.
        using var repo = Repo(Many(12).Select(x => (x.Item2, x.Item1)).ToArray()); var l = new FakeLauncher();
        var gate = new ManualResetEventSlim(false); int started = 0;
        var vm = new MainViewModel(repo, l, new FakePreflight(_ => { Interlocked.Increment(ref started); gate.Wait(TimeSpan.FromSeconds(30)); return new(PreflightState.Ready); }), new FixedDetector(OpenState.NotOpen)) { OpenCheckTimeout = TimeSpan.FromMilliseconds(400) };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        vm.OpenWorkspaceCommand.Execute(null);
        Assert.True(sw.ElapsedMilliseconds < 200, "Execute must return immediately"); Assert.True(vm.IsChecking); Assert.False(vm.OpenWorkspaceCommand.CanExecute(null));
        // Broad watchdog only: the semantics above are proven by completion-while-blocked, not by this number.
        Assert.True(vm.OpenTask.Wait(TimeSpan.FromSeconds(15)), "the aggregate deadline must end the wait while every check is still blocked");
        Assert.False(vm.IsChecking);
        Assert.Equal(12, vm.OpenProblems.Count); Assert.All(vm.OpenProblems, p => Assert.Contains("did not finish in time", p));
        Assert.InRange(Volatile.Read(ref started), 1, CheckWorkersUpperBound); // workers blocked on their first item; the queue behind them was abandoned
        Assert.Empty(l.Launched);
        gate.Set(); // let the abandoned workers exit; their late results must not change the published outcome
        Assert.Equal(12, vm.OpenProblems.Count);
    }
    private const int CheckWorkersUpperBound = 4;
    [Fact] public void Checks_UseAtMostFourWorkers()
    {
        using var repo = Repo(Many(20).Select(x => (x.Item2, x.Item1)).ToArray()); int cur = 0, max = 0;
        var vm = new MainViewModel(repo, new FakeLauncher(), new FakePreflight(_ => { var c = Interlocked.Increment(ref cur); lock (repo) max = Math.Max(max, c); Thread.Sleep(20); Interlocked.Decrement(ref cur); return new(PreflightState.Ready); }), new FixedDetector(OpenState.NotOpen));
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait();
        Assert.InRange(max, 1, 4);
    }
    [Fact] public void SelectionChangeDuringCheck_DropsResult_NothingPendingOrLaunched()
    {
        using var repo = Repo((ItemType.File, "C:\\a.txt")); var l = new FakeLauncher(); var gate = new ManualResetEventSlim();
        var vm = new MainViewModel(repo, l, new FakePreflight(_ => { gate.Wait(); return new(PreflightState.Missing); }), new FixedDetector(OpenState.NotOpen));
        vm.OpenWorkspaceCommand.Execute(null); vm.Selected = null; gate.Set(); vm.OpenTask.Wait();
        Assert.False(vm.OpenDecisionPending); Assert.Empty(vm.OpenProblems); Assert.Empty(l.Launched); Assert.False(vm.IsChecking);
    }
    [Fact] public void CancelDuringCheck_LaunchesNothing()
    {
        using var repo = Repo((ItemType.File, "C:\\a.txt")); var l = new FakeLauncher(); var gate = new ManualResetEventSlim();
        var vm = new MainViewModel(repo, l, new FakePreflight(_ => { gate.Wait(); return new(PreflightState.Ready); }), new FixedDetector(OpenState.NotOpen));
        vm.OpenWorkspaceCommand.Execute(null); vm.CancelOpenCommand.Execute(null); gate.Set(); vm.OpenTask.Wait();
        Assert.Empty(l.Launched); Assert.Equal("Open cancelled, nothing was opened", vm.Status);
    }
    [Fact] public void MembershipWithMissingItemRecord_IsReportedPerItem_NotSilentlySkipped()
    {
        var path = Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json"); using var repo = new JsonStore(path);
        repo.Mutate(c => { var w = c.CreateWorkspace("w"); c.AddToWorkspace(w.Id, c.GetOrAddItem(ItemType.File, "a", "C:\\a.txt").Id); });
        var vm = new MainViewModel(repo, new FakeLauncher(), new FakePreflight(Ready), new FixedDetector(OpenState.NotOpen));
        vm.Selected!.Items.Add(new Membership("it_ghost")); // simulates a dangling membership reaching the view model
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait();
        Assert.True(vm.OpenDecisionPending); Assert.Contains(vm.OpenProblems, p => p.Contains("it_ghost") && p.Contains("record is missing"));
    }

    private sealed class GateLauncher(ManualResetEventSlim gate) : IItemLauncher
    {
        public List<string> Started = new(); public readonly ManualResetEventSlim Entered = new();
        public LaunchResult Launch(Item item, LaunchConfig? l) { Entered.Set(); gate.Wait(); lock (Started) Started.Add(item.Target); return new(LaunchStatus.Started); }
    }
    [Fact] public void BlockingLaunch_DoesNotBlockCaller_AndCancelStopsFurtherItems_ReportingWhatWasSent()
    {
        using var repo = Repo((ItemType.File, "C:\\a.txt"), (ItemType.File, "C:\\b.txt"), (ItemType.File, "C:\\c.txt")); var gate = new ManualResetEventSlim(); var l = new GateLauncher(gate);
        var vm = new MainViewModel(repo, l, new FakePreflight(Ready), new FixedDetector(OpenState.NotOpen));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        vm.OpenWorkspaceCommand.Execute(null);
        while (!vm.IsLaunching && sw.ElapsedMilliseconds < 3000) Thread.Sleep(5);
        Assert.True(vm.IsLaunching); Assert.True(sw.ElapsedMilliseconds < 3000);
        Assert.True(l.Entered.Wait(3000)); // IsLaunching is set before the worker reaches the first item: cancelling earlier would (correctly) send nothing. Wait until a call is really in flight.
        vm.CancelOpenCommand.Execute(null); gate.Set(); vm.OpenTask.Wait();
        Assert.Single(l.Started);                       // the call already in flight completed; it cannot be undone
        Assert.Contains("a.txt: open request sent", vm.OpenReport[0]);
        Assert.Contains("not attempted (cancelled", vm.OpenReport[1]); Assert.Contains("not attempted (cancelled", vm.OpenReport[2]);
        Assert.Contains("cannot be undone", vm.Status); Assert.False(vm.IsLaunching);
    }
    [Fact] public void NeedsDecisionItems_AreNeverLaunched_EvenAfterAnyway()
    {
        using var repo = Repo((ItemType.File, "C:\\a.ps1"), (ItemType.Link, "https://user@evil.example/")); var l = new FakeLauncher(); var vm = Vm(repo, l);
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait();
        Assert.All(vm.OpenProblems, p => Assert.Contains("will be skipped, not opened", p));
        vm.ConfirmOpenAnywayCommand.Execute(null); vm.OpenTask.Wait();
        Assert.Empty(l.Launched); Assert.All(vm.OpenReport, r => Assert.Contains("skipped, not opened", r));
    }
    [Fact] public void Panel_AllSkipped_SaysSkipped_AndConfirmIsShowReport()
    {
        using var repo = Repo((ItemType.File, "C:\\a.ps1"), (ItemType.Link, "https://user@evil.example/")); var l = new FakeLauncher(); var vm = Vm(repo, l);
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait();
        Assert.Equal("2 items will be skipped. Nothing has been opened yet.", vm.Status); Assert.Equal("Show report", vm.ConfirmLabel); Assert.Empty(l.Launched);
    }
    [Fact] public void Panel_Mixed_CountsSkippedAndConfirmable_AndKeepsOpenAnyway()
    {
        using var repo = Repo((ItemType.File, "C:\\a.ps1"), (ItemType.File, "C:\\ok.txt"), (ItemType.File, "C:\\gone.txt")); var l = new FakeLauncher();
        var vm = Vm(repo, l, i => i.Target.EndsWith("gone.txt") ? new(PreflightState.Missing, "saved target") : new(PreflightState.Ready), OpenState.Unknown);
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait();
        Assert.Equal("1 item will be skipped. 2 items can be opened after your confirmation (for 1 we only cannot tell whether it is already open). Nothing has been opened yet.", vm.Status);
        Assert.Equal("Open anyway", vm.ConfirmLabel); Assert.Empty(l.Launched);
    }
    [Fact] public void Panel_OnlyUnknownAlreadyOpen_IsNotCalledSkipped()
    {
        using var repo = Repo((ItemType.File, "C:\\ok.txt")); var l = new FakeLauncher(); var vm = Vm(repo, l, null, OpenState.Unknown);
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait();
        Assert.Equal("1 item can be opened after your confirmation (for 1 we only cannot tell whether it is already open). Nothing has been opened yet.", vm.Status); Assert.Equal("Open anyway", vm.ConfirmLabel);
    }
}
