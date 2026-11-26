using System.IO;
using System.Windows;
using StartApp.Core;
using StartApp.Platform.Windows;
using StartApp.Presentation;
using StartApp.Storage.Json;

namespace StartApp.App;

public partial class App : Application
{
    // Composition root: the only place that wires concrete implementations to the Core ports.
    private JsonStore? _store;
    private SingleInstanceSession? _instance;
    private void OnStartup(object sender, StartupEventArgs e)
    {
        // Phase 2 apply-helper mode: the staged-copy instance performs the post-close swap and exits.
        if (e.Args.Length > 0 && e.Args[0] == "--apply-update") { Shutdown(UpdateApplyCli.Run(e.Args)); return; }
        _instance = new SingleInstanceSession(() => Dispatcher.BeginInvoke(() =>
        {
            if (MainWindow is MainWindow window) { window.Show(); if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal; window.Activate(); }
        }));
        if (!_instance.IsFirst) { Shutdown(); return; }
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StartApp");
        // crash evidence is opt-in (the e2e harnesses set STARTAPP_CRASHLOG=1): the product binary keeps no diagnostic surface
        if (Environment.GetEnvironmentVariable("STARTAPP_CRASHLOG") == "1")
            CrashLogger.Attach(Path.Combine(dir, "crash.log"), this); // evidence only; never marks anything handled
        _store = new JsonStore(Path.Combine(dir, "store.json"));
        var (launcher, preflight) = WindowsComposition.Create(); // real adapters; the preflight is required, it is what keeps blocked items from reaching the launcher
        var vm = new MainViewModel(_store, launcher, preflight, detector: new LiveOpenSnapshot(), capturer: new CompositeCapturer(new WindowsOpenItemCapturer(), new BrowserAddressBarCapturer()), fileRevealer: new WindowsFileRevealer());
        // An unexpected error in an open operation is rethrown on the UI dispatcher (loud, not a silently faulted task); the view model already reset its state and the report.
        vm.UnexpectedError += ex => Dispatcher.BeginInvoke(() => System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw());
        new MainWindow { DataContext = vm }.Show();
        // Logon autostart (installer Run key): open the סביבה גלובלית items after the window is up.
        if (Array.IndexOf(e.Args, "--autostart") >= 0) vm.OpenGlobalItemsOnStart();
    }
    protected override void OnExit(ExitEventArgs e) { _instance?.Dispose(); _store?.Dispose(); base.OnExit(e); }
}

/// <summary>Site detection: reads the address bar of each open Chrome/Edge/Brave window through UI Automation, briefly selecting EVERY tab
/// in turn (then restoring the tab the user was on) so every tab's address is captured, not just the active one. A browser extension
/// (prototype bridge proven on Edge only, not decided) would avoid the visible tab switching.</summary>
public sealed class BrowserAddressBarCapturer : ICooperativeCapturer
{
    private readonly bool _exactEvidence;
    public BrowserAddressBarCapturer(bool exactEvidence = false) { _exactEvidence = exactEvidence; }
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase) { "chrome", "msedge", "brave" };
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    private static readonly SemaphoreSlim ScanGate = new(1, 1);
    public CaptureScan Scan() => Scan(CancellationToken.None);
    public CaptureScan Scan(CancellationToken cancellationToken)
    {
        if (!ScanGate.Wait(0)) return new(Array.Empty<CapturedItem>(), new[] { "A browser scan is still active; no overlapping scan was started" });
        try { return ScanCore(cancellationToken); }
        finally { ScanGate.Release(); }
    }
    private CaptureScan ScanCore(CancellationToken cancellationToken)
    {
        long traceScan = 0;
        BrowserCaptureTrace.Begin(ref traceScan);
        BrowserCaptureTrace.Write(traceScan, "mode-exact", flag: _exactEvidence);
        // Input/focus changes no longer stop traversal. This lease is consulted only for best-effort window-focus return.
        using var windowFocus = new ScanFocusLease(cancellationToken, traceScan);
        var items = new List<CapturedItem>(); var notes = new List<string>();
        // The exe path of each browser process is captured up front so a found site can keep its source browser as the
        // item's default browser (spec Nov 11). The path can be unreadable (e.g. an elevated browser); the site is still
        // captured, just without a retained browser.
        var paths = new Dictionary<int, string?>();
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            // Process handles must still be disposed even after cancellation; don't throw before finally.
            try
            {
                if (!Browsers.Contains(p.ProcessName)) continue;
                string? exe = null;
                try { exe = p.MainModule?.FileName; }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                paths[p.Id] = exe;
            }
            catch (InvalidOperationException) { }
            finally { p.Dispose(); } // every enumerated Process (including all Chromium children) releases its handle on every branch
        }
        var unidentifiedSites = 0;
        var handles = new List<(IntPtr Hwnd, int Pid)>();
        EnumWindows((h, _) => { if (IsWindowVisible(h)) { GetWindowThreadProcessId(h, out var pid); if (paths.ContainsKey((int)pid)) handles.Add((h, (int)pid)); } return true; }, IntPtr.Zero);
        // Only browser windows are touched through UI Automation (never this app's own window, which would deadlock on the UI thread).
        foreach (var (hwnd, pid) in handles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            windowFocus.ObserveBrowser(hwnd);
            var browserPath = paths.TryGetValue(pid, out var bp) ? bp : null;
            System.Windows.Automation.AutomationElement? originallySelected = null;
            try
            {
                var w = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
                var edit = w.FindFirst(System.Windows.Automation.TreeScope.Descendants, new System.Windows.Automation.AndCondition(
                    new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.Edit),
                    new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, "Address and search bar")));
                if (edit is null)
                {
                    // Locale-independent fallback: the omnibox class name (OmniboxViewViews, BraveOmniboxViewViews) stays the
                    // same in every UI language; the accessible Name ("Address and search bar") is localized (IDS_ACCNAME_LOCATION).
                    var edits = w.FindAll(System.Windows.Automation.TreeScope.Descendants,
                        new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.Edit));
                    foreach (System.Windows.Automation.AutomationElement e in edits)
                    {
                        try { if ((e.Current.ClassName ?? "").EndsWith("OmniboxViewViews", StringComparison.Ordinal)) { edit = e; break; } }
                        catch (Exception ex) when (ex is System.Windows.Automation.ElementNotAvailableException or InvalidOperationException) { }
                    }
                }
                if (edit is null) continue;
                var value = (System.Windows.Automation.ValuePattern)edit.GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                // Optional transient stability signal, never a new eligibility/ownership requirement.
                // Missing/ambiguous documents (including about:blank) keep the existing omnibox policy.
                string? DocumentIdentity()
                {
                    var roots = new List<string>();
                    try
                    {
                        var documents = w.FindAll(System.Windows.Automation.TreeScope.Descendants,
                            new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.Document));
                        if (documents.Count > 64) return null;
                        foreach (System.Windows.Automation.AutomationElement document in documents)
                        {
                            var current = document.Current;
                            if (current.IsOffscreen || current.BoundingRectangle.Width <= 0 || current.BoundingRectangle.Height <= 0) continue;
                            var parent = document; var rooted = false; var nested = false;
                            for (var depth = 0; depth < 32; depth++)
                            {
                                parent = System.Windows.Automation.TreeWalker.ControlViewWalker.GetParent(parent);
                                if (parent is null) break;
                                if (System.Windows.Automation.Automation.Compare(parent, w)) { rooted = true; break; }
                                if (parent.Current.ControlType == System.Windows.Automation.ControlType.Document) { nested = true; break; }
                            }
                            if (rooted && !nested) roots.Add(string.Join(".", document.GetRuntimeId()));
                        }
                    }
                    catch (Exception e) when (e is System.Windows.Automation.ElementNotAvailableException or InvalidOperationException) { return null; }
                    return roots.Count == 1 ? roots[0] : null;
                }
                // No input/focus abort. Selected-state and stable reads reduce races, not committed-URL proof.
                // Typed-but-unsubmitted values and a delayed but stable old omnibox remain indistinguishable.
                // A bounded failure is reported as incomplete, never silently treated as captured.
                bool Collect(System.Windows.Automation.AutomationElement tab, int index)
                {
                    var budget = System.Diagnostics.Stopwatch.StartNew();
                    const int budgetMs = 1200;
                    for (var attempt = 0; attempt < 3 && budget.ElapsedMilliseconds < budgetMs; attempt++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var selected = (System.Windows.Automation.SelectionItemPattern)tab.GetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern);
                        BrowserTraversalProbe.At("visit", index);
                        var wasSelected = selected.Current.IsSelected;
                        if (!wasSelected)
                        {
                            BrowserTraversalProbe.At("before-select", index);
                            cancellationToken.ThrowIfCancellationRequested();
                            selected.Select();
                            BrowserTraversalProbe.At("after-select", index);
                        }
                        string? previous = null; string? previousDocument = null;
                        long stableSince = -1;
                        while (budget.ElapsedMilliseconds < budgetMs)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!selected.Current.IsSelected) break;
                            BrowserTraversalProbe.At("before-read", index);
                            var document = DocumentIdentity();
                            var address = value.Current.Value;
                            BrowserTraversalProbe.At("after-read", index);
                            // The check AFTER the address read is required, including the final stable read.
                            if (!selected.Current.IsSelected) break;
                            if (address != previous || document != previousDocument)
                            {
                                previous = address; previousDocument = document;
                                stableSince = budget.ElapsedMilliseconds;
                            }
                            if (stableSince < 0 || budget.ElapsedMilliseconds - stableSince < 300)
                            {
                                System.Threading.Thread.Sleep(100);
                                continue;
                            }
                            BrowserTraversalProbe.At("stable-candidate", index);
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!selected.Current.IsSelected) break;
                            var site = _exactEvidence ? (ExactObservedUrl.Key(address) is null ? null : address) : CaptureRules.SiteFromAddressText(address);
                            if (site is null) { BrowserCaptureTrace.Write(traceScan, "collect-ineligible", index: index); return true; }
                            if (!seen.Add(site)) { BrowserCaptureTrace.Write(traceScan, "collect-duplicate", index: index); return true; }
                            var host = Uri.TryCreate(site, UriKind.Absolute, out var u) ? u.Host : site;
                            items.Add(new CapturedItem(ItemType.Link, host, site, browserPath));
                            if (browserPath is null) unidentifiedSites++;
                            BrowserCaptureTrace.Write(traceScan, "collect-added", index: index, count: items.Count);
                            return true;
                        }
                    }
                    notes.Add("A browser tab could not be read consistently; the captured list may be incomplete");
                    BrowserCaptureTrace.Write(traceScan, "tab-inconclusive", index: index);
                    return false;
                }
                var tabs = w.FindAll(System.Windows.Automation.TreeScope.Descendants,
                    new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.TabItem));
                // Snapshot the user's active tab BEFORE any Select(): the first Select below deselects it,
                // so discovering it inside the scan loop misses it whenever it is not first in UIA
                // enumeration, and the restore at the end would strand the user on the last tab.
                foreach (System.Windows.Automation.AutomationElement t in tabs)
                {
                    try
                    {
                        if (t.GetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern) is System.Windows.Automation.SelectionItemPattern pre && pre.Current.IsSelected)
                        { originallySelected = t; break; }
                    }
                    catch (Exception e) when (e is System.Windows.Automation.ElementNotAvailableException or InvalidOperationException) { }
                }
                BrowserTraversalProbe.At("window", hwnd.ToInt32());
                BrowserCaptureTrace.Write(traceScan, "tabs-enumerated", count: tabs.Count, flag: originallySelected is not null);
                var traceTab = -1;
                foreach (System.Windows.Automation.AutomationElement t in tabs)
                {
                    traceTab++;
                    BrowserCaptureTrace.Write(traceScan, "tab-start", index: traceTab, flag: cancellationToken.IsCancellationRequested);
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        Collect(t, traceTab); // Visit every snapshotted tab, even if currently selected.
                    }
                    catch (Exception e) when (e is System.Windows.Automation.ElementNotAvailableException or InvalidOperationException)
                    { notes.Add("A browser tab became unavailable; the captured list may be incomplete"); BrowserCaptureTrace.Write(traceScan, "tab-exception", index: traceTab, flag: e is System.Windows.Automation.ElementNotAvailableException); }
                }
            }
            catch (Exception e) when (e is System.Windows.Automation.ElementNotAvailableException or InvalidOperationException)
            { notes.Add("A browser window could not be read; the captured list may be incomplete"); BrowserCaptureTrace.Write(traceScan, "window-exception", flag: e is System.Windows.Automation.ElementNotAvailableException); }
            finally
            {
                // Restore the originally selected tab even after input/focus changes.
                // Explicit cancellation/timeout still forbids late native writes; foreground-window return is separate.
                // Cancelled/timed-out snapshots also revoke cleanup writes, preventing a late return after a caller has moved on.
                if (originallySelected != null && !cancellationToken.IsCancellationRequested)
                    try
                    {
                        var restore = (System.Windows.Automation.SelectionItemPattern)originallySelected.GetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern);
                        BrowserTraversalProbe.At("before-restore", -1);
                        // Pattern acquisition/test observation may block. Recheck immediately before the native write.
                        if (!cancellationToken.IsCancellationRequested) restore.Select();
                    }
                    catch (Exception e) when (e is System.Windows.Automation.ElementNotAvailableException or InvalidOperationException) { }
            }
        }
        if (unidentifiedSites > 0)
            notes.Add($"{unidentifiedSites} site(s) were captured without their source browser: its exe path could not be read (for example an elevated browser); those sites open with the Windows default browser");
        notes.Add("Sites: scans open browser tabs by briefly selecting each one to read its address; verified in hosted-runner scenarios on Edge and Chrome, including Edge with a Hebrew UI and one scenario with two distinct Edge windows - Brave, discarded tabs and other browsers or versions are not verified. The same site open in several browsers appears once per browser, with the browser named, so you choose what to keep. A captured site keeps the browser it was found in as its default browser when the browser can be identified; browser profiles are not retained");
        BrowserCaptureTrace.Write(traceScan, "end", count: items.Count);
        return new(CaptureRules.Dedupe(items), notes);
    }
}
