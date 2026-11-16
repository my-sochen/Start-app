using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal static partial class Program
{
    private static readonly nint MessageOnly = (nint)(-3);
    private sealed record Binding(nint Window, uint Pid, long Birth, string FileId, string ContentHash, int Port);
    private sealed record TcpRow(IPAddress Local, int LocalPort, IPAddress Remote, int RemotePort, uint State, uint Pid);
    private static void Require(bool ok, string code) { if (!ok) throw new InvalidOperationException(code); }
    private static void Win(bool ok) { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    private static (SafeProcessHandle, uint, long) Launch(string exe, string root, SafeFileHandle job)
    {
        string[] args = { exe, "--user-data-dir=" + root, "--no-first-run", "--no-default-browser-check", "about:blank" };
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
        // Exclusive dummy-root assumption only: observed links/entry/depth caps stop deletion.
        // Attribute checks followed by pathname operations are NOT race-safe against a concurrent writer.
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
}
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct BasicLimits { public long PerProcess, PerJob; public uint LimitFlags; public nuint MinWorking, MaxWorking; public uint ActiveProcessLimit; public nuint Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] internal struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] internal struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct Startup { public uint Cb; public nint Reserved, Desktop, Title; public uint X,Y,Width,Height,XChars,YChars,Fill,Flags; public ushort Show,Reserved2; public nint ReservedBytes,Input,Output,Error; }
    [StructLayout(LayoutKind.Sequential)] internal struct ProcessInfo { public nint Process,Thread; public uint Pid,Tid; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeFileHandle CreateJobObjectW(nint attributes, string? name);
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
