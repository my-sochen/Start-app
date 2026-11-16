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

// One owned Setup activation diagnostic, typed decision facts only, no raw UIA text/pixels. Stops before focus.
internal static class SyntheticOperator
{
    private sealed class Refusal : Exception { internal string Code { get; } internal Refusal(string code) { Code = code; } }
    private static string FailureCode(Exception ex) => ex is Refusal refusal ? refusal.Code : ex switch {
        ElementNotAvailableException => "UIA_ELEMENT_UNAVAILABLE", InvalidOperationException => "PROVIDER_INVALID_OPERATION",
        System.ComponentModel.Win32Exception => "NATIVE_CALL_FAILED", UnauthorizedAccessException => "ACCESS_REFUSED",
        InvalidDataException => "INVALID_DATA_EXCEPTION",
        IOException => "IO_FAILED", System.Runtime.InteropServices.COMException => "PROVIDER_COM_FAILED",
        _ => "OTHER_EXCEPTION_NO_TEXT_RECORDED"
    };
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    private static bool Member(int pid, SafeFileHandle job)
    {
        using var p = Native.OpenProcess(0x1000, false, (uint)pid);
        return !p.IsInvalid && Native.IsProcessInJob(p, job, out bool member) && member;
    }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    private sealed record Sample(AutomationElement[] Nodes, int[] Parents);
    private static Sample ReadComplete(SafeFileHandle job)
    {
        var roots = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition)
            .Cast<AutomationElement>().Where(e => Member(e.Current.ProcessId, job)).ToArray();
        if (roots.Length > 16) throw new Refusal("ROOT_CAP");
        var nodes = new List<AutomationElement>(); var parents = new List<int>();
        var queue = new Queue<(AutomationElement Node, int Parent, int Depth)>();
        foreach (var root in roots) queue.Enqueue((root, -1, 0));
        while (queue.Count > 0) {
            if (nodes.Count >= 512) throw new Refusal("NODE_CAP");
            var (node, parent, depth) = queue.Dequeue();
            if (!Member(node.Current.ProcessId, job)) throw new Refusal("NONJOB_BOUNDARY");
            int index = nodes.Count; nodes.Add(node); parents.Add(parent);
            var child = TreeWalker.ControlViewWalker.GetFirstChild(node);
            if (child != null && depth >= 12) throw new Refusal("DEPTH_CAP");
            int siblings = 0;
            while (child != null) {
                if (++siblings > 64 || nodes.Count + queue.Count >= 512) throw new Refusal("FANOUT_CAP");
                queue.Enqueue((child, index, depth + 1)); child = TreeWalker.ControlViewWalker.GetNextSibling(child);
            }
        }
        return new Sample(nodes.ToArray(), parents.ToArray());
    }
    private static (AutomationElement Root, AutomationElement Box)? ExactBox(SafeFileHandle job, string value, Action<string, object> record)
    {
        record("COMPLETE_WALK_BEGIN", new { });
        Sample sample;
        try { sample = ReadComplete(job); } catch (Exception ex) { record.Invoke("COMPLETE_WALK_FAILED", new { failure = FailureCode(ex) }); throw; }
        record("MATCH_SCAN_BEGIN", new { nodes = sample.Nodes.Length });
        var found = new List<int>();
        for (int i = 0; i < sample.Nodes.Length; i++) {
            var e = sample.Nodes[i]; var c = e.Current;
            if (c.ControlType == ControlType.Edit && c.ClassName == "OmniboxViewViews" &&
                e.TryGetCurrentPattern(ValuePattern.Pattern, out object? p) && p is ValuePattern v && v.Current.Value == value) found.Add(i);
        }
        record.Invoke("MATCH_COUNT", new { count = found.Count, nodes = sample.Nodes.Length });
        if (found.Count > 1) throw new Refusal("AMBIGUOUS_EXACT_RESOURCE");
        if (found.Count == 0) return null;
        int root = found[0]; while (sample.Parents[root] != -1) root = sample.Parents[root];
        record("GUARD_SCALARS_BEGIN", new { });
        var r = sample.Nodes[root]; var box = sample.Nodes[found[0]];
        nint hwnd = (nint)r.Current.NativeWindowHandle;
        // Record the SAME sampled scalar values used by this guard, not a later explanatory snapshot.
        var rc = r.Current; var bc = box.Current;
        var rootType = rc.ControlType; string rootClass = rc.ClassName;
        bool rootEnabled = rc.IsEnabled, rootOffscreen = rc.IsOffscreen, boxEnabled = bc.IsEnabled, boxOffscreen = bc.IsOffscreen;
        int rootPid = rc.ProcessId, boxPid = bc.ProcessId;
        uint nativeThread = GetWindowThreadProcessId(hwnd, out uint pid);
        bool member = Member((int)pid, job);
        record.Invoke("EXACT_RESOURCE_GUARD", new { hwnd = (long)hwnd, rootTypeExpected = rootType == ControlType.Window, rootClassExpected = rootClass == "Chrome_WidgetWin_1", rootEnabled, rootOffscreen, rootPid, boxEnabled, boxOffscreen, boxPid, nativeThread, nativePid = pid, member,
            continuity = "UNPROVEN" });
        if (rootType != ControlType.Window || rootClass != "Chrome_WidgetWin_1" || hwnd == 0 ||
            !rootEnabled || rootOffscreen || !boxEnabled || boxOffscreen || nativeThread == 0 || pid != (uint)rootPid ||
            boxPid != rootPid || !member) throw new Refusal("EXACT_RESOURCE_GUARD_REFUSED");
        return (r, box);
    }
    private static (AutomationElement Root, AutomationElement Box) WaitExact(SafeFileHandle job, string value, Action<string, object> record)
    {
        var clock = Stopwatch.StartNew();
        do {
            var match = ExactBox(job, value, record); if (match.HasValue) return match.Value;
            // Only absence is a readiness condition. Ambiguity/caps/provider/identity errors refuse immediately.
            Thread.Sleep(100);
        } while (clock.ElapsedMilliseconds < 10000);
        throw new Refusal("EXACT_RESOURCE_READINESS_UNAVAILABLE");
    }
    internal static object SetupAndObserve(string setupUrl, SafeFileHandle job, string output)
    {
        Directory.CreateDirectory(output); int sequence = 0; string last = "BEGIN"; int sampleSequence = 0;
        void Record(string code, object detail) { if (sampleSequence >= 512) throw new Refusal("RECORD_CAP"); File.WriteAllText(Path.Combine(output, "activation-sample-" + (++sampleSequence).ToString("D3") + ".json"),
            JsonSerializer.Serialize(new { phase = last, code, detail, ticks = Stopwatch.GetTimestamp(), continuity = "UNPROVEN" })); }
        void Stage(string stage) { last = stage; File.WriteAllText(Path.Combine(output, "setup-stage-" + (++sequence).ToString("D2") + ".json"), JsonSerializer.Serialize(new { stage, ticks = Stopwatch.GetTimestamp(), continuity = "UNPROVEN" })); }
        try {
            string expectedSetup = new Uri(setupUrl).LocalPath.Replace('\\', '/');
            Stage("WAIT_EXACT_DEDICATED_SETUP");
            var selected = WaitExact(job, expectedSetup, Record);
            nint hwnd = (nint)selected.Root.Current.NativeWindowHandle;
            Record("SELECTED_SETUP", new { hwnd = (long)hwnd, pid = selected.Root.Current.ProcessId, exactExpectedFixture = true, continuity = "UNPROVEN" });

            Stage("PRE_ACTIVATION_REVALIDATION");
            var fresh = ExactBox(job, expectedSetup, Record) ?? throw new Refusal("SETUP_DISAPPEARED");
            bool preRootEqual = Automation.Compare(fresh.Root, selected.Root), preBoxEqual = Automation.Compare(fresh.Box, selected.Box);
            Record("PRE_ACTIVATION_COMPARE", new { preRootEqual, preBoxEqual });
            if (!preRootEqual || !preBoxEqual) throw new Refusal("SETUP_SAMPLE_CHANGED_BEFORE_ACTIVATION");
            Stage("ONE_ACTIVATION_REQUESTED");
            bool activationResult = SetForegroundWindow(hwnd);
            Record("ACTIVATION_RETURN", new { activationResult, hwnd = (long)hwnd });
            if (!activationResult) throw new Refusal("ACTIVATION_RETURNED_ZERO");
            var clock = Stopwatch.StartNew(); bool foreground = false;
            do {
                Record("POST_ACTIVATION_SAMPLE_BEGIN", new { });
                fresh = ExactBox(job, expectedSetup, Record) ?? throw new Refusal("SETUP_EXACT_MATCH_ABSENT_DURING_ACTIVATION");
                Record("BEFORE_COMPARE_AND_FOREGROUND", new { });
                long currentHwnd = fresh.Root.Current.NativeWindowHandle;
                bool rootEqual = Automation.Compare(fresh.Root, selected.Root); bool boxEqual = Automation.Compare(fresh.Box, selected.Box);
                nint actualForeground = GetForegroundWindow();
                Record("ACTIVATION_IDENTITY_AND_FOREGROUND", new { selectedHwnd = (long)hwnd, currentHwnd, rootEqual, boxEqual, actualForeground = (long)actualForeground });
                if ((nint)currentHwnd != hwnd || !rootEqual || !boxEqual) throw new Refusal("SETUP_CHANGED_DURING_ACTIVATION");
                if (actualForeground == hwnd) { foreground = true; break; }
                Thread.Sleep(50);
            } while (clock.ElapsedMilliseconds < 2000);
            if (!foreground) throw new Refusal("SETUP_FOREGROUND_UNCONFIRMED");
            Stage("ACTIVATION_DIAGNOSIS_COMPLETE_STOP_BEFORE_FOCUS");

            return new { mode = "ACTIVATION_DIAGNOSIS_ONLY_TYPED_RECORDS_NO_PIXELS", focus = "NOT_ATTEMPTED", navigation = "NOT_ATTEMPTED", On = "NOT_ATTEMPTED", Connect = "NOT_ATTEMPTED", metadata = "NOT_TESTED", continuity = "UNPROVEN" };
        } catch (Exception ex) {
            File.WriteAllText(Path.Combine(output, "setup-failure.json"), JsonSerializer.Serialize(new { last, failure = FailureCode(ex), On = "NOT_ATTEMPTED", Connect = "NOT_ATTEMPTED", retry = false }));

            throw;
        }
    }
}
