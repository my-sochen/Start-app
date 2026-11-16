using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

// Screenshot-only fresh owned Edge. Never Invoke/Allow/Cancel, never CDP or response/body reads.
internal static partial class Program
{
    private const string Inspect = "edge://inspect/#remote-debugging";
    private sealed record InspectControl(AutomationElement Element, ToggleState State);
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 2 || args[0].Length != 64 || !args[0].All(Uri.IsHexDigit) || !Path.IsPathFullyQualified(args[1]) ||
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" || Environment.GetEnvironmentVariable("ImageVersion") != "20261103.298.1") return 2;
        string output = args[1], root = Path.Combine(Path.GetTempPath(), "StartAppPendingShot-" + Guid.NewGuid().ToString("N"));
        var facts = new Dictionary<string, object?> { ["browser"] = "Edge", ["version"] = "156.0.4314.58", ["result"] = "INCONCLUSIVE", ["Allow"] = "NEVER_ATTEMPTED", ["protocol"] = "NEVER_SENT", ["metadata"] = "NEVER_REQUESTED", ["Off"] = "NOT_ATTEMPTED", ["cleanup"] = "UNVERIFIED" };
        using var job = Native.CreateJobObjectW(0, null);
        SafeProcessHandle? process = null; Socket? socket = null; FileStream? endpoint = null;
        uint pid = 0; long birth = 0; nint hwnd = 0; bool rootCreated = false, onAttempted = false, screenshot = false, cleanup = false;
        string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft/Edge/Application/msedge.exe");
        try
        {
            Require(!job.IsInvalid, "JOB_CREATE_STOP");
            var limits = new Native.ExtendedLimits(); limits.Basic.LimitFlags = 0x2000;
            Win(Native.SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Native.ExtendedLimits>()));
            using var image = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read);
            Require(Convert.ToHexString(SHA256.HashData(image)).Equals(args[0], StringComparison.OrdinalIgnoreCase) && FileVersionInfo.GetVersionInfo(exe).ProductVersion == "156.0.4314.58", "BINARY_CHANGED_STOP");
            Require(Directory.Exists(output) && !Directory.Exists(root), "DIRECTORY_STOP");
            RejectReparseChain(Path.GetDirectoryName(root)!); Directory.CreateDirectory(root); rootCreated = true; RejectReparseChain(root);
            (process, pid, birth) = Launch(exe, root, job);
            facts["pid"] = pid; facts["birthFileTime"] = birth;
            var ready = Stopwatch.StartNew();
            do
            {
                ValidateProcess(process, pid, birth, exe, job);
                var windows = OwnedTopWindows(pid); Require(windows.Count <= 1, "MULTIPLE_BROWSER_WINDOWS_STOP");
                if (windows.Count == 1) { hwnd = windows[0]; break; }
                Thread.Sleep(100);
            } while (ready.Elapsed < TimeSpan.FromSeconds(10));
            Require(hwnd != 0, "NO_OWNED_BROWSER_HWND");
            facts["setupStage"] = "WAIT_OWNED_BLANK_OMNIBOX";
            NavigateOwnedBlank(hwnd, process, pid, birth, exe, job, facts);
            facts["setupStage"] = "WAIT_EXACT_INSPECT_OFF";
            InspectControl? initial = null; ready.Restart();
            do
            {
                try { initial = ReadInspect(hwnd, process, pid, birth, exe, job, ToggleState.Off, facts); break; }
                catch (InvalidOperationException ex) when (ex.Message is "EXACT_INSPECT_ADDRESS_MISSING" or "CHECKBOX_MISSING") { facts["lastReadinessCode"] = ex.Message; Thread.Sleep(100); }
            } while (ready.Elapsed < TimeSpan.FromSeconds(5));
            Require(initial is not null, "INSPECT_NOT_READY_STOP");
            facts["setupStage"] = "EXACT_INSPECT_OFF_CONFIRMED";
            var control = initial!;
            Capture(hwnd, pid, Path.Combine(output, "01-edge-setting-off.png"));
            Require(ReadInspect(hwnd, process, pid, birth, exe, job, ToggleState.Off).State == ToggleState.Off, "BEFORE_ON_STOP");
            control = ReadInspect(hwnd, process, pid, birth, exe, job, ToggleState.Off);
            Require(control.Element.TryGetCurrentPattern(TogglePattern.Pattern, out object? pattern) && pattern is TogglePattern, "TOGGLE_PATTERN_STOP");
            var toggle = (TogglePattern)pattern!; Require(toggle.Current.ToggleState == ToggleState.Off, "TOGGLE_OFF_STOP");
            onAttempted = true; facts["On"] = "ONE_TOGGLE_REQUESTED"; toggle.Toggle();
            // Fresh FromHandle + ONLY exact scoped controls after state change. No stale global tree scan.
            ReadInspect(hwnd, process, pid, birth, exe, job, ToggleState.On);
            facts["On"] = "READBACK_ON";
            Capture(hwnd, pid, Path.Combine(output, "02-edge-setting-on.png"));
            string epPath = Path.Combine(root, "DevToolsActivePort");
            ready.Restart();
            while (!File.Exists(epPath) && ready.Elapsed < TimeSpan.FromSeconds(5)) Thread.Sleep(100);
            Require(File.Exists(epPath) && (File.GetAttributes(epPath) & FileAttributes.ReparsePoint) == 0, "ENDPOINT_MISSING_STOP");
            endpoint = new FileStream(epPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Binding binding = Bind(root, endpoint, process, pid, birth, exe, job);
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, binding.Port), deadline.Token).AsTask().GetAwaiter().GetResult();
                var local = (IPEndPoint)socket.LocalEndPoint!; var remote = (IPEndPoint)socket.RemoteEndPoint!;
                Require(local.Address.Equals(IPAddress.Loopback) && remote.Address.Equals(IPAddress.Loopback) && remote.Port == binding.Port && HasServerTuple(ReadTcp(), binding.Port, local.Port, pid), "SOCKET_OWNER_STOP");
                Require(binding == Bind(root, endpoint, process, pid, birth, exe, job), "PRE_REQUEST_BINDING_CHANGED");
                ReadInspect(hwnd, process, pid, birth, exe, job, ToggleState.On);
                byte[] request = Encoding.ASCII.GetBytes($"GET /devtools/browser/ HTTP/1.1\r\nHost: 127.0.0.1:{binding.Port}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))}\r\nSec-WebSocket-Version: 13\r\n\r\n");
                int sent = 0; facts["pendingRequests"] = 1;
                while (sent < request.Length) { int n = socket.SendAsync(request.AsMemory(sent), SocketFlags.None, deadline.Token).AsTask().GetAwaiter().GetResult(); Require(n > 0, "SEND_STOP"); sent += n; }
            }
            // No Receive call: no upgraded-protocol payload/metadata/body is read even if browser behaves unexpectedly.
            ready.Restart();
            do
            {
                ValidateProcess(process, pid, birth, exe, job); ValidateWindow(hwnd, pid);
                if (PendingPrompt(hwnd)) { Capture(hwnd, pid, Path.Combine(output, "03-edge-pending-native-dialog.png")); screenshot = true; facts["pendingDialog"] = "OBSERVED_NOT_ACCEPTED"; break; }
                Thread.Sleep(100);
            } while (ready.Elapsed < TimeSpan.FromSeconds(5));
            Require(screenshot, "PENDING_DIALOG_UNAVAILABLE");
        }
        catch (Exception ex)
        {
            facts["errorType"] = ex.GetType().Name; facts["errorCode"] = Code(ex);
            if (!onAttempted && hwnd != 0)
            {
                try { ValidateProcess(process!, pid, birth, exe, job); Capture(hwnd, pid, Path.Combine(output, "00-edge-readiness-failure.png")); facts["readinessFailureImage"] = "ACQUIRED_PENDING_PIXEL_INSPECTION"; }
                catch (Exception captureError) { facts["readinessFailureImageError"] = Code(captureError); }
            }
        }
        finally
        {
            socket?.Dispose(); endpoint?.Dispose();
            if (onAttempted && process is not null && hwnd != 0)
            {
                try
                {
                    // Never dismiss/accept a remaining modal. If it blocks settings, no blind input.
                    var dismissWait = Stopwatch.StartNew();
                    while (PendingPrompt(hwnd) && dismissWait.Elapsed < TimeSpan.FromSeconds(3)) Thread.Sleep(100);
                    Require(!PendingPrompt(hwnd), "PENDING_MODAL_REMAINS_NO_OFF_ACTION");
                    var off = ReadInspect(hwnd, process, pid, birth, exe, job, ToggleState.On);
                    Require(off.Element.TryGetCurrentPattern(TogglePattern.Pattern, out object? p) && p is TogglePattern, "OFF_PATTERN_STOP");
                    var toggle = (TogglePattern)p!; Require(toggle.Current.ToggleState == ToggleState.On, "OFF_CURRENT_STOP");
                    facts["Off"] = "ONE_TOGGLE_REQUESTED"; toggle.Toggle();
                    ReadInspect(hwnd, process, pid, birth, exe, job, ToggleState.Off);
                    Capture(hwnd, pid, Path.Combine(output, "04-edge-setting-off-after.png")); facts["Off"] = "READBACK_OFF";
                }
                catch (Exception ex) { facts["offError"] = Code(ex); }
            }
            string cleanupStage = "TERMINATE_OWNED_JOB";
            facts["dummyRootRemoved"] = !rootCreated;
            try
            {
                if (!job.IsInvalid)
                {
                    var cleanupClock = Stopwatch.StartNew();
                    Win(Native.TerminateJobObject(job, 0));
                    cleanupStage = "WAIT_OWNED_JOB_AND_ORIGINAL_PROCESS";
                    int decision;
                    do
                    {
                        uint? processWait = null;
                        if (process is not null)
                        {
                            uint observedWait = Native.WaitForSingleObject(process, 0);
                            // Capture immediately, before any other native call. Only meaningful on WAIT_FAILED.
                            if (observedWait == uint.MaxValue) facts["processWaitError"] = Marshal.GetLastWin32Error();
                            processWait = observedWait; facts["processWaitRaw"] = observedWait;
                        }
                        Win(Native.QueryInformationJobObject(job, 1, out var accounting, (uint)Marshal.SizeOf<Native.BasicAccounting>(), out _));
                        long elapsed = cleanupClock.ElapsedMilliseconds;
                        facts["activeAfterCleanup"] = accounting.ActiveProcesses;
                        facts["cleanupElapsedMs"] = elapsed;
                        facts["originalProcessHandlePresent"] = process is not null;
                        decision = CleanupDecision(accounting.ActiveProcesses, processWait, process is not null, elapsed);
                        if (decision != 0) break;
                        Thread.Sleep((int)Math.Min(50, Math.Max(1, 5000 - elapsed)));
                    } while (true);
                    cleanup = decision == 1;
                    if (!cleanup) facts["cleanupError"] = decision == 2 ? "PROCESS_WAIT_FAILED_OR_UNEXPECTED" : "CLEANUP_WAIT_BUDGET_EXHAUSTED";
                }
                if (facts.TryGetValue("errorCode", out var launchFailure) && Equals(launchFailure, "CREATED_ROOT_CLEANUP_UNVERIFIED")) cleanup = false;
                // Never delete the dummy root until BOTH observations prove termination.
                if (cleanup && rootCreated)
                {
                    cleanupStage = "DELETE_OWNED_ROOT";
                    RejectReparseChain(root); DeleteOwnedTree(root);
                    facts["dummyRootRemoved"] = !Directory.Exists(root);
                    cleanup &= !Directory.Exists(root);
                }
                if (!cleanup && !facts.ContainsKey("cleanupError")) facts["cleanupError"] = "OWNED_CLEANUP_UNVERIFIED";
            }
            catch { cleanup = false; facts["cleanupError"] = "OWNED_CLEANUP_STAGE_FAILED"; }
            facts["cleanupStage"] = cleanupStage;
            process?.Dispose(); facts["cleanup"] = cleanup ? "OWNED_JOB_EMPTY_ROOT_REMOVED" : "UNVERIFIED";
            facts["result"] = screenshot && cleanup && Equals(facts["Off"], "READBACK_OFF") ? "IMAGES_ACQUIRED_PENDING_PIXEL_INSPECTION" : "INCOMPLETE_IMAGES_MAY_EXIST";
            try { using var f = new FileStream(Path.Combine(output, "result.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None); JsonSerializer.Serialize(f, facts, new JsonSerializerOptions { WriteIndented = true }); } catch { returnCode = 3; }
        }
        return returnCode == 0 && screenshot && cleanup && Equals(facts["Off"], "READBACK_OFF") ? 0 : 3;
    }
    // Pure state only. 0=pending, 1=both proven, 2=terminal wait failure/unexpected, 3=budget exhausted.
    private static int CleanupDecision(uint active, uint? processWait, bool hasProcess, long elapsedMs)
    {
        if (hasProcess && processWait != 0 && processWait != 258) return 2;
        if (elapsedMs > 5000) return 3;
        if (active == 0 && (!hasProcess || processWait == 0)) return 1;
        return elapsedMs >= 5000 ? 3 : 0;
    }
    private static int returnCode;
    private static string Code(Exception ex) => ex is InvalidOperationException && ex.Message.Length <= 80 && ex.Message.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_') ? ex.Message : ex.GetType().Name;
    private static void ValidateWindow(nint hwnd, uint pid)
    {
        Require(Native.GetWindowThreadProcessId(hwnd, out uint current) != 0 && current == pid && Shot.IsWindow(hwnd), "HWND_OWNER_CHANGED");
    }
    private static List<nint> OwnedTopWindows(uint pid)
    {
        var result = new List<nint>(); bool overflow = false;
        Shot.EnumWindows((h, _) => { if (Native.GetWindowThreadProcessId(h, out uint p) != 0 && p == pid && Shot.IsWindowVisible(h) && Shot.GetWindow(h, 4) == 0) { result.Add(h); if (result.Count > 2) { overflow = true; return false; } } return true; }, 0);
        Require(!overflow, "TOP_WINDOW_CAP"); return result;
    }
    private static AutomationElementCollection Scoped(nint hwnd, System.Windows.Automation.Condition condition)
    {
        // Reacquire native-HWND root on EVERY operation; never walk RootElement/global desktop.
        var cache = new CacheRequest { TreeScope = TreeScope.Element };
        foreach (var prop in new[] { AutomationElement.NameProperty, AutomationElement.AutomationIdProperty, AutomationElement.IsEnabledProperty, AutomationElement.IsOffscreenProperty, AutomationElement.ControlTypeProperty }) cache.Add(prop);
        using (cache.Activate()) return AutomationElement.FromHandle(hwnd).FindAll(TreeScope.Descendants, condition);
    }
    private static void NavigateOwnedBlank(nint hwnd, SafeProcessHandle process, uint pid, long birth, string exe, SafeFileHandle job, Dictionary<string, object?> facts)
    {
        AutomationElement BlankBox()
        {
            ValidateProcess(process, pid, birth, exe, job); ValidateWindow(hwnd, pid);
            var edits = Scoped(hwnd, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            Require(edits.Count <= 8, "BLANK_EDIT_CAP_STOP"); facts["blankEditCount"] = edits.Count;
            var boxes = edits.Cast<AutomationElement>().Where(e => e.Current.ClassName == "OmniboxViewViews" &&
                e.TryGetCurrentPattern(ValuePattern.Pattern, out object? p) && p is ValuePattern v && v.Current.Value == "about:blank").ToArray();
            facts["exactBlankOmniboxCount"] = boxes.Length;
            Require(boxes.Length <= 1, "BLANK_OMNIBOX_AMBIGUOUS_STOP"); Require(boxes.Length == 1, "BLANK_OMNIBOX_MISSING");
            var box = boxes[0]; Require(box.Current.ProcessId == pid && box.Current.IsEnabled && !box.Current.IsOffscreen, "BLANK_OMNIBOX_OWNER_OR_STATE_STOP"); return box;
        }
        AutomationElement? selected = null; var ready = Stopwatch.StartNew();
        do { try { selected = BlankBox(); break; } catch (InvalidOperationException ex) when (ex.Message == "BLANK_OMNIBOX_MISSING") { facts["lastReadinessCode"] = ex.Message; Thread.Sleep(100); } }
        while (ready.ElapsedMilliseconds < 5000);
        Require(selected is not null, "BLANK_OMNIBOX_NOT_READY_STOP");
        facts["setupStage"] = "ONE_OWNED_ACTIVATION";
        Require(Shot.SetForegroundWindow(hwnd), "NAV_ACTIVATION_FAILED");
        ready.Restart();
        while (Shot.GetForegroundWindow() != hwnd && ready.ElapsedMilliseconds < 2000) { BlankBox(); Thread.Sleep(50); }
        Require(Shot.GetForegroundWindow() == hwnd && Automation.Compare(selected, BlankBox()), "NAV_FOREGROUND_OR_IDENTITY_STOP");
        facts["setupStage"] = "ONE_OWNED_OMNIBOX_FOCUS"; selected!.SetFocus();
        Require(Shot.GetForegroundWindow() == hwnd && Automation.Compare(AutomationElement.FocusedElement, selected) && Automation.Compare(selected, BlankBox()), "NAV_FOCUS_OR_IDENTITY_STOP");
        Require(selected.TryGetCurrentPattern(ValuePattern.Pattern, out object? pattern) && pattern is ValuePattern, "NAV_VALUE_PATTERN_STOP");
        var value = (ValuePattern)pattern!; Require(!value.Current.IsReadOnly && value.Current.Value == "about:blank", "NAV_VALUE_STOP");
        ValidateProcess(process, pid, birth, exe, job); ValidateWindow(hwnd, pid);
        facts["setupStage"] = "ONE_EXACT_INSPECT_SETVALUE"; value.SetValue(Inspect);
        Require(value.Current.Value == Inspect && Shot.GetForegroundWindow() == hwnd && Automation.Compare(AutomationElement.FocusedElement, selected), "NAV_BEFORE_ENTER_STOP");
        ValidateProcess(process, pid, birth, exe, job); ValidateWindow(hwnd, pid);
        Require(IntPtr.Size == 8 && Marshal.SizeOf<Shot.Input>() == 40 && Marshal.OffsetOf<Shot.Input>(nameof(Shot.Input.Data)).ToInt32() == 8 && Marshal.OffsetOf<Shot.KeyboardUnion>(nameof(Shot.KeyboardUnion.Flags)).ToInt32() == 4, "INPUT_ABI_STOP");
        var keys = new[] { new Shot.Input { Type = 1, Data = new Shot.KeyboardUnion { Key = 13 } }, new Shot.Input { Type = 1, Data = new Shot.KeyboardUnion { Key = 13, Flags = 2 } } };
        facts["setupStage"] = "ONE_EXACT_INSPECT_ENTER_PAIR"; uint sent = Shot.SendInput(2, keys, Marshal.SizeOf<Shot.Input>()); facts["navigationInputCount"] = sent;
        Require(sent == 2, "PARTIAL_INPUT_STOP_NO_RETRY");
    }
    private static InspectControl ReadInspect(nint hwnd, SafeProcessHandle process, uint pid, long birth, string exe, SafeFileHandle job, ToggleState expected, Dictionary<string, object?>? readiness = null)
    {
        ValidateProcess(process, pid, birth, exe, job); ValidateWindow(hwnd, pid);
        var edits = Scoped(hwnd, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        Require(edits.Count <= 8, "EDIT_COUNT_STOP"); int exact = 0;
        if (readiness is not null) readiness["inspectEditCount"] = edits.Count;
        foreach (AutomationElement e in edits)
            if (e.Current.ClassName == "OmniboxViewViews" && e.TryGetCurrentPattern(ValuePattern.Pattern, out object? p) && p is ValuePattern v && v.Current.Value == Inspect) exact++;
        if (readiness is not null) readiness["exactInspectOmniboxCount"] = exact;
        Require(exact <= 1, "AMBIGUOUS_INSPECT_ADDRESS_STOP");
        Require(exact == 1, "EXACT_INSPECT_ADDRESS_MISSING");
        var checks = Scoped(hwnd, new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox),
            new PropertyCondition(AutomationElement.NameProperty, "Allow remote debugging for this browser instance"),
            new PropertyCondition(AutomationElement.AutomationIdProperty, "remote-debugging-enabled")));
        if (readiness is not null) readiness["inspectCheckboxCount"] = checks.Count;
        Require(checks.Count <= 1, "CHECKBOX_AMBIGUOUS_STOP");
        Require(checks.Count == 1, "CHECKBOX_MISSING"); var box = checks[0];
        object? obj = null;
        Require(box.Cached.IsEnabled && !box.Cached.IsOffscreen && box.TryGetCurrentPattern(TogglePattern.Pattern, out obj) && obj is TogglePattern, "CHECKBOX_NOT_ACTIONABLE");
        var state = ((TogglePattern)obj!).Current.ToggleState;
        if (readiness is not null) { readiness["inspectCheckboxOff"] = state == ToggleState.Off; readiness["inspectCheckboxOn"] = state == ToggleState.On; }
        Require(state == expected, "CHECKBOX_STATE_MISMATCH");
        ValidateWindow(hwnd, pid); return new InspectControl(box, state);
    }
    private static bool BelongsToRoot(nint candidate, nint root, uint pid)
    {
        for (int i = 0; candidate != 0 && i < 8; i++)
        {
            if (Native.GetWindowThreadProcessId(candidate, out uint p) == 0 || p != pid) return false;
            if (candidate == root) return true;
            candidate = Shot.GetWindow(candidate, 4);
        }
        return false;
    }
    private static bool PendingPrompt(nint hwnd)
    {
        Require(Native.GetWindowThreadProcessId(hwnd, out uint pid) != 0, "PROMPT_ROOT_OWNER_STOP");
        var candidates = new List<nint> { hwnd }; bool capped = false;
        Shot.EnumWindows((h, _) => { if (h != hwnd && Shot.IsWindowVisible(h) && BelongsToRoot(h, hwnd, pid)) { candidates.Add(h); if (candidates.Count > 4) { capped = true; return false; } } return true; }, 0);
        Require(!capped, "OWNED_MODAL_WINDOW_CAP");
        int matches = 0;
        foreach (nint candidate in candidates)
        {
            var titles = Scoped(candidate, new PropertyCondition(AutomationElement.NameProperty, "Allow remote debugging?"));
            var modalRoot = AutomationElement.FromHandle(candidate);
            bool rootTitle = modalRoot.Current.Name == "Allow remote debugging?";
            Require(titles.Count <= 4, "PROMPT_TITLE_CAP");
            var allow = Scoped(candidate, new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button), new PropertyCondition(AutomationElement.NameProperty, "Allow")));
            var cancel = Scoped(candidate, new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button), new PropertyCondition(AutomationElement.NameProperty, "Cancel")));
            if ((rootTitle || titles.Count > 0) && allow.Count == 1 && cancel.Count == 1) matches++;
        }
        Require(matches <= 1, "AMBIGUOUS_OWNED_PROMPT"); return matches == 1;
    }
    private static void Capture(nint hwnd, uint pid, string path)
    {
        ValidateWindow(hwnd, pid);
        if (!BelongsToRoot(Shot.GetForegroundWindow(), hwnd, pid)) Require(Shot.SetForegroundWindow(hwnd), "FOREGROUND_REQUEST_FAILED");
        nint foreground = Shot.GetForegroundWindow();
        Require(BelongsToRoot(foreground, hwnd, pid), "FOREGROUND_NOT_OWNED");
        Win(Shot.GetWindowRect(hwnd, out var rect)); int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        Require(width is >= 400 and <= 4096 && height is >= 300 and <= 2160 && rect.Left >= 0 && rect.Top >= 0, "CAPTURE_BOUNDS_STOP");
        Win(Shot.GetWindowRect(foreground, out var frontRect));
        Require(frontRect.Left >= rect.Left && frontRect.Top >= rect.Top && frontRect.Right <= rect.Right && frontRect.Bottom <= rect.Bottom, "OWNED_MODAL_OUTSIDE_CAPTURE_STOP");
        nint screen = Shot.GetDC(0), memory = 0, bitmap = 0, old = 0;
        try
        {
            Require(screen != 0, "SCREEN_DC_STOP"); memory = Shot.CreateCompatibleDC(screen); bitmap = Shot.CreateCompatibleBitmap(screen, width, height);
            Require(memory != 0 && bitmap != 0, "BITMAP_STOP"); old = Shot.SelectObject(memory, bitmap);
            Win(Shot.BitBlt(memory, 0, 0, width, height, screen, rect.Left, rect.Top, 0x00CC0020));
            Require(Shot.GetForegroundWindow() == foreground && BelongsToRoot(foreground, hwnd, pid), "CAPTURE_FOREGROUND_CHANGED"); ValidateWindow(hwnd, pid);
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
            using var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); encoder.Save(f); Require(f.Length <= 4 * 1024 * 1024, "PNG_CAP_STOP");
        }
        finally { if (old != 0 && memory != 0) Shot.SelectObject(memory, old); if (bitmap != 0) Shot.DeleteObject(bitmap); if (memory != 0) Shot.DeleteDC(memory); if (screen != 0) Shot.ReleaseDC(0, screen); }
    }
}
internal static class Shot
{
    [DllImport("user32.dll")] internal static extern uint SendInput(uint count, Input[] input, int size);
    [StructLayout(LayoutKind.Explicit, Size = 40)] internal struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeyboardUnion Data; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] internal struct KeyboardUnion { [FieldOffset(0)] public ushort Key; [FieldOffset(4)] public uint Flags; }

    internal delegate bool EnumProc(nint hwnd, nint arg);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, nint arg);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint hwnd, uint cmd);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] internal static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll", SetLastError=true)] internal static extern bool BitBlt(nint dest, int x, int y, int width, int height, nint source, int sx, int sy, uint operation);
}
