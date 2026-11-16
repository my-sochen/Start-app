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
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] input, int size);
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeyboardUnion Data; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct KeyboardUnion {
        [FieldOffset(0)] public ushort Key;
        [FieldOffset(4)] public uint Flags;
    }
    internal static object RunSetupOnly(string browser, string syntheticC, SafeFileHandle job, string output)
    {
        Directory.CreateDirectory(output);
        if (browser != "chrome") throw new InvalidOperationException("This reviewed setup candidate is Chrome only");
        string inspectUri = "chrome://inspect/#remote-debugging";
        StartupObservation(job, inspectUri, output, "00-before-setup");
        // Exact expected fixture representation measured in V11 on this build.
        // No normalization of observed values, no alternative/fuzzy match.
        string expectedCValue = new Uri(syntheticC).LocalPath.Replace('\\', '/');
        int sequence = 0;
        string lastStage = "NOT_STARTED";
        void Stage(string code) {
            File.WriteAllText(Path.Combine(output, "stage-" + (++sequence).ToString("D2") + ".json"),
                JsonSerializer.Serialize(new { stage = code, elapsedTicks = Stopwatch.GetTimestamp(), continuity = "UNPROVEN" }));
            lastStage = code;
        }
        try {
        Stage("WAIT_EXACT_C_PATH");
        var window = Await(() => {
            var matches = Windows(job).Where(w => Descendants(w).Any(e => e.Current.ClassName == "OmniboxViewViews" &&
                e.TryGetCurrentPattern(ValuePattern.Pattern, out object? p) && p is ValuePattern value && value.Current.Value == expectedCValue)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }, 10000);
        Stage("C_WINDOW_MATCHED");
        var boxes = Descendants(window).Where(e => e.Current.ClassName == "OmniboxViewViews" &&
            e.TryGetCurrentPattern(ValuePattern.Pattern, out object? p) && p is ValuePattern value && value.Current.Value == expectedCValue).ToArray();
        if (boxes.Length != 1 || !Member(window.Current.ProcessId, job) || !Member(boxes[0].Current.ProcessId, job) || window.Current.IsOffscreen || !window.Current.IsEnabled)
            throw new InvalidOperationException("Unique current job-owned synthetic C window required");
        nint hwnd = (nint)window.Current.NativeWindowHandle;
        Stage("FOREGROUND_REQUESTED");
        if (!SetForegroundWindow(hwnd) || GetForegroundWindow() != hwnd) throw new InvalidOperationException("Setup foreground refused");
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
        return new { mode = "SETUP_NAVIGATION_ONLY", replacedSyntheticC = true, consent = "NOT_ATTEMPTED", socket = "NOT_OPENED", metadata = "NOT_TESTED", identityAuthorization = "NONE", continuity = "UNPROVEN" };
        } catch (Exception ex) {
            try { File.WriteAllText(Path.Combine(output, "failure-stage.json"), JsonSerializer.Serialize(new { lastRecordedStage = lastStage, errorType = ex.GetType().FullName, code = ex is TimeoutException ? (lastStage == "WAIT_EXACT_C_PATH" ? "WAIT_C_TIMEOUT" : lastStage == "WAIT_EXACT_INSPECT" ? "WAIT_INSPECT_TIMEOUT" : "OTHER_TIMEOUT") : "SETUP_STOP" })); } catch { }
            // Never replace the original failure if best-effort observation itself fails.
            try { StartupObservation(job, inspectUri, output, "02-setup-failure"); } catch { }
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
