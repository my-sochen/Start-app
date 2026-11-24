using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MenuItem = System.Windows.Controls.MenuItem;
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Controls.ContextMenu;
using StartApp.App;
using StartApp.Core;
using StartApp.Platform.Windows;
using StartApp.Presentation;
using StartApp.Storage.Json;

internal static class Program
{
    private sealed class RecordingRevealer : IFileRevealer
    {
        public List<string> Calls=new();
        public Task<RevealResult> RevealAsync(string path) {Calls.Add(path);return Task.FromResult(new RevealResult(RevealStatus.Revealed));}
    }
    private sealed class Capture(string file,string program) : IOpenItemCapturer
    {
        public CaptureScan Scan()=>new(new[]{new CapturedItem(ItemType.File,"קובץ שנוסף",file),new CapturedItem(ItemType.Program,"תוכנה שנוספה",program),new CapturedItem(ItemType.Folder,"תיקייה",Path.GetDirectoryName(file)!),new CapturedItem(ItemType.Link,"אתר","https://example.com/")},Array.Empty<string>());
    }
    [STAThread] private static int Main(string[] args)
    {
        var output=Path.GetFullPath(args.Single());Directory.CreateDirectory(output);var checks=new List<object>();var code=0;
        void Check(string name,bool ok){checks.Add(new{name,ok});if(!ok)throw new Exception(name);}
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/StartApp.App;component/SharedResources.xaml")});
        var directory=Path.Combine(output,"reveal fixture, שלום");Directory.CreateDirectory(directory);
        var file=Path.Combine(directory,"מסמך & report.txt");File.WriteAllText(file,"fixture");
        var program=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"notepad.exe");
        using var repo=new JsonStore(Path.Combine(output,"store","store.json"));
        repo.Mutate(c=>{var w=c.CreateWorkspace("סביבת בדיקה");foreach(var (type,target) in new[]{(ItemType.File,file),(ItemType.Program,program),(ItemType.Folder,directory),(ItemType.Link,"https://example.com/")})c.AddToWorkspace(w.Id,c.GetOrAddItem(type,type.ToString(),target).Id);});
        var adapter=new RecordingRevealer();var vm=new MainViewModel(repo,new NotImplementedLauncher(),capturer:new Capture(file,program),fileRevealer:adapter){ScanMinVisible=TimeSpan.Zero};
        var window=new MainWindow(new UpdateViewModel(new InactiveUpdateSource(),null,_=>throw new Exception("unexpected browser"))){Width=960,Height=650,DataContext=vm};
        window.ContentRendered+=async(_,_)=>{
            try
            {
                vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());await Idle(window);
                foreach(var row in vm.Items.ToArray())
                {
                    var host=Descendants(window).OfType<Grid>().Single(x=>ReferenceEquals(x.DataContext,row)&&x.ContextMenu is not null);host.BringIntoView();await Idle(window);
                    // Same dispatcher path as real row ContextMenuOpening, including shared menu construction.
                    typeof(MainWindow).GetMethod("ShowItemActions",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(window,new object?[]{Descendants(host).OfType<Button>().First(),vm.AllItemFromDetail(row)!,row});await Idle(window);
                    var menu=OpenMenu();var action=menu.Items.OfType<MenuItem>().SingleOrDefault(x=>Equals(x.Header,"הצג בתיקייה"));
                    Check(row.Type+" saved menu eligibility",(action is not null)==(row.Type is "File" or "Program"));
                    if(action is not null){Check(row.Type+" action enabled",action.IsEnabled);await Take(menu,output,row.Type+"-saved-menu");((IInvokeProvider)new MenuItemAutomationPeer(action).GetPattern(PatternInterface.Invoke)).Invoke();await Idle(window);Check(row.Type+" action sends exact path",adapter.Calls.Last()==row.Target);}
                    menu.IsOpen=false;
                }
                vm.OpenAllItemsCommand.Execute(null);await Idle(window);var all=vm.AllItemRows.Single(r=>r.Type=="File");
                var anchor=Descendants(window).OfType<Button>().First(x=>x.IsVisible);window.ShowAllItemActions(anchor,all);await Idle(window);
                Check("All items shared menu has action",OpenMenu().Items.OfType<MenuItem>().Any(x=>Equals(x.Header,"הצג בתיקייה")));await Take(OpenMenu(),output,"all-items-menu");OpenMenu().IsOpen=false;
                vm.CloseAllItemsCommand.Execute(null);vm.CaptureExistingCommand.Execute(null);await vm.CurrentScan!;vm.CapturePickWorkspaceCommand.Execute(vm.Workspaces.Single());
                // Stage four independent fixture rows directly through the capture's normal duplicate confirmation.
                vm.CaptureAddCommand.Execute(null);if(vm.CaptureDupGroupPending){foreach(var c in vm.CaptureDupChoices)c.IsChecked=true;vm.ConfirmCaptureDupGroupCommand.Execute(null);}await Idle(window);
                var before=JsonSerializer.Serialize(repo.Load());
                foreach(var row in vm.StagedRows.ToArray())
                {
                    var host=Descendants(window).OfType<StackPanel>().Single(x=>ReferenceEquals(x.DataContext,row)&&x.ContextMenu is not null);host.BringIntoView();await Idle(window);
                    var menu=host.ContextMenu!;menu.PlacementTarget=host;menu.DataContext=row;menu.IsOpen=true;await Idle(window);
                    var action=menu.Items.OfType<MenuItem>().Single(x=>Equals(x.Header,"הצג בתיקייה"));
                    Check(row.Type+" staged menu eligibility",action.IsVisible==(row.Type is ItemType.File or ItemType.Program));
                    if(action.IsVisible){Check(row.Type+" staged enabled",action.IsEnabled);await Take(menu,output,row.Type+"-staged-menu");((IInvokeProvider)new MenuItemAutomationPeer(action).GetPattern(PatternInterface.Invoke)).Invoke();await Idle(window);Check(row.Type+" staged exact path",adapter.Calls.Last()==row.Target);}
                    menu.IsOpen=false;
                }
                Check("Revealing never saves draft",before==JsonSerializer.Serialize(repo.Load())&&vm.CaptureDirty);
                // Real Explorer adapter separately: COM inspection confirms the selected shell item's path.
                var real=new WindowsFileRevealer();Check("Real Explorer adapter confirms selection",(await real.RevealAsync(file)).Status==RevealStatus.Revealed);
                var selected=ExplorerSelected(file);
                Desktop(output);
                File.WriteAllText(Path.Combine(output,"explorer-processes.json"),JsonSerializer.Serialize(System.Diagnostics.Process.GetProcessesByName("explorer").Select(p=>new{p.Id,p.MainWindowTitle,Handle=p.MainWindowHandle.ToInt64()})));
                Check("Explorer actually selects exact spaced Unicode file",selected);
                await Take(window,output,"capture-window");
                File.WriteAllText(Path.Combine(output,"real-explorer.txt"),"Confirmed exact selected file through Shell.Application windows: "+file);
            }
            catch(Exception e){code=1;File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());}
            finally{File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(code);}
        };app.Run(window);return code;
    }
    private static bool ExplorerSelected(string path)
    {
        object? shell=null,windows=null;
        try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);dynamic s=shell!;windows=s.Windows();dynamic ws=windows;for(var i=0;i<ws.Count;i++){object? w=null,document=null,items=null;try{w=ws.Item(i);dynamic win=w;document=win.Document;dynamic doc=document!;items=doc.SelectedItems();dynamic selected=items;for(var j=0;j<selected.Count;j++){object? item=null;try{item=selected.Item(j);dynamic value=item;if(string.Equals((string)value.Path,path,StringComparison.OrdinalIgnoreCase))return true;}finally{Release(item);}}}catch(COMException){}finally{Release(items);Release(document);Release(w);}}return false;}finally{Release(windows);Release(shell);}
    }
    private static void Desktop(string output)
    {
        var bounds=System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        using var image=new System.Drawing.Bitmap(bounds.Width,bounds.Height);
        using(var g=System.Drawing.Graphics.FromImage(image))g.CopyFromScreen(bounds.Left,bounds.Top,0,0,image.Size);
        image.Save(Path.Combine(output,"explorer-desktop.png"),System.Drawing.Imaging.ImageFormat.Png);
    }
    private static void Release(object? value){if(value is not null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
    private static ContextMenu OpenMenu()=>PresentationSource.CurrentSources.Cast<PresentationSource>().Select(x=>x.RootVisual).OfType<DependencyObject>().SelectMany(x=>new[]{x}.Concat(Descendants(x))).OfType<ContextMenu>().First(x=>x.IsOpen);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root){for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var c=VisualTreeHelper.GetChild(root,i);yield return c;foreach(var d in Descendants(c))yield return d;}}
    private static async Task Idle(Window w){w.UpdateLayout();await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(200);}
    private static async Task Take(FrameworkElement element,string output,string name){element.UpdateLayout();await element.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(250);var b=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);b.Render(element);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(b));using var f=File.Create(Path.Combine(output,name+".png"));png.Save(f);}
}
