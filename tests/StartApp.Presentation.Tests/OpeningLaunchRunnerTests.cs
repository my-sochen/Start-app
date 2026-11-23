using StartApp.Core;
using StartApp.Presentation;

public class OpeningLaunchRunnerTests
{
    private sealed class Launcher(Func<Item, LaunchConfig?, LaunchResult> launch) : IItemLauncher
    { public LaunchResult Launch(Item item, LaunchConfig? config) => launch(item, config); }
    private static List<(Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)> Plan(params PreflightState[] states)
        => states.Select((s, i) => (new Item(i.ToString(), ItemType.File, "item" + i, @"C:\file.txt"), (LaunchConfig?)null, new PreflightFinding(s, "detail"), OpenState.Unknown)).ToList();
    [Fact] public async Task Requests_run_in_order_off_caller_thread_with_exact_item_and_config()
    {
        var caller = Environment.CurrentManagedThreadId; var calls = new List<string>(); var plan = Plan(PreflightState.Ready, PreflightState.Ready);
        var (results, fault) = await OpeningLaunchRunner.RunAsync(plan, new Launcher((item, config) =>
        { Assert.NotEqual(caller, Environment.CurrentManagedThreadId); Assert.Same(plan[calls.Count].Item, item); Assert.Same(plan[calls.Count].Launch, config); calls.Add(item.Id); return new(LaunchStatus.Started); }), default);
        Assert.Null(fault); Assert.Equal(new[] { "0", "1" }, calls); Assert.All(results, r => Assert.Equal(OpeningOutcome.RequestSent, r.Outcome));
    }
    [Theory] [InlineData(PreflightState.Missing)] [InlineData(PreflightState.Unavailable)] [InlineData(PreflightState.Unsupported)] [InlineData(PreflightState.NeedsDecision)]
    public async Task Nonlaunchable_findings_are_skipped_with_detail(PreflightState state)
    {
        var (rows, fault) = await OpeningLaunchRunner.RunAsync(Plan(state), new Launcher((_, _) => throw new Exception()), default);
        Assert.Null(fault); var r = Assert.Single(rows); Assert.Equal(OpeningOutcome.Skipped, r.Outcome); Assert.Equal("detail", r.TechnicalDetails); Assert.NotEmpty(r.Message);
    }
    [Fact] public async Task Cancellation_precedes_decline_and_skipping_without_calling_launcher()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var (rows, fault) = await OpeningLaunchRunner.RunAsync(Plan(PreflightState.Unsupported), new Launcher((_, _) => throw new Exception()), cts.Token, new() { 0 });
        Assert.Null(fault); Assert.Equal(OpeningOutcome.Cancelled, Assert.Single(rows).Outcome);
    }
    [Fact] public async Task Decline_precedes_unsupported_and_does_not_count_as_problem()
    {
        var (rows, fault) = await OpeningLaunchRunner.RunAsync(Plan(PreflightState.Unsupported), new Launcher((_, _) => throw new Exception()), default, new() { 0 });
        Assert.Null(fault); var r = Assert.Single(rows); Assert.Equal(OpeningOutcome.DeclinedReopen, r.Outcome); Assert.False(r.IsProblem);
    }
    [Fact] public async Task Cancel_during_first_request_keeps_sent_result_and_stops_later_requests()
    {
        using var cts = new CancellationTokenSource(); var calls = 0;
        var (rows, fault) = await OpeningLaunchRunner.RunAsync(Plan(PreflightState.Ready, PreflightState.Ready), new Launcher((_, _) => { calls++; cts.Cancel(); return new(LaunchStatus.Started); }), cts.Token);
        Assert.Null(fault); Assert.Equal(1, calls); Assert.Equal(OpeningOutcome.RequestSent, rows[0].Outcome); Assert.Equal(OpeningOutcome.Cancelled, rows[1].Outcome);
    }
    [Fact] public async Task Fault_preserves_sent_results_marks_remaining_failed_and_returns_original_exception()
    {
        var error = new NullReferenceException(); var calls = 0;
        var (rows, fault) = await OpeningLaunchRunner.RunAsync(Plan(PreflightState.Ready, PreflightState.Ready, PreflightState.Ready), new Launcher((_, _) => { if (++calls == 2) throw error; return new(LaunchStatus.Started); }), default);
        Assert.Same(error, fault); Assert.Equal(2, calls); Assert.Equal(OpeningOutcome.RequestSent, rows[0].Outcome);
        Assert.All(rows.Skip(1), r => { Assert.Equal(OpeningOutcome.Failed, r.Outcome); Assert.Equal("NullReferenceException", r.TechnicalDetails); });
    }
    [Fact] public async Task Empty_plan_does_not_call_launcher()
    {
        var (rows, fault) = await OpeningLaunchRunner.RunAsync(new(), new Launcher((_, _) => throw new Exception()), default); Assert.Empty(rows); Assert.Null(fault);
    }
}
