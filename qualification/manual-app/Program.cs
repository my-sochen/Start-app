using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

// LOCAL preparation only. No workflow, no automatic run permission. Never invokes capture or opens a target.
internal static class Program
{
    [STAThread] static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" || args.Length != 2) return 2;
        string exe = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
        if (!exe.EndsWith("StartApp.App.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(exe) || Directory.Exists(output)) return 2;
        string store = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StartApp");
        // Never remove or replace preexisting user/test data. Fresh hosted runner is mandatory.
        if (Directory.Exists(store) || File.Exists(store)) return 2;
        Directory.CreateDirectory(output);
        var facts = new Dictionary<string, object?> { ["result"] = "INCOMPLETE", ["captureInvoked"] = false, ["browserLaunched"] = false, ["targetsOpened"] = false };
        using var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false })!;
        try
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 10000) { process.Refresh(); if (process.HasExited) throw new InvalidOperationException("APP_EXITED"); if (process.MainWindowHandle != 0) break; Thread.Sleep(100); }
            process.Refresh(); nint hwnd = process.MainWindowHandle; Require(hwnd != 0, "APP_WINDOW_MISSING");
            AutomationElement Root() { Require(!process.HasExited && Native.GetWindowThreadProcessId(hwnd, out uint pid) != 0 && pid == process.Id, "ORIGINAL_APP_WINDOW_LOST"); return AutomationElement.FromHandle(hwnd); }
            AutomationElement Find(AutomationElement root, ControlType type, string name)
            {
                var matches = root.FindAll(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, type), new PropertyCondition(AutomationElement.NameProperty, name)))
                    .Cast<AutomationElement>().Where(x => !x.Current.IsOffscreen && x.Current.IsEnabled).ToArray();
                Require(matches.Length == 1 && matches[0].Current.ProcessId == process.Id, "EXACT_APP_CONTROL_UNAVAILABLE"); return matches[0];
            }
            void Invoke(string name) => ((InvokePattern)Find(Root(), ControlType.Button, name).GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            void Set(string name, string value) => ((ValuePattern)Find(Root(), ControlType.Edit, name).GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);
            Capture(hwnd, process.Id, Path.Combine(output, "01-overview.png"));
            Set("Workspace name", "Manual verification"); Set("Workspace description", "Synthetic local test only"); Invoke("Create workspace");
            Find(Root(), ControlType.Edit, "Item target");
            Set("Item target", "https://example.invalid/manual-only"); Set("Item name", "Synthetic manual link"); Invoke("Add");
            Capture(hwnd, process.Id, Path.Combine(output, "02-details-manual-item.png"));
            // Save/back uses existing actual application commands. Never click item name or launch buttons.
            Invoke("Save workspace details"); Invoke("Back");
            var help = Find(Root(), ControlType.Button, "Browser help");
            // Invoke may block behind ShowDialog. One background call only; no repeat on timeout.
            var invocation = Task.Run(() => ((InvokePattern)help.GetCurrentPattern(InvokePattern.Pattern)).Invoke());
            nint dialog = 0; clock.Restart();
            while (clock.ElapsedMilliseconds < 5000)
            {
                var dialogs = new List<nint>();
                Require(Native.EnumWindows((h, _) => { if (h != hwnd && Native.GetWindowThreadProcessId(h, out uint pid) != 0 && pid == process.Id && Native.GetWindow(h, 4) == hwnd && Native.IsWindowVisible(h)) dialogs.Add(h); return true; }, 0), "HELP_ENUMERATION_FAILED");
                Require(dialogs.Count <= 1, "HELP_WINDOW_AMBIGUOUS");
                if (dialogs.Count == 1) { dialog = dialogs[0]; break; } Thread.Sleep(50);
            }
            Require(dialog != 0, "HELP_WINDOW_MISSING"); var helpRoot = AutomationElement.FromHandle(dialog);
            Find(helpRoot, ControlType.Text, "Browser capture unavailable");
            Find(helpRoot, ControlType.Text, "No browser permission needed");
            var chrome = Find(helpRoot, ControlType.Edit, "Chrome settings address");
            Require(((ValuePattern)chrome.GetCurrentPattern(ValuePattern.Pattern)).Current.Value == "chrome://inspect/#remote-debugging", "CHROME_HELP_VALUE_MISMATCH");
            Capture(dialog, process.Id, Path.Combine(output, "03-browser-help.png"));
            // Clipboard is a real bounded effect, only on the disposable runner. Never use external URLs from UI.
            ((InvokePattern)Find(helpRoot, ControlType.Button, "Copy Chrome settings address").GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            Require(Clipboard.GetText() == "chrome://inspect/#remote-debugging", "CHROME_COPY_FAILED");
            ((InvokePattern)Find(helpRoot, ControlType.Button, "Close browser help").GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            Require(invocation.Wait(2000), "HELP_INVOKE_NOT_SETTLED");
            facts["manualCreateAddSave"] = true; facts["helpCopy"] = true; facts["result"] = "UI_FACTS_PASSED_IMAGES_NEED_PIXEL_REVIEW";
        }
        catch (Exception ex) { facts["errorType"] = ex.GetType().Name; }
        finally
        {
            // Exact owned app only. No browser/process enumeration kill. Preserve fresh dummy store for evidence, do not delete it.
            if (!process.HasExited) { process.Kill(false); facts["ownedAppTerminated"] = process.WaitForExit(5000); }
            else facts["ownedAppTerminated"] = true;
            File.WriteAllText(Path.Combine(output, "manual-result.json"), JsonSerializer.Serialize(facts, new JsonSerializerOptions { WriteIndented = true }));
        }
        return Equals(facts["result"], "UI_FACTS_PASSED_IMAGES_NEED_PIXEL_REVIEW") && Equals(facts["ownedAppTerminated"], true) ? 0 : 3;
    }
    static void Require(bool ok, string code) { if (!ok) throw new InvalidOperationException(code); }
    static void Capture(nint hwnd, int pid, string path)
    {
        Require(Native.GetWindowThreadProcessId(hwnd, out uint actual) != 0 && actual == pid, "CAPTURE_OWNER_STOP");
        Require(Native.SetForegroundWindow(hwnd) && Native.GetForegroundWindow() == hwnd, "CAPTURE_FOREGROUND_STOP");
        Require(Native.GetWindowRect(hwnd, out var r), "CAPTURE_RECT_STOP"); int w = r.Right - r.Left, h = r.Bottom - r.Top;
        Require(w is >= 300 and <= 4096 && h is >= 250 and <= 2160 && r.Left >= 0 && r.Top >= 0, "CAPTURE_BOUNDS_STOP");
        nint dc = Native.GetDC(0), memory = Native.CreateCompatibleDC(dc), bitmap = Native.CreateCompatibleBitmap(dc, w, h), old = 0;
        try
        {
            Require(dc != 0 && memory != 0 && bitmap != 0, "CAPTURE_GDI_STOP"); old = Native.SelectObject(memory, bitmap);
            Require(Native.BitBlt(memory, 0, 0, w, h, dc, r.Left, r.Top, 0x00CC0020) && Native.GetForegroundWindow() == hwnd, "CAPTURE_CHANGED_STOP");
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); using var f = File.Create(path); encoder.Save(f);
        }
        finally { if (old != 0) Native.SelectObject(memory, old); if (bitmap != 0) Native.DeleteObject(bitmap); if (memory != 0) Native.DeleteDC(memory); if (dc != 0) Native.ReleaseDC(0, dc); }
    }
}
internal static class Native
{
    internal delegate bool EnumProc(nint hwnd, nint arg);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, nint arg);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint hwnd, uint cmd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] internal static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] internal static extern bool BitBlt(nint dst, int x, int y, int width, int height, nint src, int sx, int sy, uint op);
}
