using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using StartApp.App;
using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using TextBox = System.Windows.Controls.TextBox;
using Point = System.Windows.Point;

internal static class Program
{
    sealed class Capture : IOpenItemCapturer { public CaptureScan Scan() => new(new[] { new CapturedItem(ItemType.Link, "אתר לדוגמה", "https://example.com/") }, Array.Empty<string>()); }
    static IEnumerable<DependencyObject> Tree(DependencyObject x) { yield return x; for (int i=0;i<VisualTreeHelper.GetChildrenCount(x);i++) foreach(var child in Tree(VisualTreeHelper.GetChild(x,i))) yield return child; }
    static readonly List<object> Checks = new();
    static void Check(string name,bool ok) { Checks.Add(new{name,ok}); if(!ok)throw new Exception(name); }
    static async Task Shot(Window w,string output,string name)
    {
        w.UpdateLayout(); await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle); await Task.Delay(200);
        var point=w.PointToScreen(new Point()); using var image=new System.Drawing.Bitmap((int)w.ActualWidth,(int)w.ActualHeight);
        using(var g=System.Drawing.Graphics.FromImage(image))g.CopyFromScreen((int)point.X,(int)point.Y,0,0,image.Size);
        image.Save(Path.Combine(output,name+".png"));
    }
    static Button Advanced(Window w) => Tree(w).OfType<Button>().Single(b=>b.IsVisible&&System.Windows.Automation.AutomationProperties.GetName(b)=="Advanced color selection");
    static void Click(Button b)=>b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static async Task Editor(Window parent,string output,string name,bool choose)
    {
        var operation=parent.Dispatcher.InvokeAsync(()=>Click(Advanced(parent)));
        await parent.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        var editor=Application.Current.Windows.OfType<AdvancedColorWindow>().Single();
        var boxes=Tree(editor).OfType<TextBox>().ToArray();
        var hex=boxes.Single(x=>System.Windows.Automation.AutomationProperties.GetName(x)=="Color hex");
        editor.Activate(); hex.Focus(); await editor.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        Check(name+" hex has real keyboard focus",hex.IsKeyboardFocused);
        hex.Text="#12ab34"; hex.Select(3,1); System.Windows.Forms.SendKeys.SendWait("c");
        await editor.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        Check(name+" actual mid-edit hex value chosen="+editor.ChosenColor+" text="+hex.Text+" caret="+hex.SelectionStart,editor.ChosenColor=="#12CB34"&&string.Equals(hex.Text,"#12cb34",StringComparison.OrdinalIgnoreCase));
        Check(name+" actual mid-edit hex caret",hex.SelectionStart==4&&hex.SelectionLength==0);
        hex.Text="#12ab34";
        Check(name+" lowercase normalizes chosen value",editor.ChosenColor=="#12AB34");
        await Shot(editor,output,name);
        var accept=Tree(editor).OfType<Button>().Single(x=>x.Content as string=="בחר צבע");
        Check(name+" valid hex enabled",accept.IsEnabled&&editor.ChosenColor=="#12AB34");
        hex.Text="#NOTHEX";Check(name+" invalid hex disables",!accept.IsEnabled);await Shot(editor,output,name+"-invalid");
        var rgb = boxes.Where(x => x != hex).ToArray();
        var red = rgb.Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == "Red");
        var green = rgb.Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == "Green");
        var blue = rgb.Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == "Blue");
        red.Focus(); await editor.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        Check(name+" RGB has real keyboard focus",red.IsKeyboardFocused);
        red.Text="12"; red.Select(1,1); System.Windows.Forms.SendKeys.SendWait("3");
        await editor.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        Check(name+" RGB mid-edit caret",red.Text=="13"&&red.SelectionStart==2);
        red.Text="013";
        Check(name+" RGB leading zero normalization",red.Text=="13"&&editor.ChosenColor.StartsWith("#0D"));
        red.Text = "255"; green.Text = "128"; blue.Text = "0";
        Check(name+" RGB updates hex",editor.ChosenColor=="#FF8000"&&accept.IsEnabled);
        red.Text="256";Check(name+" invalid RGB disables",!accept.IsEnabled);red.Text="255";
        var hue=Tree(editor).OfType<Slider>().Single();hue.Value=240;
        Check(name+" hue updates selected color",editor.ChosenColor=="#0000FF");
        hex.Text="#45A1C7";
        if(choose) Click(accept); else editor.Close();
        await operation;
    }
    [STAThread]static int Main(string[] args)
    {
        var output=Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/StartApp.App;component/SharedResources.xaml")});
        using var repo=new JsonStore(Path.Combine(output,"store.json")); repo.Mutate(c=>c.CreateWorkspace("סביבת עבודה לדוגמה"));
        var vm=new MainViewModel(repo,new NotImplementedLauncher(),capturer:new Capture()){ScanMinVisible=TimeSpan.Zero};
        var window=new MainWindow(new UpdateViewModel(new InactiveUpdateSource(),null,_=>throw new Exception("Unexpected browser"))){DataContext=vm,Width=1100,Height=760,Left=0,Top=0,WindowStartupLocation=WindowStartupLocation.Manual};
        int exit=0;
        if (args.Length > 1 && args[1] == "--uia-smoke")
        {
            window.ContentRendered += (_, _) => { vm.ShowCreateScreen(); vm.NewName="UIA color consumer"; };
            var timer = new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, _) => { if(File.Exists(Path.Combine(output,"stop"))) { timer.Stop(); app.Shutdown(); } };
            timer.Start(); window.Show(); app.Run(); return 0;
        }
        window.ContentRendered+=async(_,_)=>{try{
            vm.CaptureNewCommand.Execute(null); await vm.CurrentScan!;
            Check("empty capture has no selection",vm.NewWorkspaceColor==""&&!vm.CreateDirty);
            await Editor(window,output,"empty-capture-editor-cancel",false);
            Check("cancel from empty capture preserves empty selection",vm.NewWorkspaceColor==""&&!vm.CreateDirty&&repo.Load().AdvancedColorHistory.Count==0);
            vm.CaptureCloseCommand.Execute(null); Check("empty capture closes without unsaved guard",!vm.CaptureOpen&&!vm.UnsavedOpen);
            vm.ShowCreateScreen();vm.NewName="טיוטת צבע";vm.NewWorkspaceDescription="התיאור נשאר בזמן בחירת צבע";
            window.Width=1280; window.UpdateLayout();
            Check("nine shared base colors",vm.DesignPalette.Count==9);
            await Shot(window,output,"00-nine-base-colors");
            window.Width=1100;
            await Shot(window,output,"01-create-picker");await Editor(window,output,"02-spectrum-create",true);
            Check("advanced create updates draft",vm.NewWorkspaceColor=="#45A1C7"&&vm.NewName=="טיוטת צבע"&&repo.Load().Workspaces.Count==1);
            Check("no history before outer save",repo.Load().AdvancedColorHistory.Count==0);
            await Shot(window,output,"03-create-preview");
            await Editor(window,output,"04-spectrum-cancel",false);Check("cancel editor retains draft",vm.NewWorkspaceColor=="#45A1C7");
            vm.CreateWorkspaceCommand.Execute(null);Check("history committed with create",repo.Load().AdvancedColorHistory.SequenceEqual(new[]{"#45A1C7"}));Check("create color saved",repo.Load().Workspaces.Any(w=>w.Color=="#45A1C7"));vm.BackCommand.Execute(null);
            vm.DesignCardCommand.Execute(vm.Workspaces.First());await Shot(window,output,"05-design-picker");await Editor(window,output,"06-spectrum-design",true);
            Check("design stays draft",vm.DesignColor=="#45A1C7"&&repo.Load().Workspaces.First().Color==null);
            vm.SaveDesignCommand.Execute(null);Check("design saved",repo.Load().Workspaces.First().Color=="#45A1C7");Check("same color history deduplicated",repo.Load().AdvancedColorHistory.Count==1);
            vm.CaptureNewCommand.Execute(null);await vm.CurrentScan!;vm.NewName="לכידה בצבע";
            await Shot(window,output,"07-capture-picker");await Editor(window,output,"08-spectrum-capture",true);
            Check("capture stays draft",vm.NewWorkspaceColor=="#45A1C7"&&repo.Load().Workspaces.Count==2);
            await Shot(window,output,"09-capture-preview");window.Width=720;window.Height=560;await Shot(window,output,"10-capture-narrow");
            vm.CaptureCloseCommand.Execute(null);if(vm.UnsavedOpen)vm.ExitWithoutSavingCommand.Execute(null);
            vm.ShowCreateScreen();await Shot(window,output,"11-create-narrow");vm.LeaveCreateScreen();
            vm.ApplyAdvancedColorCommand.Execute(new AdvancedColorChoice("create","#8899AA"));vm.NewName="discard history";vm.ShowCreateScreen();vm.LeaveCreateScreen();
            if(vm.UnsavedOpen)vm.ExitWithoutSavingCommand.Execute(null);
            Check("discard adds no history",repo.Load().AdvancedColorHistory.SequenceEqual(new[]{"#45A1C7"}));
            window.Width=1100;window.Height=760;vm.Reload();
            var view=Tree(window).OfType<Button>().Single(x=>x.Content as string=="תצוגה");
            typeof(MainWindow).GetMethod("ViewMenu_Click",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(window,new object[]{view,new RoutedEventArgs()});
            await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            var menu=PresentationSource.CurrentSources.Cast<PresentationSource>().Where(x=>x.RootVisual!=null).SelectMany(x=>Tree(x.RootVisual)).OfType<ContextMenu>().Single(x=>x.IsOpen);
            var filter=menu.Items.OfType<MenuItem>().Single(x=>x.Header as string=="סינון");filter.IsSubmenuOpen=true;
            var colors=filter.Items.OfType<MenuItem>().Single(x=>x.Header as string=="לפי צבע");colors.IsSubmenuOpen=true;
            Check("filter nine base plus one advanced",colors.Items.OfType<MenuItem>().Count()==10);
            await Shot(window,output,"12-filter-base-then-history");menu.IsOpen=false;
            File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new{head=typeof(MainWindow).Assembly.GetName().Version?.ToString(),checks=Checks},new JsonSerializerOptions{WriteIndented=true}));
        }catch(Exception ex){exit=1;File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString());}
        finally{File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(Checks,new JsonSerializerOptions{WriteIndented=true}));window.Close();app.Shutdown();}};
        window.Show();app.Run();return exit;
    }
}
