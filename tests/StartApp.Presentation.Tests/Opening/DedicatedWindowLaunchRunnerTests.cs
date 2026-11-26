using StartApp.Core;
using StartApp.Presentation;
using Xunit;

public class DedicatedWindowLaunchRunnerTests
{
    private sealed class Launcher : IItemLauncher, IBrowserWindowLauncher
    {
        public List<string> Singles { get; } = new();
        public List<(string Browser, string[] Names)> Windows { get; } = new();
        public Action? OnWindow;
        public LaunchResult Result = new(LaunchStatus.Started);
        public LaunchResult Launch(Item item, LaunchConfig? launch) { Singles.Add(item.Name); return Result; }
        public BrowserWindowTarget ResolveBrowser(Item item, LaunchConfig? launch) => launch?.Browser == "unsupported" ? new(null, "Unsupported browser") : new(launch?.Browser ?? "chrome");
        public LaunchResult LaunchWindow(string executable, IReadOnlyList<Item> sites) { Windows.Add((executable, sites.Select(x => x.Name).ToArray())); OnWindow?.Invoke(); return Result; }
    }
    private static List<(Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)> Plan() => new()
    {
        (new("c", ItemType.Link, "C", "https://example.org/c"), null, new(PreflightState.Ready), OpenState.Unknown),
        (new("p", ItemType.Program, "Program", @"C:\app.exe"), null, new(PreflightState.Ready), OpenState.Unknown),
        (new("e", ItemType.Link, "Edge", "https://example.org/e"), new(Browser:"edge"), new(PreflightState.Ready), OpenState.Unknown),
        (new("a", ItemType.Link, "A", "https://example.org/a"), new(Browser:"CHROME"), new(PreflightState.Ready), OpenState.Unknown),
        (new("b", ItemType.Link, "B", "https://example.org/b"), null, new(PreflightState.Ready), OpenState.Unknown)
    };

    [Fact] public async Task GroupsResolvedBrowserCaseInsensitivelyAndKeepsWorkspaceOrder()
    {
        var launcher = new Launcher(); var (rows, error) = await DedicatedWindowLaunchRunner.RunAsync(Plan(), launcher, default);
        Assert.Null(error); Assert.Equal(2, launcher.Windows.Count); Assert.Equal(new[] { "C", "A", "B" }, launcher.Windows[0].Names);
        Assert.Equal(new[] { "Edge" }, launcher.Windows[1].Names); Assert.Equal(new[] { "Program" }, launcher.Singles);
        Assert.Equal(new[] { "C", "Program", "Edge", "A", "B" }, rows.Select(x => x.Name)); Assert.All(rows, r => Assert.Equal(OpeningOutcome.RequestSent, r.Outcome));
    }
    [Fact] public async Task DeclinedAndInvalidSitesNeverReachBatch()
    {
        var plan=Plan(); plan[3]=(plan[3].Item, plan[3].Launch, new(PreflightState.Missing), OpenState.Unknown);
        var launcher=new Launcher(); var (rows,error)=await DedicatedWindowLaunchRunner.RunAsync(plan,launcher,default,new(){0});
        Assert.Null(error); Assert.Equal(OpeningOutcome.DeclinedReopen,rows[0].Outcome); Assert.Equal(OpeningOutcome.Skipped,rows[3].Outcome);
        Assert.Equal(new[]{"B"},launcher.Windows.Single(x=>x.Browser=="chrome").Names);
    }
    [Fact] public async Task CancelAfterSubmissionKeepsWholeBatchSentAndStopsLaterRequests()
    {
        using var cts=new CancellationTokenSource(); var launcher=new Launcher{OnWindow=cts.Cancel};
        var (rows,error)=await DedicatedWindowLaunchRunner.RunAsync(Plan(),launcher,cts.Token);
        Assert.Null(error); Assert.Single(launcher.Windows); Assert.Empty(launcher.Singles);
        foreach(var i in new[]{0,3,4}) Assert.Equal(OpeningOutcome.RequestSent,rows[i].Outcome);
        foreach(var i in new[]{1,2}) Assert.Equal(OpeningOutcome.Cancelled,rows[i].Outcome);
    }
    [Fact] public async Task UnsupportedDefaultNeverFallsBackToSingleLaunch()
    {
        var plan=Plan();plan[0]=(plan[0].Item,new(Browser:"unsupported"),plan[0].Finding,plan[0].Dup);
        var launcher=new Launcher();var (rows,error)=await DedicatedWindowLaunchRunner.RunAsync(plan,launcher,default);
        Assert.Null(error);Assert.Equal(OpeningOutcome.Skipped,rows[0].Outcome);Assert.DoesNotContain("C",launcher.Singles);Assert.DoesNotContain(launcher.Windows,x=>x.Names.Contains("C"));
    }
    [Fact] public async Task FailedBatchReportsEachMemberWithoutRetry()
    {
        var launcher=new Launcher{Result=new(LaunchStatus.Failed,Detail:"fixture failure")};var(rows,error)=await DedicatedWindowLaunchRunner.RunAsync(Plan(),launcher,default);
        Assert.Null(error);Assert.Equal(2,launcher.Windows.Count);Assert.All(rows,r=>Assert.Equal(OpeningOutcome.Failed,r.Outcome));
    }
}
