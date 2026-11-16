using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
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
    private static T Await<T>(Func<T?> check, int milliseconds) where T : class
    {
        var clock = Stopwatch.StartNew();
        do { T? found = check(); if (found is not null) return found; Thread.Sleep(100); } while (clock.ElapsedMilliseconds < milliseconds);
        throw new TimeoutException("Expected synthetic UI did not appear; no retry/fallback");
    }
    private static AutomationElement Inspect(SafeFileHandle job, string uri)
    {
        var matches = Windows(job).Where(w => Descendants(w).Any(e => e.Current.ClassName == "OmniboxViewViews" &&
            e.TryGetCurrentPattern(ValuePattern.Pattern, out object? p) && p is ValuePattern value && value.Current.Value == uri)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("One exact inspect window required");
        return matches[0];
    }
    private static TogglePattern Toggle(SafeFileHandle job, string uri)
    {
        var elements = Descendants(Inspect(job, uri)).Where(e => e.Current.ControlType == ControlType.CheckBox &&
            e.Current.AutomationId == "remote-debugging-enabled" && e.Current.Name == "Allow remote debugging for this browser instance" &&
            e.Current.IsEnabled && !e.Current.IsOffscreen && Member(e.Current.ProcessId, job)).ToArray();
        if (elements.Length != 1) throw new InvalidOperationException("Exact inspect toggle unavailable");
        if (elements[0].GetCurrentPattern(TogglePattern.Pattern) is not TogglePattern toggle) throw new InvalidOperationException("Toggle pattern unavailable");
        return toggle;
    }
    private static string EndpointSnapshot(SafeFileHandle job)
    {
        var windows = Windows(job).Select(w => new { hwnd = w.Current.NativeWindowHandle,
            pid = w.Current.ProcessId, bounds = w.Current.BoundingRectangle.ToString(System.Globalization.CultureInfo.InvariantCulture),
            tabs = Descendants(w).Where(e => e.Current.ControlType == ControlType.TabItem).Select(e => new {
                name = e.Current.Name, selected = e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? p) && p is SelectionItemPattern selection && selection.Current.IsSelected
            }).ToArray() }).OrderBy(w => w.hwnd).ToArray();
        return JsonSerializer.Serialize(windows);
    }
    private static void StartupObservation(SafeFileHandle job, string expectedUri, string output, string stage)
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
                            values.Add(v.Current.Value == expectedUri ? "EXACT_EXPECTED_INSPECT_URI" :
                                v.Current.Value == expectedUri.Replace("chrome://", "").Replace("edge://", "") ? "EXPECTED_INSPECT_WITHOUT_SCHEME" :
                                v.Current.Value == "about:blank" ? "ABOUT_BLANK" : "OTHER_VALUE_REDACTED");
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
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] input, int size);
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeyboardUnion Data; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct KeyboardUnion {
        [FieldOffset(0)] public ushort Key;
        [FieldOffset(4)] public uint Flags;
    }
    internal static object RunSetupOnly(string browser, string syntheticSetup, SafeFileHandle job, string output)
    {
        Directory.CreateDirectory(output);
        if (browser != "chrome") throw new InvalidOperationException("This reviewed setup candidate is Chrome only");
        string inspectUri = "chrome://inspect/#remote-debugging";
        StartupObservation(job, inspectUri, output, "00-before-setup");
        // Exact expected fixture representation measured in V11 on this build.
        // No normalization of observed values, no alternative/fuzzy match.
        string expectedSetupValue = new Uri(syntheticSetup).LocalPath.Replace('\\', '/');
        int sequence = 0;
        string lastStage = "NOT_STARTED";
        void Stage(string code) {
            File.WriteAllText(Path.Combine(output, "stage-" + (++sequence).ToString("D2") + ".json"),
                JsonSerializer.Serialize(new { stage = code, elapsedTicks = Stopwatch.GetTimestamp(), continuity = "UNPROVEN" }));
            lastStage = code;
        }
        try {
        Stage("WAIT_EXACT_SETUP_PATH");
        var window = Await(() => {
            var matches = Windows(job).Where(w => Descendants(w).Any(e => e.Current.ClassName == "OmniboxViewViews" &&
                e.TryGetCurrentPattern(ValuePattern.Pattern, out object? p) && p is ValuePattern value && value.Current.Value == expectedSetupValue)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }, 10000);
        Stage("SETUP_WINDOW_MATCHED");
        var boxes = Descendants(window).Where(e => e.Current.ClassName == "OmniboxViewViews" &&
            e.TryGetCurrentPattern(ValuePattern.Pattern, out object? p) && p is ValuePattern value && value.Current.Value == expectedSetupValue).ToArray();
        if (boxes.Length != 1 || !Member(window.Current.ProcessId, job) || !Member(boxes[0].Current.ProcessId, job) || window.Current.IsOffscreen || !window.Current.IsEnabled)
            throw new InvalidOperationException("Unique current job-owned synthetic Setup window required");
        nint hwnd = (nint)window.Current.NativeWindowHandle;
        Stage("FOREGROUND_REQUESTED");
        bool activated = SetForegroundWindow(hwnd); // ONE activation call, never repeat.
        Stage(activated ? "ACTIVATION_RETURNED_NONZERO" : "ACTIVATION_RETURNED_ZERO_STOP");
        if (!activated) throw new InvalidOperationException("Setup foreground API refused");
        Stage("WAIT_CURRENT_JOB_SETUP_FOREGROUND");
        var foregroundClock = Stopwatch.StartNew();
        bool witnessed = false;
        do {
            // Refuse if the exact selected current fixture or job identity changed while settling.
            if (window.Current.NativeWindowHandle != (int)hwnd || window.Current.IsOffscreen || !window.Current.IsEnabled ||
                !Member(window.Current.ProcessId, job) || !Member(boxes[0].Current.ProcessId, job) ||
                !boxes[0].TryGetCurrentPattern(ValuePattern.Pattern, out object? currentPattern) ||
                currentPattern is not ValuePattern currentValue || currentValue.Current.Value != expectedSetupValue)
                throw new InvalidOperationException("Setup current fixture changed during foreground wait");
            uint thread = GetWindowThreadProcessId(hwnd, out uint currentPid);
            if (thread == 0 || currentPid != (uint)window.Current.ProcessId || !Member((int)currentPid, job))
                throw new InvalidOperationException("Setup current foreground target identity refused");
            if (foregroundClock.ElapsedMilliseconds > 2000) break;
            if (GetForegroundWindow() == hwnd) { witnessed = true; break; }
            Thread.Sleep(50);
        } while (foregroundClock.ElapsedMilliseconds <= 2000);
        if (!witnessed) throw new TimeoutException("Current synthetic Setup foreground not witnessed in bounded wait");
        Stage("CURRENT_JOB_SETUP_FOREGROUND_WITNESSED");
        Stage("FOCUS_REQUESTED");
        boxes[0].SetFocus();
        if (!Automation.Compare(AutomationElement.FocusedElement, boxes[0]) || GetForegroundWindow() != hwnd)
            throw new InvalidOperationException("Setup focus refused");
        if (boxes[0].GetCurrentPattern(ValuePattern.Pattern) is not ValuePattern valuePattern || valuePattern.Current.IsReadOnly)
            throw new InvalidOperationException("Setup edit unavailable");
        Stage("SETVALUE_REQUESTED");
        valuePattern.SetValue(inspectUri); // one exact synthetic navigation input, never permission text.
        if (valuePattern.Current.Value != inspectUri || !Automation.Compare(AutomationElement.FocusedElement, boxes[0]) ||
            GetForegroundWindow() != hwnd || !Member(boxes[0].Current.ProcessId, job)) throw new InvalidOperationException("Setup readback refused before Enter");
        Stage("EXACT_INSPECT_READBACK_FOCUS_VERIFIED");
        // Refuse before ANY injected key if managed layout/architecture differs from reviewed x64 ABI.
        if (IntPtr.Size != 8 || Marshal.SizeOf<Input>() != 40 || Marshal.OffsetOf<Input>(nameof(Input.Data)).ToInt32() != 8 ||
            Marshal.OffsetOf<KeyboardUnion>(nameof(KeyboardUnion.Flags)).ToInt32() != 4) throw new InvalidOperationException("INPUT x64 layout refused");
        var keys = new[] { new Input { Type = 1, Data = new KeyboardUnion { Key = 13 } }, new Input { Type = 1, Data = new KeyboardUnion { Key = 13, Flags = 2 } } };
        Stage("ENTER_REQUESTED");
        uint sent = SendInput(2, keys, Marshal.SizeOf<Input>());
        Stage(sent == 2 ? "ENTER_RETURNED_TWO" : sent == 1 ? "ENTER_RETURNED_ONE_STOP" : "ENTER_RETURNED_ZERO_STOP");
        if (sent != 2) throw new InvalidOperationException("Setup Enter incomplete, never retry");
        Stage("WAIT_EXACT_INSPECT");
        Await(() => { try { return Inspect(job, inspectUri); } catch (InvalidOperationException) { return null; } }, 10000);
        Stage("EXACT_INSPECT_MATCHED");
        StartupObservation(job, inspectUri, output, "01-after-setup");
        return new { mode = "SETUP_NAVIGATION_ONLY", replacedDedicatedSetup = true, consent = "NOT_ATTEMPTED", socket = "NOT_OPENED", metadata = "NOT_TESTED", identityAuthorization = "NONE", continuity = "UNPROVEN" };
        } catch (Exception ex) {
            try { File.WriteAllText(Path.Combine(output, "failure-stage.json"), JsonSerializer.Serialize(new { lastRecordedStage = lastStage, errorType = ex.GetType().FullName, code = ex is TimeoutException ? (lastStage == "WAIT_EXACT_SETUP_PATH" ? "WAIT_SETUP_TIMEOUT" : lastStage == "WAIT_EXACT_INSPECT" ? "WAIT_INSPECT_TIMEOUT" : lastStage == "WAIT_CURRENT_JOB_SETUP_FOREGROUND" ? "WAIT_FOREGROUND_TIMEOUT" : "OTHER_TIMEOUT") : "SETUP_STOP" })); } catch { }
            // Never replace the original failure if best-effort observation itself fails.
            try { StartupObservation(job, inspectUri, output, "02-setup-failure"); } catch { }
            throw;
        }
    }
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    private static string KnownName(string name) => name switch {
        "Allow remote debugging?" => "PROMPT_TITLE",
        "Allow" => "ALLOW_LABEL",
        "Cancel" => "CANCEL_LABEL",
        "Turn off in settings" => "DISABLE_LABEL",
        "An external app wants full control over this Chrome session to debug it. This includes access to your saved data, cookies and site data, and the ability to navigate to any URL." => "FULL_CONTROL_PARAGRAPH",
        "Only web developers should turn on this feature, and only use it with trusted apps." => "TRUST_PARAGRAPH",
        _ => "UNKNOWN_REDACTED"
    };
    private static void ModalTree(SafeFileHandle job, string inspectUri, string output, TreeWalker walker, string view)
    {
        const int maxNodes = 512, maxDepth = 12, maxSiblings = 64;
        var rows = new List<object>(); var errors = new List<string>();
        bool capped = false; int next = 0, work = 0;
        var queue = new Queue<(AutomationElement node, int parent, int depth)>();
        // Root enumeration is existing implementation; bounded descent never FindAll(Descendants).
        var roots = Windows(job);
        if (roots.Length > 16) { roots = roots.Take(16).ToArray(); capped = true; }
        foreach (var root in roots) queue.Enqueue((root, -1, 0));
        while (queue.Count > 0 && work < maxNodes) {
            var item = queue.Dequeue(); work++; int id = next++;
            try {
                var c = item.node.Current;
                bool member = Member(c.ProcessId, job);
                if (!member) { rows.Add(new { id, parent = item.parent, depth = item.depth, boundary = "NON_JOB_REDACTED" }); continue; }
                nint hwnd = (nint)c.NativeWindowHandle;
                nint owner = hwnd == 0 ? 0 : GetWindow(hwnd, 4); // GW_OWNER, readonly.
                nint nativeRoot = hwnd == 0 ? 0 : GetAncestor(hwnd, 2); // GA_ROOT.
                uint ownerThread = owner == 0 ? 0 : GetWindowThreadProcessId(owner, out _);
                uint ownerPid = 0; if (ownerThread != 0) GetWindowThreadProcessId(owner, out ownerPid);
                bool ownerJob = ownerPid != 0 && Member((int)ownerPid, job);
                bool exactInspect = c.ClassName == "OmniboxViewViews" && item.node.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp) && vp is ValuePattern value && value.Current.Value == inspectUri;
                rows.Add(new { id, parent = item.parent, depth = item.depth, type = c.ControlType.ProgrammaticName,
                    name = KnownName(c.Name), hwnd = (long)hwnd, pid = c.ProcessId, jobMember = member,
                    enabled = c.IsEnabled, offscreen = c.IsOffscreen, exactInspect,
                    invokeAvailable = item.node.TryGetCurrentPattern(InvokePattern.Pattern, out _),
                    windowAvailable = item.node.TryGetCurrentPattern(WindowPattern.Pattern, out _),
                    ownerState = owner == 0 ? "NONE" : ownerJob ? "JOB_OWNER" : "UNRESOLVED_OR_OUTSIDE_REDACTED",
                    ownerHwnd = ownerJob ? (long)owner : 0, ownerPid = ownerJob ? ownerPid : 0,
                    nativeRootHwnd = nativeRoot == hwnd || (nativeRoot != 0 && roots.Any(w => w.Current.NativeWindowHandle == (int)nativeRoot)) ? (long)nativeRoot : 0,
                    continuity = "UNPROVEN" });
                if (item.depth >= maxDepth) { capped = true; continue; }
                var child = walker.GetFirstChild(item.node); int siblings = 0;
                while (child is not null && siblings < maxSiblings && work + queue.Count < maxNodes) {
                    queue.Enqueue((child, id, item.depth + 1)); siblings++;
                    child = walker.GetNextSibling(child);
                }
                if (child is not null) capped = true;
            } catch (Exception ex) { errors.Add("NODE_READ_" + ex.GetType().Name); }
        }
        if (queue.Count > 0) capped = true;
        File.WriteAllText(Path.Combine(output, "modal-tree-" + view + ".json"), JsonSerializer.Serialize(new {
            view, maxNodes, maxDepth, maxSiblings, capped, rows, errors,
            diagnosis = "STRUCTURE_ONLY_NOT_PERMISSION", continuity = "UNPROVEN"
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    internal static object RunModalObservation(string browser, string profile, string[] urls, SafeFileHandle job, string output)
    {
        Directory.CreateDirectory(output);
        string inspectUri = "chrome://inspect/#remote-debugging";
        void Phase(string code) => File.WriteAllText(Path.Combine(output, "modal-phase-" + code + ".json"), JsonSerializer.Serialize(new { code, ticks = Stopwatch.GetTimestamp() }));
        try {
        Phase("BEGIN");
        var off = Toggle(job, inspectUri);
        if (off.Current.ToggleState != ToggleState.Off) throw new InvalidOperationException("Fresh synthetic opt-in was not Off");
        Screenshot(Path.Combine(output, "modal-before-optin.png"));
        Phase("ON_REQUESTED"); off.Toggle(); // One existing opt-in, no retry.
        Await(() => Toggle(job, inspectUri).Current.ToggleState == ToggleState.On ? new object() : null, 5000);
        Phase("ON_READBACK");
        string endpointFile = Path.Combine(profile, "DevToolsActivePort");
        Await(() => File.Exists(endpointFile) ? new object() : null, 5000);
        string[] lines = File.ReadAllLines(endpointFile);
        if (lines.Length != 2 || !int.TryParse(lines[0], out int port) || port < 1 || port > 65535 ||
            !lines[1].StartsWith("/devtools/browser/", StringComparison.Ordinal) || lines[1].Any(c => c <= 32 || c == '?' || c == '#'))
            throw new InvalidDataException("Unexpected endpoint file");
        TcpOwner.RequireJob(port, job);
        using var socket = new ClientWebSocket();
        using var timeout = new CancellationTokenSource(15000);
        try {
            Phase("ONE_CONNECT_REQUESTED");
            var connect = socket.ConnectAsync(new Uri("ws://127.0.0.1:" + port + lines[1]), timeout.Token);
            // Fixed observation cadence only. No permission selection or action on tree matches.
            Thread.Sleep(2000);
            Phase("OBSERVATION_BEGIN");
            File.WriteAllText(Path.Combine(output, "modal-connect-status.json"), JsonSerializer.Serialize(new { status = connect.Status.ToString(), allow = "NOT_ATTEMPTED" }));
            Screenshot(Path.Combine(output, "modal-before-tree.png"));
            ModalTree(job, inspectUri, output, TreeWalker.RawViewWalker, "raw");
            ModalTree(job, inspectUri, output, TreeWalker.ControlViewWalker, "control");
            Screenshot(Path.Combine(output, "modal-after-tree.png"));
            Phase("OBSERVATION_COMPLETE");
            return new { mode = "MODAL_STRUCTURE_ONLY", allow = "NOT_ATTEMPTED", cancel = "NOT_ATTEMPTED", protocol = "NOT_SENT", metadata = "NOT_TESTED", toggleOff = "NOT_ATTEMPTED", diagnosis = "REQUIRES_TREE_AND_PIXELS_NOT_PERMISSION" };
        } finally { socket.Abort(); }
        } catch (Exception ex) {
            try { File.WriteAllText(Path.Combine(output, "modal-failure.json"), JsonSerializer.Serialize(new { errorType = ex.GetType().FullName, allow = "NOT_ATTEMPTED", toggleOff = "NOT_CONFIRMED", retry = false })); } catch { }
            try { Screenshot(Path.Combine(output, "modal-failure.png")); } catch { }
            throw;
        }
    }
    internal static object RunStartupOnly(string browser, SafeFileHandle job, string output)
    {
        Directory.CreateDirectory(output);
        string uri = browser == "chrome" ? "chrome://inspect/#remote-debugging" : "edge://inspect/#remote-debugging";
        StartupObservation(job, uri, output, "00-startup-immediate");
        Thread.Sleep(2000);
        StartupObservation(job, uri, output, "01-startup-after-delay");
        return new { mode = "STARTUP_OBSERVATION_ONLY", consent = "NOT_ATTEMPTED", socket = "NOT_OPENED", metadata = "NOT_TESTED", diagnosis = "REQUIRES_PIXELS_AND_RECORDS", identityAuthorization = "NONE" };
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
