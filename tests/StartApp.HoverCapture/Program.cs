using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using StartApp.App;
using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Application=System.Windows.Application;
using Button=System.Windows.Controls.Button;
internal static class Program
{
 [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
 static readonly List<object> Checks=new();
 static void Check(string name,bool ok){Checks.Add(new{name,ok});if(!ok)throw new Exception(name);}
 static IEnumerable<DependencyObject> Tree(DependencyObject x){yield return x;for(int i=0;i<VisualTreeHelper.GetChildrenCount(x);i++)foreach(var c in Tree(VisualTreeHelper.GetChild(x,i)))yield return c;}
 static async Task Wait(Window w,int ms=200){w.UpdateLayout();await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(ms);}
 static void Shot(Window w,string dir,string name){var p=w.PointToScreen(new System.Windows.Point());using var b=new System.Drawing.Bitmap((int)w.ActualWidth,(int)w.ActualHeight);using(var g=System.Drawing.Graphics.FromImage(b))g.CopyFromScreen((int)p.X,(int)p.Y,0,0,b.Size);b.Save(Path.Combine(dir,name+".png"));}
 [STAThread] static int Main(string[] args)
 {
  var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
  app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/StartApp.App;component/SharedResources.xaml")});
  using var repo=new JsonStore(Path.Combine(output,"fixture/store.json"));repo.Mutate(c=>c.CreateWorkspace("סביבת עבודה לדוגמה"));
  var vm=new MainViewModel(repo,new NotImplementedLauncher());using var updates=new UpdateViewModel(new InactiveUpdateSource(),null,_=>{});
  var shell=new MainWindow(updates){DataContext=vm,Width=1000,Height=720,Left=0,Top=0,WindowStartupLocation=WindowStartupLocation.Manual};int exit=0;
  shell.ContentRendered+=async(_,_)=>{try{
   await Wait(shell,500);var b=Tree(shell).OfType<Button>().Single(x=>AutomationProperties.GetName(x)=="Capture");var icon=Tree(b).OfType<CaptureHoverIcon>().Single();
   Check("button44x44",b.ActualWidth==44&&b.ActualHeight==44);Check("icon34x34",Tree(b).OfType<Viewbox>().Single().ActualWidth==34);Check("button clips all motion",b.ClipToBounds);
   var p=b.PointToScreen(new System.Windows.Point());File.WriteAllText(Path.Combine(output,"button-bounds.json"),JsonSerializer.Serialize(new{x=p.X,y=p.Y,width=b.ActualWidth,height=b.ActualHeight}));
   SetCursorPos(900,650);await Wait(shell,600);Shot(shell,output,"01-rest");Check("rest idle",!icon.IsAnimationRunning);
   SetCursorPos((int)p.X+22,(int)p.Y+22);await Wait(shell,450);Check("hover starts ticker",icon.IsAnimationRunning||icon.ReducedMotion);
   for(int i=0;i<30;i++){Shot(shell,output,$"hover-{i:D2}");await Task.Delay(90);}
   SetCursorPos(900,650);await Wait(shell,150);Shot(shell,output,"02-leaving");await Wait(shell,550);Shot(shell,output,"03-settled");Check("leave stops ticker",!icon.IsAnimationRunning&&icon.HoverEnvelope==0);
   SetCursorPos((int)p.X+22,(int)p.Y+22);await Wait(shell,120);SetCursorPos(900,650);await Wait(shell,100);SetCursorPos((int)p.X+22,(int)p.Y+22);await Wait(shell,400);Check("rapid reentry active",icon.IsAnimationRunning||icon.ReducedMotion);SetCursorPos(900,650);await Wait(shell,600);
   icon.ForceReducedMotion=true;SetCursorPos((int)p.X+22,(int)p.Y+22);await Wait(shell,500);Shot(shell,output,"04-reduced-motion");Check("reduced motion static",!icon.IsAnimationRunning&&icon.HoverEnvelope==0);
   SetCursorPos(900,650);icon.ForceReducedMotion=false;await Wait(shell,600);b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await Wait(shell);Check("capture menu works",b.ContextMenu?.IsOpen==true);if(b.ContextMenu!=null)b.ContextMenu.IsOpen=false;
   shell.Hide();await Task.Delay(100);Check("hidden stops ticker",!icon.IsAnimationRunning);
  }catch(Exception e){exit=1;File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());}finally{File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(Checks));shell.Hide();app.Shutdown(exit);}};
  app.Run(shell);return exit;
 }
}
