using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Automation;

// Diagnostic only. No product reference and no capture/duplicate decision.
internal static class Program
{
    static readonly List<object> Rows = new();
    static AutomationElement Window = null!;
    static AutomationElement[] Tabs = null!;
    static string Fixture = "";
    static string Classify(string raw)
    {
        if (raw == "about:blank") return "blank";
        if (Uri.TryCreate(raw, UriKind.Absolute, out var u) && u.IsFile)
        {
            var path = Path.GetFullPath(u.LocalPath);
            foreach (var n in new[] { "same.html", "other.html", "fixture.pdf" })
                if (string.Equals(path, Path.Combine(Fixture, n), StringComparison.OrdinalIgnoreCase)) return n;
        }
        return string.IsNullOrEmpty(raw) ? "empty" : "unrecognized-redacted";
    }
    static string Id(AutomationElement e) => string.Join(".", e.GetRuntimeId());
    static object ReadDocument(AutomationElement e)
    {
        try
        {
            var nested = false; var ancestryKnown = false; var p = e;
            for (var i = 0; i < 32; i++)
            {
                p = TreeWalker.ControlViewWalker.GetParent(p);
                if (p == null) break;
                if (Automation.Compare(p, Window)) { ancestryKnown = true; break; }
                if (p.Current.ControlType == ControlType.Document) nested = true;
            }
            var c = e.Current; var r = c.BoundingRectangle;
            var supported = e.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern);
            var url = supported ? Classify(((ValuePattern)pattern).Current.Value) : "unsupported";
            return new { id = Id(e), offscreen = c.IsOffscreen, positiveRectangle = r.Width > 0 && r.Height > 0, nested, ancestryKnown, valueSupported = supported, url };
        }
        catch (Exception ex) { return new { error = ex.GetType().Name }; }
    }
    static void Sample(string phase, int target, long elapsed)
    {
        var observation = Stopwatch.StartNew();
        var selected = Tabs.Select((t, i) => new { i, selected = ((SelectionItemPattern)t.GetCurrentPattern(SelectionItemPattern.Pattern)).Current.IsSelected }).Where(t => t.selected).Select(t => t.i).ToArray();
        var edits = Window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)).Cast<AutomationElement>().Where(e => e.Current.ClassName.EndsWith("OmniboxViewViews", StringComparison.Ordinal)).ToArray();
        var omnibox = edits.Length == 1 && edits[0].TryGetCurrentPattern(ValuePattern.Pattern, out var v) ? Classify(((ValuePattern)v).Current.Value) : "missing-or-ambiguous";
        var docs = Window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
        if (docs.Count > 64) throw new Exception("document bound exceeded");
        var documents = docs.Cast<AutomationElement>().Select(ReadDocument).ToArray();
        // Selection is sampled again AFTER all document and omnibox reads. No ownership verdict inferred.
        var selectedAfter = Tabs.Select((t, i) => new { i, selected = ((SelectionItemPattern)t.GetCurrentPattern(SelectionItemPattern.Pattern)).Current.IsSelected }).Where(t => t.selected).Select(t => t.i).ToArray();
        Rows.Add(new { phase, target, elapsed, observationDurationMs = observation.ElapsedMilliseconds, selected, selectedAfter, omnibox, documents });
    }
    static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" || args.Length != 3) return 2;
        var output = Path.GetFullPath(args[2]); Directory.CreateDirectory(output);
        try
        {
            Fixture = Path.GetFullPath(args[1]);
            Window = AutomationElement.FromHandle(new IntPtr(long.Parse(args[0])));
            Tabs = Window.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem)).Cast<AutomationElement>().ToArray();
            if (Tabs.Length != 6) throw new Exception("expected six synthetic tabs");
            // One fixed pass, no retry until capability appears. 0/1 same file,2/3 blank,4 other,5 PDF.
            foreach (var target in new[] { 0, 1, 2, 3, 4, 0, 5, 0 })
            {
                Sample("before-select", target, 0);
                ((SelectionItemPattern)Tabs[target].GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                var timer = Stopwatch.StartNew();
                foreach (var at in new[] { 0, 25, 100, 250, 600 })
                {
                    var remaining = at - (int)timer.ElapsedMilliseconds;
                    if (remaining > 0) Thread.Sleep(remaining);
                    Sample("after-select", target, timer.ElapsedMilliseconds);
                }
            }
            Rows.Add(new { outcome = "fixed sampling completed; NOT evidence of supported capability or product correctness" });
            return 0;
        }
        catch (Exception ex) { Rows.Add(new { outcome = "probe failed", type = ex.GetType().Name }); return 1; }
        finally { File.WriteAllText(Path.Combine(output, "uia-capability-samples.json"), JsonSerializer.Serialize(Rows, new JsonSerializerOptions { WriteIndented = true })); }
    }
}
