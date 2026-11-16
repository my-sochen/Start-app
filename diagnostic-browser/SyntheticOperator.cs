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

// Observation only. No UI effects, no permission patterns, no socket/protocol code.
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
    internal static object Observe(SafeFileHandle job, string output)
    {
        Directory.CreateDirectory(output);
        // Readiness only: one capture after at least three job-owned UIA roots become visible.
        // Edge may use a different root layout: timeout is an observation failure, no fallback.
        var clock = Stopwatch.StartNew();
        AutomationElement[] roots;
        do {
            roots = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition)
                .Cast<AutomationElement>().Where(e => Member(e.Current.ProcessId, job)).ToArray();
            if (roots.Length >= 3) break;
            Thread.Sleep(100);
        } while (clock.ElapsedMilliseconds < 10000);
        // Single observation cadence after root readiness, not a page-load success claim.
        Thread.Sleep(2000);
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
            setup = "COMMAND_LINE_NEW_WINDOW_INTERNAL_URI_ONLY", On = "NOT_ATTEMPTED", Connect = "NOT_ATTEMPTED",
            permission = "NONE_FROM_OBSERVATION", metadata = "NOT_TESTED", continuity = "UNPROVEN"
        }, new JsonSerializerOptions { WriteIndented = true }));
        // No effect depends on this data. Exact readback and complete tree are evidence gates only.
        if (roots.Length < 3 || !complete || values.Count(v => v == "EXACT_EDGE_INSPECT_URI") != 1)
            throw new InvalidDataException("Setup observation incomplete or exact inspect readback unavailable; stop before On");
        return new { mode = "EDGE_SETUP_OBSERVATION_ONLY", exactInspectReadback = true, completeControlWalk = true,
            consent = "NOT_ATTEMPTED", socket = "NOT_OPENED", metadata = "NOT_TESTED", Off = "NOT_APPLICABLE_NO_TOGGLE_ACTION",
            preOnPredicate = "NOT_ESTABLISHED_REQUIRES_REVIEW_OF_ACTUAL_EDGE_EVIDENCE", continuity = "UNPROVEN" };
    }
}
