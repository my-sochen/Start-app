using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using StartApp.App;
using StartApp.Core;

internal static class Program
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    private static readonly List<object> Results = new();
    private static AutomationElement Window = null!;
    private static AutomationElement[] Tabs = null!;
    private static void Check(string name, bool ok) { Results.Add(new { name, ok }); if (!ok) throw new Exception(name); }
    private static SelectionItemPattern Selection(int i) => (SelectionItemPattern)Tabs[i].GetCurrentPattern(SelectionItemPattern.Pattern);
    private static int Selected() => Array.FindIndex(Tabs, t => ((SelectionItemPattern)t.GetCurrentPattern(SelectionItemPattern.Pattern)).Current.IsSelected);
    private static void Select(int i) { Selection(i).Select(); Thread.Sleep(180); }
    private static bool Complete(CaptureScan scan) => new[] { "www.python.org", "www.mozilla.org", "www.wikipedia.org" }.All(host => scan.Items.Any(i => Uri.TryCreate(i.Target, UriKind.Absolute, out var u) && u.Host == host));
    private static string Address()
    {
        var edits = Window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)).Cast<AutomationElement>().Where(e => e.Current.ClassName.EndsWith("OmniboxViewViews", StringComparison.Ordinal)).ToArray();
        if (edits.Length != 1) throw new Exception("missing or ambiguous fixture omnibox");
        return ((ValuePattern)edits[0].GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
    }
    private static bool PublicPageContent(int index)
    {
        if (index >= 4) return true;
        var marker = index <= 1 ? "Python is a programming language" : index == 2 ? "Firefox" : "The Free Encyclopedia";
        foreach (AutomationElement document in Window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document)))
        {
            if (!document.Current.IsOffscreen && document.TryGetCurrentPattern(TextPattern.Pattern, out var text) &&
                ((TextPattern)text).DocumentRange.GetText(16000).Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
    private static void VerifyFixture()
    {
        var expected = new[] { "www.python.org", "www.python.org", "www.mozilla.org", "www.wikipedia.org", "about:blank", "about:blank" };
        for (var i = 0; i < expected.Length; i++)
        {
            Select(i); var timer = System.Diagnostics.Stopwatch.StartNew(); string? previous = null; var ready = false;
            while (timer.ElapsedMilliseconds < 10000)
            {
                var address = Address();
                var host = Uri.TryCreate(address, UriKind.Absolute, out var u) ? u.Host : "";
                var matches = expected[i] == "about:blank" ? address == expected[i] : host == expected[i];
                if (Selected() == i && matches && address == previous && PublicPageContent(i) && Selected() == i) { ready = true; break; }
                previous = matches ? address : null; Thread.Sleep(150);
            }
            Check($"real public website/blank readiness tab {i}", ready);
        }
    }
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" || args.Length != 2) return 2;
        var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        try
        {
            var hwnd = new IntPtr(long.Parse(args[0]));
            Window = AutomationElement.FromHandle(hwnd);
            Tabs = Window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem)).Cast<AutomationElement>().ToArray();
            Check("real fixture has six native tabs", Tabs.Length == 6);
            VerifyFixture();
#if STARTAPP_CAPTURE_TEST
            foreach (var exact in new[] { false, true })
            {
                foreach (var scenario in new[] { "same-url-and-blank", "select-uncaptured", "lose-selection-after-final-read", "persistent-interference", "cancel-before-select", "cancel-before-restore", "timeout-before-select" })
                {
                    Select(1);
                    var inFixture = false; var fired = 0; var visits = new HashSet<int>(); var visitCounts = new Dictionary<int, int>();
                    var token = new CancellationTokenSource();
                    using var held = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
                    using var settled = new ManualResetEventSlim();
                    BrowserTraversalProbe.Observer = (stage, index) =>
                    {
                        if (stage == "window") { inFixture = index == hwnd.ToInt32(); return; }
                        if (!inFixture) return;
                        if (stage == "visit") { visits.Add(index); visitCounts[index] = visitCounts.GetValueOrDefault(index) + 1; }
                        if (scenario == "select-uncaptured" && stage == "visit" && index == 2 && fired++ == 0)
                        { Select(2); Check($"{exact} native input injection succeeded", SetCursorPos(30, 40)); }
                        if (scenario == "lose-selection-after-final-read" && stage == "stable-candidate" && index == 1 && fired++ == 0)
                        { Select(0); }
                        if (scenario == "persistent-interference" && stage == "after-read" && index == 1)
                        { fired++; Select(0); }
                        if (scenario == "cancel-before-select" && stage == "before-select" && fired++ == 0) token.Cancel();
                        if (scenario == "cancel-before-restore" && stage == "before-restore" && fired++ == 0) token.Cancel();
                        if (scenario == "timeout-before-select" && stage == "before-select" && fired++ == 0)
                        { held.Set(); if (!release.Wait(5000)) throw new Exception("test boundary release timeout"); }
                    };
                    var capturer = new BrowserAddressBarCapturer(exact);
                    if (scenario == "timeout-before-select")
                    {
                        var real = new SettledCapturer(capturer, settled);
                        var task = CaptureScanning.BoundedAsync(real, TimeSpan.FromMilliseconds(1000));
                        Check($"{exact} timeout boundary actually reached", held.Wait(4000));
                        var before = Selected();
                        var scan = task.GetAwaiter().GetResult();
                        Check($"{exact} timeout returns honest empty result", scan.Items.Count == 0 && scan.Notes.Any(n => n.Contains("too long")));
                        release.Set(); Check($"{exact} timed-out native scan settles", settled.Wait(5000));
                        Check($"{exact} no native select or restore after cancelled boundary", Selected() == before);
                    }
                    else
                    {
                        var cancelled = false; CaptureScan? scan = null;
                        try { scan = capturer.Scan(token.Token); } catch (OperationCanceledException) { cancelled = true; }
                        if (scenario == "cancel-before-select")
                        { Check($"{exact} explicit cancellation reached native boundary", token.IsCancellationRequested && cancelled); Check($"{exact} cancelled select leaves original tab", Selected() == 1); }
                        else if (scenario == "cancel-before-restore")
                        { Check($"{exact} restore boundary cancelled", token.IsCancellationRequested && fired > 0); Check($"{exact} cancelled restoration did not write original tab", Selected() == 5); }
                        else
                        {
                            if (scenario != "same-url-and-blank") Check($"{exact} {scenario} actually injected", fired > 0);
                            Check($"{exact} {scenario} visits every snapshot tab", visits.SetEquals(new[] { 0, 1, 2, 3, 4, 5 }));
                            Check($"{exact} {scenario} restores original tab", Selected() == 1);
                            if (scenario == "lose-selection-after-final-read") Check($"{exact} final-read selection loss rejects candidate and revisits target", visitCounts.GetValueOrDefault(1) >= 2);
                            if (scenario == "persistent-interference") Check($"{exact} unstable target explicitly partial", scan != null && scan.Notes.Any(n => n.Contains("incomplete")));
                            else
                            {
                                Check($"{exact} {scenario} complete real host capture", scan != null && Complete(scan));
                                Check($"{exact} {scenario} same URL and blank do not cause false partial", scan != null && !scan.Notes.Any(n => n.Contains("incomplete")));
                                Check($"{exact} {scenario} same URL deduped and blank excluded", scan != null && scan.Items.Count == 3);
                            }
                        }
                    }
                    BrowserTraversalProbe.Observer = null; token.Dispose();
                }
            }
#else
            throw new Exception("native policy test symbol missing");
#endif
            return 0;
        }
        catch (Exception e) { Results.Add(new { name = "native fixture exception", ok = false, type = e.GetType().Name, detail = e.Message }); return 1; }
        finally
        {
#if STARTAPP_CAPTURE_TEST
            BrowserTraversalProbe.Observer = null;
#endif
            File.WriteAllText(Path.Combine(output, "native-policy-assertions.json"), JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    private sealed class SettledCapturer(BrowserAddressBarCapturer real, ManualResetEventSlim settled) : ICooperativeCapturer
    {
        public CaptureScan Scan() => Scan(CancellationToken.None);
        public CaptureScan Scan(CancellationToken token) { try { return real.Scan(token); } finally { settled.Set(); } }
    }
}
