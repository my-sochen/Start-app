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

/// <summary>PARTIAL site detection: reads the address bar of each open Chrome/Edge/Brave window through UI Automation. It sees only the ACTIVE tab of each window
/// (not background tabs), and only browsers whose address bar is named "Address and search bar". Other tabs need a browser extension (not built, not decided).</summary>
public sealed class BrowserAddressBarCapturer : IOpenItemCapturer
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase) { "chrome", "msedge", "brave" };
    public CaptureScan Scan()
    {
        var items = new List<CapturedItem>(); var notes = new List<string>();
        var ids = System.Diagnostics.Process.GetProcesses().Where(p => { try { return Browsers.Contains(p.ProcessName); } catch (InvalidOperationException) { return false; } }).Select(p => p.Id).ToHashSet();
        var root = System.Windows.Automation.AutomationElement.RootElement;
        foreach (System.Windows.Automation.AutomationElement w in root.FindAll(System.Windows.Automation.TreeScope.Children, System.Windows.Automation.Condition.TrueCondition))
        {
            try
            {
                if (!ids.Contains(w.Current.ProcessId)) continue;
                var edit = w.FindFirst(System.Windows.Automation.TreeScope.Descendants, new System.Windows.Automation.AndCondition(
                    new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.Edit),
                    new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, "Address and search bar")));
                if (edit is null) continue;
                var v = ((System.Windows.Automation.ValuePattern)edit.GetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern)).Current.Value;
                var site = CaptureRules.SiteFromAddressText(v);
                if (site is null) continue;
                var host = Uri.TryCreate(site, UriKind.Absolute, out var u) ? u.Host : site;
                items.Add(new CapturedItem(ItemType.Link, host, site));
            }
            catch (Exception e) when (e is System.Windows.Automation.ElementNotAvailableException or InvalidOperationException) { }
        }
        notes.Add("Sites: only the active tab of each open Chrome/Edge/Brave window is detected, not other tabs");
        return new(CaptureRules.Dedupe(items), notes);
    }
}
