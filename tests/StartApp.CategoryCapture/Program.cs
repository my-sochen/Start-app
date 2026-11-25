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

internal static class Program
{
    sealed class Capture : IOpenItemCapturer { public CaptureScan Scan() => new(new[] { new CapturedItem(ItemType.Link, "אתר לדוגמה", "https://example.com/") }, Array.Empty<string>()); }
    static IEnumerable<DependencyObject> Tree(DependencyObject x) { yield return x; for (int i=0;i<VisualTreeHelper.GetChildrenCount(x);i++) foreach(var child in Tree(VisualTreeHelper.GetChild(x,i))) yield return child; }
    static readonly List<object> Checks = new();
    static void Check(string name, bool ok) { Checks.Add(new {name,ok}); if(!ok) throw new Exception(name); }
    static async Task Shot(Window w,string output,string name)
    {
        w.UpdateLayout(); await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle); await Task.Delay(250);
        var point=w.PointToScreen(new System.Windows.Point()); using var image=new System.Drawing.Bitmap((int)w.ActualWidth,(int)w.ActualHeight);
        using(var g=System.Drawing.Graphics.FromImage(image))g.CopyFromScreen((int)point.X,(int)point.Y,0,0,image.Size);
        image.Save(Path.Combine(output,name+".png"));
    }
    static void OpenViaPicker(Window window,MainViewModel vm,string target)
    {
        var picker=Tree(window).OfType<CategoryPicker>().Single(x=>x.IsVisible&&x.Target==target);
        var button=Tree(picker).OfType<Button>().Single(); Check(target+" manager entry enabled",button.IsEnabled&&button.Command!=null&&button.Command.CanExecute(button.CommandParameter));
        button.Command!.Execute(button.CommandParameter); Check(target+" manager opens",vm.CategoryEditOpen);
    }
    [STAThread] static int Main(string[] args)
    {
        var output=Path.GetFullPath(args.Single());Directory.CreateDirectory(output);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/StartApp.App;component/SharedResources.xaml")});
        using var repo=new JsonStore(Path.Combine(output,"store.json")); repo.Mutate(c=>c.CreateWorkspace("סביבת עבודה לדוגמה"));
        var vm=new MainViewModel(repo,new NotImplementedLauncher(),capturer:new Capture()){ScanMinVisible=TimeSpan.Zero};
        var window=new MainWindow(new UpdateViewModel(new InactiveUpdateSource(),null,_=>throw new Exception("Unexpected browser"))){DataContext=vm,Width=1100,Height=760,Left=0,Top=0,WindowStartupLocation=WindowStartupLocation.Manual};int exit=0;
        window.ContentRendered+=async(_,_)=>{try{
            vm.ShowCreateScreen();vm.NewName="טיוטת סביבה חדשה";vm.NewWorkspaceDescription="תיאור שצריך להישאר בטיוטה";
            await Shot(window,output,"01-create-category-picker");OpenViaPicker(window,vm,"create");await Shot(window,output,"02-create-category-manager");
            vm.NewCategoryName="מוזיקה";vm.AddCategoryCommand.Execute(null);Check("duplicate notice shown",vm.DuplicateNameOpen&&vm.NewCategoryName=="מוזיקה");
            await Shot(window,output,"02b-duplicate-category-notice");
            Tree(window).OfType<Button>().Single(x=>x.IsVisible&&System.Windows.Automation.AutomationProperties.GetName(x)=="Acknowledge duplicate category").Command!.Execute(null);
            Check("duplicate acknowledged clears input",!vm.DuplicateNameOpen&&vm.NewCategoryName=="");
            Check("all settled built-ins displayed",vm.CategoryRows.Count(x=>x.IsBuiltIn)==21);
            foreach(var row in vm.CategoryRows.Where(x=>x.IsBuiltIn))Check(row.Name+" protected",!vm.DeleteCategoryCommand.CanExecute(row)&&!vm.BeginRenameCategoryCommand.CanExecute(row));
            vm.PickCategoryCommand.Execute(vm.CategoryRows.Single(x=>x.Name=="צילום"));vm.NewCategoryName="קטגוריה אישית חדשה";vm.AddCategoryCommand.Execute(null);
            Check("create name preserved",vm.NewName=="טיוטת סביבה חדשה");Check("create selection preserved",vm.NewWorkspaceCategory=="צילום");Check("no draft workspace saved",repo.Load().Workspaces.Count==1);
            vm.CloseCategoryPopupCommand.Execute(null);await Shot(window,output,"03-create-after-management");
            // Complete this synthetic draft before navigating to exercise the real committed selection too.
            vm.CreateWorkspaceCommand.Execute(null);Check("created workspace category saved",repo.Load().Workspaces.Single(x=>x.Name=="טיוטת סביבה חדשה").EffectiveCategory=="צילום");
            vm.OpenDetailsCommand.Execute(vm.Workspaces.Single(x=>x.Name=="סביבת עבודה לדוגמה"));vm.EditName="שם שנשאר בטיוטה";
            await Shot(window,output,"04-details-category-picker");OpenViaPicker(window,vm,"details");vm.PickCategoryCommand.Execute(vm.CategoryRows.Single(x=>x.Name=="תכנות"));vm.NewCategoryName="קטגוריה נוספת";vm.AddCategoryCommand.Execute(null);
            Check("details draft preserved",vm.EditName=="שם שנשאר בטיוטה"&&vm.EditCategory=="תכנות");Check("details not saved by manager",repo.Load().Workspaces.Any(x=>x.Name=="סביבת עבודה לדוגמה"&&x.EffectiveCategory==Workspace.DefaultCategory));
            await Shot(window,output,"05-details-category-manager");vm.CloseCategoryPopupCommand.Execute(null);vm.SaveDetailsCommand.Execute(null);vm.BackCommand.Execute(null);
            vm.CaptureNewCommand.Execute(null);await vm.CurrentScan!;vm.NewName="טיוטת לכידה";window.UpdateLayout();
            await Shot(window,output,"06-capture-category-picker");OpenViaPicker(window,vm,"create");vm.PickCategoryCommand.Execute(vm.CategoryRows.Single(x=>x.Name=="משחק"));
            await Shot(window,output,"07-capture-category-manager");vm.CloseCategoryPopupCommand.Execute(null);Check("capture selection stays draft",vm.NewWorkspaceCategory=="משחק"&&vm.CaptureOpen&&repo.Load().Workspaces.Count==2);
            await Shot(window,output,"08-capture-after-management");
        }catch(Exception e){exit=1;File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());}finally{
            File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(Checks,new JsonSerializerOptions{WriteIndented=true}));
            File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(new{head=Environment.GetEnvironmentVariable("GITHUB_SHA"),run=Environment.GetEnvironmentVariable("GITHUB_RUN_ID")}));app.Shutdown(exit);
        }};app.Run(window);return exit;
    }
}
