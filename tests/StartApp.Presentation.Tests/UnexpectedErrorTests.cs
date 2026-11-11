using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

/// <summary>An unexpected (programming) error in an open operation must not become a silent faulted task, and must not be turned into a normal result.</summary>
public class UnexpectedErrorTests
{
    private sealed class BugLauncher : IItemLauncher
    {
        public List<string> Launched = new();
        public LaunchResult Launch(Item item, LaunchConfig? l) { if (item.Target.Contains("bug")) throw new NullReferenceException("synthetic bug"); Launched.Add(item.Target); return new(LaunchStatus.Started); }
    }
    private sealed class Pf(Func<Item, PreflightFinding> f) : IItemPreflight { public PreflightFinding Check(Item i, LaunchConfig? l) => f(i); }
    private sealed class Det : IOpenStateDetector { public OpenState Detect(Item i, LaunchConfig? l) => OpenState.NotOpen; }
    private static JsonStore Repo(params string[] targets)
    {
        var repo = new JsonStore(Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json"));
        repo.Mutate(c => { var w = c.CreateWorkspace("w"); foreach (var t in targets) c.AddToWorkspace(w.Id, c.GetOrAddItem(ItemType.File, t, t).Id); });
        return repo;
    }
    private static readonly Func<Item, PreflightFinding> Ready = _ => new(PreflightState.Ready);

    [Fact] public void LaunchBug_WithHandler_KeepsPartialResults_ResetsState_AndReachesHandler()
    {
        using var repo = Repo("C:\\a.txt", "C:\\bug.txt", "C:\\c.txt"); var l = new BugLauncher();
        var vm = new MainViewModel(repo, l, new Pf(Ready), new Det()); Exception? got = null; vm.UnexpectedError += e => got = e;
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait();
        Assert.IsType<NullReferenceException>(got);
        Assert.False(vm.IsLaunching); Assert.False(vm.IsChecking); Assert.True(vm.OpenWorkspaceCommand.CanExecute(null));
        Assert.Equal(new[] { "C:\\a.txt" }, l.Launched);
        Assert.Equal("C:\\a.txt: open request sent (not proof it loaded)", vm.OpenReport[0]);
        Assert.Contains("not attempted (stopped by an unexpected error)", vm.OpenReport[1]); Assert.Contains("not attempted (stopped by an unexpected error)", vm.OpenReport[2]);
        Assert.Contains("unexpected error", vm.Status);
    }
    [Fact] public void LaunchBug_WithoutHandler_IsRethrownIntoTheTask_NotSwallowed()
    {
        using var repo = Repo("C:\\bug.txt"); var vm = new MainViewModel(repo, new BugLauncher(), new Pf(Ready), new Det());
        vm.OpenWorkspaceCommand.Execute(null);
        var ex = Assert.Throws<AggregateException>(() => vm.OpenTask.Wait()); Assert.IsType<NullReferenceException>(ex.InnerException);
        Assert.False(vm.IsLaunching); Assert.True(vm.OpenWorkspaceCommand.CanExecute(null));
    }
    [Fact] public void PreflightBug_IsNotReportedAsUnavailable_ItIsSurfaced_AndNothingIsLaunched()
    {
        using var repo = Repo("C:\\a.txt", "C:\\b.txt"); var l = new BugLauncher();
        var vm = new MainViewModel(repo, l, new Pf(i => i.Target.Contains("b.txt") ? throw new NullReferenceException("synthetic") : new(PreflightState.Ready)), new Det()); Exception? got = null; vm.UnexpectedError += e => got = e;
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait();
        Assert.IsType<NullReferenceException>(got); Assert.Empty(l.Launched); Assert.False(vm.OpenDecisionPending); Assert.False(vm.IsChecking); Assert.True(vm.OpenWorkspaceCommand.CanExecute(null));
    }
    [Fact] public void ExpectedIoErrors_StillBecomeUnavailable_NotBugs()
    {
        using var repo = Repo("C:\\a.txt"); Exception? got = null;
        var vm = new MainViewModel(repo, new BugLauncher(), new Pf(_ => throw new IOException("x")), new Det()); vm.UnexpectedError += e => got = e;
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait();
        Assert.Null(got); Assert.True(vm.OpenDecisionPending); Assert.Contains("could not be checked", vm.OpenProblems[0]);
    }

    [Fact] public void OpenPanel_StaysVisible_WhileCheckingAndLaunching_SoCancelIsReachable()
    {
        using var repo = Repo("C:\\a.txt"); var gate = new ManualResetEventSlim(); var changed = new List<string>();
        var vm = new MainViewModel(repo, new BugLauncher(), new Pf(_ => { gate.Wait(5000); return new(PreflightState.Ready); }), new Det()); vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
        Assert.False(vm.ShowOpenPanel);
        vm.OpenWorkspaceCommand.Execute(null);
        Assert.True(vm.IsChecking); Assert.True(vm.ShowOpenPanel); Assert.False(vm.OpenDecisionPending); Assert.True(vm.CancelOpenCommand.CanExecute(null));
        gate.Set(); vm.OpenTask.Wait();
        Assert.False(vm.ShowOpenPanel); Assert.Contains("ShowOpenPanel", changed);
    }

    [Fact] public void LateWorkerFault_AfterTheDeadline_IsStillObserved_NotLost()
    {
        using var repo = Repo("C:\\a.txt", "C:\\b.txt"); var seen = new ManualResetEventSlim(); var release = new ManualResetEventSlim(); var hold = new ManualResetEventSlim(); Exception? got = null;
        // Deterministic: b only throws after the test has seen the deadline result; a stays blocked until the end (no timing assumptions).
        var vm = new MainViewModel(repo, new BugLauncher(), new Pf(i => { if (i.Target.Contains("b.txt")) { release.Wait(10000); throw new NullReferenceException("late"); } hold.Wait(10000); return new(PreflightState.Ready); }), new Det()) { OpenCheckTimeout = TimeSpan.FromMilliseconds(100) };
        vm.UnexpectedError += e => { got = e; seen.Set(); };
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait(10000);
        Assert.True(vm.OpenDecisionPending); // deadline published first: both items "did not finish"
        release.Set();
        Assert.True(seen.Wait(5000)); Assert.IsType<NullReferenceException>(got);
        hold.Set();
    }
}
