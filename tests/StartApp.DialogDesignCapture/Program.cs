using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Threading;
using StartApp.App;
using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Application=System.Windows.Application;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
internal static class Program {
 sealed class UpdateSource:IApprovedUpdateSource { public Task<ApprovedUpdate?> ReadAsync(CancellationToken token)=>Task.FromResult<ApprovedUpdate?>(new("0.2.0-beta.1",new string('a',40),new string('b',64))); }
 sealed class Capture:IOpenItemCapturer { public CaptureScan Scan()=>new(new[]{new CapturedItem(ItemType.Link,"אתר לדוגמה","https://example.com/")},Array.Empty<string>()); }
 static readonly List<object> Checks=new();
 static void Check(string name,bool ok){Checks.Add(new{name,ok});if(!ok)throw new Exception(name);}
 static IEnumerable<DependencyObject> Tree(DependencyObject x){yield return x;for(int i=0;i<VisualTreeHelper.GetChildrenCount(x);i++)foreach(var c in Tree(VisualTreeHelper.GetChild(x,i)))yield return c;}
 static async Task Idle(Window w){w.UpdateLayout();await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(200);}
 static async Task Shot(Window w,string output,string name){await Idle(w);var p=w.PointToScreen(new System.Windows.Point());using var b=new System.Drawing.Bitmap((int)w.ActualWidth,(int)w.ActualHeight);using(var g=System.Drawing.Graphics.FromImage(b))g.CopyFromScreen((int)p.X,(int)p.Y,0,0,b.Size);b.Save(Path.Combine(output,name+".png"));}
 static Button Named(Window w,string name)=>Tree(w).OfType<Button>().Single(x=>AutomationProperties.GetName(x)==name);
 static void Click(Button b)=>((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)).Invoke();
 static async Task Modal(Window owner,Window dialog,string output,string name,Func<Window,Task> exercise){dialog.Owner=owner;var op=owner.Dispatcher.InvokeAsync(()=>dialog.ShowDialog());await owner.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Idle(dialog);Check(name+" uses shared frame",dialog.Content is DialogFrame);Check(name+" no native caption",dialog.WindowStyle==WindowStyle.None);var primary=Tree(dialog).OfType<Button>().FirstOrDefault(b=>b.Style==Application.Current.FindResource("DialogPrimaryButton"));if(primary!=null){var right=primary.PointToScreen(new System.Windows.Point(primary.ActualWidth,0)).X;Check(name+" primary aligned physical right",Math.Abs(dialog.PointToScreen(new System.Windows.Point(dialog.ActualWidth,0)).X-right-29)<3);}await Shot(dialog,output,name);await exercise(dialog);await op;Check(name+" restores owner",owner.IsVisible);}
 [STAThread]static int Main(string[] args){var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/StartApp.App;component/SharedResources.xaml")});using var repo=new JsonStore(Path.Combine(output,"fixture/store.json"));repo.Mutate(c=>{var w=c.CreateWorkspace("סביבת עבודה לדוגמה");c.AddToWorkspace(w.Id,c.GetOrAddItem(ItemType.Link,"אתר לדוגמה","https://example.com/").Id);});var vm=new MainViewModel(repo,new NotImplementedLauncher(),capturer:new Capture()){ScanMinVisible=TimeSpan.Zero};using var updates=new UpdateViewModel(new UpdateSource(),"0.2.0-beta.0",_=>throw new Exception("Unexpected browser"));using var shellUpdates=new UpdateViewModel(new InactiveUpdateSource(),null,_=>throw new Exception("Unexpected browser"));var shell=new MainWindow(shellUpdates){DataContext=vm,Width=1000,Height=720,Left=0,Top=0,WindowStartupLocation=WindowStartupLocation.Manual};int exit=0;shell.ContentRendered+=async(_,_)=>{try{
 await updates.CheckAsync();await Modal(shell,new UpdateAvailableWindow(updates),output,"01-update",async d=>{Check("update shared primary",Named(d,"Update via official download").Style==app.FindResource("DialogPrimaryButton"));Click(Named(d,"Close update notice"));await Idle(shell);});
 await Modal(shell,new AdvancedColorWindow("#315CC5"),output,"02-color",async d=>{var input=Tree(d).OfType<TextBox>().Single(x=>AutomationProperties.GetName(x)=="Color hex");input.Text="#bad";var choose=Tree(d).OfType<Button>().Single(x=>x.Content as string=="בחר צבע");Check("invalid color disabled",!choose.IsEnabled);await Shot(d,output,"03-color-invalid");input.Text="#12AB34";Check("valid color enabled",choose.IsEnabled&&((AdvancedColorWindow)d).ChosenColor=="#12AB34");Click(choose);await Idle(shell);});
 var before=File.ReadAllBytes(Path.Combine(output,"fixture/store.json"));
 await Modal(shell,SavedItemPromptWindow.Conflict("הפריט שמור ברשימה אחרת. אפשר להעביר אותו לרשימה המבוקשת."),output,"04-conflict",async d=>{Click(Named(d,"Close dialog"));await Idle(shell);});
 Check("cancel conflict leaves store",before.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(output,"fixture/store.json"))));
 vm.OpenDetailsCommand.Execute(vm.Workspaces.First());var source=vm.SavedTransferFromDetail(vm.Items.Single())!;vm.BackCommand.Execute(null);
 await Modal(shell,new NewWorkspaceFromItemWindow(vm,source),output,"05-new-from-item",async d=>{var input=Tree(d).OfType<TextBox>().Single();var create=Named(d,"Create item environment");Check("empty create disabled",!create.IsEnabled);input.Text="סביבה מהאייטם";Check("named create enabled",create.IsEnabled);await Shot(d,output,"06-new-from-item-ready");Click(create);await Idle(shell);});
 Check("create item environment persisted",repo.Load().Workspaces.Any(x=>x.Name=="סביבה מהאייטם"));
 vm.DismissToastCommand.Execute(null);vm.CaptureNewCommand.Execute(null);await vm.CurrentScan!;vm.NewName="סביבה חדשה מלכידה";await Idle(shell);Check("capture new form shown",vm.IsCaptureNew);await Shot(shell,output,"07-capture-new");
 var field=Tree(shell).OfType<TextBox>().Single(x=>AutomationProperties.GetName(x)=="Capture workspace name");Check("capture styled input",field.Style==app.FindResource("DetailsInput"));Check("capture binds name",field.Text==vm.NewName);field.Text="סביבת לכידה מעוצבת";Check("capture name two way",vm.NewName==field.Text);
 var createCapture=Named(shell,"Create workspace from capture");createCapture.BringIntoView();await Idle(shell);await Shot(shell,output,"07b-capture-new-bottom");Check("capture shared primary",createCapture.Style==app.FindResource("DialogPrimaryButton"));
 vm.CaptureCloseCommand.Execute(null);await Idle(shell);await Shot(shell,output,"08-capture-close-guard");
 }catch(Exception e){exit=1;File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());}finally{File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(Checks));foreach(Window w in app.Windows.Cast<Window>().ToArray())w.Hide();app.Shutdown(exit);}};app.Run(shell);return exit;}
}
