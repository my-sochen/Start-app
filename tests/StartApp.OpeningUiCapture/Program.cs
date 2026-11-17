using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StartApp.App;
using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;

// Separate Windows-only test executable. Real product MainWindow/BAML, isolated synthetic store and adapters.
// No real resource launches, real App.OnStartup or full-page network requests.
internal static partial class Program
{
    private sealed class Launcher : IItemLauncher
    {
        public readonly List<string> Sent = new();
        public LaunchResult Launch(Item item, LaunchConfig? launch) { Sent.Add(item.Target); return new(LaunchStatus.Started); }
    }
    private sealed class Preflight : IItemPreflight
    {
        public PreflightFinding Check(Item item, LaunchConfig? launch) => new(item.Name.Contains("חסר") ? PreflightState.Missing : PreflightState.Ready, "Synthetic test finding");
    }
    private sealed class Detector : IOpenStateDetector
    { public OpenState Detect(Item item, LaunchConfig? launch) => item.Name.Contains("פתוח") ? OpenState.Open : OpenState.Unknown; }
    private sealed class BrowserEvidence : IOpenItemCapturer
    {
        public int Calls;
        public CaptureScan Scan() { Calls++; return new(new[] { new CapturedItem(ItemType.Link, "synthetic address", "https://example.com/") }, Array.Empty<string>()); }
    }
    private static readonly List<object> Checks = new();
    private static void Check(string name, bool ok) { Checks.Add(new { name, ok }); if (!ok) throw new Exception(name); }
    [STAThread] private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--readiness") return Readiness(args[1]);
        if (args.Length == 2 && args[0] == "--native-duplicate") return NativeDuplicate(args[1]);
        if (!OperatingSystem.IsWindows() || args.Length != 1) return 2;
        var output = Path.GetFullPath(args[0]); if (Directory.Exists(output) || File.Exists(output)) return 3;
        Directory.CreateDirectory(output);
        var app = new Application();
        var window = new MainWindow();
        using var store = new JsonStore(Path.Combine(output, "synthetic", "store.json"));
        store.Mutate(c =>
        {
            var ws = c.CreateWorkspace("בדיקת פתיחה בעברית - סביבה לדוגמה");
            foreach (var (name, target) in new[] { ("מסמך פתוח לדוגמה", "C:\\synthetic\\open.txt"), ("מסמך חדש", "C:\\synthetic\\new.txt"), ("קובץ חסר", "C:\\synthetic\\missing.txt") })
                c.AddToWorkspace(ws.Id, c.GetOrAddItem(ItemType.File, name, target).Id);
        });
        var launcher = new Launcher(); var vm = new MainViewModel(store, launcher, new Preflight(), new Detector());
        window.DataContext = vm;
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        window.ContentRendered += async (_, _) =>
        {
            try
            {
                var browser = new BrowserEvidence();
                var factoryCalls = 0;
                var source = new LiveOpenSnapshot(() => { factoryCalls++; return browser; });
                var link = new Item("link", ItemType.Link, "synthetic address", "https://example.com/");
                var snapshot = await source.ObserveAsync(new[] { link }, CancellationToken.None);
                Check("selected link invokes browser route once and retains exact positive evidence", factoryCalls == 1 && browser.Calls == 1 && snapshot.Detect(link, null) == OpenState.Open);
                Check("different page remains Unknown", snapshot.Detect(new Item("other", ItemType.Link, "other", "https://example.com/other"), null) == OpenState.Unknown);
                await source.ObserveAsync(new[] { new Item("file", ItemType.File, "synthetic", "C:\\synthetic\\file.txt") }, CancellationToken.None);
                Check("file-only selection never invokes browser route", factoryCalls == 1 && browser.Calls == 1);
                foreach (var (width,height) in new[]{(900,560),(720,400),(1100,750)})
                {
                    window.Width=width; window.Height=height;
                    vm.OpenWorkspaceCommand.Execute(null); await Idle(window);
                    Check("initial selection visible", vm.SelectionPending && vm.OpeningChoices.All(x=>x.IsChecked));
                    Capture(window,output,$"{width}-selection");
                    foreach(var x in vm.OpeningChoices)x.IsChecked=false; await Idle(window);
                    var confirm=Descendants(window).OfType<Button>().Single(x=>ReferenceEquals(x.Command,vm.ConfirmSelectionCommand));
                    Check("empty selection disabled in actual XAML", !confirm.IsEnabled);
                    vm.OpeningChoices[0].IsChecked=true; vm.OpeningChoices[2].IsChecked=true;
                    vm.ConfirmSelectionCommand.Execute(null); await vm.OpenTask; await Idle(window);
                    Check("only confirmed visible and unchecked",vm.OpenDecisionPending && vm.ReopenChoices.Count==1 && !vm.ReopenChoices[0].IsChecked);
                    Check("duplicate XAML discloses editable address limitation", Descendants(window).OfType<TextBlock>().Any(x=>x.Text.Contains("כתובת שטרם נפתחה")));
                    Capture(window,output,$"{width}-duplicates");
                    var before=launcher.Sent.Count;
                    vm.BackToOpeningSelectionCommand.Execute(null); await Idle(window);
                    Check("back preserves selected snapshot",vm.SelectionPending && !vm.OpeningChoices[1].IsChecked && launcher.Sent.Count==before);
                    vm.ConfirmSelectionCommand.Execute(null); await vm.OpenTask;
                    vm.ConfirmOpenAnywayCommand.Execute(null); await vm.OpenTask; await Idle(window);
                    Check("declined and missing not launched",launcher.Sent.Count==before && vm.LastOpeningResults.Count==2);
                    Check("results not automatic",!vm.LastResultsOpen && vm.ToastPersistent);
                    Capture(window,output,$"{width}-problem-toast");
                    vm.ToastDetailsCommand.Execute(null); await Idle(window);
                    Check("results problems first distinct from declined",vm.LastResultsOpen && vm.LastOpeningResults[0].Outcome==OpeningOutcome.Skipped && vm.LastOpeningResults[1].Outcome==OpeningOutcome.DeclinedReopen);
                    Capture(window,output,$"{width}-results");
                    vm.CloseLastOpeningResultsCommand.Execute(null);
                    vm.OpenWorkspaceCommand.Execute(null); foreach(var x in vm.OpeningChoices)x.IsChecked=false;vm.OpeningChoices[1].IsChecked=true;
                    vm.ConfirmSelectionCommand.Execute(null); await vm.OpenTask; await Idle(window);
                    Check("unknown attempts without duplicate dialog",!vm.OpenDecisionPending && !vm.ToastPersistent && launcher.Sent.Count==before+1);
                    Capture(window,output,$"{width}-success-toast");
                    await Task.Delay(3200); Check("success toast hides",!vm.ToastVisible);
                    vm.NewTarget=" bad ";vm.AddItemCommand.Execute(null);await Idle(window);
                    Check("validation stays inline",vm.AddItemError.Length>0 && !vm.ToastVisible);
                    Capture(window,output,$"{width}-inline-error");
                }
                vm.BackCommand.Execute(null);
                vm.CaptureNewCommand.Execute(null); await vm.CurrentScan!; vm.NewName = " "; vm.CaptureCreateCommand.Execute(null); await Idle(window);
                Check("capture invalid name is visible in real XAML", Descendants(window).OfType<TextBlock>().Any(x=>x.IsVisible && x.Text==vm.CreateError && x.Text.Length>0));
                Capture(window,output,"capture-inline-error");
                // Actual local executable icon success and invalid-path fallback. No URLs are displayed in this harness.
                var iconWindow = new Window { Width=720,Height=400,Title="Icon evidence" };
                var icon=new TargetIcon {Target=Environment.ProcessPath!,ItemType="Program",Width=40,Height=40};
                var fallback=new TargetIcon {Target="C:\\synthetic-missing\\app.exe",ItemType="Program",Width=40,Height=40};
                iconWindow.Content=new StackPanel { Children={new TextBlock{Text="Target program icon and generic fallback"},icon,fallback}};
                iconWindow.Show();await Task.Delay(1500);await Idle(iconWindow);
                Check("target native icon is not null",icon.Source is BitmapSource);Check("missing uses drawing fallback",fallback.Source is DrawingImage);
                Capture(iconWindow,output,"icons-local-and-fallback");iconWindow.Close();
                File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(Checks,new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(0);
            }
            catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(Checks));app.Shutdown(1);}
        };
        return app.Run(window);
    }
    // Focused acceptance: real default browser observation, real WPF dialog, recording launch boundary.
    private static int NativeDuplicate(string directory)
    {
        if (!OperatingSystem.IsWindows()) return 2;
        var output = Path.GetFullPath(directory);
        if (Directory.Exists(output) || File.Exists(output)) return 3;
        Directory.CreateDirectory(output);
        var app = new Application();
        using var store = new JsonStore(Path.Combine(output, "isolated-store", "store.json"));
        store.Mutate(c =>
        {
            var ws = c.CreateWorkspace("בדיקת זיהוי קישור פתוח");
            c.AddToWorkspace(ws.Id, c.GetOrAddItem(ItemType.Link, "קישור שכבר פתוח", "https://example.com/").Id);
            c.AddToWorkspace(ws.Id, c.GetOrAddItem(ItemType.Link, "עמוד אחר שלא נפתח", "https://example.com/native-acceptance-not-open").Id);
        });
        var launcher = new Launcher();
        var vm = new MainViewModel(store, launcher, detector: new LiveOpenSnapshot());
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 560 };
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        window.ContentRendered += async (_, _) =>
        {
            try
            {
                vm.OpenWorkspaceCommand.Execute(null);
                Check("native initial selection all checked", vm.SelectionPending && vm.OpeningChoices.Count == 2 && vm.OpeningChoices.All(x => x.IsChecked));
                vm.ConfirmSelectionCommand.Execute(null); await vm.OpenTask; await Idle(window);
                Check("native matching URL reaches duplicate dialog", vm.OpenDecisionPending);
                Check("only exact matching page is duplicate and default unchecked", vm.ReopenChoices.Count == 1 && vm.ReopenChoices[0].Item.Target == "https://example.com/" && !vm.ReopenChoices[0].IsChecked);
                Check("no launch before duplicate choice", launcher.Sent.Count == 0);
                Check("native dialog editable-address limitation visible", Descendants(window).OfType<TextBlock>().Any(x => x.IsVisible && x.Text.Contains("כתובת שטרם נפתחה")));
                Capture(window, output, "native-duplicate-default");
                vm.BackToOpeningSelectionCommand.Execute(null); await Idle(window);
                Check("native back preserves both selections without launch", vm.SelectionPending && vm.OpeningChoices.Count == 2 && vm.OpeningChoices.All(x => x.IsChecked) && launcher.Sent.Count == 0);
                vm.ConfirmSelectionCommand.Execute(null); await vm.OpenTask; await Idle(window);
                Check("native recheck still detects only matching page", vm.OpenDecisionPending && vm.ReopenChoices.Count == 1 && !vm.ReopenChoices[0].IsChecked);
                vm.ConfirmOpenAnywayCommand.Execute(null); await vm.OpenTask; await Idle(window);
                Check("default skip does not send open URL to launch boundary", launcher.Sent.SequenceEqual(new[] { "https://example.com/native-acceptance-not-open" }));
                Check("default skip outcome is explicit", vm.LastOpeningResults.Any(x => x.Outcome == OpeningOutcome.DeclinedReopen));
                vm.OpenWorkspaceCommand.Execute(null); vm.ConfirmSelectionCommand.Execute(null); await vm.OpenTask; await Idle(window);
                Check("native override begins unchecked", vm.OpenDecisionPending && vm.ReopenChoices.Count == 1 && !vm.ReopenChoices[0].IsChecked);
                vm.ReopenChoices[0].IsChecked = true; await Idle(window);
                Capture(window, output, "native-duplicate-explicit-override");
                vm.ConfirmOpenAnywayCommand.Execute(null); await vm.OpenTask; await Idle(window);
                Check("explicit override sends matching URL once", launcher.Sent.Count(x => x == "https://example.com/") == 1);
                Check("different-page launch boundary reached once per confirmed operation", launcher.Sent.Count(x => x == "https://example.com/native-acceptance-not-open") == 2);
                app.Shutdown(0);
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(output, "error.txt"), e.ToString()); app.Shutdown(1); }
            finally { File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(Checks, new JsonSerializerOptions { WriteIndented = true })); }
        };
        return app.Run(window);
    }
    private static async Task Idle(Window w){await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);w.UpdateLayout();}
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var c=VisualTreeHelper.GetChild(root,i);yield return c;foreach(var n in Descendants(c))yield return n;}}
    private static void Capture(Window window,string output,string name)
    {
        window.UpdateLayout();var width=(int)Math.Ceiling(window.ActualWidth);var height=(int)Math.Ceiling(window.ActualHeight);
        Check(name+" bounds",width>=720 && height>=400);
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,name+".png"));encoder.Save(stream);
    }
}
