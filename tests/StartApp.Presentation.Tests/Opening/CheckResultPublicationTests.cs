using StartApp.Presentation;

public class CheckResultPublicationTests
{
    [Fact] public void Unfinished_slots_remain_null_in_original_index_order()
    {
        var s = new CheckResultPublication<int>(3); s.Record(2, 20); s.Record(0, 10);
        var (rows, fault) = s.Publish(); Assert.Equal(new int?[] { 10, null, 20 }, rows); Assert.Null(fault);
    }
    [Fact] public void Late_results_are_ignored_and_snapshot_is_detached()
    {
        var s = new CheckResultPublication<int>(1); s.Record(0, 1); var first = s.Publish().Results;
        s.Record(0, 2); Assert.Equal(1, first[0]); first[0] = 99; Assert.Equal(1, s.Publish().Results[0]);
    }
    [Fact] public void First_prepublication_fault_belongs_to_publisher_and_is_never_reported_again()
    {
        var s = new CheckResultPublication<int>(0); var first = new Exception("first"); var second = new Exception("second");
        Assert.Null(s.ObserveFault(first)); Assert.Null(s.ObserveFault(second));
        Assert.Same(first, s.Publish().Fault); Assert.Null(s.ObserveFault(second));
    }
    [Fact] public void First_late_fault_is_returned_once_without_waiting_for_other_workers()
    {
        var s = new CheckResultPublication<int>(2); s.Publish(); var error = new Exception();
        Assert.Same(error, s.ObserveFault(error)); Assert.Null(s.ObserveFault(new Exception()));
    }
    [Fact] public void Empty_snapshot_is_valid_and_separate_owners_do_not_share_faults()
    {
        var a = new CheckResultPublication<int>(0); var b = new CheckResultPublication<int>(0);
        a.ObserveFault(new Exception()); Assert.Empty(b.Publish().Results); Assert.Null(b.Publish().Fault);
    }
    [Fact] public void Concurrent_late_faults_have_exactly_one_report_owner()
    {
        var s = new CheckResultPublication<int>(0); s.Publish();
        var reports = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        Parallel.For(0, 32, _ => { if (s.ObserveFault(new Exception()) is { } error) reports.Add(error); });
        Assert.Single(reports);
    }
    [Fact] public async Task Worker_fault_racing_publication_has_exactly_one_delivery_path()
    {
        var s = new CheckResultPublication<int>(1); var error = new Exception(); Exception? late = null;
        using var start = new ManualResetEventSlim();
        var worker = Task.Run(() => { start.Wait(); late = s.ObserveFault(error); }); start.Set();
        var published = s.Publish(); await worker;
        Assert.True((published.Fault is not null) ^ (late is not null)); Assert.Same(error, published.Fault ?? late);
    }
}
