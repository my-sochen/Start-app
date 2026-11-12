using StartApp.Core;

namespace StartApp.Core.Tests;

public class CaptureScanningTests
{
    private sealed class Quick : IOpenItemCapturer { public CaptureScan Scan() => new(new[] { new CapturedItem(ItemType.Link, "e", "https://e.example/") }, new[] { "n" }); }
    private sealed class Stuck : IOpenItemCapturer { public CaptureScan Scan() { Thread.Sleep(TimeSpan.FromSeconds(5)); return new(Array.Empty<CapturedItem>(), Array.Empty<string>()); } }
    private sealed class Boom : IOpenItemCapturer { public CaptureScan Scan() => throw new InvalidOperationException("x"); }

    [Fact] public async Task A_fast_scan_returns_its_items_and_notes()
    {
        var r = await CaptureScanning.BoundedAsync(new Quick(), TimeSpan.FromSeconds(5));
        Assert.Equal("https://e.example/", r.Items.Single().Target); Assert.Equal("n", r.Notes.Single());
    }

    [Fact] public async Task A_stuck_scan_times_out_with_a_note_instead_of_waiting_forever()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = await CaptureScanning.BoundedAsync(new Stuck(), TimeSpan.FromMilliseconds(100));
        sw.Stop();
        Assert.Empty(r.Items); Assert.Contains(r.Notes, n => n.Contains("took too long"));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4), $"bounded scan took {sw.Elapsed}");
    }

    [Fact] public async Task Cancellation_stops_the_wait()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CaptureScanning.BoundedAsync(new Stuck(), TimeSpan.FromSeconds(30), cts.Token));
    }

    [Fact] public async Task A_failing_source_reaches_the_caller()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => CaptureScanning.BoundedAsync(new Boom(), TimeSpan.FromSeconds(5)));
    }
}
