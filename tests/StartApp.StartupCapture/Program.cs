using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using StartApp.App;
using StartApp.Core;
using StartApp.Presentation;
using StartApp.Platform.Windows;
using StartApp.Storage.Json;
internal static class Program
{
    sealed class Readiness(bool online) : ISignInReadiness { public Task<bool> WaitAsync(CancellationToken token)=>Task.FromResult(online); }
    sealed class Launcher : IItemLauncher {public List<string> Sent=new();public LaunchResult Launch(Item i,LaunchConfig? c){Sent.Add(i.Name);return new(LaunchStatus.Started);}}
    sealed class Ready : IItemPreflight {public PreflightFinding Check(Item i,LaunchConfig? c)=>new(PreflightState.Ready);}
    [STAThread] static int Main(string[] args)
    {
        var output=Path.GetFullPath(args.Single());Directory.CreateDirectory(output);var checks=new List<object>();var code=0;var started=false;
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");var original=key.GetValue("Start App");
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("pack://application:,,,/StartApp.App;component/SharedResources.xaml")});
        using var repo=new JsonStore(Path.Combine(output,"fixture","store.json"));repo.Mutate(c=>{var w=c.CreateWorkspace("סביבת עבודה לדוגמה");c.AddToWorkspace(w.Id,c.GetOrAddItem(ItemType.Link,"אתר לדוגמה","https://example.org/").Id);c.AddToWorkspace(w.Id,c.GetOrAddItem(ItemType.File,"קובץ מקומי",@"C:\test.txt").Id);c.Store.DefaultWorkspaceId=w.Id;});
        var launcher=new Launcher();var vm=new MainViewModel(repo,launcher,new Ready());var registration=new WindowsStartupRegistration(Environment.ProcessPath!);registration.SetEnabled(false);vm.ConfigureWindowsStartup(registration);
        var window=new MainWindow(new UpdateViewModel(new InactiveUpdateSource(),null,_=>{})) {Width=960,Height=650,DataContext=vm};
        void Check(string name,bool ok){checks.Add(new{name,ok});if(!ok)throw new Exception(name);}
        window.ContentRendered+=async(_,_)=>{if(started)return;started=true;try{
            vm.OpenSettingsCommand.Execute(null);await Settle(window);Check("Windows startup defaults off",!vm.StartsWithWindows);await Take(window,output,"01-startup-default");
            var toggle=Descendants(window).OfType<CheckBox>().Single(c=>System.Windows.Automation.AutomationProperties.GetName(c)=="Start with Windows");((IToggleProvider)new CheckBoxAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)).Toggle();await Settle(window);
            Check("Real checkbox writes installer-compatible Run value",registration.IsEnabled()&&((string)key.GetValue("Start App")!)==WindowsStartupRegistration.Command(Environment.ProcessPath!));await Take(window,output,"02-startup-enabled");
            window.Width=720;window.Height=400;await Take(window,output,"03-startup-minimum");window.Width=960;window.Height=650;
            vm.CloseSettingsCommand.Execute(null);vm.StartSignInStartup(new Readiness(false));await vm.SignInTask;Check("Offline timeout launches local only",launcher.Sent.SequenceEqual(new[]{"קובץ מקומי"}));Check("Offline site appears in results",vm.LastOpeningResults.Any(r=>r.Name=="אתר לדוגמה"&&r.Outcome==OpeningOutcome.Skipped));vm.ShowLastOpeningResultsCommand.Execute(null);await Take(window,output,"04-offline-results");vm.CloseLastOpeningResultsCommand.Execute(null);
            vm.OpenSettingsCommand.Execute(null);vm.StartsWithWindows=false;Check("Toggle removes shared Run registration",key.GetValue("Start App")==null);
            var second=new MainViewModel(repo,new Launcher(),new Ready());second.ConfigureWindowsStartup(registration);Check("New session reads Windows not stale JSON",!second.StartsWithWindows);
            var picker=new DefaultEnvPickerWindow(vm){Owner=window};picker.Show();await Take(picker,output,"05-existing-picker");picker.Close();
            vm.KeepRunningInTray=false;window.Close();
        }catch(Exception e){code=1;File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());}finally{if(original is null)key.DeleteValue("Start App",false);else key.SetValue("Start App",original);File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(code);}};app.Run(window);return code;
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject root){for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var c=VisualTreeHelper.GetChild(root,i);yield return c;foreach(var d in Descendants(c))yield return d;}}
    static async Task Settle(FrameworkElement w){w.UpdateLayout();await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(150);}
    static async Task Take(FrameworkElement w,string output,string name){await Settle(w);var b=new RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth),(int)Math.Ceiling(w.ActualHeight),96,96,PixelFormats.Pbgra32);b.Render(w);var p=new PngBitmapEncoder();p.Frames.Add(BitmapFrame.Create(b));using var f=File.Create(Path.Combine(output,name+".png"));p.Save(f);}
}
