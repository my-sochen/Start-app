using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using StartApp.App;
using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Application=System.Windows.Application;
using Button=System.Windows.Controls.Button;
using ContextMenu=System.Windows.Controls.ContextMenu;
internal static class Program
{
 sealed class Capture:IOpenItemCapturer{public CaptureScan Scan()=>new(new[]{new CapturedItem(ItemType.Link,"אתר עבודה","https://example.com/"),new CapturedItem(ItemType.Folder,"מסמכי הפרויקט",@"C:\Projects")},Array.Empty<string>());}
 static IEnumerable<DependencyObject> Tree(DependencyObject root){yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var c in Tree(VisualTreeHelper.GetChild(root,i)))yield return c;}
 static async Task Idle(Window w){w.UpdateLayout();await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(250);}
 static async Task Click(Window w,string name){var b=Tree(w).OfType<Button>().Single(b=>b.IsVisible&&AutomationProperties.GetName(b)==name);((IInvokeProvider)new ButtonAutomationPeer(b)).Invoke();await Idle(w);}
 [STAThread]static int Main(string[] args)
 {
  if(args.Length!=2||Directory.Exists(args[0]))return 2;var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);var notes=new List<object>();
  var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/StartApp.App;component/SharedResources.xaml")});
  using var repo=new JsonStore(Path.Combine(output,"demo","store.json"));var vm=new MainViewModel(repo,new NotImplementedLauncher(),capturer:new Capture()){ScanMinVisible=TimeSpan.Zero};
  var w=new MainWindow(new UpdateViewModel(new InactiveUpdateSource(),null,_=>throw new Exception("Unexpected browser"))){DataContext=vm,Width=900,Height=600,Left=10,Top=10};int code=0;
  async Task Save(string name,string action,string expected){await Idle(w);var p=w.PointToScreen(new System.Windows.Point(0,0));using var bitmap=new System.Drawing.Bitmap((int)(w.PointToScreen(new System.Windows.Point(w.ActualWidth,w.ActualHeight)).X-p.X),(int)(w.PointToScreen(new System.Windows.Point(w.ActualWidth,w.ActualHeight)).Y-p.Y));using(var g=System.Drawing.Graphics.FromImage(bitmap))g.CopyFromScreen((int)p.X,(int)p.Y,0,0,bitmap.Size);bitmap.Save(Path.Combine(output,args[1]+"-"+name+".png"),System.Drawing.Imaging.ImageFormat.Png);notes.Add(new{name,action,result=expected,expected});}
  w.ContentRendered+=async(_,_)=>{try{
   vm.NewName="סביבת עבודה לדוגמה";vm.CreateWorkspaceCommand.Execute(null);vm.NewTarget="https://example.com/";vm.NewItemName="אתר הפרויקט";vm.AddItemCommand.Execute(null);vm.BackCommand.Execute(null);
   await Save("01-home","נוצרה סביבת הדגמה וחזרנו למסך הסביבות","כרטיס הסביבה מוצג במסך הראשי");
   vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());await Save("02-items","נפתחה הסביבה","פרטי הסביבה והאייטם השמור מוצגים");
   vm.BackCommand.Execute(null);await Click(w,"Options menu");await Save("03-options","נפתח תפריט אפשרויות","התפריט מופיע ליד הכותרת");
   foreach(var source in PresentationSource.CurrentSources.Cast<PresentationSource>().ToArray())if(source.RootVisual is DependencyObject root)foreach(var m in Tree(root).OfType<ContextMenu>())m.IsOpen=false;
   vm.CaptureExistingCommand.Execute(null);await vm.CurrentScan!;vm.CapturePickWorkspaceCommand.Execute(vm.Workspaces.Single());vm.CaptureAddCommand.Execute(null);if(vm.CaptureDupGroupPending)vm.CancelCaptureDupGroupCommand.Execute(null);await Save("04-capture","נפתח מסך לכידה ונוספו פריטי הדגמה להכנה","פריטים מוכנים לשמירה לצד פריטי המקור");
   var first=vm.StagedRows.First();vm.EditStagedCommand.Execute(first);await Save("05-editor","נפתחה עריכת פריט לפני שמירה","שדות העריכה וכפתורי הפעולה מוצגים בתוך רשימת ההכנה");vm.CloseStagedEditCommand.Execute(first);vm.CaptureSaveCommand.Execute(null);vm.CaptureCloseCommand.Execute(null);
   vm.OpenGlobalEnvCommand.Execute(null);await Save("06-lists","נפתחה הסביבה הגלובלית","שלוש הרשימות וכפתורי ההוספה מוצגים");
   vm.CloseGlobalEnvCommand.Execute(null);vm.BackCommand.Execute(null);vm.CaptureExistingCommand.Execute(null);await vm.CurrentScan!;await Save("07-picker","נפתחה בחירת יעד ללכידה","חלון בחירת הסביבה מוצג עם הסביבה הזמינה");vm.CaptureCloseCommand.Execute(null);
   File.WriteAllText(Path.Combine(output,"notes.json"),JsonSerializer.Serialize(new{source=args[1],notes},new JsonSerializerOptions{WriteIndented=true}));
  }catch(Exception e){code=1;File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());}finally{w.DataContext=null;w.Close();app.Shutdown(code);}};app.Run(w);return code;
 }
}
