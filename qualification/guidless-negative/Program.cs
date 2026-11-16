using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

// Negative flag-mode observation ONLY. No CDP, metadata, UI, native permission acceptance,
// inspect preferences, product references, HTTP discovery or GUID request fallback.
internal static class Program
{
    internal const string ChromeVersion = "156.0.8078.80", EdgeVersion = "156.0.4314.58";
    private static readonly nint MessageOnly = (nint)(-3);
    private sealed record Binding(nint Window, uint Pid, long Birth, string FileId, string ContentHash, int Port);
    private sealed record TcpRow(IPAddress Local, int LocalPort, IPAddress Remote, int RemotePort, uint State, uint Pid);
    private static void Require(bool ok, string code) { if (!ok) throw new InvalidOperationException(code); }
    private static void Win(bool ok) { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test") return SelfTest();
        if (!OperatingSystem.IsWindows() || args.Length != 3 || (args[0] != "chrome" && args[0] != "edge") ||
            args[1].Length != 64 || !args[1].All(Uri.IsHexDigit) || !Path.IsPathFullyQualified(args[2]) ||
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" || Environment.GetEnvironmentVariable("ImageVersion") != "20261103.298.1") return 2;
        return Observe(args[0], args[1], args[2]).GetAwaiter().GetResult();
    }
    private static async Task<int> Observe(string browser, string measuredDigest, string output)
    {
        var evidence = new Dictionary<string, object?> { ["browser"] = browser, ["mode"] = "OWNED_FRESH_FLAG_MODE_NEGATIVE_ONLY",
            ["result"] = "INCONCLUSIVE", ["metadata"] = "NOT_REQUESTED", ["positiveCompatibility"] = "NOT_TESTED", ["cleanup"] = "NOT_CONFIRMED" };
        string root = Path.Combine(Path.GetTempPath(), "StartAppGuidlessNegative-" + Guid.NewGuid().ToString("N"));
        string exe = Path.Combine(Environment.GetFolderPath(browser == "chrome" ? Environment.SpecialFolder.ProgramFiles : Environment.SpecialFolder.ProgramFilesX86),
            browser == "chrome" ? "Google/Chrome/Application/chrome.exe" : "Microsoft/Edge/Application/msedge.exe");
        using var job = Native.CreateJobObjectW(0, "Local\\StartAppNegative-" + Guid.NewGuid().ToString("N"));
        int jobError = Marshal.GetLastWin32Error();
        SafeProcessHandle? process = null;
        bool createdRoot = false, cleanup = false;
        try
        {
            Require(!job.IsInvalid && jobError != 183, "JOB_CREATE_STOP");
            var limits = new Native.ExtendedLimits(); limits.Basic.LimitFlags = 0x2000;
            Win(Native.SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Native.ExtendedLimits>()));
            using var image = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read);
            Require(Convert.ToHexString(SHA256.HashData(image)).Equals(measuredDigest, StringComparison.OrdinalIgnoreCase), "IMAGE_DIGEST_CHANGED");
            string expectedVersion = browser == "chrome" ? ChromeVersion : EdgeVersion;
            Require(FileVersionInfo.GetVersionInfo(exe).ProductVersion == expectedVersion, "IMAGE_VERSION_STOP");
            evidence["version"] = expectedVersion; evidence["measuredSha256"] = measuredDigest;
            Require(!Directory.Exists(root) && !File.Exists(root), "ROOT_COLLISION");
            Directory.CreateDirectory(root); createdRoot = true; RejectReparseChain(root);
            (process, uint pid, long birth) = Launch(exe, root, job);
            evidence["pid"] = pid; evidence["birthFileTime"] = birth; evidence["assignedBeforeResume"] = true;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var ready = Stopwatch.StartNew();
            Binding? binding = null;
            FileStream? endpoint = null;
            try
            {
                // Readiness retries only this owned root. Never connect until one stable full binding exists.
                while (ready.Elapsed < TimeSpan.FromSeconds(10))
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    ValidateProcess(process, pid, birth, exe, job);
                    var hwnds = MatchingWindows(root);
                    Require(hwnds.Count <= 1, "AMBIGUOUS_ROOT_WINDOW");
                    if (hwnds.Count == 1 && File.Exists(Path.Combine(root, "DevToolsActivePort")))
                    {
                        try
                        {
                            RejectReparseChain(root);
                            string epPath = Path.Combine(root, "DevToolsActivePort");
                            Require((File.GetAttributes(epPath) & FileAttributes.ReparsePoint) == 0, "ENDPOINT_REPARSE");
                            endpoint = new FileStream(epPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                            binding = Bind(root, endpoint, process, pid, birth, exe, job);
                            break;
                        }
                        catch (IOException) { endpoint?.Dispose(); endpoint = null; }
                    }
                    await Task.Delay(100, deadline.Token);
                }
                Require(binding is not null && endpoint is not null, "READINESS_INCONCLUSIVE");
                Binding before = binding!;
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                // Only numeric IPv4 loopback, no DNS/proxy/redirect/cookies.
                await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, before.Port), deadline.Token);
                var local = (IPEndPoint)socket.LocalEndPoint!; var remote = (IPEndPoint)socket.RemoteEndPoint!;
                Require(remote.Address.Equals(IPAddress.Loopback) && remote.Port == before.Port && local.Address.Equals(IPAddress.Loopback), "SOCKET_TUPLE_STOP");
                Require(HasServerTuple(ReadTcp(), before.Port, local.Port, pid), "CONNECTED_SERVER_OWNER_STOP");
                Require(before == Bind(root, endpoint!, process, pid, birth, exe, job), "BINDING_CHANGED_BEFORE_REQUEST");
                evidence["windowMessageOnly"] = true; evidence["endpointFileId"] = before.FileId;
                evidence["endpointContentSha256"] = before.ContentHash;
                evidence["tuple"] = new { localAddress = "127.0.0.1", localPort = local.Port, remoteAddress = "127.0.0.1", remotePort = remote.Port, ownerPid = pid };
                string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
                byte[] request = Encoding.ASCII.GetBytes($"GET /devtools/browser/ HTTP/1.1\r\nHost: 127.0.0.1:{before.Port}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n");
                evidence["upgradeAttempts"] = 1;
                int sent = 0;
                while (sent < request.Length) { int n = await socket.SendAsync(request.AsMemory(sent), SocketFlags.None, deadline.Token); Require(n > 0, "SEND_EOF"); sent += n; }
                // Header only, byte-at-a-time so never consume a response body or WS metadata frame.
                var header = new List<byte>(512); byte[] one = new byte[1];
                while (header.Count < 8192)
                {
                    int n = await socket.ReceiveAsync(one, SocketFlags.None, deadline.Token);
                    Require(n == 1, "HEADER_EOF"); header.Add(one[0]);
                    if (header.Count >= 4 && header[^4] == 13 && header[^3] == 10 && header[^2] == 13 && header[^1] == 10) break;
                }
                int status = ParseStatus(header.ToArray());
                evidence["httpStatus"] = status; evidence["headerBytes"] = header.Count;
                evidence["observed101"] = status == 101;
                if (status == 101) { evidence["result"] = Classify(status); socket.Dispose(); }
                // No raw header, endpoint text/GUID, profile or body is written to evidence.
                Require(before == Bind(root, endpoint!, process, pid, birth, exe, job), "BINDING_CHANGED_AFTER_RESPONSE");
                evidence["bindingStableAfterResponse"] = true;
                evidence["result"] = Classify(status);
            }
            finally { endpoint?.Dispose(); }
        }
        catch (Exception ex)
        {
            // Codes/types only: OS exception messages can contain unrelated filesystem paths.
            evidence["errorType"] = ex.GetType().Name;
            evidence["errorCode"] = ex is InvalidOperationException && ex.Message.Length <= 80 && ex.Message.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_') ? ex.Message : "OPERATION_INCONCLUSIVE";
            if (!evidence.TryGetValue("observed101", out var observed) || observed is not true) evidence["result"] = "INCONCLUSIVE";
        }
        finally
        {
            try
            {
                if (!job.IsInvalid)
                {
                    Win(Native.TerminateJobObject(job, 0));
                    var wait = Stopwatch.StartNew(); uint active;
                    do
                    {
                        Win(Native.QueryInformationJobObject(job, 1, out var accounting, (uint)Marshal.SizeOf<Native.BasicAccounting>(), out _));
                        active = accounting.ActiveProcesses;
                        if (active == 0) break;
                        Thread.Sleep(50);
                    } while (wait.Elapsed < TimeSpan.FromSeconds(5));
                    evidence["ownedJobActiveAfterCleanup"] = active;
                    cleanup = active == 0 && (process is null || Native.WaitForSingleObject(process, 0) == 0);
                }
            }
            catch { evidence["cleanupError"] = "OWNED_JOB_CLEANUP_UNVERIFIED"; }
            process?.Dispose();
            if (cleanup && createdRoot)
            {
                try { RejectReparseChain(root); DeleteOwnedTree(root); evidence["dummyRootRemoved"] = !Directory.Exists(root); cleanup &= !Directory.Exists(root); }
                catch { cleanup = false; evidence["dummyRootRemoved"] = false; }
            }
            if (evidence.TryGetValue("errorCode", out var error) && Equals(error, "CREATED_ROOT_CLEANUP_UNVERIFIED")) cleanup = false;
            evidence["cleanup"] = cleanup ? "OWNED_JOB_EMPTY_AND_ROOT_REMOVED" : "NOT_CONFIRMED";
            if (!cleanup && (!evidence.TryGetValue("observed101", out var observed) || observed is not true)) evidence["result"] = "INCONCLUSIVE";
            try { using var f = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None); JsonSerializer.Serialize(f, evidence, new JsonSerializerOptions { WriteIndented = true }); }
            catch { cleanup = false; }
        }
        return cleanup && (string)evidence["result"]! == "NEGATIVE_404_PASS_ONLY" ? 0 : 3;
    }
    private static (SafeProcessHandle, uint, long) Launch(string exe, string root, SafeFileHandle job)
    {
        string[] args = { exe, "--user-data-dir=" + root, "--remote-debugging-port=0", "--remote-debugging-address=127.0.0.1", "--no-first-run", "--no-default-browser-check", "about:blank" };
        Require(args.All(x => !x.Contains('"') && !x.Contains('\0') && !x.EndsWith('\\')), "ARGUMENT_STOP");
        var command = new StringBuilder(string.Join(" ", args.Select(x => "\"" + x + "\"")));
        var startup = new Native.Startup { Cb = (uint)Marshal.SizeOf<Native.Startup>() };
        Win(Native.CreateProcessW(exe, command, 0, 0, false, 4, 0, null, ref startup, out var info));
        var process = new SafeProcessHandle(info.Process, true);
        using var thread = new SafeFileHandle(info.Thread, true);
        bool resumed = false;
        try
        {
            Win(Native.GetProcessTimes(process, out long birth, out _, out _, out _));
            Win(Native.AssignProcessToJobObject(job, process)); Win(Native.IsProcessInJob(process, job, out bool member)); Require(member, "JOB_ASSIGN_STOP");
            Require(Native.ResumeThread(thread) == 1, "RESUME_COUNT_STOP"); resumed = true;
            return (process, info.Pid, birth);
        }
        finally { if (!resumed) { bool terminated = Native.TerminateProcess(process, 3); uint waited = Native.WaitForSingleObject(process, 3000); process.Dispose(); Require(terminated && waited == 0, "CREATED_ROOT_CLEANUP_UNVERIFIED"); } }
    }
    private static void ValidateProcess(SafeProcessHandle process, uint pid, long birth, string exe, SafeFileHandle job)
    {
        Require(Native.GetProcessId(process) == pid && Native.WaitForSingleObject(process, 0) == 258, "PROCESS_NOT_ALIVE");
        Win(Native.GetProcessTimes(process, out long current, out _, out _, out _)); Require(current == birth, "PROCESS_BIRTH_CHANGED");
        Win(Native.IsProcessInJob(process, job, out bool owned)); Require(owned, "PROCESS_NOT_OWNED");
        uint length = 32768; var image = new StringBuilder((int)length); Win(Native.QueryFullProcessImageNameW(process, 0, image, ref length));
        Require(string.Equals(Path.GetFullPath(image.ToString()), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase), "PROCESS_IMAGE_CHANGED");
    }
    private static List<nint> MatchingWindows(string root)
    {
        var result = new List<nint>(); nint previous = 0;
        for (int i = 0; i < 2; i++)
        {
            // This search's parent is explicitly HWND_MESSAGE. No EnumWindows/physical activation.
            nint hwnd = Native.FindWindowExW(MessageOnly, previous, "Chrome_MessageWindow", root);
            if (hwnd == 0) break;
            result.Add(hwnd); previous = hwnd;
        }
        return result;
    }
    private static Binding Bind(string root, FileStream file, SafeProcessHandle process, uint pid, long birth, string exe, SafeFileHandle job)
    {
        ValidateProcess(process, pid, birth, exe, job);
        var windows = MatchingWindows(root); Require(windows.Count == 1, "WINDOW_COUNT_STOP");
        Require(Native.GetWindowThreadProcessId(windows[0], out uint owner) != 0 && owner == pid, "WINDOW_OWNER_STOP");
        var title = new StringBuilder(32768); int titleLength = Native.GetWindowTextW(windows[0], title, title.Capacity);
        Require(titleLength == root.Length && title.ToString() == root, "ROOT_CAPTION_STOP");
        RejectReparseChain(root);
        Require((File.GetAttributes(Path.Combine(root, "DevToolsActivePort")) & FileAttributes.ReparsePoint) == 0, "ENDPOINT_REPARSE");
        Win(Native.GetFileInformationByHandleEx(file.SafeFileHandle, 18, out var identity, (uint)Marshal.SizeOf<Native.FileId>()));
        Require(file.Length is > 0 and <= 512, "ENDPOINT_SIZE_STOP");
        file.Position = 0; byte[] data = new byte[(int)file.Length]; file.ReadExactly(data);
        int port = ParseEndpoint(data);
        Require(OnlyListener(ReadTcp(), port, pid), "LISTENER_OWNER_OR_ADDRESS_STOP");
        return new Binding(windows[0], pid, birth, $"{identity.Volume:x16}:{identity.Low:x16}{identity.High:x16}", Convert.ToHexString(SHA256.HashData(data)), port);
    }
    private static int ParseEndpoint(byte[] data)
    {
        Require(data.Length is > 0 and <= 512 && data.All(b => b is >= 10 and <= 126 && b != 11 && b != 12), "ENDPOINT_ENCODING_STOP");
        string text = Encoding.ASCII.GetString(data).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (text.EndsWith('\n')) text = text[..^1];
        var lines = text.Split('\n');
        Require(lines.Length == 2 && lines[0].Length is > 0 and <= 5 && lines[0].All(char.IsAsciiDigit), "ENDPOINT_FORMAT_STOP");
        Require(int.TryParse(lines[0], out int port) && port is > 0 and <= 65535, "ENDPOINT_PORT_STOP");
        const string prefix = "/devtools/browser/";
        Require(lines[1].StartsWith(prefix, StringComparison.Ordinal) && lines[1].Length is > 18 and <= 200 && lines[1][prefix.Length..].All(c => char.IsAsciiLetterOrDigit(c) || c == '-'), "ENDPOINT_PATH_STOP");
        return port;
    }
    private static string Classify(int status) => status == 404 ? "NEGATIVE_404_PASS_ONLY" : status == 101 ? "NEGATIVE_TEST_FAILED_DISCRIMINATOR_NOT_VALID" : "INCONCLUSIVE";
    private static int ParseStatus(byte[] header)
    {
        Require(header.Length is >= 16 and <= 8192 && header.All(b => b is 9 or 10 or 13 || b is >= 32 and <= 126), "HEADER_ENCODING_OR_CAP_STOP");
        string text = Encoding.ASCII.GetString(header);
        Require(text.EndsWith("\r\n\r\n", StringComparison.Ordinal), "HEADER_INCOMPLETE");
        string[] lines = text[..^4].Split("\r\n", StringSplitOptions.None);
        Require(lines[0].Length >= 13 && lines[0].All(c => c is >= ' ' and <= '~') && lines[0].StartsWith("HTTP/1.1 ", StringComparison.Ordinal) &&
            lines[0][9..12].All(char.IsAsciiDigit) && lines[0][12] == ' ', "STATUS_LINE_STOP");
        foreach (string line in lines.Skip(1))
        {
            int colon = line.IndexOf(':');
            Require(colon > 0 && line[..colon].All(c => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c)) && !line.Contains('\r') && !line.Contains('\n'), "HEADER_FIELD_STOP");
        }
        return int.Parse(lines[0][9..12], System.Globalization.CultureInfo.InvariantCulture);
    }
    private static bool OnlyListener(List<TcpRow> rows, int port, uint pid)
    {
        var found = rows.Where(x => x.State == 2 && x.LocalPort == port).ToArray();
        return found.Length == 1 && found[0].Pid == pid && found[0].Local.Equals(IPAddress.Loopback);
    }
    private static bool HasServerTuple(List<TcpRow> rows, int port, int clientPort, uint pid) =>
        rows.Count(x => x.State == 5 && x.Local.Equals(IPAddress.Loopback) && x.LocalPort == port && x.Remote.Equals(IPAddress.Loopback) && x.RemotePort == clientPort && x.Pid == pid) == 1;
    private static List<TcpRow> ReadTcp()
    {
        var result = new List<TcpRow>();
        foreach (int family in new[] { 2, 23 })
        {
            int size = 0; uint first = Native.GetExtendedTcpTable(0, ref size, false, family, 5, 0);
            Require(first == 122 && size is >= 4 and <= 1048576, "TCP_TABLE_SIZE_STOP");
            nint buffer = Marshal.AllocHGlobal(size);
            try
            {
                int capacity = size; Require(Native.GetExtendedTcpTable(buffer, ref size, false, family, 5, 0) == 0 && size <= capacity && size >= 4, "TCP_TABLE_READ_STOP");
                byte[] bytes = new byte[size]; Marshal.Copy(buffer, bytes, 0, size);
                int count = BitConverter.ToInt32(bytes); int stride = family == 2 ? 24 : 56;
                Require(count is >= 0 and <= 32768 && 4L + (long)count * stride <= size, "TCP_ROW_CAP_STOP");
                for (int i = 0; i < count; i++)
                {
                    int p = 4 + i * stride;
                    int lp = p + (family == 2 ? 8 : 20), rp = p + (family == 2 ? 16 : 44);
                    result.Add(new TcpRow(new IPAddress(bytes.AsSpan(p + (family == 2 ? 4 : 0), family == 2 ? 4 : 16)),
                        (bytes[lp] << 8) | bytes[lp + 1], new IPAddress(bytes.AsSpan(p + (family == 2 ? 12 : 24), family == 2 ? 4 : 16)),
                        (bytes[rp] << 8) | bytes[rp + 1], BitConverter.ToUInt32(bytes, p + (family == 2 ? 0 : 48)), BitConverter.ToUInt32(bytes, p + (family == 2 ? 20 : 52))));
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return result;
    }
    private static void RejectReparseChain(string path)
    {
        for (DirectoryInfo? dir = new(path); dir is not null; dir = dir.Parent)
            Require((dir.Attributes & FileAttributes.ReparsePoint) == 0, "ROOT_REPARSE_STOP");
    }
    private static void DeleteOwnedTree(string root)
    {
        // Never recurse into links. A link or >4096 entries stops deletion and makes cleanup inconclusive.
        int count = 0;
        void Remove(string dir, int depth)
        {
            Require(depth <= 32, "DELETE_DEPTH_CAP_STOP");
            foreach (var path in Directory.EnumerateFileSystemEntries(dir))
            {
                Require(++count <= 4096, "DELETE_ENTRY_CAP_STOP");
                var attr = File.GetAttributes(path); Require((attr & FileAttributes.ReparsePoint) == 0, "DELETE_REPARSE_STOP");
                if ((attr & FileAttributes.Directory) != 0) Remove(path, depth + 1); else File.Delete(path);
            }
            Directory.Delete(dir);
        }
        Remove(root, 0);
    }
    private static int SelfTest()
    {
        int n = 0;
        void Test(bool ok) { if (!ok) throw new Exception("SELF_TEST_FAILED"); n++; }
        Test(ParseStatus(Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\n\r\n")) == 404);
        Test(Classify(101) == "NEGATIVE_TEST_FAILED_DISCRIMINATOR_NOT_VALID"); Test(Classify(404) == "NEGATIVE_404_PASS_ONLY"); Test(Classify(403) == "INCONCLUSIVE");
        foreach (var h in new[] { "HTTP/1.1 404\r\n\r\n", "HTTP/1.1 404 Bad\nReason\r\n\r\n", "HTTP/1.0 404 Not Found\r\n\r\n", "HTTP/1.1 404x\r\n\r\n", "HTTP/1.1 404 Not Found\r\nBad\r\n\r\n", "HTTP/1.1 404 Not Found\r\n", "HTTP/1.1 404 Not Found\r\n folded:x\r\n\r\n" })
        { bool rejected = false; try { ParseStatus(Encoding.ASCII.GetBytes(h)); } catch (InvalidOperationException) { rejected = true; } Test(rejected); }
        Test(ParseEndpoint(Encoding.ASCII.GetBytes("12345\n/devtools/browser/fixture-guid\n")) == 12345);
        foreach (var f in new[] { "0\n/devtools/browser/a", "9\n/devtools/page/a", "9\n/devtools/browser/a/b", "9\n/devtools/browser/a\nextra", "9\n/devtools/browser/" })
        { bool rejected = false; try { ParseEndpoint(Encoding.ASCII.GetBytes(f)); } catch (InvalidOperationException) { rejected = true; } Test(rejected); }
        var row = new TcpRow(IPAddress.Loopback, 9, IPAddress.Any, 0, 2, 123);
        Test(OnlyListener(new() { row }, 9, 123)); Test(!OnlyListener(new() { row, row }, 9, 123));
        Test(!OnlyListener(new() { row with { Local = IPAddress.Any } }, 9, 123)); Test(!OnlyListener(new() { row }, 9, 456));
        Console.WriteLine($"SELF_TEST_PASS {n} pure assertions, no OS/browser/socket calls"); return 0;
    }
}
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct BasicLimits { public long PerProcess, PerJob; public uint LimitFlags; public nuint MinWorking, MaxWorking; public uint ActiveProcessLimit; public nuint Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] internal struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] internal struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct Startup { public uint Cb; public nint Reserved, Desktop, Title; public uint X,Y,Width,Height,XChars,YChars,Fill,Flags; public ushort Show,Reserved2; public nint ReservedBytes,Input,Output,Error; }
    [StructLayout(LayoutKind.Sequential)] internal struct ProcessInfo { public nint Process,Thread; public uint Pid,Tid; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeFileHandle CreateJobObjectW(nint attributes, string name);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool SetInformationJobObject(SafeFileHandle job, int cls, ref ExtendedLimits value, uint bytes);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool CreateProcessW(string exe, StringBuilder command, nint processAttributes, nint threadAttributes, bool inherit, uint flags, nint environment, string? cwd, ref Startup startup, out ProcessInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool IsProcessInJob(SafeProcessHandle process, SafeFileHandle job, out bool member);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint ResumeThread(SafeFileHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool TerminateProcess(SafeProcessHandle process, uint code);
    [StructLayout(LayoutKind.Sequential)] internal struct BasicAccounting { public long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime; public uint PageFaultCount, TotalProcesses, ActiveProcesses, TotalTerminated; }
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool QueryInformationJobObject(SafeFileHandle job, int cls, out BasicAccounting info, uint size, out uint returned);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool TerminateJobObject(SafeFileHandle job, uint code);

    [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint GetProcessId(SafeProcessHandle process);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder name, ref uint size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint FindWindowExW(nint parent, nint after, string className, string windowName);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern int GetWindowTextW(nint window, StringBuilder text, int capacity);
    [StructLayout(LayoutKind.Sequential)] internal struct FileId { public ulong Volume, Low, High; }
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int cls, out FileId info, uint bytes);
    [DllImport("iphlpapi.dll", SetLastError = true)] internal static extern uint GetExtendedTcpTable(nint table, ref int size, bool order, int family, int cls, uint reserved);
}
