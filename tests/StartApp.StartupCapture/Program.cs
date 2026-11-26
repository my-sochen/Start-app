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
    sealed class Waiting : ISignInReadiness {public TaskCompletionSource<bool> Gate=new();public async Task<bool> WaitAsync(CancellationToken token)=>await Gate.Task.WaitAsync(token);}
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
            window.Width=720;window.Height=400;await Settle(window);Descendants(Descendants(window).OfType<SettingsView>().Single()).OfType<ScrollViewer>().First().ScrollToVerticalOffset(210);await Take(window,output,"03-startup-minimum");window.Width=960;window.Height=650;
            vm.CloseSettingsCommand.Execute(null);vm.StartSignInStartup(new Readiness(false));await vm.SignInTask;Check("Offline timeout launches local only",launcher.Sent.SequenceEqual(new[]{"קובץ מקומי"}));Check("Offline site appears in results",vm.LastOpeningResults.Any(r=>r.Name=="אתר לדוגמה"&&r.Outcome==OpeningOutcome.Skipped));vm.ShowLastOpeningResultsCommand.Execute(null);await Take(window,output,"04-offline-results");vm.CloseLastOpeningResultsCommand.Execute(null);
            vm.OpenSettingsCommand.Execute(null);vm.StartsWithWindows=false;Check("Toggle removes shared Run registration",key.GetValue("Start App")==null);
            var second=new MainViewModel(repo,new Launcher(),new Ready());second.ConfigureWindowsStartup(registration);Check("New session reads Windows not stale JSON",!second.StartsWithWindows);
            var picker=new DefaultEnvPickerWindow(vm){Owner=window};picker.Show();await Take(picker,output,"05-existing-picker");picker.Close();
            // Installer state and portable move use the same actual Windows value.
            key.SetValue("Start App",WindowsStartupRegistration.Command(Environment.ProcessPath!));second.RefreshWindowsStartup();Check("Installer checked state is read by app",second.StartsWithWindows);
            var movedPath=Path.Combine(output,"portable moved עברית","Start App.exe");Directory.CreateDirectory(Path.GetDirectoryName(movedPath)!);File.Copy(Environment.ProcessPath!,movedPath,true);var moved=new WindowsStartupRegistration(movedPath);Check("Moved portable path does not falsely appear enabled",!moved.IsEnabled());moved.SetEnabled(true);Check("Portable toggle repairs quoted path",moved.IsEnabled()&&!registration.IsEnabled());moved.SetEnabled(false);Check("Portable toggle off removes shared entry",key.GetValue("Start App")==null);
            var restores=0;using(var first=new SingleInstanceSession(()=>Interlocked.Increment(ref restores)))using(var duplicate=new SingleInstanceSession(()=>{})){await Task.Delay(250);Check("Second process signal restores without duplicate instance",first.IsFirst&&!duplicate.IsFirst&&restores==1);}
            var trayLauncher=new Launcher();var trayVm=new MainViewModel(repo,trayLauncher,new Ready());trayVm.KeepRunningInTray=true;var trayWindow=new MainWindow(new UpdateViewModel(new InactiveUpdateSource(),null,_=>{})){DataContext=trayVm};trayWindow.Closed+=(_,_)=>trayVm.CancelSignInStartup();trayWindow.Show();await Settle(trayWindow);var trayWait=new Waiting();trayVm.StartSignInStartup(trayWait);trayWindow.Close();Check("Tray hide preserves pending sign-in",!trayWindow.IsVisible&&!trayVm.SignInTask.IsCompleted);trayWait.Gate.SetResult(false);await trayVm.SignInTask;Check("Hidden tray starts local items once",trayLauncher.Sent.SequenceEqual(new[]{"קובץ מקומי"}));trayVm.StartSignInStartup(new Readiness(true));await trayVm.SignInTask;Check("Tray restore cannot trigger sign-in twice",trayLauncher.Sent.Count==1);trayWindow.Tray!.Restore();trayWindow.RequestTrayExit();Check("Explicit tray exit closes window",trayWindow.Tray is null);
            var closingLauncher=new Launcher();var closingVm=new MainViewModel(repo,closingLauncher,new Ready());closingVm.KeepRunningInTray=false;var closingWindow=new MainWindow(new UpdateViewModel(new InactiveUpdateSource(),null,_=>{})){DataContext=closingVm};closingWindow.Closed+=(_,_)=>closingVm.CancelSignInStartup();closingWindow.Show();await Settle(closingWindow);var closingWait=new Waiting();closingVm.StartSignInStartup(closingWait);closingWindow.Close();await closingVm.SignInTask;closingWait.Gate.TrySetResult(true);Check("Actual close cancels wait before launching",closingLauncher.Sent.Count==0&&closingWindow.Tray is null);
            using(var cancel=new CancellationTokenSource()){cancel.CancelAfter(50);var cancelled=false;try{await new SignInReadiness().WaitAsync(cancel.Token);}catch(OperationCanceledException){cancelled=true;}Check("Native readiness accepts cancellation",cancelled);}
            var clock=System.Diagnostics.Stopwatch.StartNew();var nativeOnline=await new SignInReadiness().WaitAsync(CancellationToken.None);Check("Native Windows readiness honors settle and bounded wait",clock.Elapsed>=TimeSpan.FromSeconds(15)&&clock.Elapsed<TimeSpan.FromSeconds(82));File.WriteAllText(Path.Combine(output,"native-readiness.json"),JsonSerializer.Serialize(new{online=nativeOnline,elapsedSeconds=clock.Elapsed.TotalSeconds}));
            vm.KeepRunningInTray=false;window.Close();
        }catch(Exception e){code=1;File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());}finally{if(original is null)key.DeleteValue("Start App",false);else key.SetValue("Start App",original);File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(code);}};app.Run(window);return code;
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject root){for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var c=VisualTreeHelper.GetChild(root,i);yield return c;foreach(var d in Descendants(c))yield return d;}}
    static async Task Settle(FrameworkElement w){w.UpdateLayout();await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(150);}
    static async Task Take(FrameworkElement w,string output,string name){await Settle(w);var b=new RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth),(int)Math.Ceiling(w.ActualHeight),96,96,PixelFormats.Pbgra32);b.Render(w);var p=new PngBitmapEncoder();p.Frames.Add(BitmapFrame.Create(b));using var f=File.Create(Path.Combine(output,name+".png"));p.Save(f);}
}
