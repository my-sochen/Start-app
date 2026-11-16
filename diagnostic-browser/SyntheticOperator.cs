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
    internal sealed class Refusal : Exception { internal string Code { get; } internal Refusal(string code) { Code = code; } }
    private static string FailureCode(Exception ex) => ex is Refusal refusal ? refusal.Code : ex switch {
        ElementNotAvailableException => "UIA_ELEMENT_UNAVAILABLE", InvalidOperationException => "PROVIDER_INVALID_OPERATION",
        System.ComponentModel.Win32Exception => "NATIVE_CALL_FAILED", UnauthorizedAccessException => "ACCESS_REFUSED",
        InvalidDataException => "INVALID_DATA_EXCEPTION",
        IOException => "IO_FAILED", System.Runtime.InteropServices.COMException => "PROVIDER_COM_FAILED",
        _ => "OTHER_EXCEPTION_NO_TEXT_RECORDED"
    };
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    private static bool Member(int pid, SafeFileHandle job)
    {
        using var p = Native.OpenProcess(0x1000, false, (uint)pid);
        return !p.IsInvalid && Native.IsProcessInJob(p, job, out bool member) && member;
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
            Stage("ONE_FOCUS_REQUESTED"); selected.Box.SetFocus();
            if (!Automation.Compare(AutomationElement.FocusedElement, selected.Box) || GetForegroundWindow() != hwnd) throw new Refusal("FOCUS_REFUSED");
            if (selected.Box.GetCurrentPattern(ValuePattern.Pattern) is not ValuePattern value || value.Current.IsReadOnly || value.Current.Value != expectedSetup ||
                GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid != (uint)selected.Root.Current.ProcessId || !Member((int)pid, job)) throw new Refusal("BEFORE_SETVALUE_GUARD");
            Stage("ONE_SETVALUE_REQUESTED"); value.SetValue(InspectUri);
            if (value.Current.Value != InspectUri || !Automation.Compare(AutomationElement.FocusedElement, selected.Box) || GetForegroundWindow() != hwnd ||
                !Member(selected.Box.Current.ProcessId, job) || GetWindowThreadProcessId(hwnd, out pid) == 0 || pid != (uint)selected.Root.Current.ProcessId || !Member((int)pid, job)) throw new Refusal("BEFORE_ENTER_GUARD");
            if (IntPtr.Size != 8 || Marshal.SizeOf<Input>() != 40 || Marshal.OffsetOf<Input>(nameof(Input.Data)).ToInt32() != 8 ||
                Marshal.OffsetOf<KeyboardUnion>(nameof(KeyboardUnion.Flags)).ToInt32() != 4) throw new Refusal("INPUT_ABI_REFUSED");
            var keys = new[] { new Input { Type = 1, Data = new KeyboardUnion { Key = 13 } }, new Input { Type = 1, Data = new KeyboardUnion { Key = 13, Flags = 2 } } };
            Stage("ONE_ENTER_PAIR_REQUESTED"); uint sent = SendInput(2, keys, Marshal.SizeOf<Input>());
            Stage(sent == 2 ? "ENTER_TWO" : "ENTER_PARTIAL_REFUSE"); if (sent != 2) throw new Refusal("NO_PARTIAL_INPUT_RETRY");
            Stage("WAIT_EXACT_INSPECT_RESOURCE"); var inspect = WaitExact(job, InspectUri, Record);
            if ((nint)inspect.Root.Current.NativeWindowHandle != hwnd || !Automation.Compare(inspect.Root, selected.Root)) throw new Refusal("INSPECT_NOT_IN_DEDICATED_SETUP_ROOT");
            Stage("EXACT_DEDICATED_INSPECT_READBACK");
            var setup = StrictOptin(job, ToggleState.Off); OptinProof(setup, Path.Combine(output,"setup-inspect-proof.json"));
            return new { mode = "SETUP_EXACT_INSPECT_STRICT_OFF", On = "NOT_ATTEMPTED", Connect = "NOT_ATTEMPTED", continuity = "UNPROVEN" };
        } catch (Exception ex) {
            File.WriteAllText(Path.Combine(output, "setup-failure.json"), JsonSerializer.Serialize(new { last, failure = FailureCode(ex), On = "NOT_ATTEMPTED", Connect = "NOT_ATTEMPTED", retry = false }));

            throw;
        }
    }
    private const string Broad = "Turning on this setting allows external apps to request full control of this browser. This includes read access to your saved data, cookies and site data, and the ability to navigate to any URL.";
    private const string Trust = "Only web developers should turn on this feature, and only use it with trusted apps.";
    private sealed record Optin(AutomationElement Root, AutomationElement Box, AutomationElement Document, AutomationElement CheckBox,
        nint Hwnd, int Pid, int[] RootId, int[] BoxId, int[] DocumentId, int[] CheckBoxId, long[] RootHwnds, string ShapeDigest, ToggleState State);
    private static int[] Id(AutomationElement element) {
        var id = element.GetRuntimeId(); if (id is null || id.Length == 0) throw new Refusal("MISSING_SAMPLED_RUNTIME_ID"); return id;
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
        if (boxes.Length != 1) throw new Refusal("UNIQUE_EXACT_INSPECT_OMNIBOX_REQUIRED");
        int rootIndex = RootOf(boxes[0]); var root = nodes[rootIndex]; var box = nodes[boxes[0]];
        var rc = root.Current; nint hwnd = (nint)rc.NativeWindowHandle;
        if (rc.ControlType != ControlType.Window || rc.ClassName != "Chrome_WidgetWin_1" || hwnd == 0 || !rc.IsEnabled || rc.IsOffscreen ||
            !box.Current.IsEnabled || box.Current.IsOffscreen || GetWindowThreadProcessId(hwnd, out uint pid) == 0 ||
            pid != (uint)rc.ProcessId || box.Current.ProcessId != rc.ProcessId || !Member((int)pid, job)) throw new Refusal("NATIVE_INSPECT_OWNER_REFUSED");
        var docs = Enumerable.Range(0, nodes.Length).Where(i => RootOf(i) == rootIndex && nodes[i].Current.ControlType == ControlType.Document &&
            nodes[i].Current.AutomationId == "RootWebArea" && nodes[i].Current.Name == "Inspect with Edge Developer Tools" &&
            nodes[i].TryGetCurrentPattern(ValuePattern.Pattern, out object? p) && p is ValuePattern v && v.Current.Value == InspectUri).ToArray();
        if (docs.Length != 1) throw new Refusal("UNIQUE_EXACT_EDGE_INSPECT_DOCUMENT_REQUIRED");
        int document = docs[0];
        int UniqueChild(ControlType type, string name, string? automationId = null) {
            var found = Enumerable.Range(0, nodes.Length).Where(i => sample.Parents[i] == document && nodes[i].Current.ControlType == type &&
                nodes[i].Current.Name == name && (automationId is null || nodes[i].Current.AutomationId == automationId)).ToArray();
            if (found.Length != 1) throw new Refusal("EXACT_INSPECT_DIRECT_CHILD_REFUSED"); return found[0];
        }
        int checkbox = UniqueChild(ControlType.CheckBox, "Allow remote debugging for this browser instance", "remote-debugging-enabled");
        int broad = UniqueChild(ControlType.Text, Broad); int trust = UniqueChild(ControlType.Text, Trust);
        foreach (var i in new[] { boxes[0], document, checkbox, broad, trust }) {
            var c = nodes[i].Current;
            if (!c.IsEnabled || c.IsOffscreen || c.ProcessId != (int)pid || !Member(c.ProcessId, job)) throw new Refusal("INSPECT_CHILD_AVAILABILITY_JOB_REFUSED");
        }
        if (!nodes[checkbox].TryGetCurrentPattern(TogglePattern.Pattern, out object? pattern) || pattern is not TogglePattern toggle || toggle.Current.ToggleState != expected)
            throw new Refusal("EXACT_EXPECTED_TOGGLE_PATTERN_STATE_REQUIRED");
        var rootHwnds = Enumerable.Range(0, nodes.Length).Where(i => sample.Parents[i] == -1).Select(i => {
            var c = nodes[i].Current; nint h = (nint)c.NativeWindowHandle;
            if (c.ControlType != ControlType.Window || h == 0 || GetWindowThreadProcessId(h, out uint p) == 0 || p != (uint)c.ProcessId || !Member((int)p, job)) throw new Refusal("ROOT_NATIVE_JOB_PREDICATE");
            return (long)h;
        }).OrderBy(h => h).ToArray();
        if (rootHwnds.Length != 3) throw new Refusal("EXPECTED_THREE_OWNED_ROOTS_ONLY_NOT_COVERAGE_PROOF");
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
    private static object ObservePrompt(SafeFileHandle job, Optin expected, string output)
    {
        // No screenshots, raw full-tree dumps, omnibox values or unrelated-window text are persisted.
        var sample = ReadComplete(job); var nodes = sample.Nodes;
        var roots = Enumerable.Range(0,nodes.Length).Where(i => sample.Parents[i] == -1 && nodes[i].Current.NativeWindowHandle == (int)expected.Hwnd).ToArray();
        if (roots.Length != 1) throw new Refusal("PROMPT_INSPECT_ROOT_ABSENT_OR_AMBIGUOUS");
        int root = roots[0]; var rc = nodes[root].Current;
        bool sampledRootEqual = Automation.Compare(nodes[root],expected.Root);
        bool sampledRootIdEqual = Id(nodes[root]).SequenceEqual(expected.RootId);
        if (!sampledRootEqual || !sampledRootIdEqual) throw new Refusal("PROMPT_SAMPLED_INSPECT_ROOT_IDENTITY_CHANGED");
        if (rc.ProcessId != expected.Pid || rc.IsEnabled || rc.IsOffscreen ||
            GetWindowThreadProcessId(expected.Hwnd,out uint pid) == 0 || pid != (uint)expected.Pid || !Member((int)pid,job)) throw new Refusal("PROMPT_INSPECT_ROOT_NOT_MODAL_SAMPLE");
        bool Under(int i,int ancestor) { for(int p = sample.Parents[i];p >= 0;p = sample.Parents[p]) if(p == ancestor)return true; return false; }
        var panes = Enumerable.Range(0,nodes.Length).Where(i => Under(i,root) && nodes[i].Current.ControlType == ControlType.Pane &&
            nodes[i].Current.NativeWindowHandle != 0 && GetWindow((nint)nodes[i].Current.NativeWindowHandle,4) == expected.Hwnd).ToArray();
        File.WriteAllText(Path.Combine(output,"prompt-candidate-facts.json"),JsonSerializer.Serialize(new {
            sampledNodes = nodes.Length, sampledJobRootCount = sample.Parents.Count(p => p == -1),
            inspectRoot = (long)expected.Hwnd, inspectRootDisabled = !rc.IsEnabled, sampledRootEqual, sampledRootIdEqual,
            candidatePredicate = "JOB_OWNED_PANE_DESCENDANT_WITH_NONZERO_NATIVE_HWND_OWNED_BY_DISABLED_INSPECT_ROOT_HYPOTHESIS_NOT_EDGE_CONTRACT",
            candidateCount = panes.Length,
            nativeNodes = Enumerable.Range(0,nodes.Length).Where(i => nodes[i].Current.NativeWindowHandle != 0).Select(i => new {
                index = i, parent = sample.Parents[i], hwnd = nodes[i].Current.NativeWindowHandle,
                nativeOwner = (long)GetWindow((nint)nodes[i].Current.NativeWindowHandle,4),
                isRoot = sample.Parents[i] == -1, isPane = nodes[i].Current.ControlType == ControlType.Pane,
                isWindow = nodes[i].Current.ControlType == ControlType.Window, pid = nodes[i].Current.ProcessId,
                nodes[i].Current.IsEnabled, nodes[i].Current.IsOffscreen }),
            text = "NONE_IN_THIS_FILE", continuity = "UNPROVEN"
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (panes.Length != 1) throw new Refusal("UNIQUE_NATIVE_OWNED_PROMPT_PANE_NOT_OBSERVED");
        int pane = panes[0]; var pc = nodes[pane].Current; nint paneHwnd = (nint)pc.NativeWindowHandle;
        if (GetWindowThreadProcessId(paneHwnd,out uint panePid) == 0 || panePid != (uint)expected.Pid || pc.ProcessId != expected.Pid || !Member((int)panePid,job)) throw new Refusal("PROMPT_PANE_PID_JOB_REFUSED");
        var included = Enumerable.Range(0,nodes.Length).Where(i => i == pane || Under(i,pane)).ToArray();
        if (included.Length > 128) throw new Refusal("PROMPT_SUBTREE_RECORD_CAP");
        var indexMap = included.Select((node,index) => (node,index)).ToDictionary(x => x.node,x => x.index);
        int textChars = 0; var rows = new List<object>();
        foreach (int i in included) {
            var c = nodes[i].Current; string text = c.Name;
            // Bounded unknown prompt source text, not trusted instructions or an authenticated author claim.
            if (text.Length > 2048 || textChars + text.Length > 8192) throw new Refusal("PROMPT_TEXT_CAP");
            textChars += text.Length;
            if (c.ProcessId != expected.Pid || !Member(c.ProcessId,job)) throw new Refusal("PROMPT_NODE_PID_JOB_REFUSED");
            bool button = c.ControlType == ControlType.Button, window = c.ControlType == ControlType.Window, textNode = c.ControlType == ControlType.Text;
            rows.Add(new { index = indexMap[i], parent = i == pane ? -1 : indexMap[sample.Parents[i]],
                kind = i == pane ? "NativePane" : button ? "Button" : window ? "Window" : textNode ? "Text" : "OtherControl",
                boundedUntrustedName = text, c.IsEnabled, c.IsOffscreen, pid = c.ProcessId, hwnd = c.NativeWindowHandle,
                runtimeId = Id(nodes[i]), invokeAvailable = button && nodes[i].TryGetCurrentPattern(InvokePattern.Pattern,out _),
                provenance = "OBSERVED_JOB_UIA_PROVIDER_NOT_AUTHENTICATED_PERSON", continuity = "UNPROVEN" });
        }
        File.WriteAllText(Path.Combine(output,"edge-prompt-structure.json"),JsonSerializer.Serialize(new {
            rootHwnd = (long)expected.Hwnd, paneHwnd = (long)paneHwnd, nativeOwner = (long)GetWindow(paneHwnd,4), pid = expected.Pid,
            rootDisabled = true, sampledRootEqual, sampledRootIdEqual, completeControlNodes = nodes.Length, promptNodes = rows.Count, boundedTextCharacters = textChars,
            rows, coverage = "ONE_NATIVE_OWNED_PANE_SUBTREE_SAMPLE_ONLY", pixels = "NOT_CAPTURED", recipientProof = "NOT_ESTABLISHED", continuity = "UNPROVEN"
        },new JsonSerializerOptions { WriteIndented = true }));
        return new { result = "BOUNDED_NATIVE_OWNED_PANE_STRUCTURE_OBSERVED", nodes = rows.Count, pixels = "NOT_CAPTURED", recipientProof = "NOT_ESTABLISHED", continuity = "UNPROVEN" };
    }
    internal static object ObserveConnection(string profile, SafeFileHandle job, string output)
    {
        var evidence = new Dictionary<string, object?> { ["On"] = "NOT_ATTEMPTED", ["Connect"] = "NOT_ATTEMPTED", ["Allow"] = "NOT_ATTEMPTED", ["Cancel"] = "NOT_ATTEMPTED",
            ["protocol"] = "NOT_SENT", ["metadata"] = "NOT_TESTED", ["Off"] = "NOT_NEEDED_UNTIL_ON", ["continuity"] = "UNPROVEN", ["recipientProof"] = "NOT_ESTABLISHED" };
        bool onAttempted = false; Optin? baseline = null; string? preConnectShape = null;
        using var socket = new ClientWebSocket(); using var timeout = new CancellationTokenSource(15000);
        System.Threading.Tasks.Task? connect = null;
        try {
            if (File.Exists(Path.Combine(profile, "DevToolsActivePort"))) throw new Refusal("UNEXPECTED_PREEXISTING_ACTIVEPORT");
            var first = StrictOptin(job, ToggleState.Off); OptinProof(first, Path.Combine(output, "pre-on-first.json"));
            var final = StrictOptin(job, ToggleState.Off); OptinProof(final, Path.Combine(output, "pre-on-final.json"));
            if (!Same(first,final)) throw new Refusal("PRE_ON_SAMPLED_MISMATCH");
            baseline = final; evidence["preOnSampledEqual"] = true;
            // Fresh owned custom profile is the boundary. These samples do not authenticate a continuous recipient.
            if (final.CheckBox.GetCurrentPattern(TogglePattern.Pattern) is not TogglePattern toggle || toggle.Current.ToggleState != ToggleState.Off) throw new Refusal("IMMEDIATE_TOGGLE_OFF_REFUSED");
            evidence["On"] = "ONE_REQUESTED"; onAttempted = true; toggle.Toggle();
            var after = StrictOptin(job, ToggleState.On);
            if (!Same(final,after)) throw new Refusal("POST_ON_SAMPLED_MISMATCH");
            evidence["On"] = "ONE_READBACK_ON"; OptinProof(after, Path.Combine(output, "post-on.json"));
            string activePort = Path.Combine(profile, "DevToolsActivePort"); var clock = Stopwatch.StartNew();
            while (!File.Exists(activePort) && clock.ElapsedMilliseconds < 5000) Thread.Sleep(100);
            // Partial file, wrong path, non-loopback/missing listener all refuse, never retry a parse or connect.
            var lines = File.ReadAllLines(activePort);
            if (lines.Length != 2 || !int.TryParse(lines[0], out int port) || port < 1 || port > 65535 ||
                !lines[1].StartsWith("/devtools/browser/", StringComparison.Ordinal) || lines[1].Length <= "/devtools/browser/".Length || lines[1].Any(c => c <= 32 || c == '?' || c == '#')) throw new Refusal("ACTIVEPORT_FORMAT_REFUSED");
            TcpOwner.RequireJob(port, job);
            var preConnect = StrictOptin(job, ToggleState.On);
            if (!Same(final, preConnect)) throw new Refusal("PRE_CONNECT_INSPECT_IDENTITY_CHANGED");
            preConnectShape = preConnect.ShapeDigest; OptinProof(preConnect, Path.Combine(output, "pre-connect.json"));
            evidence["Connect"] = "ONE_REQUESTED";
            connect = socket.ConnectAsync(new Uri("ws://127.0.0.1:" + port + lines[1]), timeout.Token);
            // One documented observation cadence only; not a successful handshake or modal readiness claim.
            Thread.Sleep(2000);
            evidence["connectionTaskStatusAtObservation"] = connect.Status.ToString();
            try { evidence["promptObservation"] = ObservePrompt(job, after, output); }
            catch (Exception ex) { evidence["promptObservationErrorType"] = FailureCode(ex); }
            evidence["result"] = evidence.ContainsKey("promptObservationErrorType") ? "INCONCLUSIVE" : "CONNECTION_OBSERVATION_ONLY_NOT_METADATA";
        } catch (Exception ex) { evidence["errorType"] = FailureCode(ex); evidence["result"] = "INCONCLUSIVE"; }
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
                    if (baseline is null || !Same(baseline,current)) throw new Refusal("OFF_SAMPLED_IDENTITY_CHANGED");
                    if (connect != null && (preConnectShape is null || current.ShapeDigest != preConnectShape)) throw new Refusal("OFF_REFUSES_CHANGED_OR_REMAINING_PROMPT_STRUCTURE");
                    if (current.CheckBox.GetCurrentPattern(TogglePattern.Pattern) is not TogglePattern off || off.Current.ToggleState != ToggleState.On) throw new Refusal("OFF_STATE_REFUSED");
                    evidence["Off"] = "ONE_REQUESTED"; off.Toggle();
                    var done = StrictOptin(job, ToggleState.Off);
                    if (!Same(current,done)) throw new Refusal("OFF_SAMPLED_IDENTITY_CHANGED_AFTER_EFFECT");
                    evidence["Off"] = "READBACK_OFF"; OptinProof(done, Path.Combine(output, "post-off.json"));
                } catch (Exception ex) { evidence["offErrorType"] = FailureCode(ex); }
            }
            File.WriteAllText(Path.Combine(output, "connection-observation.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        }
        if (!Equals(evidence.GetValueOrDefault("result"), "CONNECTION_OBSERVATION_ONLY_NOT_METADATA")) throw new Refusal("CONNECTION_OBSERVATION_INCOMPLETE_REFER_TO_PRESERVED_EVIDENCE");
        return evidence;
    }
}

internal static class TcpOwner
{
    [DllImport("iphlpapi.dll", SetLastError = true)] private static extern uint GetExtendedTcpTable(nint table, ref int bytes, bool order, int family, int cls, uint reserved);
    internal static void RequireJob(int port, SafeFileHandle job)
    {
        int size = 0; uint initial = GetExtendedTcpTable(0, ref size, false, 2, 3, 0); // IPv4, OWNER_PID_LISTENER.
        if (initial != 122 || size < 4 || size > 1024 * 1024) throw new SyntheticOperator.Refusal("TCP_TABLE_SIZING_FAILED");
        nint memory = Marshal.AllocHGlobal(size);
        try {
            if (GetExtendedTcpTable(memory, ref size, false, 2, 3, 0) != 0) throw new SyntheticOperator.Refusal("TCP_TABLE_CHANGED_UNAVAILABLE");
            int count = Marshal.ReadInt32(memory); if (count < 0 || 4L + count * 24L > size) throw new SyntheticOperator.Refusal("TCP_TABLE_BOUNDS");
            var owners = new List<int>();
            for (int i = 0; i < count; i++) {
                nint row = memory + 4 + i * 24;
                int localPort = (Marshal.ReadByte(row, 8) << 8) | Marshal.ReadByte(row, 9);
                uint address = unchecked((uint)Marshal.ReadInt32(row, 4));
                if (localPort == port && address == 0x0100007f) owners.Add(Marshal.ReadInt32(row, 20));
            }
            if (owners.Count != 1) throw new SyntheticOperator.Refusal("ONE_EXACT_LOOPBACK_LISTENER_REQUIRED");
            using var process = Native.OpenProcess(0x1000, false, (uint)owners[0]);
            if (process.IsInvalid || !Native.IsProcessInJob(process, job, out bool member) || !member) throw new SyntheticOperator.Refusal("LISTENER_NOT_A_CURRENT_JOB_MEMBER");
        } finally { Marshal.FreeHGlobal(memory); }
    }
}
