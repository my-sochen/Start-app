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
        _store = new JsonStore(Path.Combine(dir, "store.json"));
        var (launcher, preflight) = WindowsComposition.Create(); // real adapters; the preflight is required, it is what keeps blocked items from reaching the launcher
        var vm = new MainViewModel(_store, launcher, preflight, capturer: new CompositeCapturer(new WindowsOpenItemCapturer(), new BrowserAddressBarCapturer()));
        // An unexpected error in an open operation is rethrown on the UI dispatcher (loud, not a silently faulted task); the view model already reset its state and the report.
        vm.UnexpectedError += ex => Dispatcher.BeginInvoke(() => System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw());
        new MainWindow { DataContext = vm }.Show();
    }
    protected override void OnExit(ExitEventArgs e) { _store?.Dispose(); base.OnExit(e); }
}

/// <summary>Site detection: reads the address bar of each open Chrome/Edge/Brave window through UI Automation, briefly selecting EVERY tab
/// in turn (then restoring the tab the user was on) so every tab's address is captured, not just the active one. A browser extension
/// (prototype bridge proven on Edge only, not decided) would avoid the visible tab switching.</summary>
public sealed class BrowserAddressBarCapturer : IOpenItemCapturer
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase) { "chrome", "msedge", "brave" };
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    public CaptureScan Scan()
    {
        var items = new List<CapturedItem>(); var notes = new List<string>();
        var ids = System.Diagnostics.Process.GetProcesses().Where(p => { try { return Browsers.Contains(p.ProcessName); } catch (InvalidOperationException) { return false; } }).Select(p => p.Id).ToHashSet();
        var handles = new List<IntPtr>();
        EnumWindows((h, _) => { if (IsWindowVisible(h)) { GetWindowThreadProcessId(h, out var pid); if (ids.Contains((int)pid)) handles.Add(h); } return true; }, IntPtr.Zero);
        // Only browser windows are touched through UI Automation (never this app's own window, which would deadlock on the UI thread).
        foreach (var hwnd in handles)
        {
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
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // The address bar always shows the ACTIVE tab's address, so each read below follows a real tab selection.
                void Collect()
                {
                    var site = CaptureRules.SiteFromAddressText(value.Current.Value);
                    if (site is null || !seen.Add(site)) return;
                    var host = Uri.TryCreate(site, UriKind.Absolute, out var u) ? u.Host : site;
                    items.Add(new CapturedItem(ItemType.Link, host, site));
                }
                Collect(); // the tab the user is looking at
                var tabs = w.FindAll(System.Windows.Automation.TreeScope.Descendants,
                    new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.TabItem));
                foreach (System.Windows.Automation.AutomationElement t in tabs)
                {
                    try
                    {
                        if (t.GetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern) is not System.Windows.Automation.SelectionItemPattern sip) continue;
                        if (sip.Current.IsSelected) { originallySelected ??= t; continue; }
                        var before = value.Current.Value;
                        sip.Select();
                        var until = DateTime.UtcNow.AddMilliseconds(400);
                        while (DateTime.UtcNow < until)
                        {
                            System.Threading.Thread.Sleep(50);
                            try { if (value.Current.Value != before) break; } catch (System.Windows.Automation.ElementNotAvailableException) { break; }
                        }
                        Collect();
                    }
                    catch (Exception e) when (e is System.Windows.Automation.ElementNotAvailableException or InvalidOperationException) { }
                }
            }
            catch (Exception e) when (e is System.Windows.Automation.ElementNotAvailableException or InvalidOperationException) { }
            // Leave the user's browser exactly as found: re-select the tab that was active before the scan.
            if (originallySelected != null)
                try { ((System.Windows.Automation.SelectionItemPattern)originallySelected.GetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern)).Select(); }
                catch (Exception e) when (e is System.Windows.Automation.ElementNotAvailableException or InvalidOperationException) { }
        }
        notes.Add("Sites: scans open browser tabs by briefly selecting each one to read its address; verified on Edge with a single window so far - Chrome, Brave, multiple windows and discarded tabs are not verified");
        return new(CaptureRules.Dedupe(items), notes);
    }
}
