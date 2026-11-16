using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows.Automation;
using Microsoft.Win32.SafeHandles;

// Disposable Edge setup, broad opt-in and ONE connection observation. No Allow/Cancel or protocol messages.
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
    internal static object Observe(SafeFileHandle job, string output, string prefix = "edge")
    {
        Directory.CreateDirectory(output);
        var roots = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition)
            .Cast<AutomationElement>().Where(e => Member(e.Current.ProcessId, job)).ToArray();
        Screenshot(Path.Combine(output, prefix + "-inspect-observed.png"));
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
                    if (c.ControlType == ControlType.Edit && c.ClassName == "OmniboxViewViews" && value == InspectUri) values.Add("EXACT_EDGE_INSPECT_OMNIBOX");
                    else if (value == "edge://inspect#remote-debugging" || value == "inspect/#remote-debugging" || value == "inspect#remote-debugging") values.Add("OTHER_INSPECT_REPRESENTATION_NOT_EXACT");
                }
                uint nativePid = 0;
                if (c.NativeWindowHandle != 0) GetWindowThreadProcessId((nint)c.NativeWindowHandle, out nativePid);
                bool toggleAvailable = node.TryGetCurrentPattern(TogglePattern.Pattern, out object? toggleObject);
                string? toggleState = toggleAvailable && toggleObject is TogglePattern toggle ? toggle.Current.ToggleState.ToString() : null;
                rows.Add(new { index, parent, depth, c.Name, controlType = c.ControlType.ProgrammaticName,
                    c.ClassName, c.AutomationId, c.ProcessId, c.NativeWindowHandle, c.IsEnabled, c.IsOffscreen,
                    nativePid, nativeOwner = c.NativeWindowHandle == 0 ? 0L : (long)GetWindow((nint)c.NativeWindowHandle, 4),
                    runtimeId = node.GetRuntimeId(), value, toggleAvailable, toggleState, continuity = "UNPROVEN" });
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
        File.WriteAllText(Path.Combine(output, prefix + "-control-tree.json"), JsonSerializer.Serialize(new {
            roots = roots.Length, nodeCount = rows.Count, complete, capped, errors, rows, inspectValueClasses = values,
            setup = "EXPLICIT_DEDICATED_SETUP_NAVIGATION", On = prefix == "connection" ? "REFER_TO_CONNECTION_EVIDENCE" : "NOT_ATTEMPTED", Connect = prefix == "connection" ? "ONE_REQUESTED_REFER_TO_TASK_STATUS" : "NOT_ATTEMPTED",
            permission = "NONE_FROM_OBSERVATION", metadata = "NOT_TESTED", continuity = "UNPROVEN"
        }, new JsonSerializerOptions { WriteIndented = true }));
        // No effect depends on this data. Exact readback and complete tree are evidence gates only.
        if (roots.Length < 3 || !complete || values.Count(v => v == "EXACT_EDGE_INSPECT_OMNIBOX") != 1)
            throw new InvalidDataException("Setup observation incomplete or exact inspect readback unavailable; stop before On");
        return new { mode = "EDGE_EXPLICIT_SETUP_OBSERVATION_ONLY", exactInspectReadback = true, completeControlWalk = true,
            consent = "NO_ALLOW_ATTEMPTED", socket = prefix == "connection" ? "ONE_CONNECT_REQUESTED" : "NOT_OPENED", metadata = "NOT_TESTED", Off = prefix == "connection" ? "REFER_TO_CONNECTION_EVIDENCE" : "NOT_APPLICABLE_NO_TOGGLE_ACTION",
            preOnPredicate = "NOT_ESTABLISHED_REQUIRES_REVIEW_OF_ACTUAL_EDGE_EVIDENCE", continuity = "UNPROVEN" };
    }

    private const string Broad = "Turning on this setting allows external apps to request full control of this browser. This includes read access to your saved data, cookies and site data, and the ability to navigate to any URL.";
    private const string Trust = "Only web developers should turn on this feature, and only use it with trusted apps.";
    private sealed record Optin(AutomationElement Root, AutomationElement Box, AutomationElement Document, AutomationElement CheckBox,
        nint Hwnd, int Pid, int[] RootId, int[] BoxId, int[] DocumentId, int[] CheckBoxId, long[] RootHwnds, string ShapeDigest, ToggleState State);
    private static int[] Id(AutomationElement element) {
        var id = element.GetRuntimeId(); if (id is null || id.Length == 0) throw new InvalidDataException("Missing sampled runtime ID"); return id;
    }
    private static Optin StrictOptin(SafeFileHandle job, ToggleState expected)
    {
        var sample = ReadComplete(job); var nodes = sample.Nodes;
        int RootOf(int i) { while (sample.Parents[i] != -1) i = sample.Parents[i]; return i; }
        var boxes = Enumerable.Range(0, nodes.Length).Where(i => {
            var c = nodes[i].Current;
            return c.ControlType == ControlType.Edit && c.ClassName == "OmniboxViewViews" &&
                nodes[i].TryGetCurrentPattern(ValuePattern.Pattern, out object? p) && p is ValuePattern v && v.Current.Value == InspectUri;
        }).ToArray();
        if (boxes.Length != 1) throw new InvalidDataException("Unique exact inspect Omnibox required");
        int rootIndex = RootOf(boxes[0]); var root = nodes[rootIndex]; var box = nodes[boxes[0]];
        var rc = root.Current; nint hwnd = (nint)rc.NativeWindowHandle;
        if (rc.ControlType != ControlType.Window || rc.ClassName != "Chrome_WidgetWin_1" || hwnd == 0 || !rc.IsEnabled || rc.IsOffscreen ||
            !box.Current.IsEnabled || box.Current.IsOffscreen || GetWindowThreadProcessId(hwnd, out uint pid) == 0 ||
            pid != (uint)rc.ProcessId || box.Current.ProcessId != rc.ProcessId || !Member((int)pid, job)) throw new InvalidDataException("Native inspect owner refused");
        var docs = Enumerable.Range(0, nodes.Length).Where(i => RootOf(i) == rootIndex && nodes[i].Current.ControlType == ControlType.Document &&
            nodes[i].Current.AutomationId == "RootWebArea" && nodes[i].Current.Name == "Inspect with Edge Developer Tools" &&
            nodes[i].TryGetCurrentPattern(ValuePattern.Pattern, out object? p) && p is ValuePattern v && v.Current.Value == InspectUri).ToArray();
        if (docs.Length != 1) throw new InvalidDataException("Unique exact Edge inspect document required");
        int document = docs[0];
        int UniqueChild(ControlType type, string name, string? automationId = null) {
            var found = Enumerable.Range(0, nodes.Length).Where(i => sample.Parents[i] == document && nodes[i].Current.ControlType == type &&
                nodes[i].Current.Name == name && (automationId is null || nodes[i].Current.AutomationId == automationId)).ToArray();
            if (found.Length != 1) throw new InvalidDataException("Exact inspect direct child refused"); return found[0];
        }
        int checkbox = UniqueChild(ControlType.CheckBox, "Allow remote debugging for this browser instance", "remote-debugging-enabled");
        int broad = UniqueChild(ControlType.Text, Broad); int trust = UniqueChild(ControlType.Text, Trust);
        foreach (var i in new[] { boxes[0], document, checkbox, broad, trust }) {
            var c = nodes[i].Current;
            if (!c.IsEnabled || c.IsOffscreen || c.ProcessId != (int)pid || !Member(c.ProcessId, job)) throw new InvalidDataException("Inspect child availability/job refused");
        }
        if (!nodes[checkbox].TryGetCurrentPattern(TogglePattern.Pattern, out object? pattern) || pattern is not TogglePattern toggle || toggle.Current.ToggleState != expected)
            throw new InvalidDataException("Exact expected Toggle pattern/state required");
        var rootHwnds = Enumerable.Range(0, nodes.Length).Where(i => sample.Parents[i] == -1).Select(i => {
            var c = nodes[i].Current; nint h = (nint)c.NativeWindowHandle;
            if (c.ControlType != ControlType.Window || h == 0 || GetWindowThreadProcessId(h, out uint p) == 0 || p != (uint)c.ProcessId || !Member((int)p, job)) throw new InvalidDataException("Root native/job predicate");
            return (long)h;
        }).OrderBy(h => h).ToArray();
        if (rootHwnds.Length != 3) throw new InvalidDataException("Expected three owned roots only; not coverage proof");
        // Complete sampled tree structure before Connect, used only to refuse cleanup on a changed/remaining prompt.
        string shape = JsonSerializer.Serialize(Enumerable.Range(0,nodes.Length).Select(i => { var c = nodes[i].Current; return new {
            parent = sample.Parents[i], type = c.ControlType.ProgrammaticName, c.Name, c.ClassName, c.AutomationId, c.ProcessId, c.NativeWindowHandle }; }));
        string shapeDigest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(shape)));
        return new Optin(root, box, nodes[document], nodes[checkbox], hwnd, (int)pid, Id(root), Id(box), Id(nodes[document]), Id(nodes[checkbox]), rootHwnds, shapeDigest, toggle.Current.ToggleState);
    }
    private static bool Same(Optin a, Optin b) => a.Hwnd == b.Hwnd && a.Pid == b.Pid &&
        a.RootId.SequenceEqual(b.RootId) && a.BoxId.SequenceEqual(b.BoxId) && a.DocumentId.SequenceEqual(b.DocumentId) && a.CheckBoxId.SequenceEqual(b.CheckBoxId) && a.RootHwnds.SequenceEqual(b.RootHwnds) &&
        Automation.Compare(a.Root,b.Root) && Automation.Compare(a.Box,b.Box) && Automation.Compare(a.Document,b.Document) && Automation.Compare(a.CheckBox,b.CheckBox);
    private static void OptinProof(Optin x, string path) => File.WriteAllText(path, JsonSerializer.Serialize(new { hwnd = (long)x.Hwnd, x.Pid,
        x.RootId, x.BoxId, x.DocumentId, x.CheckBoxId, x.RootHwnds, x.ShapeDigest, state = x.State.ToString(), toggleAvailable = true, exactEdgeDocumentAndBody = true,
        continuity = "UNPROVEN", recipientProof = "NOT_ESTABLISHED" }));
    internal static object ObserveConnection(string profile, SafeFileHandle job, string output)
    {
        var evidence = new Dictionary<string, object?> { ["On"] = "NOT_ATTEMPTED", ["Connect"] = "NOT_ATTEMPTED", ["Allow"] = "NOT_ATTEMPTED", ["Cancel"] = "NOT_ATTEMPTED",
            ["protocol"] = "NOT_SENT", ["metadata"] = "NOT_TESTED", ["Off"] = "NOT_NEEDED_UNTIL_ON", ["continuity"] = "UNPROVEN", ["recipientProof"] = "NOT_ESTABLISHED" };
        bool onAttempted = false; Optin? baseline = null; string? preConnectShape = null;
        using var socket = new ClientWebSocket(); using var timeout = new CancellationTokenSource(15000);
        System.Threading.Tasks.Task? connect = null;
        try {
            if (File.Exists(Path.Combine(profile, "DevToolsActivePort"))) throw new InvalidDataException("Unexpected preexisting ActivePort");
            var first = StrictOptin(job, ToggleState.Off); OptinProof(first, Path.Combine(output, "pre-on-first.json"));
            Screenshot(Path.Combine(output, "pre-on.png"));
            var final = StrictOptin(job, ToggleState.Off); OptinProof(final, Path.Combine(output, "pre-on-final.json"));
            if (!Same(first,final)) throw new InvalidDataException("Pre-On sampled mismatch");
            baseline = final; evidence["preOnSampledEqual"] = true;
            // Fresh owned custom profile is the boundary. These samples do not authenticate a continuous recipient.
            if (final.CheckBox.GetCurrentPattern(TogglePattern.Pattern) is not TogglePattern toggle || toggle.Current.ToggleState != ToggleState.Off) throw new InvalidDataException("Immediate Toggle Off refused");
            evidence["On"] = "ONE_REQUESTED"; onAttempted = true; toggle.Toggle();
            var after = StrictOptin(job, ToggleState.On);
            if (!Same(final,after)) throw new InvalidDataException("Post-On sampled mismatch");
            evidence["On"] = "ONE_READBACK_ON"; OptinProof(after, Path.Combine(output, "post-on.json")); Screenshot(Path.Combine(output, "post-on.png"));
            string activePort = Path.Combine(profile, "DevToolsActivePort"); var clock = Stopwatch.StartNew();
            while (!File.Exists(activePort) && clock.ElapsedMilliseconds < 5000) Thread.Sleep(100);
            // Partial file, wrong path, non-loopback/missing listener all refuse, never retry a parse or connect.
            var lines = File.ReadAllLines(activePort);
            if (lines.Length != 2 || !int.TryParse(lines[0], out int port) || port < 1 || port > 65535 ||
                !lines[1].StartsWith("/devtools/browser/", StringComparison.Ordinal) || lines[1].Length <= "/devtools/browser/".Length || lines[1].Any(c => c <= 32 || c == '?' || c == '#')) throw new InvalidDataException("ActivePort format refused");
            TcpOwner.RequireJob(port, job);
            var preConnect = StrictOptin(job, ToggleState.On);
            if (!Same(final, preConnect)) throw new InvalidDataException("Pre-connect inspect identity changed");
            preConnectShape = preConnect.ShapeDigest; OptinProof(preConnect, Path.Combine(output, "pre-connect.json"));
            evidence["Connect"] = "ONE_REQUESTED";
            connect = socket.ConnectAsync(new Uri("ws://127.0.0.1:" + port + lines[1]), timeout.Token);
            // One documented observation cadence only; not a successful handshake or modal readiness claim.
            Thread.Sleep(2000);
            evidence["connectionTaskStatusAtObservation"] = connect.Status.ToString();
            try { evidence["promptObservation"] = Observe(job, output, "connection"); }
            catch (Exception ex) { evidence["promptObservationErrorType"] = ex.GetType().FullName; }
            evidence["result"] = evidence.ContainsKey("promptObservationErrorType") ? "INCONCLUSIVE" : "CONNECTION_OBSERVATION_ONLY_NOT_METADATA";
        } catch (Exception ex) { evidence["errorType"] = ex.GetType().FullName; evidence["result"] = "INCONCLUSIVE"; }
        finally {
            socket.Abort(); timeout.Cancel();
            // Observe any completed fault, including a fault caused by abort. No await/reconnect/protocol request.
            if (connect != null) _ = connect.ContinueWith(t => { _ = t.Exception; }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            if (onAttempted) {
                evidence["Off"] = "UNCONFIRMED";
                try {
                    // If prompt remains/root disabled/extra windows/provider errors/state mismatch, REFUSE Off. Never dismiss a modal.
                    var current = StrictOptin(job, ToggleState.On);
                    OptinProof(current, Path.Combine(output, "pre-off.json"));
                    if (baseline is null || !Same(baseline,current)) throw new InvalidDataException("Off sampled identity changed");
                    if (connect != null && (preConnectShape is null || current.ShapeDigest != preConnectShape)) throw new InvalidDataException("Off refuses changed or remaining prompt structure");
                    if (current.CheckBox.GetCurrentPattern(TogglePattern.Pattern) is not TogglePattern off || off.Current.ToggleState != ToggleState.On) throw new InvalidDataException("Off state refused");
                    evidence["Off"] = "ONE_REQUESTED"; off.Toggle();
                    var done = StrictOptin(job, ToggleState.Off);
                    if (!Same(current,done)) throw new InvalidDataException("Off sampled identity changed after effect");
                    evidence["Off"] = "READBACK_OFF"; OptinProof(done, Path.Combine(output, "post-off.json")); Screenshot(Path.Combine(output, "post-off.png"));
                } catch (Exception ex) { evidence["offErrorType"] = ex.GetType().FullName; }
            }
            File.WriteAllText(Path.Combine(output, "connection-observation.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        }
        if (!Equals(evidence.GetValueOrDefault("result"), "CONNECTION_OBSERVATION_ONLY_NOT_METADATA")) throw new InvalidDataException("Connection observation incomplete; refer to preserved evidence");
        return evidence;
    }
}

internal static class TcpOwner
{
    [DllImport("iphlpapi.dll", SetLastError = true)] private static extern uint GetExtendedTcpTable(nint table, ref int bytes, bool order, int family, int cls, uint reserved);
    internal static void RequireJob(int port, SafeFileHandle job)
    {
        int size = 0; uint initial = GetExtendedTcpTable(0, ref size, false, 2, 3, 0); // IPv4, OWNER_PID_LISTENER.
        if (initial != 122 || size < 4 || size > 1024 * 1024) throw new InvalidDataException("TCP table sizing failed");
        nint memory = Marshal.AllocHGlobal(size);
        try {
            if (GetExtendedTcpTable(memory, ref size, false, 2, 3, 0) != 0) throw new InvalidDataException("TCP table changed/unavailable");
            int count = Marshal.ReadInt32(memory); if (count < 0 || 4L + count * 24L > size) throw new InvalidDataException("TCP table bounds");
            var owners = new List<int>();
            for (int i = 0; i < count; i++) {
                nint row = memory + 4 + i * 24;
                int localPort = (Marshal.ReadByte(row, 8) << 8) | Marshal.ReadByte(row, 9);
                uint address = unchecked((uint)Marshal.ReadInt32(row, 4));
                if (localPort == port && address == 0x0100007f) owners.Add(Marshal.ReadInt32(row, 20));
            }
            if (owners.Count != 1) throw new InvalidDataException("One exact loopback listener required");
            using var process = Native.OpenProcess(0x1000, false, (uint)owners[0]);
            if (process.IsInvalid || !Native.IsProcessInJob(process, job, out bool member) || !member) throw new InvalidDataException("Listener not a current job member");
        } finally { Marshal.FreeHGlobal(memory); }
    }
}
