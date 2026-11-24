using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StartApp.App;
using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Application = System.Windows.Application;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;

// Same source and synthetic data against immutable dev and candidate on one Windows runner.
internal static class Program
{
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    private static readonly List<object> Focus = new();
    private static readonly List<object> Checks = new();
    [STAThread] private static int Main(string[] args)
    {
        if (args.Length != 1 || Directory.Exists(args[0])) return 2;
        var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/StartApp.App;component/SharedResources.xaml") });
        using var repo = new JsonStore(Path.Combine(output, "fixture", "store.json"));
        repo.Mutate(c => {
            var id=c.CreateWorkspace("Field style workspace", "Fixture description", "כללי").Id;
            c.AddToWorkspace(id,c.GetOrAddItem(ItemType.Link,"Example site","https://example.com/").Id);
            c.AddCustomCategory("Fixture category");
        });
        var vm=new MainViewModel(repo,new NotImplementedLauncher());
        var shell=new MainWindow(new UpdateViewModel(new InactiveUpdateSource(),null,_=>throw new Exception("Unexpected browser"))) {DataContext=vm,Width=960,Height=650,Left=12,Top=12};
        var code=0;
        void Check(string name,bool ok){Checks.Add(new{name,ok});if(!ok)throw new Exception(name);}
        shell.ContentRendered += async (_,_) => {
            try {
                vm.ShowCreateScreen(); await Idle(shell);
                var create=Descendants(shell).OfType<CreateWorkspaceView>().Single();
                var input=Descendants(create).OfType<TextBox>().First(x=>x.IsVisible);
                Check("Create field binds",input.GetBindingExpression(TextBox.TextProperty)?.DataItem==vm);
                await Take(shell,output,"01-create");
                input.Focus();await Tabs(shell,"create",5);await FocusedTake(shell,output,"02-create-keyboard-focus");
                await Popup(shell,Descendants(create).OfType<ComboBox>().Single(x=>x.IsVisible),output,"03-create-category",Check);
                input.IsEnabled=false; await Take(shell,output,"04-create-field-disabled");input.IsEnabled=true;
                vm.LeaveCreateScreen();vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());await Idle(shell);
                var details=Descendants(shell).OfType<WorkspaceDetailsView>().Single();
                Check("Details uses selected data",Descendants(details).OfType<TextBox>().Any(x=>x.Text=="Field style workspace"));
                await Take(shell,output,"05-details");
                await Popup(shell,Descendants(details).OfType<ComboBox>().First(x=>x.IsVisible),output,"06-details-category",Check);
                // No MainWindow event handler is attached to the isolated editor VM.
                var editorVm=new MainViewModel(repo,new NotImplementedLauncher());
                editorVm.OpenDetailsCommand.Execute(editorVm.Workspaces.Single());
                var row=editorVm.Items.Single();editorVm.EditItemCommand.Execute(row);
                var editor=new ItemEditWindow(editorVm){Left=30,Top=30};editor.Show();await Idle(editor);
                Check("Single editor fields bound",Inputs(editor).Count()==3 && Input(editor,"Edit item name").Text=="Example site");
                await Take(editor,output,"07-single-editor");
                await TabsFrom(editor,"single-editor","Edit item name",5);await FocusedTake(editor,output,"08-single-keyboard-focus");
                await Popup(editor,Descendants(editor).OfType<ComboBox>().Single(),output,"09-single-open-with",Check);
                Input(editor,"Edit item description").IsEnabled=false;await Take(editor,output,"10-single-disabled");
                Check("Single draft unchanged by focus",!editorVm.ItemEditDirty);editor.Close();
                var all=editorVm.AllItemFromDetail(row)!;
                var targets=editorVm.ItemActionTargets(all,new[]{editorVm.Selected!.Id});
                var batch=new ItemBatchEditWindow(editorVm,all,targets){Left=30,Top=30};batch.Show();await Idle(batch);
                Check("Batch editor fields populated",Inputs(batch).Count()==3 && Input(batch,"Selected item name").Text=="Example site");
                await Take(batch,output,"11-batch-editor");
                await TabsFrom(batch,"batch-editor","Selected item name",5);await FocusedTake(batch,output,"12-batch-keyboard-focus");
                await Popup(batch,Descendants(batch).OfType<ComboBox>().Single(),output,"13-batch-open-with",Check);
                Input(batch,"Selected item description").IsEnabled=false;await Take(batch,output,"14-batch-disabled");batch.Close();
                File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(Checks));
                File.WriteAllText(Path.Combine(output,"focus.json"),JsonSerializer.Serialize(Focus));
            }catch(Exception e){code=1;File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());}
            finally{foreach(Window w in app.Windows.Cast<Window>().ToArray())w.Hide();app.Shutdown(code);}
        };
        app.Run(shell);return code;
    }
    private static async Task FocusedTake(Window window,string output,string name)
    {
        // Keep actual keyboard focus; only text carets are hidden for deterministic evidence.
        var inputs=Inputs(window).ToArray();var brushes=inputs.Select(x=>x.CaretBrush).ToArray();
        foreach(var input in inputs)input.CaretBrush=System.Windows.Media.Brushes.Transparent;
        await Idle(window);await Render(window,output,name);
        for(var i=0;i<inputs.Length;i++)inputs[i].CaretBrush=brushes[i];
    }
    private static async Task Popup(Window window,ComboBox combo,string output,string name,Action<string,bool> check)
    {
        combo.Focus();Key(0x73);await Idle(window); // F4 uses native keyboard dropdown behavior.
        check(name+" opens by F4",combo.IsDropDownOpen);
        var popup=Descendants(combo).OfType<Popup>().Single();
        check(name+" popup rows",popup.IsOpen && popup.Child is FrameworkElement {ActualHeight: >0});
        await Render((FrameworkElement)popup.Child,output,name+"-popup");
        Key(0x1B);await Idle(window);
        check(name+" closes by Escape",!combo.IsDropDownOpen);
    }
    private static async Task Render(FrameworkElement element,string output,string name)
    {
        await element.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(element);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var f=File.Create(Path.Combine(output,name+".png"));encoder.Save(f);
    }
    private static void Key(byte key) { keybd_event(key, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero); }
    private static string Name(IInputElement? e) => e is DependencyObject d ? AutomationProperties.GetName(d) : "<none>";
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var next in Descendants(child)) yield return next;
        }
    }
    private static IEnumerable<TextBox> Inputs(Window w) => Descendants(w).OfType<TextBox>().Where(x => x.IsVisible);
    private static TextBox Input(Window w, string name) => Inputs(w).Single(x => AutomationProperties.GetName(x) == name);
    private static async Task Idle(Window w) { w.Activate(); w.UpdateLayout(); await w.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); await Task.Delay(120); }
    private static async Task TabsFrom(Window w, string state, string name, int count) { Input(w, name).Focus(); await Idle(w); await Tabs(w, state, count); }
    private static async Task Tabs(Window w, string state, int count)
    {
        var sequence = new List<string> { Name(Keyboard.FocusedElement) };
        for (var i = 0; i < count; i++) {
            Key(0x09); await Idle(w);
            if (Keyboard.FocusedElement is not UIElement { IsVisible: true, IsEnabled: true }) throw new Exception("Tab left enabled visible input: " + state);
            sequence.Add(Name(Keyboard.FocusedElement));
        }
        Focus.Add(new { state, sequence });
    }
    private static async Task Take(Window w, string output, string name)
    {
        Keyboard.ClearFocus(); FocusManager.SetFocusedElement(w, null); w.Focus(); await Idle(w);
        RenderTargetBitmap? bitmap = null; byte[]? previous = null; var steady = 0;
        for (var i = 0; i < 30; i++) {
            bitmap = new RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(w);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            steady = previous is not null && previous.AsSpan().SequenceEqual(pixels) ? steady + 1 : 0;
            if (steady >= 3) break;
            previous = pixels; await Idle(w);
        }
        if (steady < 3 || bitmap is null) throw new Exception("Unstable frame: " + name);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var f = File.Create(Path.Combine(output, name + ".png")); encoder.Save(f);
    }
}
