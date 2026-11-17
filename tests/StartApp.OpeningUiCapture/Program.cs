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
internal static class Program
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
    private static readonly List<object> Checks = new();
    private static void Check(string name, bool ok) { Checks.Add(new { name, ok }); if (!ok) throw new Exception(name); }
    [STAThread] private static int Main(string[] args)
    {
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
        window.ContentRendered += async (_, _) =>
        {
            try
            {
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
