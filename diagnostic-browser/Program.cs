using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

// LOCAL candidate. No workflow exists, no browser run authorized by this source.
// Synthetic setup navigation only. No consent/socket/metadata call path.
internal static class Program
{
    private static readonly List<SafeProcessHandle> OriginalRoots = new();
    private static void Check(bool value) { if (!value) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 4 || args[0] != "chrome" ||
            args[1].Length != 64 || args[1].Any(c => !Uri.IsHexDigit(c)) || !Path.IsPathFullyQualified(args[2])) return 2;
        if (args[3] != "--setup-navigate-only") return 2;
        string browser = args[0];
        string exe = browser == "chrome" ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google/Chrome/Application/chrome.exe") :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft/Edge/Application/msedge.exe");
        exe = Path.GetFullPath(exe);
        string expectedVersion = browser == "chrome" ? "156.0.8078.80" : "156.0.4314.58";
        string output = Path.GetFullPath(args[2]);
        if (File.Exists(output) || !File.Exists(exe)) return 2;
        string root = Path.Combine(Path.GetTempPath(), "StartAppSynthetic-" + Guid.NewGuid().ToString("N"));
        string profile = Path.Combine(root, "profile");
        string jobName = "Local\\StartAppSynthetic-" + Guid.NewGuid().ToString("N");
        var evidence = new Dictionary<string, object?> { ["browser"] = browser, ["expectedImage"] = exe,
            ["expectedVersion"] = expectedVersion, ["expectedSha256"] = args[1], ["jobName"] = jobName,
            ["freshRoot"] = root, ["scope"] = "disposable-synthetic-launch-only", ["metadata"] = "NOT_TESTED",
            ["consent"] = "FORBIDDEN_IN_STARTUP_MODE", ["personalProfile"] = "FORBIDDEN", ["continuity"] = "UNPROVEN", ["toggleOffCleanup"] = "NOT_RUN", ["cleanup"] = "NOT_CONFIRMED" };
        var launched = new List<object>();
        Marshal.SetLastPInvokeError(0);
        using var job = Native.CreateJobObjectW(0, jobName);
        int jobCreateError = Marshal.GetLastWin32Error();
        if (job.IsInvalid) return 3;
        if (jobCreateError == 183) return 3; // Never reuse an existing named job.
        bool didLaunch = false;
        int result = 3;
        try
        {
            // Held through CreateProcess: deny concurrent writes/deletion. Digest is supplied
            // by a separately reviewed inventory, never self-approved by this program.
            using var image = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read);
            string digest = Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant();
            string? version = FileVersionInfo.GetVersionInfo(exe).ProductVersion;
            if (!StringComparer.OrdinalIgnoreCase.Equals(digest, args[1]) || version != expectedVersion)
                throw new InvalidOperationException("Exact digest/version mismatch, no launch");
            evidence["actualSha256"] = digest; evidence["actualVersion"] = version;
            var limits = new Native.ExtendedLimits();
            limits.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE, NO breakaway flags.
            Check(Native.SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<Native.ExtendedLimits>()));
            if (Directory.Exists(root)) throw new InvalidOperationException("Fresh root collision");
            Directory.CreateDirectory(profile);
            string[] urls = new[] { "A", "B", "C" }.Select(label =>
            {
                string path = Path.Combine(root, label + ".html");
                using var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
                writer.Write("<!doctype html><meta charset=utf-8><title>StartAppSynthetic-" + label + "</title><h1>StartAppSynthetic-" + label + "</h1>");
                return new Uri(path).AbsoluteUri;
            }).ToArray();
            // No remote-debugging flags, account switches, sandbox disabling or policy edits.
            Launch(exe, new[] { "--user-data-dir=" + profile, "--no-first-run", "--no-default-browser-check", "--new-window", urls[0], urls[1] }, job, launched);
            didLaunch = true;
            evidence["launches"] = launched;
            evidence["status"] = "LAUNCHED_SYNTHETIC_ONLY";
            // Readiness output does not grant another process permission to automate this job.
            using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                JsonSerializer.Serialize(stream, evidence, new JsonSerializerOptions { WriteIndented = true });
            Launch(exe, new[] { "--user-data-dir=" + profile, "--no-first-run", "--no-default-browser-check", "--new-window", urls[2] }, job, launched);
            evidence["operator"] = SyntheticOperator.RunSetupOnly(browser, urls[2], job, output + ".evidence");
            evidence["toggleOffCleanup"] = "NOT_APPLICABLE_NO_TOGGLE_IN_STARTUP_MODE";
            Check(Native.TerminateJobObject(job, 0));
            evidence["cleanup"] = "job-termination-requested-not-yet-confirmed";
            // Query active count until zero, bounded. No name/PID kill fallback.
            var cleanupClock = Stopwatch.StartNew();
            bool empty = false;
            do {
                Check(Native.QueryInformationJobObject(job, 1, out Native.BasicAccounting accounting, (uint)Marshal.SizeOf<Native.BasicAccounting>(), out _));
                if (accounting.ActiveProcesses == 0) { empty = true; break; }
                Thread.Sleep(50);
            } while (cleanupClock.ElapsedMilliseconds < 5000);
            evidence["cleanup"] = empty ? "job-active-count-zero" : "UNCONFIRMED";
            if (!empty) throw new InvalidOperationException("Job termination unconfirmed");
            result = 0;
        }
        catch (Exception ex) { evidence["errorType"] = ex.GetType().FullName; evidence["toggleOffCleanup"] = "NOT_CONFIRMED_ON_FAILURE"; result = 3; }
        finally
        {
            // Closing this sole non-inheritable job handle kills only associated process objects.
            // This source does not delete profile files while termination remains unconfirmed.
            job.Dispose();
            foreach (var original in OriginalRoots) original.Dispose();
            evidence["jobClosed"] = true; evidence["didLaunch"] = didLaunch;
            string final = output + ".final.json";
            try
            {
                using var stream = new FileStream(final, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(stream, evidence, new JsonSerializerOptions { WriteIndented = true });
            }
            catch { result = 4; }
        }
        return result;
    }
    private static void Launch(string exe, string[] arguments, SafeFileHandle job, List<object> evidence)
    {
        // All generated args contain no quote/NUL. Reject rather than use ad hoc escaping.
        if (new[] { exe }.Concat(arguments).Any(s => s.Contains('"') || s.Contains('\0'))) throw new ArgumentException();
        var command = new StringBuilder(string.Join(" ", new[] { exe }.Concat(arguments).Select(s => "\"" + s + "\"")));
        var startup = new Native.Startup { Cb = (uint)Marshal.SizeOf<Native.Startup>() };
        Check(Native.CreateProcessW(exe, command, 0, 0, false, 4, 0, null, ref startup, out Native.ProcessInfo info));
        var process = new SafeProcessHandle(info.Process, true);
        OriginalRoots.Add(process);
        using var thread = new SafeFileHandle(info.Thread, true);
        bool resumed = false;
        try
        {
            Check(Native.GetProcessTimes(process, out long birth, out _, out _, out _));
            Check(Native.AssignProcessToJobObject(job, process));
            Check(Native.IsProcessInJob(process, job, out bool member));
            if (!member) throw new InvalidOperationException("Created root not in own job");
            uint resume = Native.ResumeThread(thread);
            if (resume != 1) throw new InvalidOperationException("Unexpected suspend count");
            resumed = true;
            evidence.Add(new { pid = info.Pid, birthFileTime = birth, originalCreationHandle = true,
                assignedBeforeResume = true, jobMember = member, resumePreviousCount = resume });
        }
        finally
        {
            // Only this exact CreateProcess returned object, never a rediscovered PID/name.
            if (!resumed) Check(Native.TerminateProcess(process, 3));
        }
    }
}
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct BasicLimits { public long PerProcess, PerJob; public uint LimitFlags; public nuint MinWorking, MaxWorking; public uint ActiveProcessLimit; public nuint Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] internal struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] internal struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct Startup { public uint Cb; public nint Reserved, Desktop, Title; public uint X,Y,Width,Height,XChars,YChars,Fill,Flags; public ushort Show,Reserved2; public nint ReservedBytes,Input,Output,Error; }
    [StructLayout(LayoutKind.Sequential)] internal struct ProcessInfo { public nint Process,Thread; public uint Pid,Tid; }
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
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
}
