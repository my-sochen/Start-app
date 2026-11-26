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
    private sealed class FixedCapture : IOpenItemCapturer
    {
        public CaptureScan Scan() => new(new CapturedItem[] {
            new(ItemType.Link,"אתר לדוגמה","https://example.com/", @"C:\Browsers\chrome.exe"),
            new(ItemType.Link,"אתר לדוגמה","https://example.com/", @"C:\Browsers\msedge.exe"),
            new(ItemType.Folder,"Project files",@"C:\CaptureFixture") }, Array.Empty<string>());
    }
    [STAThread] private static int Main(string[] args)
    {
        var output=Path.GetFullPath(args.Single());Directory.CreateDirectory(output);
        var results=new List<object>();var code=0;
        var app=new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("pack://application:,,,/StartApp.App;component/SharedResources.xaml") });
        using var repo=new JsonStore(Path.Combine(output,"fixture","store.json"));
        var vm=new MainViewModel(repo,new NotImplementedLauncher(),capturer:new FixedCapture()) { ScanMinVisible=TimeSpan.Zero };
        var window=new MainWindow(new UpdateViewModel(new InactiveUpdateSource(),null,_=>throw new Exception("Unexpected browser"))) { Width=960,Height=650,DataContext=vm };
        void Check(string name,bool ok) { results.Add(new{name,ok});if(!ok)throw new Exception(name); }
        Button Button(string automationName)=>Descendants(window).OfType<Button>().Single(b=>b.IsVisible && System.Windows.Automation.AutomationProperties.GetName(b)==automationName);
        async Task Click(string name) { var b=Button(name);Check(name+" enabled",b.IsEnabled);((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)).Invoke();await Settle(window); }
        window.ContentRendered+=async(_,_)=>{
            try
            {
                vm.NewName="סביבת לכידה";vm.CreateWorkspaceCommand.Execute(null);vm.BackCommand.Execute(null);
                vm.CaptureExistingCommand.Execute(null);await vm.CurrentScan!;vm.CapturePickWorkspaceCommand.Execute(vm.Workspaces.Single());
                vm.CaptureAddCommand.Execute(null);vm.CaptureSaveCommand.Execute(null);
                Check("Initial capture saved",repo.Load().Workspaces.Single().Items.Count==3);
                vm.CaptureAddCommand.Execute(null);await Settle(window);
                Check("Grouped dialog lists all duplicates unchecked",vm.CaptureDupGroupPending && vm.CaptureDupChoices.Count==3 && vm.CaptureDupChoices.All(c=>!c.IsChecked));
                Check("Choices translated with both source browsers",vm.CaptureDupChoices.All(c=>c.Label.StartsWith("הוסף שוב: ")) && vm.CaptureDupChoices.Any(c=>c.Label.Contains("Chrome")) && vm.CaptureDupChoices.Any(c=>c.Label.Contains("Edge")));
                Check("Grouped content RTL",Button("Capture duplicates add ticked").FlowDirection==FlowDirection.RightToLeft);
                Check("Grouped buttons Hebrew",Equals(Button("Capture duplicates add ticked").Content,"הוסף מסומנים") && Equals(Button("Capture duplicates skip").Content,"דלג על הכפילויות"));
                await Take(window,output,"01-grouped-duplicates");
                await Click("Capture duplicates add ticked");Check("No checked choices adds nothing",!vm.CaptureDupGroupPending && vm.StagedRows.Count==0);
                vm.CaptureAddCommand.Execute(null);await Settle(window);
                var choice=Descendants(window).OfType<CheckBox>().Single(c=>c.IsVisible && Equals(c.Content,vm.CaptureDupChoices.First().Label));
                ((IToggleProvider)new CheckBoxAutomationPeer(choice).GetPattern(PatternInterface.Toggle)).Toggle();await Settle(window);
                await Click("Capture duplicates add ticked");Check("Only checked duplicate staged",!vm.CaptureDupGroupPending && vm.StagedRows.Count==1);
                vm.CaptureSaveCommand.Execute(null);Check("Chosen copy saves independently",repo.Load().Workspaces.Single().Items.Count==4);
                vm.CaptureAddCommand.Execute(null);await Settle(window);window.Width=720;window.Height=400;await Take(window,output,"02-grouped-minimum-size");
                await Click("Capture duplicates skip");Check("Skip adds nothing and closes",!vm.CaptureDupGroupPending && vm.StagedRows.Count==0);
                Check("Skip status Hebrew",vm.Status=="הכפילויות לא נוספו");
                window.Width=960;window.Height=650;vm.DropCaptured(vm.CaptureRows.First());await Settle(window);
                Check("Single warning Hebrew includes item name",vm.CaptureDupSinglePending && vm.CaptureDupSingleMessage.Contains("כבר קיים בסביבה") && vm.CaptureDupSingleMessage.Contains("אתר לדוגמה"));
                Check("Single dialog RTL",Button("Capture duplicate add anyway").FlowDirection==FlowDirection.RightToLeft);
                Check("Single buttons Hebrew",Equals(Button("Capture duplicate add anyway").Content,"הוסף בכל זאת") && Equals(Button("Capture duplicate cancel").Content,"ביטול"));
                await Take(window,output,"03-single-duplicate");await Click("Capture duplicate cancel");
                Check("Single cancel adds nothing",!vm.CaptureDupSinglePending && vm.StagedRows.Count==0);
                vm.DropCaptured(vm.CaptureRows.First());await Settle(window);await Click("Capture duplicate add anyway");
                Check("Single confirm stages one copy",!vm.CaptureDupSinglePending && vm.StagedRows.Count==1);
                Check("Changes remain draft",repo.Load().Workspaces.Single().Items.Count==4);
            }
            catch(Exception e) {code=1;File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());}
            finally {File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions {WriteIndented=true}));window.Close();app.Shutdown(code);}
        };
        app.Run(window);return code;
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
