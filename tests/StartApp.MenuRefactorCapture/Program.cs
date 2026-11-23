using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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
    static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var x in Tree(VisualTreeHelper.GetChild(root,i)))yield return x;
    }
    static object? Invoke(object target,string name,params object?[] args)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,args);
    static async Task Settle(Window w)
    {
        w.UpdateLayout();await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(180);
    }
    static ContextMenu OpenMenu()=>PresentationSource.CurrentSources.Cast<PresentationSource>().Select(s=>s.RootVisual).Where(s=>s is not null).SelectMany(s=>Tree(s!)).OfType<ContextMenu>().First(m=>m.IsOpen);
    static Button Button(Window w,string name)=>Tree(w).OfType<Button>().First(b=>System.Windows.Automation.AutomationProperties.GetName(b)==name);
    [STAThread]static int Main(string[] args)
    {
        if(args.Length!=1||Directory.Exists(args[0]))return 2;
        var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
        RenderOptions.ProcessRenderMode=RenderMode.SoftwareOnly;
        var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/StartApp.App;component/SharedResources.xaml")});
        using var repo=new JsonStore(Path.Combine(output,"fixture","store.json"));
        var vm=new MainViewModel(repo,new NotImplementedLauncher());
        var window=new MainWindow(new UpdateViewModel(new InactiveUpdateSource(),null,_=>throw new Exception("Unexpected browser"))){DataContext=vm,Width=960,Height=650,Left=12,Top=12};
        var code=0;var results=new List<object>();
        async Task Save(string name,ContextMenu menu,MenuItem? submenu=null)
        {
            await Settle(window);
            if(!menu.IsOpen)throw new Exception("Menu closed before capture:"+name);
            FrameworkElement target=submenu?.Template.FindName("PART_Popup",submenu) is System.Windows.Controls.Primitives.Popup p?(FrameworkElement)p.Child:menu;
            var width=(int)Math.Ceiling(target.ActualWidth);var height=(int)Math.Ceiling(target.ActualHeight);
            if(width<=0||height<=0)throw new Exception("Empty menu: "+name);
            var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(target);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(Path.Combine(output,name+".png")))png.Save(file);
            var point=target.PointToScreen(new Point(0,0));
            var items=(submenu?.Items??menu.Items).OfType<MenuItem>().Select(i=>new{header=i.Header?.ToString(),enabled=i.IsEnabled,checkable=i.IsCheckable,selected=i.IsChecked,children=i.Items.Count}).ToArray();
            results.Add(new{name,width,height,x=point.X,y=point.Y,items});
        }
        window.ContentRendered+=async(_,_)=>
        {
            try
            {
                vm.NewName="Menu fixture";vm.CreateWorkspaceCommand.Execute(null);vm.BackCommand.Execute(null);await Settle(window);
                foreach(var entry in new[]{("options","Options menu","OptionsMenu_Click"),("view","View menu","ViewMenu_Click"),("help","Help menu","HelpMenu_Click"),("capture","Capture","CaptureMenu_Click")})
                {
                    var b=Button(window,entry.Item2);Invoke(window,entry.Item3,b,new RoutedEventArgs());await Settle(window);var m=OpenMenu();await Save(entry.Item1,m);
                    if(entry.Item1=="view"){var sub=m.Items.OfType<MenuItem>().First();sub.IsSubmenuOpen=true;await Save("view-submenu",m,sub);sub.IsSubmenuOpen=false;}
                    m.IsOpen=false;await Settle(window);
                }
                var host=Tree(window).OfType<FrameworkElement>().First(e=>System.Windows.Automation.AutomationProperties.GetName(e)=="Workspace actions");
                var menu=host.ContextMenu;Invoke(window,"RowContextMenu_Opening",host,(ContextMenuEventArgs)Activator.CreateInstance(typeof(ContextMenuEventArgs),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{host,true,0d,0d},null)!);menu.IsOpen=true;await Save("workspace-row",menu);menu.IsOpen=false;
                // The shared Add-to builder is exercised with a synthetic source, without invoking mutations.
                var source=new SavedItemTransfer(ItemType.Link,"Fixture item","https://example.com/","",null);
                Invoke(window,"ShowSavedTransferActions",Button(window,"Capture"),source);await Settle(window);menu=OpenMenu();await Save("saved-add",menu);
                var add=menu.Items.OfType<MenuItem>().Single();add.IsSubmenuOpen=true;await Save("saved-add-submenu",menu,add);add.IsSubmenuOpen=false;menu.IsOpen=false;
                File.WriteAllText(Path.Combine(output,"menus.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
            }
            catch(Exception e){code=1;File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());}
            finally{window.Close();app.Shutdown(code);}
        };
        app.Run(window);return code;
    }
}
