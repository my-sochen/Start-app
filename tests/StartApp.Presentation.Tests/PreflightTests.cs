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
    private static MainViewModel Vm(JsonStore r, FakeLauncher l, Func<Item, PreflightFinding>? f = null) => new(r, l, new FakePreflight(f ?? Ready));

    [Fact] public void AllReady_LaunchesWithoutAsking()
    {
        using var repo = Repo((ItemType.File, "C:\\a.txt")); var l = new FakeLauncher(); var vm = Vm(repo, l);
        vm.OpenWorkspaceCommand.Execute(null);
        Assert.False(vm.OpenDecisionPending); Assert.Single(l.Launched); Assert.Equal("Opened 1 items", vm.Status); Assert.Equal("C:\\a.txt: started", vm.OpenReport[0]);
    }
    [Fact] public void Missing_WarnsFirst_NothingLaunchedUntilUserDecides()
    {
        using var repo = Repo((ItemType.File, "C:\\a.txt"), (ItemType.File, "C:\\gone.txt")); var l = new FakeLauncher();
        var vm = Vm(repo, l, i => i.Target.EndsWith("gone.txt") ? new(PreflightState.Missing, "saved target") : new(PreflightState.Ready));
        vm.OpenWorkspaceCommand.Execute(null);
        Assert.True(vm.OpenDecisionPending); Assert.Empty(l.Launched);
        Assert.Contains("gone.txt", vm.OpenProblems[0]); Assert.Contains("not found", vm.OpenProblems[0]);
    }
    [Fact] public void OpenAnyway_AttemptsMissingAsSaved_ReportsEachItem_NoSubstitute()
    {
        using var repo = Repo((ItemType.File, "C:\\a.txt"), (ItemType.Program, "C:\\Apps\\gone-fail.exe")); var l = new FakeLauncher();
        var vm = Vm(repo, l, i => i.Type == ItemType.Program ? new(PreflightState.Missing) : new(PreflightState.Ready));
        vm.OpenWorkspaceCommand.Execute(null); vm.ConfirmOpenAnywayCommand.Execute(null);
        Assert.Equal(new[] { "C:\\a.txt", "C:\\Apps\\gone-fail.exe" }, l.Launched);
        Assert.Contains("1 of 2", vm.Status); Assert.Contains("C:\\Apps\\gone-fail.exe: not opened (boom)", vm.OpenReport);
    }
    [Fact] public void Cancel_OpensNothing()
    {
        using var repo = Repo((ItemType.File, "C:\\gone.txt")); var l = new FakeLauncher(); var vm = Vm(repo, l, _ => new(PreflightState.Missing));
        vm.OpenWorkspaceCommand.Execute(null); vm.CancelOpenCommand.Execute(null);
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
        using var repo = Repo((t, target)); var l = new FakeLauncher(); var pf = new FakePreflight(Ready); var vm = new MainViewModel(repo, l, pf);
        vm.OpenWorkspaceCommand.Execute(null);
        Assert.True(vm.OpenDecisionPending); Assert.Equal(0, pf.Calls); Assert.Contains(reason, vm.OpenProblems[0]);
        vm.ConfirmOpenAnywayCommand.Execute(null);
        Assert.Empty(l.Launched); Assert.Contains("never opened by policy", vm.Status); Assert.Contains("not opened", vm.OpenReport[0]);
    }
    [Theory] [InlineData("http://example.com")] [InlineData("https://example.com/a?b=c")]
    public void HttpAndHttps_AreOpenable(string url)
    {
        using var repo = Repo((ItemType.Link, url)); var l = new FakeLauncher(); var vm = Vm(repo, l);
        vm.OpenWorkspaceCommand.Execute(null); Assert.Single(l.Launched);
    }
    [Fact] public void Unavailable_IsWarned_ThenAttemptedOnlyAfterConfirm()
    {
        using var repo = Repo((ItemType.File, "C:\\a.txt")); var l = new FakeLauncher(); var vm = Vm(repo, l, _ => new(PreflightState.Unavailable, "UnauthorizedAccessException"));
        vm.OpenWorkspaceCommand.Execute(null); Assert.Empty(l.Launched); Assert.Contains("UnauthorizedAccessException", vm.OpenProblems[0]);
        vm.ConfirmOpenAnywayCommand.Execute(null); Assert.Single(l.Launched);
    }
    [Fact] public void ChangingSelection_DropsPendingDecision()
    {
        using var repo = Repo((ItemType.File, "C:\\gone.txt")); var vm = Vm(repo, new FakeLauncher(), _ => new(PreflightState.Missing));
        vm.OpenWorkspaceCommand.Execute(null); vm.Selected = null;
        Assert.False(vm.OpenDecisionPending); Assert.Empty(vm.OpenProblems);
    }
}
