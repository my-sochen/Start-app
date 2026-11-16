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
    internal static object RunFull(string browser, string profile, string[] urls, SafeFileHandle job, string output)
    {
        Directory.CreateDirectory(output);
        int fullSequence = 0;
        void FullStage(string code) => File.WriteAllText(Path.Combine(output, "full-stage-" + (++fullSequence).ToString("D2") + ".json"), JsonSerializer.Serialize(new { stage = code, ticks = Stopwatch.GetTimestamp(), continuity = "UNPROVEN" }));
        try {
        FullStage("FULL_PATH_BEGIN");
        string inspectUri = browser == "chrome" ? "chrome://inspect/#remote-debugging" : "edge://inspect/#remote-debugging";
        // Literal URI must match actual ValuePattern. No normalization/focus/navigation fallback.
        Await(() => { try { return Inspect(job, inspectUri); } catch (InvalidOperationException) { return null; } }, 10000);
        var off = Toggle(job, inspectUri);
        if (off.Current.ToggleState != ToggleState.Off) throw new InvalidOperationException("Fresh synthetic opt-in was not Off");
        Screenshot(Path.Combine(output, "01-before-optin.png"));
        // This is a full-debugging opt-in for this disposable synthetic session only.
        FullStage("OPTIN_ON_REQUESTED");
        off.Toggle(); // exactly once. An exception may mean effect happened; never retry.
        Await(() => Toggle(job, inspectUri).Current.ToggleState == ToggleState.On ? new object() : null, 5000);
        FullStage("OPTIN_ON_READBACK");
        Screenshot(Path.Combine(output, "02-optin-on.png"));
        string endpointFile = Path.Combine(profile, "DevToolsActivePort");
        Await(() => File.Exists(endpointFile) ? new object() : null, 5000);
        string[] lines = File.ReadAllLines(endpointFile);
        if (lines.Length != 2 || !int.TryParse(lines[0], out int port) || port < 1 || port > 65535 ||
            !lines[1].StartsWith("/devtools/browser/", StringComparison.Ordinal) || lines[1].Any(c => c <= 32 || c == '?' || c == '#'))
            throw new InvalidDataException("Unexpected endpoint file");
        // Loopback TCP owner must currently belong to this explicit job. PID/port equality is endpoint-only.
        TcpOwner.RequireJob(port, job);
        string before = ""; // Capture bracket begins only after explicit consent has completed.
        Screenshot(Path.Combine(output, "03-before-connect.png"));
        using var socket = new ClientWebSocket();
        using var timeout = new CancellationTokenSource(15000);
        FullStage("SOCKET_CONNECT_REQUESTED");
        var connect = socket.ConnectAsync(new Uri("ws://127.0.0.1:" + port + lines[1]), timeout.Token);
        bool invoked = false;
        try
        {
            var prompt = Await(() => {
                if (connect.IsCompleted) throw new InvalidOperationException("Connection completed without the expected new consent prompt");
                var matches = Windows(job).Where(w => w.Current.Name == "Allow remote debugging?" && w.Current.IsEnabled && !w.Current.IsOffscreen).ToArray();
                return matches.Length == 1 ? matches[0] : null;
            }, 6000);
            Screenshot(Path.Combine(output, "04-full-debugging-consent.png"));
            var buttons = Descendants(prompt).Where(e => e.Current.ControlType == ControlType.Button && e.Current.Name == "Allow" &&
                e.Current.IsEnabled && !e.Current.IsOffscreen && Member(e.Current.ProcessId, job)).ToArray();
            if (buttons.Length != 1 || !Member(prompt.Current.ProcessId, job)) throw new InvalidOperationException("Unique job-bound Allow unavailable");
            TcpOwner.RequireJob(port, job);
            // NO claim of HWND continuity or complete UIA-provider authentication.
            // This effect requires separately reviewed clean-VM synthetic-test authority.
            if (buttons[0].GetCurrentPattern(InvokePattern.Pattern) is not InvokePattern invoke) throw new InvalidOperationException("Invoke pattern unavailable");
            FullStage("SINGLE_ALLOW_REQUESTED");
            invoked = true; invoke.Invoke(); // exactly once, never retry after exception.
            connect.GetAwaiter().GetResult();
            FullStage("CONNECTED_AFTER_SINGLE_ALLOW");
            TcpOwner.RequireJob(port, job);
            before = EndpointSnapshot(job);
            nint captureForeground = GetForegroundWindow();
            FullStage("CAPTURE_BRACKET_BEGIN_NO_UI_ACTIONS");
            byte[] request = Encoding.UTF8.GetBytes("{\"id\":1,\"method\":\"Target.getTargets\"}");
            FullStage("TARGET_GETTARGETS_ONCE_REQUESTED");
            socket.SendAsync(new ArraySegment<byte>(request), WebSocketMessageType.Text, true, timeout.Token).GetAwaiter().GetResult();
            using var message = new MemoryStream(); var buffer = new byte[8192];
            WebSocketReceiveResult part;
            do {
                part = socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token).GetAwaiter().GetResult();
                if (part.MessageType != WebSocketMessageType.Text || message.Length + part.Count > 262144) throw new InvalidDataException("Unexpected/oversized protocol message");
                message.Write(buffer, 0, part.Count);
            } while (!part.EndOfMessage);
            using var response = JsonDocument.Parse(message.ToArray());
            if (!response.RootElement.TryGetProperty("id", out var id) || id.GetInt32() != 1 || response.RootElement.TryGetProperty("error", out _))
                throw new InvalidDataException("Unexpected protocol response");
            var targets = response.RootElement.GetProperty("result").GetProperty("targetInfos").EnumerateArray().Where(t => t.GetProperty("type").GetString() == "page").ToArray();
            bool exact = urls.Select((url, i) => new { url, title = "StartAppSynthetic-" + (char)('A' + i) })
                .All(expected => targets.Count(t => t.GetProperty("url").GetString() == expected.url && t.GetProperty("title").GetString() == expected.title) == 1);
            int unknownPageCount = targets.Count(t => !urls.Contains(t.GetProperty("url").GetString()));
            // Return only fixed expected fixture values, never untrusted target titles/URLs.
            var matches = urls.Select((url, i) => new { title = "StartAppSynthetic-" + (char)('A' + i), url,
                exactCount = targets.Count(t => t.GetProperty("url").GetString() == url && t.GetProperty("title").GetString() == "StartAppSynthetic-" + (char)('A' + i)) }).ToArray();
            // Never save arbitrary target payload or unknown titles/URLs.
            socket.Abort();
            string after = EndpointSnapshot(job);
            Screenshot(Path.Combine(output, "05-after-close.png"));
            bool endpointEqual = before == after;
            bool foregroundEqual = captureForeground == GetForegroundWindow();
            FullStage("CAPTURE_BRACKET_END_NO_UI_ACTIONS");
            File.WriteAllText(Path.Combine(output, "metadata.json"), JsonSerializer.Serialize(new {
                browser, singleAllowAttempted = invoked, requestCount = 1, method = "Target.getTargets", exactSyntheticMatches = exact,
                matches, unknownPageCount, coverage = "KNOWN_A_B_C_FIXTURES_ONLY_NOT_COMPLETENESS", endpointEqual, foregroundEqual, continuity = "UNPROVEN", allProfiles = "NOT_TESTED", normalDefaultSession = "NOT_TESTED"
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (!exact || !endpointEqual || !foregroundEqual) throw new InvalidOperationException("Synthetic metadata/endpoint comparison inconclusive");
            var toggle = Toggle(job, inspectUri);
            if (toggle.Current.ToggleState != ToggleState.On) throw new InvalidOperationException("Unexpected opt-in state before cleanup");
            FullStage("OPTIN_OFF_REQUESTED");
            toggle.Toggle(); // one explicit Off cleanup, no revocation/reconnect claim.
            Await(() => Toggle(job, inspectUri).Current.ToggleState == ToggleState.Off ? new object() : null, 3000);
            FullStage("OPTIN_OFF_READBACK");
            Screenshot(Path.Combine(output, "06-optin-off.png"));
            return new { knownFixtureCoverage = exact, unknownPageCount, endpointEqual, foregroundEqual, continuity = "UNPROVEN", fullDebuggingGrant = "synthetic-only", cleanupToggle = "OFF-readback", revocation = "NOT_TESTED" };
        }
        catch { socket.Abort(); try { StartupObservation(job, inspectUri, output, "full-failure"); } catch { } File.WriteAllText(Path.Combine(output, "incomplete.json"), JsonSerializer.Serialize(new { status = "INCONCLUSIVE", singleAllowAttempted = invoked, toggleOffCleanup = "NOT_CONFIRMED_ON_FAILURE", retry = false })); throw; }
        } catch (Exception ex) {
            try { File.WriteAllText(Path.Combine(output, "full-failure-type.json"), JsonSerializer.Serialize(new { errorType = ex.GetType().FullName, completedStageFiles = fullSequence, result = "INCONCLUSIVE", retry = false })); } catch { }
            try { StartupObservation(job, "chrome://inspect/#remote-debugging", output, "full-path-failure"); } catch { }
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
