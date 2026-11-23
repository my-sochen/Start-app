using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StartApp.App;
using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Application = System.Windows.Application;

// Test-only executable. Actual production MainWindow/XAML, fixed scan adapter and isolated synthetic store.
// Same binary source runs against baseline and candidate on the same Windows machine, no product hooks.
internal static class Program
{
    private sealed class FixedCapture : IOpenItemCapturer
    {
        public CaptureScan Scan() => new(new CapturedItem[] {
            new(ItemType.Link,"Example site","https://example.com/"),
            new(ItemType.Folder,"Project files",@"C:\RefactorFixture"),
            new(ItemType.Program,"Editor",@"C:\RefactorFixture\editor.exe") }, Array.Empty<string>());
    }
    private static readonly List<object> Results = new();
    [STAThread] private static int Main(string[] args)
    {
        if (args.Length != 1 || Directory.Exists(args[0])) return 2;
        var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        // Use the same deterministic WPF software renderer for both products. No text/layout overrides.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var app = new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/StartApp.App;component/SharedResources.xaml") });
        var window = new MainWindow(new UpdateViewModel(new InactiveUpdateSource(), null, _ => throw new Exception("Unexpected browser"))) { Width=960, Height=650, Left=12, Top=12 };
        using var repo = new JsonStore(Path.Combine(output,"fixture","store.json"));
        var vm = new MainViewModel(repo,new NotImplementedLauncher(),capturer:new FixedCapture()) { ScanMinVisible=TimeSpan.Zero };
        window.DataContext=vm;
        var code=0;
        void Check(string name,bool ok) { Results.Add(new{name,ok}); if(!ok)throw new Exception(name); }
        window.ContentRendered += async (_,_) => {
            try {
                vm.NewName="Capture workspace"; vm.CreateWorkspaceCommand.Execute(null); vm.BackCommand.Execute(null);
                vm.CaptureNewCommand.Execute(null); await vm.CurrentScan!; await Settle(window);
                Check("New capture has fixed three selected rows",vm.CaptureRows.Count==3 && vm.CaptureRows.All(r=>r.IsChecked));
                vm.NewName="New captured environment"; await Take(window,output,"01-new-form");
                // New-form name is unsaved; clear it before ordinary close.
                vm.NewName="";vm.CaptureCloseCommand.Execute(null); Check("Close new capture",!vm.CaptureOpen);
                vm.CaptureExistingCommand.Execute(null);await vm.CurrentScan!;await Take(window,output,"02-pick-environment");
                var workspace=vm.Workspaces.Single();vm.CapturePickWorkspaceCommand.Execute(workspace);await Take(window,output,"03-side-empty");
                vm.CaptureAddCommand.Execute(null);Check("Staging does not save",repo.Load().Workspaces.Single().Items.Count==0 && vm.StagedRows.Count==3);
                await Take(window,output,"04-staged-summary");
                var first=vm.StagedRows.First();vm.EditStagedCommand.Execute(first);first.Name="Edited local name";first.Description="Independent staged description";
                await Take(window,output,"05-staged-editor");vm.CloseStagedEditCommand.Execute(first);vm.CaptureSaveCommand.Execute(null);
                Check("Save commits staged rows and keeps capture open",repo.Load().Workspaces.Single().Items.Count==3 && vm.StagedRows.Count==0 && vm.IsCaptureSide);
                await Take(window,output,"06-saved-side");vm.CaptureAddCommand.Execute(null);
                Check("Grouped duplicate warning",vm.CaptureDupGroupPending && vm.CaptureDupChoices.Count==3);await Take(window,output,"07-grouped-duplicates");
                vm.CancelCaptureDupGroupCommand.Execute(null);vm.DropCaptured(vm.CaptureRows.First());
                Check("Single duplicate warning",vm.CaptureDupSinglePending);await Take(window,output,"08-drag-duplicate");
                vm.CancelCaptureDupSingleCommand.Execute(null);vm.CaptureCloseCommand.Execute(null);Check("Close saved capture",!vm.CaptureOpen);
                window.Width=720;window.Height=400;vm.CaptureExistingCommand.Execute(null);await vm.CurrentScan!;vm.CapturePickWorkspaceCommand.Execute(workspace);
                await Take(window,output,"09-minimum-size");
                vm.CaptureCloseCommand.Execute(null);
                File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true}));
            } catch(Exception e){code=1;File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());}
            finally {window.Close();app.Shutdown(code);}
        };
        app.Run(window);return code;
    }
    private static async Task Settle(Window window)
    {
        window.Activate();Keyboard.ClearFocus();window.Focus();window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        await Task.Delay(120);
    }
    private static async Task Take(Window window,string output,string name)
    {
        await Settle(window);
        var width=(int)Math.Ceiling(window.ActualWidth);var height=(int)Math.Ceiling(window.ActualHeight);
        // Prime WPF glyph caches before taking the compared frame. Keep a hard exact baseline-repeat gate.
        for (var i=0;i<3;i++)
        {
            var warm=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);warm.Render(window);
            await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(80);
        }
        var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(window);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var f=File.Create(Path.Combine(output,name+".png")))png.Save(f);
        // Native desktop screenshot as separate evidence: includes actual window/composition and focus.
        var point=window.PointToScreen(new System.Windows.Point(0,0));
        using var bitmap=new System.Drawing.Bitmap(width,height);using(var graphics=System.Drawing.Graphics.FromImage(bitmap))
            graphics.CopyFromScreen((int)point.X,(int)point.Y,0,0,bitmap.Size);
        bitmap.Save(Path.Combine(output,name+"-desktop.png"),System.Drawing.Imaging.ImageFormat.Png);
    }
}
