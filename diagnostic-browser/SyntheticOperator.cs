using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
    [DllImport("user32.dll")] private static extern int SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    internal static object RunForegroundOnly(string[] fixtures, SafeFileHandle job, string output)
    {
        Directory.CreateDirectory(output);
        try {
        StartupObservation(job, fixtures, output, "00-before-activation");
        string expectedC = new Uri(fixtures[2]).LocalPath.Replace('\\', '/');
        AutomationElement? window = null;
        var ready = Stopwatch.StartNew();
        do {
            var matches = Windows(job).Where(w => Descendants(w).Any(e => e.Current.ClassName == "OmniboxViewViews" &&
                e.TryGetCurrentPattern(ValuePattern.Pattern, out object? p) && p is ValuePattern v && v.Current.Value == expectedC)).ToArray();
            if (matches.Length == 1) { window = matches[0]; break; }
            Thread.Sleep(100);
        } while (ready.ElapsedMilliseconds < 10000);
        if (window is null) throw new TimeoutException("EXACT_C_NOT_READY");
        var boxes = Descendants(window).Where(e => e.Current.ClassName == "OmniboxViewViews" &&
            e.TryGetCurrentPattern(ValuePattern.Pattern, out object? p) && p is ValuePattern v && v.Current.Value == expectedC).ToArray();
        if (boxes.Length != 1 || !Member(window.Current.ProcessId, job) || !Member(boxes[0].Current.ProcessId, job) || window.Current.IsOffscreen || !window.Current.IsEnabled)
            throw new InvalidOperationException("EXACT_C_CURRENT_GUARD_STOP");
        nint target = (nint)window.Current.NativeWindowHandle;
        object Sample(nint foreground, long ticks) {
            string label;
            if (foreground == 0) label = "NULL";
            else if (foreground == target) label = "TARGET_C";
            else {
                uint thread = GetWindowThreadProcessId(foreground, out uint pid);
                label = thread == 0 || pid == 0 ? "UNRESOLVED_REDACTED" :
                    Member((int)pid, job) ? "OTHER_JOB" : "OUTSIDE_JOB_REDACTED";
            }
            return new { label, elapsedTicks = ticks, continuity = "UNPROVEN" };
        }
        File.WriteAllText(Path.Combine(output, "activation-requested.json"), "{\"stage\":\"ONE_ACTIVATION_REQUESTED\"}");
        long before = Stopwatch.GetTimestamp();
        int rawBool = SetForegroundWindow(target); // Exactly one call. No activation retry.
        nint immediate = GetForegroundWindow(); // Sample before any classification or disk I/O.
        long after = Stopwatch.GetTimestamp();
        File.WriteAllText(Path.Combine(output, "activation-return.json"), JsonSerializer.Serialize(new {
            rawBool, beforeTicks = before, afterTicks = after, immediate = Sample(immediate, after), diagnosis = "OBSERVATION_ONLY_NO_ACTION_GRANT"
        }));
        // Independent bounded observations only. Matching never unlocks another action.
        var clock = Stopwatch.StartNew();
        var samples = new List<object>();
        for (int i = 0; i < 21; i++) {
            if (i > 0) {
                int remaining = 100 * i - (int)clock.ElapsedMilliseconds;
                if (remaining > 0) Thread.Sleep(remaining);
                if (clock.ElapsedMilliseconds > 2100) break;
            }
            nint foreground = GetForegroundWindow();
            samples.Add(Sample(foreground, Stopwatch.GetTimestamp()));
        }
        File.WriteAllText(Path.Combine(output, "foreground-samples.json"), JsonSerializer.Serialize(new { samples, nominalWindowMilliseconds = 2000, actualElapsedMilliseconds = clock.ElapsedMilliseconds }));
        StartupObservation(job, fixtures, output, "01-after-activation-observation");
        return new { mode = "FOREGROUND_OBSERVATION_ONLY", activationCalls = 1, focus = "NOT_ATTEMPTED", edit = "NOT_ATTEMPTED", keys = "NOT_SENT",
            consent = "NOT_ATTEMPTED", socket = "NOT_OPENED", metadata = "NOT_TESTED", identityAuthorization = "NONE", continuity = "UNPROVEN" };
        } catch {
            try { StartupObservation(job, fixtures, output, "02-foreground-failure"); } catch { }
            throw;
        }
    }
}
