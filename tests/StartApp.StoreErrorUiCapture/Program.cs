using System.IO;
using System.Reflection;
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
using Application=System.Windows.Application;
internal static class Program
{
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T t)yield return t;foreach(var inner in Children<T>(child))yield return inner;}}
    static async Task Idle(Window w){await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(150);w.UpdateLayout();}
    static async Task Click(Button b,Window w){Check(b.IsVisible&&b.IsEnabled,"Button unavailable: "+b.Content);((IInvokeProvider)new ButtonAutomationPeer(b)).Invoke();await Idle(w);}
    [STAThread] static int Main(string[] args)
    {
        if(args.Length!=1||Directory.Exists(args[0]))return 2;Directory.CreateDirectory(args[0]);var records=new List<object>();var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/StartApp.App;component/SharedResources.xaml")});int result=0;
        app.Startup+=async(_,_)=>{
            try{
                foreach(var newer in new[]{false,true})foreach(var action in new[]{"saved","add","remove","move"})
                {
                    var name=action+(newer?"-newer":"-corrupt");var path=Path.Combine(args[0],name,"store.json");using var repo=new JsonStore(path);var vm=new MainViewModel(repo,new NotImplementedLauncher());
                    vm.NewName="source";vm.CreateWorkspaceCommand.Execute(null);vm.NewTarget="https://example.com/";vm.NewItemName="test";vm.AddItemCommand.Execute(null);var source=vm.SavedTransferFromDetail(vm.Items.Single())!;
                    var w=new MainWindow(new UpdateViewModel(new InactiveUpdateSource(),null,_=>throw new Exception("Unexpected network"))){DataContext=vm,Width=1100,Height=800};w.Show();await Idle(w);
                    if(action!="saved")
                    {
                        vm.OpenGlobalEnvCommand.Execute(null);await Idle(w);
                        if(action is "remove" or "move"){vm.StartGlobalAddCommand.Execute(action=="move"?GlobalListKind.Blacklist:GlobalListKind.GlobalItems);vm.NewEntryTypeIndex=0;vm.NewEntryName="test";vm.NewEntryTarget="https://example.com/";vm.ConfirmGlobalAddCommand.Execute(null);}
                        if(action is "add" or "move"){vm.StartGlobalAddCommand.Execute(action=="move"?GlobalListKind.Whitelist:GlobalListKind.GlobalItems);vm.NewEntryTypeIndex=0;vm.NewEntryName="draft";vm.NewEntryTarget="https://example.com/";if(action=="move")vm.ConfirmGlobalAddCommand.Execute(null);}
                        await Idle(w);
                    }
                    var bytes=newer?"{\"schemaVersion\":999}":"invalid json";File.WriteAllText(path,bytes);
                    if(action=="saved")
                    {
                        var menu=(MenuItem)typeof(MainWindow).GetMethod("BuildSavedItemAddMenu",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(w,new object[]{vm,source})!;
                        var item=menu.Items.OfType<MenuItem>().Single(m=>(string)m.Header=="סביבה גלובלית");item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await Idle(w);
                    }
                    else
                    {
                        var command=action=="add"?vm.ConfirmGlobalAddCommand:action=="remove"?vm.RemoveGlobalEntryCommand:vm.MoveGlobalConflictCommand;
                        await Click(Children<Button>(w).Single(b=>ReferenceEquals(b.Command,command)&&b.IsVisible),w);
                    }
                    Check(vm.ReadOnly&&vm.ToastVisible&&vm.ToastPersistent,"Not fail closed");Check(File.ReadAllText(path)==bytes,"Store changed");
                    var notification=Children<Border>(w).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Notification");var expander=Children<Expander>(notification).Single();expander.IsExpanded=true;await Idle(w);
                    var details=Children<TextBox>(expander).Single(t=>t.IsReadOnly&&t.Text==vm.ToastTechnicalDetails);
                    Check(details.IsVisible,"Technical details not visible");await Click(Children<Button>(w).Single(b=>ReferenceEquals(b.Command,vm.CopyTechnicalDetailsCommand)&&b.IsVisible),w);
                    Check(Clipboard.GetText()==vm.ToastTechnicalDetails,"Actual clipboard mismatch");if(!newer)Check(vm.ToastTechnicalDetails.Contains(Directory.GetFiles(Path.GetDirectoryName(path)!,"store.json.diag-*").Single()),"Copy path missing");
                    var png=new PngBitmapEncoder();var image=new RenderTargetBitmap((int)w.ActualWidth,(int)w.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(w);png.Frames.Add(BitmapFrame.Create(image));using(var f=File.Create(Path.Combine(args[0],name+".png")))png.Save(f);
                    records.Add(new{name,readonlyState=vm.ReadOnly,notificationVisible=vm.ToastVisible,detailsVisible=details.IsVisible,clipboardExact=true,storeUnchanged=true});w.DataContext=null;w.Close();
                }
                File.WriteAllText(Path.Combine(args[0],"results.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));
            }catch(Exception e){result=1;File.WriteAllText(Path.Combine(args[0],"error.txt"),e.ToString());}finally{app.Shutdown(result);}
        };app.Run();return result;
    }
}
