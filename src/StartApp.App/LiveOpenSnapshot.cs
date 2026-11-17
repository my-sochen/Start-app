using StartApp.Core;
using StartApp.Platform.Windows;

namespace StartApp.App;

/// <summary>One serialized, bounded observation per attempted open. No cached positives, never claims complete coverage.</summary>
internal sealed class LiveOpenSnapshot : IOpenSnapshotSource
{
    private int _active;
    public OpenState Detect(Item item, LaunchConfig? launch) => OpenState.Unknown;
    public async Task<IOpenStateDetector> ObserveAsync(IReadOnlyCollection<Item> selected, CancellationToken token)
    {
        var sources = new List<IOpenItemCapturer>();
        if (selected.Any(i => i.Type is ItemType.Program or ItemType.Folder)) sources.Add(new WindowsOpenItemCapturer());
        if (selected.Any(i => i.Type == ItemType.Link)) sources.Add(new BrowserAddressBarCapturer(exactEvidence: true));
        if (sources.Count == 0) return new NoOpenStateDetection();
        var source = new CompositeCapturer(sources.ToArray());
        if (Interlocked.CompareExchange(ref _active,1,0) != 0) return new NoOpenStateDetection();
        try
        {
            var scan = await CaptureScanning.BoundedAsync(source, TimeSpan.FromSeconds(10), token, () => Volatile.Write(ref _active,0));
            token.ThrowIfCancellationRequested();
            return new ObservedOpenSnapshot(scan.Items);
        }
        catch(OperationCanceledException) { return new NoOpenStateDetection(); }
        catch(Exception e) when(e is System.IO.IOException or InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or NotSupportedException)
        { return new NoOpenStateDetection(); }
    }
}
