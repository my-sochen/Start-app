using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

internal static class Program
{
    [StructLayout(LayoutKind.Sequential)] private struct Msg
    { public nint Hwnd; public uint Message; public nuint WParam; public nint LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out Msg msg, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref Msg msg);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--child") { Thread.Sleep(3000); return 0; }
        if (args.Length != 1 || !OperatingSystem.IsWindows() || File.Exists(args[0])) return 2;
        // Built-in STATIC class, synthetic labels only. No registration, input or browser.
        nint a = CreateWindowExW(0, "STATIC", "Raw fixture A", 0x10CF0000, 80, 80, 300, 180, 0, 0, 0, 0);
        nint b = CreateWindowExW(0, "STATIC", "Raw fixture B", 0x10CF0000, 420, 80, 300, 180, 0, 0, 0, 0);
        if (a == 0 || b == 0) return 3;
        using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--child") { UseShellExecute = false });
        if (child is null) return 4;
        using (var output = new FileStream(args[0] + ".tmp", FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            JsonSerializer.Serialize(output, new { pid = Environment.ProcessId, childPid = child.Id, childBirth = child.StartTime.ToUniversalTime().ToFileTimeUtc(), hwnds = new[] { a.ToInt64(), b.ToInt64() } });
        File.Move(args[0] + ".tmp", args[0], false);
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 15000)
        {
            while (PeekMessageW(out Msg msg, 0, 0, 0, 1)) { TranslateMessage(ref msg); DispatchMessageW(ref msg); }
            Thread.Sleep(10);
        }
        DestroyWindow(b); DestroyWindow(a);
        child.WaitForExit(1000);
        return 0;
    }
}
