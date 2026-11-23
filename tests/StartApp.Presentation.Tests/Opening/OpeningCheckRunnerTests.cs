using StartApp.Core;
using StartApp.Presentation;

public class OpeningCheckRunnerTests
{
    private sealed class Probe(Func<Item, PreflightFinding> check) : IItemPreflight
    { public PreflightFinding Check(Item item, LaunchConfig? launch) => check(item); }
    private sealed class Detector : IOpenStateDetector
    { public int Calls; public OpenState Detect(Item item, LaunchConfig? launch) { Calls++; return OpenState.Open; } }
    private static List<(Membership M, Item? Item)> Rows() => new() { (new Membership("i"), new Item("i", ItemType.File, "name", @"C:\file.txt")) };
    [Fact] public async Task Ready_probe_keeps_item_and_exact_detector_result()
    {
        var detector = new Detector(); var rows = Rows();
        var (results, stop) = await new OpeningCheckRunner(4).RunAsync(rows, new Probe(_ => new(PreflightState.Ready)), detector, TimeSpan.FromSeconds(3), default, _ => throw new Exception()); stop();
        var r = Assert.Single(results)!.Value; Assert.Same(rows[0].Item, r.Item); Assert.Equal(OpenState.Open, r.Dup); Assert.Equal(1, detector.Calls);
    }
    [Fact] public async Task Missing_catalog_record_is_unsupported_without_probe_or_detector()
    {
        var detector = new Detector(); var rows = new List<(Membership, Item?)> { (new Membership("missing"), null) };
        var (results, stop) = await new OpeningCheckRunner(4).RunAsync(rows, new Probe(_ => throw new Exception()), detector, TimeSpan.FromSeconds(3), default, _ => throw new Exception()); stop();
        Assert.Equal(PreflightState.Unsupported, results[0]!.Value.Finding.State); Assert.Equal(OpenState.NotOpen, results[0]!.Value.Dup); Assert.Equal(0, detector.Calls);
    }
    [Fact] public async Task Expected_probe_failure_becomes_unavailable_unknown()
    {
        var detector = new Detector();
        var (results, stop) = await new OpeningCheckRunner(4).RunAsync(Rows(), new Probe(_ => throw new IOException()), detector, TimeSpan.FromSeconds(3), default, _ => throw new Exception()); stop();
        Assert.Equal(PreflightState.Unavailable, results[0]!.Value.Finding.State); Assert.Equal("IOException", results[0]!.Value.Finding.Detail); Assert.Equal(OpenState.Unknown, results[0]!.Value.Dup); Assert.Equal(0, detector.Calls);
    }
    [Fact] public async Task Unexpected_probe_failure_stays_loud_before_publication()
    {
        var error = new NullReferenceException();
        var thrown = await Assert.ThrowsAsync<NullReferenceException>(() => new OpeningCheckRunner(4).RunAsync(Rows(), new Probe(_ => throw error), new Detector(), TimeSpan.FromSeconds(3), default, _ => throw new Exception()));
        Assert.Same(error, thrown);
    }
    [Fact] public async Task Timed_out_worker_holds_capacity_for_later_runs_and_cannot_change_snapshot()
    {
        var runner = new OpeningCheckRunner(1); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var probe = new Probe(_ => { entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); return new(PreflightState.Ready); });
        try
        {
            var first = runner.RunAsync(Rows(), probe, new Detector(), TimeSpan.FromMilliseconds(150), default, _ => { });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3))); var (snapshot, stop) = await first; stop(); Assert.Null(snapshot[0]);
            var (second, stopSecond) = await runner.RunAsync(Rows(), new Probe(_ => throw new Exception("capacity leaked")), new Detector(), TimeSpan.FromSeconds(3), default, _ => throw new Exception()); stopSecond();
            Assert.Null(second[0]); Assert.Null(snapshot[0]);
        }
        finally { release.Set(); }
    }
    [Fact] public async Task Empty_run_has_no_results_and_does_not_call_probe()
    {
        var (results, stop) = await new OpeningCheckRunner(4).RunAsync(new(), new Probe(_ => throw new Exception()), new Detector(), TimeSpan.FromSeconds(3), default, _ => throw new Exception()); stop(); Assert.Empty(results);
    }
}
