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
    internal static object Run(string browser, string profile, string[] urls, SafeFileHandle job, string output)
    {
        Directory.CreateDirectory(output);
        string inspectUri = browser == "chrome" ? "chrome://inspect/#remote-debugging" : "edge://inspect/#remote-debugging";
        // Literal URI must match actual ValuePattern. No normalization/focus/navigation fallback.
        Await(() => { try { return Inspect(job, inspectUri); } catch (InvalidOperationException) { return null; } }, 10000);
        var off = Toggle(job, inspectUri);
        if (off.Current.ToggleState != ToggleState.Off) throw new InvalidOperationException("Fresh synthetic opt-in was not Off");
        Screenshot(Path.Combine(output, "01-before-optin.png"));
        // This is a full-debugging opt-in for this disposable synthetic session only.
        off.Toggle(); // exactly once. An exception may mean effect happened; never retry.
        Await(() => Toggle(job, inspectUri).Current.ToggleState == ToggleState.On ? new object() : null, 5000);
        Screenshot(Path.Combine(output, "02-optin-on.png"));
        string endpointFile = Path.Combine(profile, "DevToolsActivePort");
        Await(() => File.Exists(endpointFile) ? new object() : null, 5000);
        string[] lines = File.ReadAllLines(endpointFile);
        if (lines.Length != 2 || !int.TryParse(lines[0], out int port) || port < 1 || port > 65535 ||
            !lines[1].StartsWith("/devtools/browser/", StringComparison.Ordinal) || lines[1].Any(c => c <= 32 || c == '?' || c == '#'))
            throw new InvalidDataException("Unexpected endpoint file");
        // Loopback TCP owner must currently belong to this explicit job. PID/port equality is endpoint-only.
        TcpOwner.RequireJob(port, job);
        string before = EndpointSnapshot(job);
        Screenshot(Path.Combine(output, "03-before-connect.png"));
        using var socket = new ClientWebSocket();
        using var timeout = new CancellationTokenSource(15000);
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
            invoked = true; invoke.Invoke(); // exactly once, never retry after exception.
            connect.GetAwaiter().GetResult();
            byte[] request = Encoding.UTF8.GetBytes("{\"id\":1,\"method\":\"Target.getTargets\"}");
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
            bool exact = urls.All(url => targets.Count(t => t.GetProperty("url").GetString() == url) == 1);
            var matches = targets.Where(t => t.GetProperty("url").GetString() is string targetUrl && urls.Contains(targetUrl)).Select(t => new {
                title = t.GetProperty("title").GetString(), url = t.GetProperty("url").GetString() }).ToArray();
            // Never save arbitrary target payload or unknown titles/URLs.
            socket.Abort();
            string after = EndpointSnapshot(job);
            Screenshot(Path.Combine(output, "05-after-close.png"));
            bool endpointEqual = before == after;
            File.WriteAllText(Path.Combine(output, "metadata.json"), JsonSerializer.Serialize(new {
                browser, singleAllowAttempted = invoked, requestCount = 1, method = "Target.getTargets", exactSyntheticMatches = exact,
                matches, endpointEqual, continuity = "UNPROVEN", allProfiles = "NOT_TESTED", normalDefaultSession = "NOT_TESTED"
            }, new JsonSerializerOptions { WriteIndented = true }));
            if (!exact || !endpointEqual) throw new InvalidOperationException("Synthetic metadata/endpoint comparison inconclusive");
            var toggle = Toggle(job, inspectUri);
            if (toggle.Current.ToggleState != ToggleState.On) throw new InvalidOperationException("Unexpected opt-in state before cleanup");
            toggle.Toggle(); // one explicit Off cleanup, no revocation/reconnect claim.
            Await(() => Toggle(job, inspectUri).Current.ToggleState == ToggleState.Off ? new object() : null, 3000);
            return new { exactSyntheticMatches = exact, endpointEqual, continuity = "UNPROVEN", fullDebuggingGrant = "synthetic-only", cleanupToggle = "OFF-readback", revocation = "NOT_TESTED" };
        }
        catch { socket.Abort(); File.WriteAllText(Path.Combine(output, "incomplete.json"), JsonSerializer.Serialize(new { status = "INCONCLUSIVE", singleAllowAttempted = invoked, toggleOffCleanup = "NOT_CONFIRMED_ON_FAILURE", retry = false })); throw; }
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
