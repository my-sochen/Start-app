using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Application=System.Windows.Application;
using TextBox=System.Windows.Controls.TextBox;
using FlowDirection=System.Windows.FlowDirection;
using StartApp.App;
using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;

internal static class Program
{
 sealed class Capture:IOpenItemCapturer { public CaptureScan Scan()=>new(new[]{new CapturedItem(ItemType.Link,"אתר ללכידה","https://capture.example.com/"),new CapturedItem(ItemType.File,"מסמך הפרויקט",@"C:\Project files\שלום.txt")},Array.Empty<string>()); }
 static IEnumerable<DependencyObject> Tree(DependencyObject x){yield return x;for(int i=0;i<VisualTreeHelper.GetChildrenCount(x);i++)foreach(var c in Tree(VisualTreeHelper.GetChild(x,i)))yield return c;}
 static async Task Take(Window w,string output,string name){w.UpdateLayout();await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(250);var point=w.PointToScreen(new System.Windows.Point());using(var image=new System.Drawing.Bitmap((int)w.ActualWidth,(int)w.ActualHeight)){using(var graphics=System.Drawing.Graphics.FromImage(image))graphics.CopyFromScreen((int)point.X,(int)point.Y,0,0,image.Size);image.Save(Path.Combine(output,name+"-screen.png"));}var b=new RenderTargetBitmap((int)w.ActualWidth,(int)w.ActualHeight,96,96,PixelFormats.Pbgra32);b.Render(w);var p=new PngBitmapEncoder();p.Frames.Add(BitmapFrame.Create(b));using var f=File.Create(Path.Combine(output,name+".png"));p.Save(f);}
 [STAThread]static int Main(string[] args)
 {
  var output=Path.GetFullPath(args.Single());Directory.CreateDirectory(output);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/StartApp.App;component/SharedResources.xaml")});
  using var repo=new JsonStore(Path.Combine(output,"fixture","store.json"));repo.Mutate(c=>{var w=c.CreateWorkspace("סביבת עבודה לדוגמה");c.AddToWorkspace(w.Id,c.GetOrAddItem(ItemType.Link,"אתר הפרויקט","https://example.com/").Id);});
  var vm=new MainViewModel(repo,new NotImplementedLauncher(),capturer:new Capture()){ScanMinVisible=TimeSpan.Zero};var shell=new MainWindow(new UpdateViewModel(new InactiveUpdateSource(),null,_=>throw new Exception("Unexpected browser"))){DataContext=vm,Width=960,Height=650};var checks=new List<object>();int code=0;
  void Check(string n,bool ok){checks.Add(new{name=n,ok});if(!ok)throw new Exception(n);}
  void Prose(Window w){foreach(var t in Tree(w).OfType<TextBlock>().Where(t=>t.IsVisible&&t.Text.Any(c=>c>='א'&&c<='ת')&&t.TextWrapping==TextWrapping.Wrap))Check(w.GetType().Name+" prose right: "+t.Name,t.TextAlignment==(t.FlowDirection==FlowDirection.RightToLeft?TextAlignment.Left:TextAlignment.Right));}
  shell.ContentRendered+=async(_,_)=>{try{
   var diag=new Window{Width=500,Height=240,Title="Alignment calibration"};var stack=new StackPanel();foreach(var flow in new[]{FlowDirection.LeftToRight,FlowDirection.RightToLeft})foreach(var align in new[]{TextAlignment.Left,TextAlignment.Right})stack.Children.Add(new TextBlock{Text=flow+" / "+align+": טקסט בעברית לבדיקה.",FlowDirection=flow,TextAlignment=align,Margin=new Thickness(8),Background=System.Windows.Media.Brushes.White});diag.Content=stack;diag.Show();await Take(diag,output,"00-alignment-calibration");diag.Close();
   vm.OpenSettingsCommand.Execute(null);await Take(shell,output,"01-settings");Prose(shell);vm.CloseSettingsCommand.Execute(null);
   vm.CaptureExistingCommand.Execute(null);await vm.CurrentScan!;await Take(shell,output,"02-capture-picker");Prose(shell);vm.CapturePickWorkspaceCommand.Execute(vm.Workspaces.Single());vm.CaptureAddCommand.Execute(null);await Take(shell,output,"03-capture-staged");Prose(shell);vm.CaptureSaveCommand.Execute(null);vm.CaptureCloseCommand.Execute(null);
   // Isolated editor VM avoids MainWindow's modal event subscribers.
   var editVm=new MainViewModel(repo,new NotImplementedLauncher());editVm.OpenDetailsCommand.Execute(editVm.Workspaces.Single());var row=editVm.Items.Single(x=>x.Target=="https://example.com/");editVm.EditItemCommand.Execute(row);
   var edit=new ItemEditWindow(editVm);edit.Show();await Take(edit,output,"04-item-edit");Prose(edit);
   Check("Single target preserves LTR and left alignment",Tree(edit).OfType<TextBox>().Single(t=>System.Windows.Automation.AutomationProperties.GetName(t)=="Edit item target") is {FlowDirection:FlowDirection.LeftToRight,TextAlignment:TextAlignment.Left});editVm.EditItemTarget="not a URL";editVm.SaveItemEditCommand.Execute(null);await Take(edit,output,"04b-item-error");Prose(edit);edit.Close();await Take(edit,output,"04c-item-unsaved");Prose(edit);edit.Hide();
   var all=editVm.AllItemFromDetail(row)!;var batch=new ItemBatchEditWindow(editVm,all,editVm.ItemActionTargets(all,new[]{editVm.Selected!.Id}));batch.Show();await Take(batch,output,"05-batch-edit");Prose(batch);Tree(batch).OfType<TextBox>().Single(t=>System.Windows.Automation.AutomationProperties.GetName(t)=="Selected item target").Text="invalid URL";batch.Close();await Take(batch,output,"05b-batch-unsaved");Prose(batch);batch.Hide();
   var picker=new ItemWorkspacePickerWindow(repo.Load().Workspaces.ToArray(),"אתר הפרויקט","share");picker.Show();await Take(picker,output,"06-workspace-picker");Prose(picker);picker.Hide();
   var defaults=new DefaultEnvPickerWindow(editVm);defaults.Show();await Take(defaults,output,"07-default-picker");Prose(defaults);defaults.Hide();
   vm.OpenAllItemsCommand.Execute(null);await Take(shell,output,"08-all-items");
  }catch(Exception e){code=1;File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());}finally{File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(code);}};app.Run(shell);return code;
 }
}
