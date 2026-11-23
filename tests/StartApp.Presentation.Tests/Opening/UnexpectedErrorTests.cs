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
        vm.OpenWorkspaceCommand.Execute(null); vm.ConfirmSelectionCommand.Execute(null); vm.OpenTask.Wait();
        Assert.IsType<NullReferenceException>(got);
        Assert.False(vm.IsLaunching); Assert.False(vm.IsChecking); Assert.True(vm.OpenWorkspaceCommand.CanExecute(null));
        Assert.Equal(new[] { "C:\\a.txt" }, l.Launched);
        Assert.Single(vm.LastOpeningResults.Where(r => r.Outcome == OpeningOutcome.RequestSent));
        Assert.Equal(2, vm.LastOpeningResults.Count(r => r.Outcome == OpeningOutcome.Failed));
        Assert.Contains("unexpected error", vm.Status);
    }
    [Fact] public void LaunchBug_WithoutHandler_IsRethrownIntoTheTask_NotSwallowed()
    {
        using var repo = Repo("C:\\bug.txt"); var vm = new MainViewModel(repo, new BugLauncher(), new Pf(Ready), new Det());
        vm.OpenWorkspaceCommand.Execute(null); vm.ConfirmSelectionCommand.Execute(null);
        var ex = Assert.Throws<AggregateException>(() => vm.OpenTask.Wait()); Assert.IsType<NullReferenceException>(ex.InnerException);
        Assert.False(vm.IsLaunching); Assert.True(vm.OpenWorkspaceCommand.CanExecute(null));
    }
    [Fact] public void PreflightBug_IsNotReportedAsUnavailable_ItIsSurfaced_AndNothingIsLaunched()
    {
        using var repo = Repo("C:\\a.txt", "C:\\b.txt"); var l = new BugLauncher();
        var vm = new MainViewModel(repo, l, new Pf(i => i.Target.Contains("b.txt") ? throw new NullReferenceException("synthetic") : new(PreflightState.Ready)), new Det()); Exception? got = null; vm.UnexpectedError += e => got = e;
        vm.OpenWorkspaceCommand.Execute(null); vm.ConfirmSelectionCommand.Execute(null); vm.OpenTask.Wait();
        Assert.IsType<NullReferenceException>(got); Assert.Empty(l.Launched); Assert.False(vm.OpenDecisionPending); Assert.False(vm.IsChecking); Assert.True(vm.OpenWorkspaceCommand.CanExecute(null));
    }
    [Fact] public void ExpectedIoErrors_StillBecomeUnavailable_NotBugs()
    {
        using var repo = Repo("C:\\a.txt"); Exception? got = null;
        var vm = new MainViewModel(repo, new BugLauncher(), new Pf(_ => throw new IOException("x")), new Det()); vm.UnexpectedError += e => got = e;
        vm.OpenWorkspaceCommand.Execute(null); vm.ConfirmSelectionCommand.Execute(null); vm.OpenTask.Wait();
        Assert.Null(got); Assert.False(vm.OpenDecisionPending); Assert.Equal(OpeningOutcome.Skipped, Assert.Single(vm.LastOpeningResults).Outcome);
    }

    [Fact] public void OpenPanel_StaysVisible_WhileCheckingAndLaunching_SoCancelIsReachable()
    {
        using var repo = Repo("C:\\a.txt"); var gate = new ManualResetEventSlim(); var changed = new List<string>();
        var vm = new MainViewModel(repo, new BugLauncher(), new Pf(_ => { gate.Wait(5000); return new(PreflightState.Ready); }), new Det()); vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
        Assert.False(vm.ShowOpenPanel);
        vm.OpenWorkspaceCommand.Execute(null); vm.ConfirmSelectionCommand.Execute(null);
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
        vm.OpenWorkspaceCommand.Execute(null); vm.ConfirmSelectionCommand.Execute(null); vm.OpenTask.Wait(10000);
        Assert.All(vm.LastOpeningResults, r => Assert.Equal(OpeningOutcome.Skipped, r.Outcome)); // deadline results cannot be launched
        release.Set();
        Assert.True(seen.Wait(5000)); Assert.IsType<NullReferenceException>(got);
        hold.Set();
    }
    [Fact] public async Task PreflightBug_ResetsStateBeforeHandler_AndOpenTaskIncludesHandlerCompletion()
    {
        using var repo = Repo("C:\\a.txt");
        using var releaseCheck = new ManualResetEventSlim();
        using var releaseHandler = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool checkingAtHandler = true, launchingAtHandler = true, handlerReleased = false;
        var vm = new MainViewModel(repo, new BugLauncher(), new Pf(_ =>
        {
            if (!releaseCheck.Wait(10000)) throw new TimeoutException("test did not release preflight");
            throw new NullReferenceException("controlled preflight bug");
        }), new Det());
        vm.UnexpectedError += error =>
        {
            checkingAtHandler = vm.IsChecking; launchingAtHandler = vm.IsLaunching;
            entered.TrySetResult();
            handlerReleased = releaseHandler.Wait(10000);
            delivered.TrySetResult(error);
        };
        // The preflight gate ensures OpenTask is assigned before the fault. The
        // handler gate makes the completion assertion independent of handler speed.
        try
        {
            await Task.Run(() => { vm.OpenWorkspaceCommand.Execute(null); vm.ConfirmSelectionCommand.Execute(null); });
            releaseCheck.Set();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(checkingAtHandler); Assert.False(launchingAtHandler);
            Assert.False(vm.OpenTask.IsCompleted);
        }
        finally { releaseCheck.Set(); releaseHandler.Set(); }
        await vm.OpenTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsType<NullReferenceException>(await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(handlerReleased);
    }

    [Fact] public async Task PreflightBug_WithoutHandler_FaultsOpenTask_AfterResettingState()
    {
        using var repo = Repo("C:\\a.txt");
        using var release = new ManualResetEventSlim();
        var vm = new MainViewModel(repo, new BugLauncher(), new Pf(_ =>
        {
            if (!release.Wait(10000)) throw new TimeoutException("test did not release preflight");
            throw new NullReferenceException("controlled unhandled preflight bug");
        }), new Det());
        try
        {
            await Task.Run(() => { vm.OpenWorkspaceCommand.Execute(null); vm.ConfirmSelectionCommand.Execute(null); });
            release.Set();
            await Assert.ThrowsAsync<NullReferenceException>(() => vm.OpenTask.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(vm.OpenTask.IsFaulted); Assert.False(vm.IsChecking); Assert.False(vm.IsLaunching);
            Assert.Null(vm.LateError);
        }
        finally { release.Set(); }
    }

    private sealed class GatedLauncher(Action onLaunch, ManualResetEventSlim release) : IItemLauncher
    {
        public LaunchResult Launch(Item item, LaunchConfig? launch)
        {
            onLaunch();
            if (!release.Wait(10000)) throw new TimeoutException("test did not release launch");
            return new(LaunchStatus.Started);
        }
    }

    [Fact] public async Task LatePreflightBug_IsReportedEvenWhileLaunchCallRemainsBlocked()
    {
        using var repo = Repo("C:\\a.txt", "C:\\b.txt");
        using var releasePreflight = new ManualResetEventSlim();
        using var releaseLaunch = new ManualResetEventSlim();
        var launchEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new MainViewModel(repo, new GatedLauncher(() => launchEntered.TrySetResult(), releaseLaunch), new Pf(item =>
        {
            if (item.Target.Contains("b.txt"))
            {
                if (!releasePreflight.Wait(10000)) throw new TimeoutException("test did not release late preflight");
                throw new NullReferenceException("controlled late preflight bug during blocked launch");
            }
            return new(PreflightState.Ready);
        }), new Det()) { OpenCheckTimeout = TimeSpan.FromMilliseconds(100) };
        var reports = 0;
        vm.UnexpectedError += error => { Interlocked.Increment(ref reports); delivered.TrySetResult(error); };
        try
        {
            await Task.Run(() => { vm.OpenWorkspaceCommand.Execute(null); vm.ConfirmSelectionCommand.Execute(null); });
            await launchEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            releasePreflight.Set();
            // The launch gate stays closed until after delivery. No worker or OS
            // launch completion is required to observe this genuinely late error.
            Assert.IsType<NullReferenceException>(await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(vm.OpenTask.IsCompleted);
        }
        finally { releasePreflight.Set(); releaseLaunch.Set(); }
        await vm.OpenTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref reports));
    }

    [Fact] public async Task TwoDifferentLateFaults_ReportFirstObservedExceptionExactlyOnce()
    {
        using var repo = Repo("C:\\a.txt", "C:\\b.txt");
        using var bothEntered = new CountdownEvent(2);
        using var releaseFirst = new ManualResetEventSlim();
        using var releaseSecond = new ManualResetEventSlim();
        using var releaseHandler = new ManualResetEventSlim();
        var first = new NullReferenceException("first late fault");
        var second = new ArgumentException("second different late fault");
        var handlerEntered = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread? secondWorker = null;
        var reports = 0;
        var vm = new MainViewModel(repo, new BugLauncher(), new Pf(item =>
        {
            bool isFirst = item.Target.Contains("a.txt");
            if (!isFirst) secondWorker = Thread.CurrentThread;
            bothEntered.Signal();
            if (!(isFirst ? releaseFirst : releaseSecond).Wait(10000)) throw new TimeoutException("late fault gate not released");
            throw isFirst ? first : second;
        }), new Det()) { OpenCheckTimeout = TimeSpan.FromMilliseconds(100) };
        vm.UnexpectedError += error =>
        {
            Interlocked.Increment(ref reports);
            handlerEntered.TrySetResult(error);
            releaseHandler.Wait(10000);
        };
        try
        {
            await Task.Run(() => { vm.OpenWorkspaceCommand.Execute(null); vm.ConfirmSelectionCommand.Execute(null); });
            Assert.True(bothEntered.Wait(5000));
            await vm.OpenTask.WaitAsync(TimeSpan.FromSeconds(5)); // publication definitely happened
            releaseFirst.Set();
            Assert.Same(first, await handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            releaseSecond.Set();
            // Dedicated worker lifetime, not a sleep: the second catch/report path
            // has completed while the first subscriber remains blocked.
            Assert.True(secondWorker!.Join(5000));
            Assert.Equal(1, Volatile.Read(ref reports));
            Assert.False(vm.IsChecking); Assert.False(vm.IsLaunching);
        }
        finally { releaseFirst.Set(); releaseSecond.Set(); releaseHandler.Set(); }
    }

    [Fact] public async Task CancelledDelay_PublishesHandoff_DropsPlan_AndStillReportsLateFault()
    {
        using var repo = Repo("C:\\a.txt");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var late = new NullReferenceException("fault after cancelled delay");
        var delivered = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var launcher = new BugLauncher();
        var vm = new MainViewModel(repo, launcher, new Pf(_ =>
        {
            entered.Set();
            if (!release.Wait(10000)) throw new TimeoutException("cancelled worker gate not released");
            throw late;
        }), new Det()) { OpenCheckTimeout = TimeSpan.FromMinutes(1) };
        vm.UnexpectedError += error => delivered.TrySetResult(error);
        try
        {
            await Task.Run(() => { vm.OpenWorkspaceCommand.Execute(null); vm.ConfirmSelectionCommand.Execute(null); });
            Assert.True(entered.Wait(5000));
            vm.CancelOpenCommand.Execute(null);
            await vm.OpenTask.WaitAsync(TimeSpan.FromSeconds(5)); // not the minute-long deadline
            Assert.Empty(launcher.Launched); Assert.Empty(vm.LastOpeningResults); Assert.False(vm.IsChecking);
            release.Set();
            Assert.Same(late, await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Empty(launcher.Launched); Assert.Empty(vm.LastOpeningResults); Assert.False(vm.IsChecking);
        }
        finally { release.Set(); }
    }

}
