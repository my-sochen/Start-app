using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using StartApp.Core;
using StartApp.Platform.Windows;
using StartApp.Presentation;
using StartApp.Storage.Json;

// Dedicated hosted-runner acceptance, not a product entry point. No fake probe,
// launcher, starter, capturer or store. This drives the production single-item VM
// command but not the WPF button. It must never run on a personal desktop.
internal static class Program
{
    private sealed record Window(long Hwnd, uint Pid, long ProcessStartedUtcTicks, string Class, string Path);
    private sealed record Check(string Name, bool Ok, string Detail);
    private static readonly List<Check> Checks = [];
    private static readonly List<Window> Owned = [];
    private static HashSet<long> Baseline = [];
    private static string Output = "";
    private static string Root = "";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    [STAThread]
    private static int Main(string[] args)
    {
        // Exact opt-in prevents accidental local use. The workflow supplies an
        // absent output and a unique scratch directory below RUNNER_TEMP.
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
            Environment.GetEnvironmentVariable("STARTAPP_ROOT_ACCEPTANCE") != "hosted-disposable-desktop-only" || args.Length != 2)
        { Console.Error.WriteLine("Only the opted-in disposable GitHub Windows runner is supported."); return 2; }
        Output = Path.GetFullPath(args[0]);
        var scratch = Path.GetFullPath(args[1]);
        var runnerTemp = Path.GetFullPath(Environment.GetEnvironmentVariable("RUNNER_TEMP") ?? throw new Exception("RUNNER_TEMP missing"));
        if (!scratch.StartsWith(runnerTemp.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) || Directory.Exists(scratch) || Directory.Exists(Output))
        { Console.Error.WriteLine("Scratch must be new beneath RUNNER_TEMP and output must not exist."); return 2; }
        Directory.CreateDirectory(Output);
        Directory.CreateDirectory(scratch);
        Root = Path.GetPathRoot(Environment.SystemDirectory)!;
        try
        {
            var initial = Windows();
            Baseline = initial.Select(w => w.Hwnd).ToHashSet();
            File.WriteAllText(Path.Combine(Output, "scope.json"), JsonSerializer.Serialize(new {
                commit = Environment.GetEnvironmentVariable("GITHUB_SHA"), root = Root,
                os = RuntimeInformation.OSDescription, apartment = Thread.CurrentThread.GetApartmentState().ToString(),
                scope = "Production VM single-item command, real probe/starter/Explorer capture/JSON reload. Not a WPF click test or consumer Windows 11 proof.",
                unc = "NOT EXERCISED. No controlled share supplied. No remote host probing, SMB configuration, share creation or admin-share fallback. UNC unit/fake-adapter evidence only.",
                ownership = "Disposable runner, no concurrent interactive actor. New matching HWND absent from baseline after our one open request, explorer executable/class/PID/process-start identity. Temporal correlation is scoped runner evidence, not a general ownership detector."
            }, Json));
            Require("STA", Thread.CurrentThread.GetApartmentState() == ApartmentState.STA, "COM Explorer enumeration on main STA");
            Require("clean root baseline", !initial.Any(w => SamePath(w.Path, Root)), "Refuse to reuse or close an existing root window");
            var item = new Item("root-acceptance", ItemType.Folder, "System drive root", Root);
            Require("folder form", PreflightRules.CheckForm(item, null) is null, Root);
            var real = WindowsComposition.Create();
            Require("real filesystem preflight", real.Preflight.Check(item, null).State == PreflightState.Ready, Root);
            // Use the actual production launcher for the initial open. Later open
            // is driven by MainViewModel after persistence reload.
            Require("initial real shell request", real.Launcher.Launch(item, null).Status == LaunchStatus.Started, Root);
            var first = AwaitNewRoot("first-open");
            Screenshot(first, "first-root.png");
            var scan = new WindowsOpenItemCapturer().Scan();
            var captured = scan.Items.SingleOrDefault(i => i.Type == ItemType.Folder && i.Target == Root);
            Require("real Explorer root captured exactly", captured is not null, Root);
            // Persist only the controlled root item, not unrelated program or
            // folder rows found by the real scanner on this disposable desktop.
            var storePath = Path.Combine(scratch, "store.json");
            using (var repo = new JsonStore(storePath))
            {
                repo.Mutate(c => {
                    var workspace = c.CreateWorkspace("Root acceptance");
                    var stored = c.GetOrAddItem(captured!.Type, captured.Name, captured.Target);
                    c.AddToWorkspace(workspace.Id, stored.Id);
                });
            }
            CloseOwned(first);
            using (var repo = new JsonStore(storePath))
            {
                var state = repo.Load();
                var saved = state.Items.Values.Single();
                Require("exact root survives disk reload", saved.Type == ItemType.Folder && saved.Target == Root, saved.Target);
                var (launcher, preflight) = WindowsComposition.Create();
                var vm = new MainViewModel(repo, launcher, preflight, capturer: new WindowsOpenItemCapturer());
                vm.Selected = vm.Workspaces.Single();
                var row = vm.Items.Single();
                Require("reloaded VM command enabled", vm.OpenItemCommand.CanExecute(row), row.Target);
                vm.OpenItemCommand.Execute(row);
                vm.OpenTask.GetAwaiter().GetResult();
                Require("VM single-item open not pending", !vm.IsChecking && !vm.IsLaunching && !vm.OpenDecisionPending, vm.Status);
                Require("VM reports a sent request only", vm.Status.Contains(": open request sent (not proof it loaded)", StringComparison.Ordinal), vm.Status);
                var second = AwaitNewRoot("reopened-from-reloaded-VM");
                Screenshot(second, "reopened-root.png");
                Require("reopened root recaptured", new WindowsOpenItemCapturer().Scan().Items.Any(i => i.Type == ItemType.Folder && i.Target == Root), Root);
                CloseOwned(second);
            }
            Checks.Add(new("FLOW-COMPLETE", true, Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "missing"));
        }
        catch (Exception e) { Checks.Add(new("UNHANDLED", false, e.ToString())); }
        finally
        {
            foreach (var window in Owned.ToArray())
                try { CloseOwned(window); }
                catch (Exception e) { Checks.Add(new("cleanup owned HWND", false, e.Message)); }
            // Only delete our newly created scratch tree, never LOCALAPPDATA.
            try { Directory.Delete(scratch, true); Checks.Add(new("scratch removed", true, "unique RUNNER_TEMP directory")); }
            catch (Exception e) { Checks.Add(new("scratch removed", false, e.Message)); }
            File.WriteAllText(Path.Combine(Output, "root-assertions.json"), JsonSerializer.Serialize(Checks, Json));
            File.WriteAllText(Path.Combine(Output, "owned-windows-remaining.json"), JsonSerializer.Serialize(Owned, Json));
            foreach (var check in Checks) Console.WriteLine($"{(check.Ok ? "PASS" : "FAIL")} {check.Name}: {check.Detail}");
        }
        return Checks.Any(c => !c.Ok) || !Checks.Any(c => c.Name == "FLOW-COMPLETE") ? 1 : 0;
    }

    private static void Require(string name, bool ok, string detail)
    { Checks.Add(new(name, ok, detail)); if (!ok) throw new InvalidOperationException(name + ": " + detail); }
    private static bool SamePath(string a, string b) => string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static List<Window> Windows()
    {
        var result = new List<Window>();
        dynamic? shell = null; dynamic? windows = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application") ?? throw new Exception("Shell COM unavailable"));
            windows = shell!.Windows();
            for (int i = 0; i < windows.Count; i++)
            {
                dynamic? w = null;
                try
                {
                    w = windows.Item(i);
                    if (!string.Equals(Path.GetFileName((string)w.FullName), "explorer.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    var hwnd = new IntPtr((long)w.HWND);
                    if (!IsWindowVisible(hwnd)) continue;
                    GetWindowThreadProcessId(hwnd, out var pid);
                    using var process = Process.GetProcessById((int)pid);
                    // Do not trust COM FullName alone for cleanup authority.
                    var actualExe = process.MainModule?.FileName;
                    if (!string.Equals(actualExe, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), StringComparison.OrdinalIgnoreCase)) continue;
                    var cls = ClassOf(hwnd);
                    if (cls is not ("CabinetWClass" or "ExploreWClass")) continue;
                    var path = CaptureRules.FileUrlToPath((string?)w.LocationURL);
                    if (path is null) continue;
                    result.Add(new(hwnd.ToInt64(), pid, process.StartTime.ToUniversalTime().Ticks, cls, path));
                }
                catch (Exception e) when (e is COMException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
                finally { Release(w); }
            }
        }
        finally { Release(windows); Release(shell); }
        return result;
    }

    private static Window AwaitNewRoot(string stage)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(20))
        {
            var candidates = Windows().Where(w => !Baseline.Contains(w.Hwnd) && !Owned.Any(o => o.Hwnd == w.Hwnd) && SamePath(w.Path, Root)).ToList();
            if (candidates.Count > 1) throw new Exception("Ambiguous new root windows. Refusing to claim or close them.");
            if (candidates.Count == 1)
            {
                var candidate = candidates[0];
                Owned.Add(candidate);
                File.WriteAllText(Path.Combine(Output, stage + "-window.json"), JsonSerializer.Serialize(candidate, Json));
                Require(stage + " window identity", candidate.Path == Root, "new root HWND bound to real Explorer PID/start/class/path");
                return candidate;
            }
            Thread.Sleep(200);
        }
        throw new Exception("No unique new Explorer root window within 20 seconds");
    }

    private static void CloseOwned(Window expected)
    {
        // No process kill, no all-Explorer sweep. A changed path or identity is
        // left alone and reported as a failure, never closed speculatively.
        Require("cleanup not baseline", !Baseline.Contains(expected.Hwnd), expected.Hwnd.ToString());
        var current = Windows().SingleOrDefault(w => w.Hwnd == expected.Hwnd);
        if (current is null) { Owned.Remove(expected); return; }
        Require("cleanup exact identity unchanged", current == expected, expected.Hwnd.ToString());
        Require("WM_CLOSE sent to owned root only", PostMessage(new(expected.Hwnd), 0x0010, IntPtr.Zero, IntPtr.Zero), expected.Hwnd.ToString());
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(8) && IsWindow(new(expected.Hwnd))) Thread.Sleep(100);
        Require("owned HWND closed", !IsWindow(new(expected.Hwnd)), expected.Hwnd.ToString());
        Owned.Remove(expected);
    }

    private static void Screenshot(Window window, string name)
    {
        var h = new IntPtr(window.Hwnd);
        ShowWindow(h, 9); SetForegroundWindow(h); Thread.Sleep(700);
        Require(name + " foreground ownership", GetForegroundWindow() == h, window.Hwnd.ToString());
        Require(name + " current identity", Windows().Any(w => w == window), window.Path);
        Require(name + " bounds", GetWindowRect(h, out var r) && r.Right > r.Left && r.Bottom > r.Top, "native root window");
        using var bitmap = new Bitmap(r.Right - r.Left, r.Bottom - r.Top);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(r.Left, r.Top, 0, 0, bitmap.Size);
        bitmap.Save(Path.Combine(Output, name), ImageFormat.Png);
    }

    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    private static string ClassOf(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, s.Capacity); return s.ToString(); }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
}
