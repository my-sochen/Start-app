using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

// RAW OBSERVATIONS ONLY. Never authorizes input or a debugging connection.
// No process launch, termination, UI input, titles, URLs or command-line collection.
internal static class Program
{
    private const uint QueryLimited = 0x1000, Synchronize = 0x100000;
    private const int MaxProcesses = 256, MaxWindows = 128, MaxOwnerDepth = 8;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly List<object> Records = new();
    private static readonly List<(int Pid, long Birth, SafeProcessHandle Handle)> Retained = new();
    private static string Expected = "";
    private static string BaseName = "";
    private static bool Incomplete;

    private static void Emit(string kind, object data) => Records.Add(new { kind, monotonicTicks = Clock.ElapsedTicks, data });
    private static void Error(string stage, int? pid, int code)
    { Incomplete = true; Emit("error", new { stage, pid, win32 = code }); }

    private static int Main(string[] args)
    {
        if (args.Length != 2 || !OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(args[0])) return 2;
        Expected = Path.GetFullPath(args[0]); BaseName = Path.GetFileNameWithoutExtension(Expected);
        #if FIXTURE_ONLY
        if (!StringComparer.Ordinal.Equals(BaseName, "RawObserverFixture")) return 2;
#else
        if (!new[] { "chrome", "msedge" }.Contains(BaseName, StringComparer.OrdinalIgnoreCase)) return 2;
#endif
        string output = Path.GetFullPath(args[1]);
        if (File.Exists(output) || !File.Exists(Expected)) return 2;
        int result = 2;
        try
        {
            Emit("scope", new { purpose = "observation-only", expectedImage = Expected,
                authorization = "NONE", ownership = "NOT_ESTABLISHED", windowContinuity = "UNPROVEN",
                uiaDependencies = "NOT_COLLECTED", processHistory = "NOT_COMPLETE", Stopwatch.Frequency });
            Sample(0);
            if (Clock.ElapsedMilliseconds < 8000) { Thread.Sleep(2000); Sample(1); }
            else Error("deadline-before-second-sample", null, 0);
            foreach (var item in Retained)
            {
                uint wait = Native.WaitForSingleObject(item.Handle, 0);
                Emit("retained-process-end", new { item.Pid, item.Birth, waitResult = wait,
                    meaning = wait == 0 ? "original-object-signaled-exit" : wait == 258 ? "original-object-not-signaled" : "query-error" });
                if (wait != 0 && wait != 258) Error("wait", item.Pid, Marshal.GetLastWin32Error());
            }
            Emit("end", new { elapsedMs = Clock.ElapsedMilliseconds, incomplete = Incomplete,
                observationOnly = true, browserCapture = "NOT_TESTED", effectAuthorization = "NONE" });
            // Exit 0 means JSON collection completed, never identity/capture/safety PASS.
            result = Incomplete ? 3 : 0;
        }
        catch (Exception ex) { Emit("collector-exception", new { type = ex.GetType().FullName }); result = 3; }
        finally
        {
            foreach (var item in Retained) item.Handle.Dispose();
            try
            {
                using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(stream, Records, new JsonSerializerOptions { WriteIndented = true });
            }
            catch { result = 4; }
        }
        return result;
    }

    private static void Sample(int sample)
    {
        long start = Clock.ElapsedTicks;
        Process[] processes;
        try { processes = Process.GetProcessesByName(BaseName); }
        catch (Exception ex) { Incomplete = true; Emit("enumeration-error", new { sample, type = ex.GetType().FullName }); return; }
        var pids = new HashSet<uint>();
        try
        {
            Emit("enumeration-return", new { sample, count = processes.Length, atomicHistory = false });
            if (processes.Length > MaxProcesses) { Error("process-cap", null, 0); return; }
            foreach (var process in processes)
            {
                if (Clock.ElapsedMilliseconds >= 10000) { Error("collection-deadline", null, 0); break; }
                int pid;
                try { pid = process.Id; }
                catch { Error("enumerated-pid-unavailable", null, 0); continue; }
                long openStart = Clock.ElapsedTicks;
                var handle = Native.OpenProcess(QueryLimited | Synchronize, false, (uint)pid);
                if (handle.IsInvalid) { Error("open-process", pid, Marshal.GetLastWin32Error()); handle.Dispose(); continue; }
                var image = new StringBuilder(32768); uint size = (uint)image.Capacity;
                if (!Native.QueryFullProcessImageName(handle, 0, image, ref size))
                { Error("process-image", pid, Marshal.GetLastWin32Error()); handle.Dispose(); continue; }
                if (!Native.GetProcessTimes(handle, out long birth, out long exit, out _, out _))
                { Error("process-times", pid, Marshal.GetLastWin32Error()); handle.Dispose(); continue; }
                // A path match is a raw comparison, not ownership or file identity proof.
                bool expectedPath = StringComparer.OrdinalIgnoreCase.Equals(image.ToString(), Expected);
                Retained.Add((pid, birth, handle));
                Emit("process", new { sample, pid, birthFileTime = birth, exitFileTime = exit,
                    image = image.ToString(), expectedPath, handleToken = Retained.Count,
                    openStart, openEnd = Clock.ElapsedTicks, ownership = "UNRESOLVED", parentMembership = "NOT_COLLECTED" });
                if (!expectedPath) { Error("unexpected-image-path", pid, 0); continue; }
                pids.Add((uint)pid);
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        int windows = 0;
        Native.EnumWindowsProc callback = (hwnd, _) =>
        {
            if (Clock.ElapsedMilliseconds >= 10000) { Error("window-deadline", null, 0); return false; }
            uint thread = Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (!pids.Contains(pid)) return true;
            if (++windows > MaxWindows) { Error("window-cap", null, 0); return false; }
            var chain = new List<object>(); var seen = new HashSet<nint>(); nint current = hwnd;
            for (int depth = 0; current != 0 && depth < MaxOwnerDepth; depth++)
            {
                if (!seen.Add(current)) { Error("owner-cycle", (int)pid, 0); break; }
                uint tid = Native.GetWindowThreadProcessId(current, out uint ownerPid);
                nint owner = Native.GetWindow(current, 4); // GW_OWNER, no input or mutation.
                chain.Add(new { hwnd = current.ToInt64(), pid = ownerPid, thread = tid, owner = owner.ToInt64(),
                    processObservedInThisSample = pids.Contains(ownerPid) });
                if (tid == 0 || !pids.Contains(ownerPid)) Incomplete = true;
                current = owner;
            }
            if (current != 0) Error("owner-depth-or-cycle", (int)pid, 0);
            bool rectOk = Native.GetWindowRect(hwnd, out Native.Rect rect);
            Emit("window-endpoint", new { sample, hwnd = hwnd.ToInt64(), pid, thread,
                visible = Native.IsWindowVisible(hwnd), iconic = Native.IsIconic(hwnd),
                rectOk, bounds = new[] { rect.Left, rect.Top, rect.Right, rect.Bottom }, chain,
                continuity = "UNPROVEN", dependencyCompleteness = "UNPROVEN_NO_UIA" });
            return true;
        };
        bool enumOk = Native.EnumWindows(callback, 0);
        Emit("window-enumeration-return", new { sample, enumOk, windows, start, end = Clock.ElapsedTicks });
        if (!enumOk) Error("window-enumeration-incomplete", null, Marshal.GetLastWin32Error());
        GC.KeepAlive(callback);
    }
}
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    internal delegate bool EnumWindowsProc(nint hwnd, nint value);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryFullProcessImageNameW")] internal static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder image, ref uint size);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool EnumWindows(EnumWindowsProc callback, nint value);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
}
