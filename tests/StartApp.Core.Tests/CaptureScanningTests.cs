using StartApp.Core;

namespace StartApp.Core.Tests;

public class CaptureScanningTests
{
    private sealed class Quick : IOpenItemCapturer { public CaptureScan Scan() => new(new[] { new CapturedItem(ItemType.Link, "e", "https://e.example/") }, new[] { "n" }); }
    private sealed class Stuck : IOpenItemCapturer { public CaptureScan Scan() { Thread.Sleep(TimeSpan.FromSeconds(5)); return new(Array.Empty<CapturedItem>(), Array.Empty<string>()); } }
    private sealed class Blocking : IOpenItemCapturer
    {
        private readonly ManualResetEventSlim _gate; public bool ScanBlocked;
        public Blocking(ManualResetEventSlim gate) { _gate = gate; }
        public CaptureScan Scan() { ScanBlocked = true; _gate.Wait(); ScanBlocked = false; return new(Array.Empty<CapturedItem>(), Array.Empty<string>()); }
    }
    private sealed class Boom : IOpenItemCapturer { public CaptureScan Scan() => throw new InvalidOperationException("x"); }

    [Fact] public async Task A_fast_scan_returns_its_items_and_notes()
    {
        var r = await CaptureScanning.BoundedAsync(new Quick(), TimeSpan.FromSeconds(5));
        Assert.Equal("https://e.example/", r.Items.Single().Target); Assert.Equal("n", r.Notes.Single());
    }

    [Fact] public async Task A_stuck_scan_times_out_with_a_note_instead_of_waiting_forever()
    {
        // Deterministic: the scan blocks on a gate, so a correct timeout returns while Scan() is STILL blocked. No wall-clock
        // assert - the 15 s watchdog only catches a regression back to waiting on the scan itself (it would hang otherwise).
        var gate = new ManualResetEventSlim(false); var stuck = new Blocking(gate);
        var r = await CaptureScanning.BoundedAsync(stuck, TimeSpan.FromMilliseconds(100)).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(stuck.ScanBlocked, "returned only after the scan finished - the timeout did not bound the wait");
        Assert.Empty(r.Items); Assert.Contains(r.Notes, n => n.Contains("took too long"));
        gate.Set();
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
