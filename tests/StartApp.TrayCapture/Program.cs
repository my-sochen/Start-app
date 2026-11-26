using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StartApp.App;
using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;

internal static class Program
{
    [STAThread] private static int Main(string[] args)
    {
        var output=Path.GetFullPath(args.Single());Directory.CreateDirectory(output);var results=new List<object>();var code=0;var started=false;
        var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("pack://application:,,,/StartApp.App;component/SharedResources.xaml") });
        using var repo=new JsonStore(Path.Combine(output,"fixture","store.json"));
        repo.Mutate(c=> {var w=c.CreateWorkspace("סביבת עבודה לדוגמה");c.AddToWorkspace(w.Id,c.GetOrAddItem(ItemType.Link,"בדיקה","https://example.org/").Id);c.CreateWorkspace("סביבה ריקה");});
        var vm=new MainViewModel(repo,new NotImplementedLauncher());
        var window=new MainWindow(new UpdateViewModel(new InactiveUpdateSource(),null,_=>throw new Exception("Unexpected browser"))) { Width=960,Height=650,DataContext=vm };
        void Check(string name,bool ok) { results.Add(new{name,ok});if(!ok)throw new Exception(name); }
        static void Invoke(MenuItem item)=>item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        window.ContentRendered+=async(_,_)=> {
            if(started)return;started=true;
            try {
                Check("Default preference off",!vm.KeepRunningInTray);Check("Native icon initialized",window.Tray is not null);
                vm.OpenSettingsCommand.Execute(null);await Settle(window);await Take(window,output,"01-settings-default");
                var toggle=Descendants(window).OfType<CheckBox>().Single(c=>c.IsVisible && System.Windows.Automation.AutomationProperties.GetName(c)=="Keep running in tray");
                ((IToggleProvider)new CheckBoxAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)).Toggle();await Settle(window);
                Check("Real settings binding saved",vm.KeepRunningInTray && repo.Load().KeepRunningInTray);await Take(window,output,"02-settings-enabled");
                window.Width=720;window.Height=400;await Take(window,output,"03-settings-minimum");window.Width=960;window.Height=650;vm.CloseSettingsCommand.Execute(null);
                window.Close();Check("Close hides without exit",!window.IsVisible && window.Tray is not null);window.Tray!.Restore();Check("Tray restores",window.IsVisible);
                // The screenshot contains the actual shared WPF menu, not a reconstructed mock.
                var menu=window.Tray.BuildMenu();menu.IsOpen=true;await Settle(window);await Take(menu,output,"04-tray-menu");
                var environments=(MenuItem)menu.Items[1];Check("Saved menu includes both workspaces",environments.Items.Count==2);
                Check("Empty workspace disabled",!((MenuItem)environments.Items[1]).IsEnabled);
                Invoke((MenuItem)environments.Items[0]);menu.IsOpen=false;Check("Tray uses production selection dialog",vm.SelectionPending);await Take(window,output,"05-launch-selection");vm.CancelOpenCommand.Execute(null);
                vm.NewWorkspaceDescription="draft preserved";window.Close();Check("Hide preserves draft",!window.IsVisible && vm.CreateDirty && !vm.UnsavedOpen);window.Tray.Restore();
                var dirtyMenu=window.Tray.BuildMenu();Check("Dirty editor disables workspace switch",!((MenuItem)((MenuItem)dirtyMenu.Items[1]).Items[0]).IsEnabled);
                window.RequestTrayExit();Check("Explicit exit guards draft",window.IsVisible && vm.UnsavedOpen);await Take(window,output,"06-exit-guard");vm.CloseUnsavedCommand.Execute(null);
                window.Close();Check("Cancelled explicit exit resets policy",!window.IsVisible && vm.CreateDirty);window.Tray.Restore();
                window.RequestTrayExit();vm.ExitWithoutSavingCommand.Execute(null);Check("Approved full exit disposes icon",window.Tray is null);
                var reopened=new MainViewModel(repo,new NotImplementedLauncher());Check("Preference survives new session",reopened.KeepRunningInTray);
                var second=new MainWindow(new UpdateViewModel(new InactiveUpdateSource(),null,_=>{})) { DataContext=reopened };second.Show();await Settle(second);reopened.KeepRunningInTray=false;second.Close();Check("Off closes normally",second.Tray is null);
                var restores=0;using var first=new SingleInstanceSession(()=>Interlocked.Increment(ref restores));using var duplicate=new SingleInstanceSession(()=>{});
                await Task.Delay(250);Check("Duplicate requests restore instead of second instance",first.IsFirst && !duplicate.IsFirst && restores==1);
            } catch(Exception e) {code=1;File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());}
            finally {File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions {WriteIndented=true}));app.Shutdown(code);}
        };app.Run(window);return code;
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject root) {for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var c=VisualTreeHelper.GetChild(root,i);yield return c;foreach(var d in Descendants(c))yield return d;}}
    static async Task Settle(FrameworkElement w) {w.UpdateLayout();await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(150);}
    static async Task Take(FrameworkElement w,string output,string name) {await Settle(w);var b=new RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth),(int)Math.Ceiling(w.ActualHeight),96,96,PixelFormats.Pbgra32);b.Render(w);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(b));using var f=File.Create(Path.Combine(output,name+".png"));png.Save(f);}
}
