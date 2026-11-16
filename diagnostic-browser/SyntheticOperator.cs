using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Windows.Automation;
using Microsoft.Win32.SafeHandles;

// NEW local candidate, not the terminal C1 script and not its replacement guard.
// Authorization must come from reviewed isolated synthetic-test scope, NOT endpoint observations.
internal static class SyntheticOperator
{
    private static bool Member(int pid, SafeFileHandle job)
    {
        using var p = Native.OpenProcess(0x1000, false, (uint)pid);
        return !p.IsInvalid && Native.IsProcessInJob(p, job, out bool member) && member;
    }
    private static AutomationElement[] Windows(SafeFileHandle job) =>
        AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition).Cast<AutomationElement>()
            .Where(e => Member(e.Current.ProcessId, job)).ToArray();
    private static AutomationElement[] Descendants(AutomationElement e) => e.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>().ToArray();
    private static void Screenshot(string path)
    {
        var r = System.Windows.Forms.SystemInformation.VirtualScreen;
        using var b = new System.Drawing.Bitmap(r.Width, r.Height);
        using (var g = System.Drawing.Graphics.FromImage(b)) g.CopyFromScreen(r.Left, r.Top, 0, 0, b.Size);
        b.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
    private static void StartupObservation(SafeFileHandle job, string[] fixtures, string output, string stage)
    {
        // Best effort only, never replaces the original failure or permits a consent action.
        var errors = new List<string>();
        try { Screenshot(Path.Combine(output, stage + ".png")); }
        catch (Exception ex) { errors.Add("screenshot:" + ex.GetType().Name); }
        var rows = new List<object>();
        try {
            var windows = Windows(job);
            if (windows.Length > 64) throw new InvalidDataException("Observation window cap");
            foreach (var w in windows) {
                var values = new List<string>();
                try {
                    foreach (var e in Descendants(w).Where(e => e.Current.ClassName == "OmniboxViewViews").Take(4)) {
                        if (e.TryGetCurrentPattern(ValuePattern.Pattern, out object? p) && p is ValuePattern v)
                            values.Add(Classify(v.Current.Value, fixtures));
                        else values.Add("NO_VALUE_PATTERN");
                    }
                    rows.Add(new { hwnd = w.Current.NativeWindowHandle, pid = w.Current.ProcessId,
                        offscreen = w.Current.IsOffscreen, enabled = w.Current.IsEnabled,
                        omniboxStates = values, continuity = "UNPROVEN" });
                } catch (Exception ex) { errors.Add("window-read:" + ex.GetType().Name); }
            }
        } catch (Exception ex) { errors.Add("window-enumeration:" + ex.GetType().Name); }
        try {
            File.WriteAllText(Path.Combine(output, stage + ".json"), JsonSerializer.Serialize(new {
                stage, elapsedTicks = Stopwatch.GetTimestamp(), rows, errors,
                grant = "NONE_FROM_OBSERVATION", diagnosis = "UNRESOLVED", retry = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        } catch { /* Evidence write failure is not a successful observation. Original gate unchanged. */ }
    }
    // Classification is diagnostic output only, never a recipient/action predicate.
    private static string Classify(string actual, string[] fixtures)
    {
        var labels = new List<string>();
        for (int i = 0; i < fixtures.Length; i++) {
            string name = ((char)('A' + i)).ToString();
            string path = new Uri(fixtures[i]).LocalPath;
            var known = new[] {
                ("ABSOLUTE_URI", fixtures[i]),
                ("LOCAL_PATH", path),
                ("LOCAL_PATH_FORWARD_SLASH", path.Replace('\\', '/')),
                ("URI_WITHOUT_FILE_SCHEME", fixtures[i].Substring("file://".Length))
            };
            foreach (var variant in known)
                if (StringComparer.Ordinal.Equals(actual, variant.Item2)) labels.Add(name + "_" + variant.Item1);
        }
        return labels.Count == 0 ? "OTHER_VALUE_REDACTED" : string.Join("|", labels);
    }
    internal static object RunFixtureOnly(string[] fixtures, SafeFileHandle job, string output)
    {
        Directory.CreateDirectory(output);
        var clock = Stopwatch.StartNew();
        try {
            StartupObservation(job, fixtures, output, "00-fixture-immediate");
            // Observations are non-atomic. These are minimum elapsed times, not exact deadlines.
            foreach (int target in new[] { 2000, 10000 }) {
                int delay = (int)Math.Max(0, target - clock.ElapsedMilliseconds);
                if (delay > 0) Thread.Sleep(delay);
                StartupObservation(job, fixtures, output, target == 2000 ? "01-fixture-after-2s" : "02-fixture-after-10s");
            }
            return new { mode = "FIXTURE_OBSERVATION_ONLY", consent = "NOT_ATTEMPTED", socket = "NOT_OPENED", metadata = "NOT_TESTED",
                diagnosis = "REQUIRES_PIXELS_AND_RECORDS", identityAuthorization = "NONE", capture = "NOT_TESTED" };
        } catch {
            StartupObservation(job, fixtures, output, "03-fixture-exception");
            throw;
        }
    }
}
