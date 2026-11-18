using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

/// <summary>Nov17 contract supersedes the old open-anyway panel: selected-only checks; confirmed-only unchecked duplicate list; missing/unavailable skipped.</summary>
public class PreflightTests
{
    private sealed class FakeLauncher : IItemLauncher
    {
        public List<string> Launched = new();
        public LaunchResult Launch(Item item, LaunchConfig? l) { Launched.Add(item.Target); return item.Target.Contains("fail") ? new(LaunchStatus.Failed, Detail: "boom") : new(LaunchStatus.Started); }
    }
    private sealed class FakePreflight(Func<Item, PreflightFinding> f) : IItemPreflight
    {
        public int Calls; public PreflightFinding Check(Item i, LaunchConfig? l) { Interlocked.Increment(ref Calls); return f(i); }
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
    private static void SelectAndCheck(MainViewModel vm) { vm.OpenWorkspaceCommand.Execute(null); vm.ConfirmSelectionCommand.Execute(null); vm.OpenTask.GetAwaiter().GetResult(); }

    [Fact] public void WorkspaceAlwaysAsksBeforeAnyChecks_AllInitiallyChecked()
    {
        using var repo = Repo((ItemType.File, "C:\\a.txt")); var l = new FakeLauncher(); var pf = new FakePreflight(Ready); var vm = new MainViewModel(repo,l,pf);
        vm.OpenWorkspaceCommand.Execute(null);
        Assert.True(vm.SelectionPending); Assert.All(vm.OpeningChoices,x=>Assert.True(x.IsChecked)); Assert.Equal(0,pf.Calls); Assert.Empty(l.Launched);
        Assert.False(vm.OpenDecisionPending); Assert.False(vm.IsChecking);
        vm.ConfirmSelectionCommand.Execute(null); vm.OpenTask.GetAwaiter().GetResult();
        Assert.Single(l.Launched); Assert.Equal(OpeningOutcome.RequestSent,Assert.Single(vm.LastOpeningResults).Outcome); Assert.False(vm.LastResultsOpen); Assert.True(vm.ToastVisible); Assert.False(vm.ToastPersistent);
    }
    [Fact] public void UncheckedItemsAreNeverCheckedOrLaunched_EmptyDisablesConfirm()
    {
        using var repo=Repo((ItemType.File,"C:\\a.txt"),(ItemType.File,"C:\\b.txt")); var l=new FakeLauncher(); var pf=new FakePreflight(Ready); var vm=new MainViewModel(repo,l,pf);
        vm.OpenWorkspaceCommand.Execute(null); foreach(var x in vm.OpeningChoices) x.IsChecked=false;
        Assert.False(vm.ConfirmSelectionCommand.CanExecute(null)); vm.OpeningChoices[1].IsChecked=true;
        vm.ConfirmSelectionCommand.Execute(null); vm.OpenTask.GetAwaiter().GetResult();
        Assert.Equal(1,pf.Calls); Assert.Equal(new[]{"C:\\b.txt"},l.Launched);
    }
    [Theory] [InlineData(PreflightState.Missing)] [InlineData(PreflightState.Unavailable)]
    public void MissingAndUnavailableSkippedAndReported_NoDuplicateDialog(PreflightState state)
    {
        using var repo=Repo((ItemType.File,"C:\\a.txt"),(ItemType.File,"C:\\gone.txt")); var l=new FakeLauncher(); var vm=Vm(repo,l,i=>i.Target.Contains("gone")?new(state,"detail"):new(PreflightState.Ready),OpenState.Unknown);
        SelectAndCheck(vm); Assert.False(vm.OpenDecisionPending); Assert.Single(l.Launched); Assert.True(vm.ToastPersistent); Assert.False(vm.LastResultsOpen);
        Assert.Equal(OpeningOutcome.Skipped,vm.LastOpeningResults[0].Outcome); Assert.Equal("detail",vm.LastOpeningResults[0].TechnicalDetails);
    }
    [Theory]
    [InlineData(ItemType.Program,"notepad")]
    [InlineData(ItemType.Link,"file:///C:/Windows/System32/calc.exe")]
    [InlineData(ItemType.Link,"myapp://do-thing")]
    [InlineData(ItemType.Link,"javascript:alert(1)")]
    [InlineData(ItemType.File,"relative\\a.txt")]
    public void UnsupportedNeverLaunched_AdapterNotConsulted(ItemType t,string target)
    {
        using var repo=Repo((t,target)); var l=new FakeLauncher(); var pf=new FakePreflight(Ready); var vm=new MainViewModel(repo,l,pf,new FixedDetector(OpenState.Open));
        SelectAndCheck(vm); Assert.Equal(0,pf.Calls); Assert.Empty(l.Launched); Assert.False(vm.OpenDecisionPending); Assert.Equal(OpeningOutcome.Skipped,Assert.Single(vm.LastOpeningResults).Outcome);
    }
    [Theory] [InlineData("http://example.com")] [InlineData("https://example.com/a?b=c")]
    public void HttpAndHttpsRemainOpenable(string url) { using var repo=Repo((ItemType.Link,url));var l=new FakeLauncher();SelectAndCheck(Vm(repo,l));Assert.Single(l.Launched); }
    [Fact] public void UnknownDoesNotAppearInConfirmedList_AndAttemptsNormally()
    {
        using var repo=Repo((ItemType.File,"C:\\a.txt"));var l=new FakeLauncher();var vm=new MainViewModel(repo,l,new FakePreflight(Ready));
        SelectAndCheck(vm);Assert.False(vm.OpenDecisionPending);Assert.Empty(vm.ReopenChoices);Assert.Single(l.Launched);Assert.False(vm.ToastPersistent);
    }
    [Fact] public void ConfirmedRowsStartUnchecked_DeclinedReopenNotFailure()
    {
        using var repo=Repo((ItemType.File,"C:\\a.txt"),(ItemType.File,"C:\\b.txt"));var l=new FakeLauncher();var vm=Vm(repo,l,dup:OpenState.Open);
        SelectAndCheck(vm);Assert.True(vm.OpenDecisionPending);Assert.Empty(l.Launched);Assert.Equal(2,vm.ReopenChoices.Count);Assert.All(vm.ReopenChoices,x=>Assert.False(x.IsChecked));
        vm.ReopenChoices[1].IsChecked=true;vm.ConfirmOpenAnywayCommand.Execute(null);vm.OpenTask.GetAwaiter().GetResult();
        Assert.Equal(new[]{"C:\\b.txt"},l.Launched);Assert.Contains(vm.LastOpeningResults,x=>x.Outcome==OpeningOutcome.DeclinedReopen);Assert.All(vm.LastOpeningResults,x=>Assert.False(x.IsProblem));
    }
    [Fact] public void BackPreservesInitialSelection_AndRechecksOnlySelection()
    {
        using var repo=Repo((ItemType.File,"C:\\a.txt"),(ItemType.File,"C:\\b.txt"));var l=new FakeLauncher();var vm=Vm(repo,l,dup:OpenState.Open);
        vm.OpenWorkspaceCommand.Execute(null);vm.OpeningChoices[0].IsChecked=false;vm.ConfirmSelectionCommand.Execute(null);vm.OpenTask.GetAwaiter().GetResult();
        vm.BackToOpeningSelectionCommand.Execute(null);Assert.True(vm.SelectionPending);Assert.False(vm.OpeningChoices[0].IsChecked);Assert.True(vm.OpeningChoices[1].IsChecked);Assert.Empty(l.Launched);
    }
    [Fact] public void SingleConfirmedAlsoAsksButHasNoInitialSelectionOrBack()
    {
        using var repo=Repo((ItemType.File,"C:\\a.txt"));var l=new FakeLauncher();var vm=Vm(repo,l,dup:OpenState.Open);
        vm.OpenItemCommand.Execute(vm.Items[0]);vm.OpenTask.GetAwaiter().GetResult();Assert.False(vm.SelectionPending);Assert.True(vm.OpenDecisionPending);Assert.False(vm.CanBackToOpeningSelection);Assert.Empty(l.Launched);
        vm.ReopenChoices[0].IsChecked=true;vm.ConfirmOpenAnywayCommand.Execute(null);vm.OpenTask.GetAwaiter().GetResult();Assert.Single(l.Launched);Assert.Null(repo.Load().Workspaces.Single().LastOpenedAt);
    }
    [Fact] public void CancelAndSelectionChangeDoNotReplaceLastResults()
    {
        using var repo=Repo((ItemType.File,"C:\\a.txt"));var l=new FakeLauncher();var vm=Vm(repo,l);SelectAndCheck(vm);var previous=vm.LastOpeningResults.ToArray();
        vm.OpenWorkspaceCommand.Execute(null);vm.CancelOpenCommand.Execute(null);Assert.Equal(previous,vm.LastOpeningResults);Assert.Single(l.Launched);
        vm.OpenWorkspaceCommand.Execute(null);vm.Selected=null;Assert.False(vm.SelectionPending);Assert.False(vm.OpenDecisionPending);Assert.Equal(previous,vm.LastOpeningResults);
    }
    private static (ItemType,string)[] Many(int n)=>Enumerable.Range(0,n).Select(i=>(ItemType.File,$"C:\\d\\f{i}.txt")).ToArray();
    [Fact] public void AggregateDeadlineSkipsUnfinished_BoundedFourWorkers_LateResultsIgnored()
    {
        using var repo=Repo(Many(12));var l=new FakeLauncher();using var gate=new ManualResetEventSlim();int started=0;
        var vm=new MainViewModel(repo,l,new FakePreflight(_=>{Interlocked.Increment(ref started);gate.Wait(TimeSpan.FromSeconds(15));return new(PreflightState.Ready);}),new FixedDetector(OpenState.NotOpen)){OpenCheckTimeout=TimeSpan.FromMilliseconds(100)};
        vm.OpenWorkspaceCommand.Execute(null);vm.ConfirmSelectionCommand.Execute(null);Assert.True(vm.IsChecking);Assert.True(vm.ShowOpenPanel);Assert.False(vm.OpenWorkspaceCommand.CanExecute(null));
        Assert.True(vm.OpenTask.Wait(TimeSpan.FromSeconds(5)));Assert.InRange(started,1,4);Assert.Equal(12,vm.LastOpeningResults.Count);Assert.All(vm.LastOpeningResults,x=>Assert.Equal(OpeningOutcome.Skipped,x.Outcome));Assert.Empty(l.Launched);gate.Set();
    }
    [Fact] public void ChecksUseAtMostFourWorkers()
    {
        using var repo=Repo(Many(20));var l=new FakeLauncher();int cur=0,max=0;var gate=new object();
        var vm=Vm(repo,l,_=>{var c=Interlocked.Increment(ref cur);lock(gate)max=Math.Max(max,c);Thread.Sleep(20);Interlocked.Decrement(ref cur);return new(PreflightState.Ready);});
        SelectAndCheck(vm);Assert.InRange(max,1,4);Assert.Equal(20,l.Launched.Count);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void CancelOrChangeDuringCheckDropsResultsAndLaunchesNothing(bool cancel)
    {
        using var repo=Repo((ItemType.File,"C:\\a.txt"));var l=new FakeLauncher();using var gate=new ManualResetEventSlim();
        var vm=Vm(repo,l,_=>{gate.Wait(TimeSpan.FromSeconds(10));return new(PreflightState.Ready);});vm.OpenWorkspaceCommand.Execute(null);vm.ConfirmSelectionCommand.Execute(null);
        if(cancel)vm.CancelOpenCommand.Execute(null);else vm.Selected=null;gate.Set();vm.OpenTask.GetAwaiter().GetResult();Assert.Empty(l.Launched);Assert.False(vm.OpenDecisionPending);Assert.Empty(vm.LastOpeningResults);Assert.False(vm.IsChecking);
    }
    [Fact] public void DanglingMembershipReportedPerItem()
    {
        using var repo=Repo((ItemType.File,"C:\\a.txt"));var l=new FakeLauncher();var vm=Vm(repo,l);vm.Selected!.Items.Add(new Membership("it_ghost"));
        SelectAndCheck(vm);Assert.Equal(2,vm.LastOpeningResults.Count);Assert.Single(l.Launched);Assert.Contains(vm.LastOpeningResults,x=>x.TechnicalDetails?.Contains("record is missing")==true);
    }
    private sealed class GateLauncher(ManualResetEventSlim gate):IItemLauncher
    {
        public List<string> Started=new();public readonly ManualResetEventSlim Entered=new();
        public LaunchResult Launch(Item item,LaunchConfig? l){Entered.Set();gate.Wait(TimeSpan.FromSeconds(10));Started.Add(item.Target);return new(LaunchStatus.Started);}
    }
    [Fact] public void CancelDuringLaunchStopsFurtherItems_ReportsAlreadySent()
    {
        using var repo=Repo(Many(3));using var gate=new ManualResetEventSlim();var l=new GateLauncher(gate);var vm=new MainViewModel(repo,l,new FakePreflight(Ready),new FixedDetector(OpenState.NotOpen));
        vm.OpenWorkspaceCommand.Execute(null);vm.ConfirmSelectionCommand.Execute(null);Assert.True(l.Entered.Wait(5000));Assert.True(vm.ShowOpenPanel);vm.CancelOpenCommand.Execute(null);gate.Set();vm.OpenTask.GetAwaiter().GetResult();
        Assert.Single(l.Started);Assert.Equal(2,vm.LastOpeningResults.Count(x=>x.Outcome==OpeningOutcome.Cancelled));Assert.Single(vm.LastOpeningResults.Where(x=>x.Outcome==OpeningOutcome.RequestSent));Assert.False(vm.IsLaunching);
    }
    [Fact] public void NeedsDecisionNeverLaunched()
    {
        using var repo=Repo((ItemType.File,"C:\\a.ps1"),(ItemType.Link,"https://user@evil.example/"));var l=new FakeLauncher();var vm=Vm(repo,l);SelectAndCheck(vm);Assert.Empty(l.Launched);Assert.All(vm.LastOpeningResults,x=>Assert.Equal(OpeningOutcome.Skipped,x.Outcome));
    }
    [Fact] public void LastResultsSessionOnlyAndExplicitlyOpened()
    {
        using var repo=Repo((ItemType.File,"C:\\a.txt"));var vm=Vm(repo,new());SelectAndCheck(vm);Assert.False(vm.LastResultsOpen);vm.ShowLastOpeningResultsCommand.Execute(null);Assert.True(vm.LastResultsOpen);vm.CloseLastOpeningResultsCommand.Execute(null);Assert.False(vm.LastResultsOpen);Assert.False(Vm(repo,new()).HasLastOpeningResults);
    }
}
