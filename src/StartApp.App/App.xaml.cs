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
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StartApp");
        // crash evidence is opt-in (the e2e harnesses set STARTAPP_CRASHLOG=1): the product binary keeps no diagnostic surface
        if (Environment.GetEnvironmentVariable("STARTAPP_CRASHLOG") == "1")
            CrashLogger.Attach(Path.Combine(dir, "crash.log"), this); // evidence only; never marks anything handled
        _store = new JsonStore(Path.Combine(dir, "store.json"));
        var (launcher, preflight) = WindowsComposition.Create(); // real adapters; the preflight is required, it is what keeps blocked items from reaching the launcher
        var vm = new MainViewModel(_store, launcher, preflight, detector: new LiveOpenSnapshot(), capturer: new CompositeCapturer(new WindowsOpenItemCapturer(), new BrowserAddressBarCapturer()));
        // An unexpected error in an open operation is rethrown on the UI dispatcher (loud, not a silently faulted task); the view model already reset its state and the report.
        vm.UnexpectedError += ex => Dispatcher.BeginInvoke(() => System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw());
        new MainWindow { DataContext = vm }.Show();
    }
    protected override void OnExit(ExitEventArgs e) { _store?.Dispose(); base.OnExit(e); }
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
        using var focus = new ScanFocusLease(cancellationToken);
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
            if (!focus.CanContinue) { notes.Add("Browser scan stopped after user activity or an uncertain focus change"); break; }
            focus.ObserveBrowser(hwnd);
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
                // Read the active tab's editable address text after tab selection; it is not committed-document evidence.
                void Collect(System.Windows.Automation.AutomationElement? tab, string? before = null)
                {
                    var address = value.Current.Value;
                    if (_exactEvidence)
                    {
                        if (tab is null || ExactObservedUrl.Key(address) is null || (before is not null && address == before)) return;
                        // Prototype policy approved Nov 17: accept stable selected-tab omnibox evidence, including the
                        // disclosed risk of typed-but-unsubmitted text. This does NOT prove a committed document URL.
                        // Stability checks reduce transient reads, not that accepted semantic limitation.
                        for (int read = 0; read < 3; read++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!focus.CanContinue || tab.GetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern) is not System.Windows.Automation.SelectionItemPattern selected || !selected.Current.IsSelected) return;
                            System.Threading.Thread.Sleep(100);
                            if (value.Current.Value != address) return;
                        }
                    }
                    var site = _exactEvidence ? address : CaptureRules.SiteFromAddressText(address);
                    if (site is null || !seen.Add(site)) return;
                    var host = Uri.TryCreate(site, UriKind.Absolute, out var u) ? u.Host : site;
                    items.Add(new CapturedItem(ItemType.Link, host, site, browserPath));
                    if (browserPath is null) unidentifiedSites++;
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
                Collect(originallySelected);
                foreach (System.Windows.Automation.AutomationElement t in tabs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!focus.CanContinue) break;
                    try
                    {
                        if (t.GetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern) is not System.Windows.Automation.SelectionItemPattern sip) continue;
                        if (sip.Current.IsSelected) continue; // the active tab was already captured by the first Collect()
                        var before = value.Current.Value;
                        if (!focus.CanContinue) break;
                        sip.Select();
                        var until = DateTime.UtcNow.AddMilliseconds(400);
                        while (DateTime.UtcNow < until)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!focus.CanContinue) break;
                            System.Threading.Thread.Sleep(50);
                            try { if (value.Current.Value != before) break; } catch (System.Windows.Automation.ElementNotAvailableException) { break; }
                        }
                        if (!focus.CanContinue) break;
                        Collect(t, before);
                    }
                    catch (Exception e) when (e is System.Windows.Automation.ElementNotAvailableException or InvalidOperationException) { }
                }
            }
            catch (Exception e) when (e is System.Windows.Automation.ElementNotAvailableException or InvalidOperationException) { }
            finally
            {
                // Best effort only: restore the original tab unless input or uncertain focus revoked permission.
                // Cancelled/timed-out snapshots also revoke cleanup writes, preventing a late return after a caller has moved on.
                if (originallySelected != null && focus.CanContinue)
                    try
                    {
                        var restore = (System.Windows.Automation.SelectionItemPattern)originallySelected.GetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern);
                        // Pattern acquisition may block. Recheck immediately before the native write, not just before acquisition.
                        if (!cancellationToken.IsCancellationRequested && focus.CanContinue) restore.Select();
                    }
                    catch (Exception e) when (e is System.Windows.Automation.ElementNotAvailableException or InvalidOperationException) { }
            }
        }
        if (unidentifiedSites > 0)
            notes.Add($"{unidentifiedSites} site(s) were captured without their source browser: its exe path could not be read (for example an elevated browser); those sites open with the Windows default browser");
        notes.Add("Sites: scans open browser tabs by briefly selecting each one to read its address; verified in hosted-runner scenarios on Edge and Chrome, including Edge with a Hebrew UI and one scenario with two distinct Edge windows - Brave, discarded tabs and other browsers or versions are not verified. The same site open in several browsers appears once per browser, with the browser named, so you choose what to keep. A captured site keeps the browser it was found in as its default browser when the browser can be identified; browser profiles are not retained");
        return new(CaptureRules.Dedupe(items), notes);
    }
}
