using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Media;

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
        string stage = "APP_START";
        try
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 10000) { process.Refresh(); Require(!process.HasExited, "APP_EXITED"); if (process.MainWindowHandle != 0) break; Thread.Sleep(100); }
            process.Refresh(); nint hwnd = process.MainWindowHandle; Require(hwnd != 0, "APP_WINDOW_MISSING");
            AutomationElement Root() { Require(!process.HasExited && Native.GetWindowThreadProcessId(hwnd, out uint pid) != 0 && pid == process.Id, "ORIGINAL_APP_WINDOW_LOST"); return AutomationElement.FromHandle(hwnd); }
            AutomationElement[] Matches(AutomationElement root, ControlType type, string name, bool enabled = true) =>
                root.FindAll(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, type), new PropertyCondition(AutomationElement.NameProperty, name)))
                    .Cast<AutomationElement>().Where(x => !x.Current.IsOffscreen && (!enabled || x.Current.IsEnabled) && x.Current.ProcessId == process.Id).ToArray();
            AutomationElement Find(AutomationElement root, ControlType type, string name, bool enabled = true)
            {
                var matches = Matches(root, type, name, enabled);
                Require(matches.Length == 1, "EXACT_APP_CONTROL_UNAVAILABLE"); return matches[0];
            }
            bool Unique(AutomationElement root, ControlType type, string name) => Matches(root, type, name).Length == 1;
            void Wait(Func<bool> condition, string code)
            {
                var timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < 5000)
                {
                    try { if (condition()) return; } catch (ElementNotAvailableException) { }
                    Thread.Sleep(100);
                }
                Require(false, code);
            }
            string Value(string name) => ((ValuePattern)Find(Root(), ControlType.Edit, name).GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
            void Invoke(string name) => ((InvokePattern)Find(Root(), ControlType.Button, name).GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            void Set(string name, string value)
            {
                ((ValuePattern)Find(Root(), ControlType.Edit, name).GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);
                Wait(() => Value(name) == value, "EDIT_VALUE_NOT_APPLIED");
            }
            stage = "OVERVIEW_READY";
            Wait(() => Unique(Root(), ControlType.Edit, "Workspace name") && Unique(Root(), ControlType.Button, "Browser help"), "OVERVIEW_NOT_READY");
            Capture(hwnd, process.Id, Path.Combine(output, "01-overview.png"));
            facts["overviewReady"] = true;
            stage = "CREATE_WORKSPACE";
            Set("Workspace name", "Manual verification"); Set("Workspace description", "Synthetic local test only"); Invoke("Create workspace");
            Wait(() => Unique(Root(), ControlType.Edit, "Item target") && Value("Workspace name") == "Manual verification", "CREATE_POSTCONDITION_FAILED");
            facts["workspaceCreated"] = true;
            stage = "SELECT_LINK";
            // Actual XAML: ItemTypes = Enum.GetValues<ItemType>(); Model.cs names the Link enum.
            var combo = Find(Root(), ControlType.ComboBox, "Item type");
            var expansion = (ExpandCollapsePattern)combo.GetCurrentPattern(ExpandCollapsePattern.Pattern);
            expansion.Expand();
            Wait(() => Unique(combo, ControlType.ListItem, "Link"), "LINK_OPTION_UNAVAILABLE");
            ((SelectionItemPattern)Find(combo, ControlType.ListItem, "Link").GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            expansion.Collapse();
            Wait(() => { var chosen = ((SelectionPattern)combo.GetCurrentPattern(SelectionPattern.Pattern)).Current.GetSelection(); return chosen.Length == 1 && chosen[0].Current.ProcessId == process.Id && chosen[0].Current.Name == "Link"; }, "LINK_SELECTION_FAILED");
            stage = "ADD_LINK";
            Set("Item target", "https://example.invalid/manual-only"); Set("Item name", "Synthetic manual link"); Invoke("Add");
            Wait(() => Value("Item target") == "" && Unique(Root(), ControlType.Button, "Synthetic manual link") && Unique(Root(), ControlType.Text, "https://example.invalid/manual-only"), "ADD_LINK_POSTCONDITION_FAILED");
            facts["manualLinkAdded"] = true;
            stage = "DETAILS_READY";
            Capture(hwnd, process.Id, Path.Combine(output, "02-details-manual-item.png"));
            // Creation/add persist immediately; change the synthetic description so details Save is meaningful/enabled.
            stage = "SAVE_DETAILS";
            Set("Workspace description", "Synthetic local test saved"); Invoke("Save workspace details");
            Wait(() => { var save = Matches(Root(), ControlType.Button, "Save workspace details", false); return save.Length == 0 && Unique(Root(), ControlType.Text, "Workspace details saved") && Value("Workspace description") == "Synthetic local test saved"; }, "SAVE_POSTCONDITION_FAILED");
            facts["detailsSaved"] = true;
            stage = "BACK_TO_OVERVIEW";
            Invoke("Back");
            Wait(() => Unique(Root(), ControlType.Button, "Browser help") && Unique(Root(), ControlType.Button, "Manual verification"), "BACK_POSTCONDITION_FAILED");
            stage = "OPEN_HELP";
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
            stage = "HELP_READY";
            try
            {
                Wait(() => Unique(helpRoot, ControlType.Text, "Browser capture unavailable") && Unique(helpRoot, ControlType.Text, "No browser permission needed") && Unique(helpRoot, ControlType.Edit, "Chrome settings address"), "HELP_POSTCONDITION_FAILED");
            }
            catch (GuardFailure failure) when (failure.Code == "HELP_POSTCONDITION_FAILED")
            {
                // Failure-only observation. Never dump UIA text/tree or let diagnostics replace the original guard.
                try
                {
                    Require(Native.GetWindowThreadProcessId(dialog, out uint helpPid) != 0 && helpPid == process.Id && Native.GetWindow(dialog, 4) == hwnd && Native.IsWindowVisible(dialog), "HELP_DIAGNOSTIC_OWNER_STOP");
                    facts["helpTitleMatchesExpected"] = helpRoot.Current.Name == "עזרה והגדרות דפדפן";
                    facts["helpClassMatchesExpected"] = helpRoot.Current.ClassName.StartsWith("HwndWrapper[StartApp.App", StringComparison.Ordinal);
                    object Describe(ControlType type, string name)
                    {
                        var found = helpRoot.FindAll(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, type), new PropertyCondition(AutomationElement.NameProperty, name))).Cast<AutomationElement>().ToArray();
                        var own = found.Where(x => x.Current.ProcessId == process.Id).ToArray();
                        return new { totalExactCount = found.Length, samePidCount = own.Length, visibleEnabledCount = own.Count(x => !x.Current.IsOffscreen && x.Current.IsEnabled), sample = own.Take(3).Select(x =>
                        {
                            var c = x.Current; var rect = c.BoundingRectangle;
                            double? Bounded(double n) => double.IsFinite(n) && Math.Abs(n) <= 16384 ? Math.Round(n, 1) : null;
                            return new { offscreen = c.IsOffscreen, enabled = c.IsEnabled, rectFiniteBounded = Bounded(rect.X).HasValue && Bounded(rect.Y).HasValue && Bounded(rect.Width).HasValue && Bounded(rect.Height).HasValue, x = Bounded(rect.X), y = Bounded(rect.Y), width = Bounded(rect.Width), height = Bounded(rect.Height) };
                        }).ToArray() };
                    }
                    facts["helpHeading"] = Describe(ControlType.Text, "Browser capture unavailable");
                    facts["helpDisclaimer"] = Describe(ControlType.Text, "No browser permission needed");
                    facts["helpChromeAddress"] = Describe(ControlType.Edit, "Chrome settings address");
                }
                catch (Exception diagnostic) { facts["helpDiagnosticErrorCode"] = diagnostic is GuardFailure guard ? guard.Code : "HELP_DIAGNOSTIC_UNAVAILABLE"; }
                try
                {
                    // Existing third evidence slot, explicitly a failed-readiness view, not successful help verification.
                    Require(Native.GetWindow(dialog, 4) == hwnd && Native.IsWindowVisible(dialog), "HELP_FAILURE_IMAGE_OWNER_STOP");
                    Capture(dialog, process.Id, Path.Combine(output, "03-browser-help.png"));
                    facts["helpFailureImageCaptured"] = true;
                }
                catch (Exception image) { facts["helpFailureImageCaptured"] = false; facts["helpFailureImageErrorCode"] = image is GuardFailure guard ? guard.Code : "HELP_FAILURE_IMAGE_UNAVAILABLE"; }
                throw;
            }
            var chrome = Find(helpRoot, ControlType.Edit, "Chrome settings address");
            Require(((ValuePattern)chrome.GetCurrentPattern(ValuePattern.Pattern)).Current.Value == "chrome://inspect/#remote-debugging", "CHROME_HELP_VALUE_MISMATCH");
            Capture(dialog, process.Id, Path.Combine(output, "03-browser-help.png"));
            // Clipboard is a real bounded effect, only on the disposable runner. Never use external URLs from UI.
            stage = "COPY_CHROME_ADDRESS";
            ((InvokePattern)Find(helpRoot, ControlType.Button, "Copy Chrome settings address").GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            Require(Clipboard.GetText() == "chrome://inspect/#remote-debugging", "CHROME_COPY_FAILED");
            stage = "CLOSE_HELP";
            ((InvokePattern)Find(helpRoot, ControlType.Button, "Close browser help").GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            Require(invocation.Wait(2000), "HELP_INVOKE_NOT_SETTLED");
            Wait(() => !Native.IsWindowVisible(dialog) && Unique(Root(), ControlType.Button, "Browser help"), "CLOSE_HELP_POSTCONDITION_FAILED");
            facts["manualCreateAddSave"] = true; facts["helpCopy"] = true; facts["result"] = "UI_FACTS_PASSED_IMAGES_NEED_PIXEL_REVIEW";
        }
        catch (Exception ex) { facts["errorType"] = ex.GetType().Name; facts["failureStage"] = stage; facts["errorCode"] = ex is GuardFailure guard ? guard.Code : "UNEXPECTED_EXCEPTION"; }
        finally
        {
            // Exact owned app only. No browser/process enumeration kill. Preserve fresh dummy store for evidence, do not delete it.
            if (!process.HasExited) { process.Kill(false); facts["ownedAppTerminated"] = process.WaitForExit(5000); }
            else facts["ownedAppTerminated"] = true;
            File.WriteAllText(Path.Combine(output, "manual-result.json"), JsonSerializer.Serialize(facts, new JsonSerializerOptions { WriteIndented = true }));
        }
        return Equals(facts["result"], "UI_FACTS_PASSED_IMAGES_NEED_PIXEL_REVIEW") && Equals(facts["ownedAppTerminated"], true) ? 0 : 3;
    }
    sealed class GuardFailure(string code) : Exception { internal string Code { get; } = code; }
    static void Require(bool ok, string code) { if (!ok) throw new GuardFailure(code); }
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
            // Bounded render observation after semantic postconditions. Reject a still-white client instead of publishing it.
            var timer = Stopwatch.StartNew();
            do
            {
                Require(Native.GetForegroundWindow() == hwnd, "CAPTURE_CHANGED_STOP");
                Require(Native.DwmFlush() == 0, "CAPTURE_COMPOSITOR_STOP");
                Require(Native.BitBlt(memory, 0, 0, w, h, dc, r.Left, r.Top, 0x00CC0020) && Native.GetForegroundWindow() == hwnd, "CAPTURE_CHANGED_STOP");
                var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                var pixels = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                byte[] bytes = new byte[w * h * 4]; pixels.CopyPixels(bytes, w * 4, 0);
                int ink = 0;
                for (int y = 45; y < h - 20; y += 3) for (int x = 20; x < w - 20; x += 3)
                { int i = (y * w + x) * 4; if (bytes[i] < 180 && bytes[i + 1] < 180 && bytes[i + 2] < 180) ink++; }
                if (ink >= 100)
                {
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); using var f = File.Create(path); encoder.Save(f); return;
                }
                Thread.Sleep(100);
            } while (timer.ElapsedMilliseconds < 3000);
            Require(false, "CAPTURE_CLIENT_NOT_RENDERED");
        }
        finally { if (old != 0) Native.SelectObject(memory, old); if (bitmap != 0) Native.DeleteObject(bitmap); if (memory != 0) Native.DeleteDC(memory); if (dc != 0) Native.ReleaseDC(0, dc); }
    }
}
internal static class Native
{
    internal delegate bool EnumProc(nint hwnd, nint arg);
    [DllImport("dwmapi.dll")] internal static extern int DwmFlush();
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
