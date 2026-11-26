using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StartApp.App;
using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;

// Actual production window and commands, isolated store and deterministic capture data. No real launches.
internal static class Program
{
    private sealed class Ready : IItemPreflight { public PreflightFinding Check(Item item,LaunchConfig? launch)=>new(PreflightState.Ready); }
    private sealed class Launcher : IItemLauncher, IBrowserWindowLauncher {
        public List<string[]> Batches=new();
        public LaunchResult Launch(Item item,LaunchConfig? launch)=>throw new Exception("Unexpected single launch");
        public BrowserWindowTarget ResolveBrowser(Item item,LaunchConfig? launch)=>new(launch?.Browser??"chrome");
        public LaunchResult LaunchWindow(string executable,IReadOnlyList<Item> sites){Batches.Add(sites.Select(x=>x.Name).ToArray());return new(LaunchStatus.Started);}
    }
    [STAThread] private static int Main(string[] args)
    {
        var output=Path.GetFullPath(args.Single());Directory.CreateDirectory(output);
        var results=new List<object>();var code=0;
        var app=new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("pack://application:,,,/StartApp.App;component/SharedResources.xaml") });
        using var repo=new JsonStore(Path.Combine(output,"fixture","store.json"));
        repo.Mutate(c=> {var w=c.CreateWorkspace("סביבת עבודה לדוגמה");foreach(var name in new[]{"דואר","משימות","מסמכים"})c.AddToWorkspace(w.Id,c.GetOrAddItem(ItemType.Link,name,"https://example.org/"+Uri.EscapeDataString(name)).Id);});
        var launcher=new Launcher();
        var vm=new MainViewModel(repo,launcher,new Ready());
        var window=new MainWindow(new UpdateViewModel(new InactiveUpdateSource(),null,_=>throw new Exception("Unexpected browser"))) { Width=960,Height=650,DataContext=vm };
        void Check(string name,bool ok) { results.Add(new{name,ok});if(!ok)throw new Exception(name); }
        Button Button(string automationName)=>Descendants(window).OfType<Button>().Single(b=>b.IsVisible && System.Windows.Automation.AutomationProperties.GetName(b)==automationName);
        async Task Click(string name) { var b=Button(name);Check(name+" enabled",b.IsEnabled);((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)).Invoke();await Settle(window); }
        window.ContentRendered+=async(_,_)=>{
            try
            {
                vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());await Settle(window);
                Check("Arrange existing tabs explicitly disabled on item list",!Button("Arrange existing tabs unavailable").IsEnabled);
                await Take(window,output,"00-item-list-disabled-action");
                window.Width=720;window.Height=400;await Take(window,output,"00-item-list-minimum");
                Check("Disabled action visible on minimum item list",Button("Arrange existing tabs unavailable").IsVisible);
                window.Width=960;window.Height=650;
                vm.OpenWorkspaceForCommand.Execute(vm.Workspaces.Single());await Settle(window);
                Check("New window option defaults off",!vm.DedicatedBrowserWindows && vm.HasOpeningSites);
                var arrange=Button("Arrange existing tabs unavailable");
                Check("Rearrange is outside opening selection",!IsInsideDialog(arrange));
                await Take(window,output,"01-opening-choice");
                var toggle=Descendants(window).OfType<CheckBox>().Single(c=>c.IsVisible && System.Windows.Automation.AutomationProperties.GetName(c)=="Dedicated browser windows");
                ((IToggleProvider)new CheckBoxAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)).Toggle();await Settle(window);
                Check("Actual checkbox toggles production binding",vm.DedicatedBrowserWindows);
                await Take(window,output,"02-dedicated-selected");
                window.Width=720;window.Height=400;await Take(window,output,"03-minimum-size");
                Check("Launch remains visible at minimum size",Button("Confirm opening selection").IsVisible);
                window.Width=960;window.Height=650;await Click("Confirm opening selection");await vm.OpenTask;
                Check("Ordered production runner batch",launcher.Batches.Count==1 && launcher.Batches[0].SequenceEqual(new[]{"דואר","משימות","מסמכים"}));
                Check("Per-site report retained",vm.LastOpeningResults.Count==3 && vm.LastOpeningResults.All(x=>x.Outcome==OpeningOutcome.RequestSent));
                vm.ShowLastOpeningResultsCommand.Execute(null);await Take(window,output,"04-results");
                vm.CloseLastOpeningResultsCommand.Execute(null);vm.OpenWorkspaceForCommand.Execute(vm.Workspaces.Single());
                Check("Fresh opening resets dedicated option",!vm.DedicatedBrowserWindows);

            }
            catch(Exception e) {code=1;File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());}
            finally {File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions {WriteIndented=true}));window.Close();app.Shutdown(code);}
        };
        app.Run(window);return code;
    }
    private static bool IsInsideDialog(DependencyObject element)
    {
        for(var parent=VisualTreeHelper.GetParent(element);parent is not null;parent=VisualTreeHelper.GetParent(parent))
            if(parent is DialogFrame)return true;
        return false;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) {var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var d in Descendants(child))yield return d;}
    }
    private static async Task Settle(Window window) {window.UpdateLayout();await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(150);}
    private static async Task Take(Window window,string output,string name)
    {
        await Settle(window);var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(window);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+".png"));png.Save(file);
    }
}
