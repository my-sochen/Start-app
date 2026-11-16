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

// Setup navigation plus observation only. No permission patterns or socket/protocol code.
internal static class SyntheticOperator
{
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    private static bool Member(int pid, SafeFileHandle job)
    {
        using var p = Native.OpenProcess(0x1000, false, (uint)pid);
        return !p.IsInvalid && Native.IsProcessInJob(p, job, out bool member) && member;
    }
    private static void Screenshot(string path)
    {
        var r = System.Windows.Forms.SystemInformation.VirtualScreen;
        using var b = new System.Drawing.Bitmap(r.Width, r.Height);
        using (var g = System.Drawing.Graphics.FromImage(b)) g.CopyFromScreen(r.Left, r.Top, 0, 0, b.Size);
        b.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
    private const string InspectUri = "edge://inspect/#remote-debugging";
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] input, int size);
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeyboardUnion Data; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct KeyboardUnion {
        [FieldOffset(0)] public ushort Key;
        [FieldOffset(4)] public uint Flags;
    }
    private sealed record Sample(AutomationElement[] Nodes, int[] Parents);
    private static Sample ReadComplete(SafeFileHandle job)
    {
        var roots = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition)
            .Cast<AutomationElement>().Where(e => Member(e.Current.ProcessId, job)).ToArray();
        if (roots.Length > 16) throw new InvalidDataException("Root cap");
        var nodes = new List<AutomationElement>(); var parents = new List<int>();
        var queue = new Queue<(AutomationElement Node, int Parent, int Depth)>();
        foreach (var root in roots) queue.Enqueue((root, -1, 0));
        while (queue.Count > 0) {
            if (nodes.Count >= 512) throw new InvalidDataException("Node cap");
            var (node, parent, depth) = queue.Dequeue();
            if (!Member(node.Current.ProcessId, job)) throw new InvalidDataException("Nonjob boundary");
            int index = nodes.Count; nodes.Add(node); parents.Add(parent);
            var child = TreeWalker.ControlViewWalker.GetFirstChild(node);
            if (child != null && depth >= 12) throw new InvalidDataException("Depth cap");
            int siblings = 0;
            while (child != null) {
                if (++siblings > 64 || nodes.Count + queue.Count >= 512) throw new InvalidDataException("Fanout cap");
                queue.Enqueue((child, index, depth + 1)); child = TreeWalker.ControlViewWalker.GetNextSibling(child);
            }
        }
        return new Sample(nodes.ToArray(), parents.ToArray());
    }
    private static (AutomationElement Root, AutomationElement Box)? ExactBox(SafeFileHandle job, string value)
    {
        var sample = ReadComplete(job); var found = new List<int>();
        for (int i = 0; i < sample.Nodes.Length; i++) {
            var e = sample.Nodes[i]; var c = e.Current;
            if (c.ControlType == ControlType.Edit && c.ClassName == "OmniboxViewViews" &&
                e.TryGetCurrentPattern(ValuePattern.Pattern, out object? p) && p is ValuePattern v && v.Current.Value == value) found.Add(i);
        }
        if (found.Count > 1) throw new InvalidDataException("Ambiguous exact resource");
        if (found.Count == 0) return null;
        int root = found[0]; while (sample.Parents[root] != -1) root = sample.Parents[root];
        var r = sample.Nodes[root]; var box = sample.Nodes[found[0]];
        nint hwnd = (nint)r.Current.NativeWindowHandle;
        if (r.Current.ControlType != ControlType.Window || r.Current.ClassName != "Chrome_WidgetWin_1" || hwnd == 0 ||
            !r.Current.IsEnabled || r.Current.IsOffscreen || !box.Current.IsEnabled || box.Current.IsOffscreen ||
            GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid != (uint)r.Current.ProcessId ||
            box.Current.ProcessId != r.Current.ProcessId || !Member((int)pid, job)) throw new InvalidDataException("Exact window native/job guard");
        return (r, box);
    }
    private static (AutomationElement Root, AutomationElement Box) WaitExact(SafeFileHandle job, string value)
    {
        var clock = Stopwatch.StartNew();
        do {
            var match = ExactBox(job, value); if (match.HasValue) return match.Value;
            // Only absence is a readiness condition. Ambiguity/caps/provider/identity errors refuse immediately.
            Thread.Sleep(100);
        } while (clock.ElapsedMilliseconds < 10000);
        throw new TimeoutException("Exact resource readiness unavailable");
    }
    internal static object SetupAndObserve(string setupUrl, SafeFileHandle job, string output)
    {
        Directory.CreateDirectory(output); int sequence = 0; string last = "BEGIN"; bool observationStarted = false;
        void Stage(string stage) { last = stage; File.WriteAllText(Path.Combine(output, "setup-stage-" + (++sequence).ToString("D2") + ".json"), JsonSerializer.Serialize(new { stage, ticks = Stopwatch.GetTimestamp(), continuity = "UNPROVEN" })); }
        try {
            string expectedSetup = new Uri(setupUrl).LocalPath.Replace('\\', '/');
            Stage("WAIT_EXACT_DEDICATED_SETUP");
            var selected = WaitExact(job, expectedSetup);
            nint hwnd = (nint)selected.Root.Current.NativeWindowHandle;
            File.WriteAllText(Path.Combine(output, "setup-selected.json"), JsonSerializer.Serialize(new { expectedSetup, hwnd = (long)hwnd, pid = selected.Root.Current.ProcessId,
                rootRuntimeId = selected.Root.GetRuntimeId(), boxRuntimeId = selected.Box.GetRuntimeId(), setupRepresentation = "EXPECTED_FORWARD_SLASH_LOCAL_PATH", continuity = "UNPROVEN" }));
            Screenshot(Path.Combine(output, "edge-before-navigation.png"));
            var fresh = ExactBox(job, expectedSetup) ?? throw new InvalidDataException("Setup disappeared");
            if (!Automation.Compare(fresh.Root, selected.Root) || !Automation.Compare(fresh.Box, selected.Box)) throw new InvalidDataException("Setup sample changed");
            Stage("ONE_ACTIVATION_REQUESTED");
            if (!SetForegroundWindow(hwnd)) throw new InvalidOperationException("Activation refused");
            var clock = Stopwatch.StartNew(); bool foreground = false;
            do {
                fresh = ExactBox(job, expectedSetup) ?? throw new InvalidDataException("Setup disappeared during foreground wait");
                if ((nint)fresh.Root.Current.NativeWindowHandle != hwnd || !Automation.Compare(fresh.Root, selected.Root) || !Automation.Compare(fresh.Box, selected.Box)) throw new InvalidDataException("Setup changed during activation");
                if (GetForegroundWindow() == hwnd) { foreground = true; break; }
                Thread.Sleep(50);
            } while (clock.ElapsedMilliseconds < 2000);
            if (!foreground) throw new TimeoutException("Setup foreground unconfirmed");
            Stage("ONE_FOCUS_REQUESTED"); selected.Box.SetFocus();
            if (!Automation.Compare(AutomationElement.FocusedElement, selected.Box) || GetForegroundWindow() != hwnd) throw new InvalidDataException("Focus refused");
            if (selected.Box.GetCurrentPattern(ValuePattern.Pattern) is not ValuePattern value || value.Current.IsReadOnly || value.Current.Value != expectedSetup ||
                GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid != (uint)selected.Root.Current.ProcessId || !Member((int)pid, job)) throw new InvalidDataException("Before SetValue guard");
            Stage("ONE_SETVALUE_REQUESTED"); value.SetValue(InspectUri);
            if (value.Current.Value != InspectUri || !Automation.Compare(AutomationElement.FocusedElement, selected.Box) || GetForegroundWindow() != hwnd ||
                !Member(selected.Box.Current.ProcessId, job) || GetWindowThreadProcessId(hwnd, out pid) == 0 || pid != (uint)selected.Root.Current.ProcessId || !Member((int)pid, job)) throw new InvalidDataException("Before Enter guard");
            if (IntPtr.Size != 8 || Marshal.SizeOf<Input>() != 40 || Marshal.OffsetOf<Input>(nameof(Input.Data)).ToInt32() != 8 ||
                Marshal.OffsetOf<KeyboardUnion>(nameof(KeyboardUnion.Flags)).ToInt32() != 4) throw new InvalidDataException("INPUT ABI refused");
            var keys = new[] { new Input { Type = 1, Data = new KeyboardUnion { Key = 13 } }, new Input { Type = 1, Data = new KeyboardUnion { Key = 13, Flags = 2 } } };
            Stage("ONE_ENTER_PAIR_REQUESTED"); uint sent = SendInput(2, keys, Marshal.SizeOf<Input>());
            Stage(sent == 2 ? "ENTER_TWO" : "ENTER_PARTIAL_REFUSE"); if (sent != 2) throw new InvalidOperationException("No partial input retry");
            Stage("WAIT_EXACT_INSPECT_RESOURCE"); var inspect = WaitExact(job, InspectUri);
            if ((nint)inspect.Root.Current.NativeWindowHandle != hwnd || !Automation.Compare(inspect.Root, selected.Root)) throw new InvalidDataException("Inspect not in dedicated Setup root");
            Stage("EXACT_DEDICATED_INSPECT_READBACK");
            observationStarted = true; return Observe(job, output);
        } catch (Exception ex) {
            File.WriteAllText(Path.Combine(output, "setup-failure.json"), JsonSerializer.Serialize(new { last, errorType = ex.GetType().FullName, On = "NOT_ATTEMPTED", Connect = "NOT_ATTEMPTED", retry = false }));
            if (!observationStarted) try { Observe(job, output); } catch { /* One best-effort failure observation, no repeated capture or effect. */ }
            throw;
        }
    }
    internal static object Observe(SafeFileHandle job, string output)
    {
        Directory.CreateDirectory(output);
        var roots = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition)
            .Cast<AutomationElement>().Where(e => Member(e.Current.ProcessId, job)).ToArray();
        Screenshot(Path.Combine(output, "edge-inspect-observed.png"));
        var rows = new List<object>();
        var errors = new List<string>();
        var values = new List<string>();
        bool capped = false;
        var queue = new Queue<(AutomationElement Node, int Parent, int Depth)>();
        if (roots.Length > 16) capped = true;
        else foreach (var root in roots) queue.Enqueue((root, -1, 0));
        try {
            while (queue.Count > 0 && !capped) {
                if (rows.Count >= 512) { capped = true; break; }
                var (node, parent, depth) = queue.Dequeue();
                var c = node.Current;
                if (!Member(c.ProcessId, job)) throw new InvalidDataException("Nonjob UIA boundary");
                int index = rows.Count;
                string? value = null;
                if (node.TryGetCurrentPattern(ValuePattern.Pattern, out object? p) && p is ValuePattern vp) {
                    value = vp.Current.Value;
                    if (value == "edge://inspect/#remote-debugging") values.Add("EXACT_EDGE_INSPECT_URI");
                    else if (value == "edge://inspect#remote-debugging" || value == "inspect/#remote-debugging" || value == "inspect#remote-debugging") values.Add("OTHER_INSPECT_REPRESENTATION_NOT_EXACT");
                }
                uint nativePid = 0;
                if (c.NativeWindowHandle != 0) GetWindowThreadProcessId((nint)c.NativeWindowHandle, out nativePid);
                rows.Add(new { index, parent, depth, c.Name, controlType = c.ControlType.ProgrammaticName,
                    c.ClassName, c.AutomationId, c.ProcessId, c.NativeWindowHandle, c.IsEnabled, c.IsOffscreen,
                    nativePid, nativeOwner = c.NativeWindowHandle == 0 ? 0L : (long)GetWindow((nint)c.NativeWindowHandle, 4),
                    runtimeId = node.GetRuntimeId(), value, continuity = "UNPROVEN" });
                var child = TreeWalker.ControlViewWalker.GetFirstChild(node);
                if (child != null && depth >= 12) { capped = true; break; }
                int sibling = 0;
                while (child != null) {
                    if (++sibling > 64 || rows.Count + queue.Count >= 512) { capped = true; break; }
                    queue.Enqueue((child, index, depth + 1));
                    child = TreeWalker.ControlViewWalker.GetNextSibling(child);
                }
            }
        } catch (Exception ex) { errors.Add(ex.GetType().FullName ?? ex.GetType().Name); }
        bool complete = !capped && errors.Count == 0 && queue.Count == 0;
        File.WriteAllText(Path.Combine(output, "edge-control-tree.json"), JsonSerializer.Serialize(new {
            roots = roots.Length, nodeCount = rows.Count, complete, capped, errors, rows, inspectValueClasses = values,
            setup = "EXPLICIT_DEDICATED_SETUP_NAVIGATION", On = "NOT_ATTEMPTED", Connect = "NOT_ATTEMPTED",
            permission = "NONE_FROM_OBSERVATION", metadata = "NOT_TESTED", continuity = "UNPROVEN"
        }, new JsonSerializerOptions { WriteIndented = true }));
        // No effect depends on this data. Exact readback and complete tree are evidence gates only.
        if (roots.Length < 3 || !complete || values.Count(v => v == "EXACT_EDGE_INSPECT_URI") != 1)
            throw new InvalidDataException("Setup observation incomplete or exact inspect readback unavailable; stop before On");
        return new { mode = "EDGE_EXPLICIT_SETUP_OBSERVATION_ONLY", exactInspectReadback = true, completeControlWalk = true,
            consent = "NOT_ATTEMPTED", socket = "NOT_OPENED", metadata = "NOT_TESTED", Off = "NOT_APPLICABLE_NO_TOGGLE_ACTION",
            preOnPredicate = "NOT_ESTABLISHED_REQUIRES_REVIEW_OF_ACTUAL_EDGE_EVIDENCE", continuity = "UNPROVEN" };
    }
}
