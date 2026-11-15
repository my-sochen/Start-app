using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;

namespace StartApp.Presentation.Tests;

/// <summary>The capture scan runs off the UI thread (a stuck COM/UIA call must never freeze the window): the capture screen opens
/// immediately with a visible scanning state, a stuck scan is bounded with an honest note, and X discards a late result.</summary>
public class CaptureAsyncScanTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");
    private sealed class BlockingCapturer : IOpenItemCapturer
    {
        public readonly ManualResetEventSlim Gate = new(false);
        public int ScanCalls;
        public CaptureScan Scan() { Interlocked.Increment(ref ScanCalls); Gate.Wait(TimeSpan.FromSeconds(30)); return new(new[] { new CapturedItem(ItemType.Folder, "proj", "D:\\proj") }, Array.Empty<string>()); }
    }
    private sealed class SleepingCapturer : IOpenItemCapturer
    {
        public CaptureScan Scan() { Thread.Sleep(TimeSpan.FromSeconds(5)); return new(new[] { new CapturedItem(ItemType.Folder, "proj", "D:\\proj") }, Array.Empty<string>()); }
    }
    private static void ScanDone(MainViewModel vm)
    {
        Assert.NotNull(vm.CurrentScan);
        Assert.True(vm.CurrentScan!.Wait(TimeSpan.FromSeconds(15)), "scan did not finish");
        vm.CurrentScan!.GetAwaiter().GetResult();
    }

    [Fact] public void The_screen_opens_at_once_with_a_scanning_state_and_rows_arrive_when_the_scan_finishes()
    {
        using var repo = new JsonStore(Tmp()); var cap = new BlockingCapturer();
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        try
        {
            vm.CaptureNewCommand.Execute(null);
            Assert.True(vm.IsCaptureNew); Assert.True(vm.IsScanning);                 // the screen is up before the scan ends
            Assert.Empty(vm.CaptureRows); Assert.False(vm.CaptureEmpty);              // no "nothing detected" flash while scanning
            vm.NewName = "x";
            Assert.False(vm.CaptureCreateCommand.CanExecute(null));                   // create waits for the scan
            cap.Gate.Set();
            ScanDone(vm);
            Assert.False(vm.IsScanning);
            Assert.Equal("proj", vm.CaptureRows.Single().Name);
            Assert.True(vm.CaptureCreateCommand.CanExecute(null));
        }
        finally { cap.Gate.Set(); }
    }

    [Fact] public void X_during_a_slow_scan_discards_the_late_result()
    {
        using var repo = new JsonStore(Tmp()); var cap = new BlockingCapturer();
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        try
        {
            vm.CaptureNewCommand.Execute(null);
            Assert.True(vm.IsScanning);
            vm.CaptureCloseCommand.Execute(null);                                     // nothing dirty: closes at once
            Assert.False(vm.CaptureOpen); Assert.False(vm.IsScanning);
            cap.Gate.Set();
            ScanDone(vm);
            Assert.Empty(vm.CaptureRows); Assert.False(vm.CaptureOpen);               // nothing applied after X
        }
        finally { cap.Gate.Set(); }
    }

    [Fact] public void A_scan_that_takes_too_long_is_stopped_with_an_honest_note_and_the_screen_stays_usable()
    {
        using var repo = new JsonStore(Tmp());
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: new SleepingCapturer()) { ScanMinVisible = TimeSpan.Zero, ScanTimeout = TimeSpan.FromMilliseconds(150) };
        vm.CaptureNewCommand.Execute(null);
        ScanDone(vm);
        Assert.False(vm.IsScanning); Assert.Empty(vm.CaptureRows);
        Assert.Contains(vm.CaptureNotes, n => n.Contains("took too long"));
        Assert.True(vm.CaptureEmpty);                                                 // the honest empty state shows after the stopped scan
        Assert.Equal("Nothing open was detected", vm.Status);
        Assert.True(vm.CaptureCloseCommand.CanExecute(null));                         // the screen never froze
    }

    [Fact] public void Stuck_scans_do_not_accumulate_forever__after_two_unreturned_scans_no_new_one_starts()
    {
        using var repo = new JsonStore(Tmp()); var cap = new BlockingCapturer();      // the gate stays closed: every scan hangs until the test ends
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero, ScanTimeout = TimeSpan.FromMilliseconds(80) };
        try
        {
            vm.CaptureNewCommand.Execute(null); ScanDone(vm);                         // scan 1 hangs -> times out, abandoned
            Assert.Contains(vm.CaptureNotes, n => n.Contains("took too long"));
            vm.CaptureCloseCommand.Execute(null);
            vm.CaptureNewCommand.Execute(null); ScanDone(vm);                         // scan 2 hangs -> times out, abandoned
            vm.CaptureCloseCommand.Execute(null);
            vm.CaptureNewCommand.Execute(null); ScanDone(vm);                         // two calls still stuck: no third scan starts
            Assert.Equal(2, cap.ScanCalls);
            Assert.Contains(vm.CaptureNotes, n => n.Contains("no new scan was started"));
            Assert.False(vm.IsScanning); Assert.True(vm.CaptureOpen);                 // the screen itself stays usable
        }
        finally { cap.Gate.Set(); }                                                   // let the stuck calls return so the process stays clean
    }
}
