using Application = System.Windows.Application;
using ComboBox = System.Windows.Controls.ComboBox;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StartApp.App;
using System.Text.Json;
using StartApp.Platform.Windows;
using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
internal static partial class Program
{
    private static async Task Files(Window window,string output,Action<string,bool> check)
    {
        using var store = new JsonStore(Path.Combine(output,"file-fixture","store.json"));
        store.Mutate(c=>{
            var workspace=c.CreateWorkspace("File and folder fixture","","כללי").Id;
            c.AddToWorkspace(workspace,c.GetOrAddItem(ItemType.File,"Text file",@"C:\fixture.txt").Id);
            c.AddToWorkspace(workspace,c.GetOrAddItem(ItemType.Folder,"Folder",@"C:\Fixture").Id);
        });
        var vm = new MainViewModel(store,new NotImplementedLauncher());
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        foreach(var type in new[]{"File","Folder"}){
            var row=vm.Items.Single(x=>x.Type==type);
            check(type+" saved row open-with action enabled state",vm.OpenWithCommand.CanExecute(row)==(type=="File"));
            vm.EditItemCommand.Execute(row);
            var editor=new ItemEditWindow(vm);editor.Show();await Idle(editor);
            var picker=Descendants(editor).OfType<ComboBox>().Single();
            check(type+" single open-with enabled state",picker.IsEnabled==(type=="File"));
            await Take(editor,output,type.ToLowerInvariant()+"-single-editor");
            if(type=="File") await Popup(editor,picker,output,"file-handler-dropdown",check);
            check(type+" single unedited draft",!vm.ItemEditDirty);editor.Close();
            var all=vm.AllItemFromDetail(row)!;
            var targets=vm.ItemActionTargets(all,new[]{vm.Selected!.Id});
            var batch=new ItemBatchEditWindow(vm,all,targets);batch.Show();await Idle(batch);
            check(type+" batch open-with enabled state",Descendants(batch).OfType<ComboBox>().Single().IsEnabled==(type=="File"));
            await Take(batch,output,type.ToLowerInvariant()+"-batch-editor");batch.Close();
        }
        window.DataContext=vm;await Idle(window);
        var folderRow=vm.Items.Single(x=>x.Type=="Folder");
        var folderElement=Descendants(window).OfType<Grid>().Single(x=>ReferenceEquals(x.DataContext,folderRow) && x.ContextMenu is not null);
        var folderMenu=folderElement.ContextMenu!;folderMenu.PlacementTarget=folderElement;folderMenu.DataContext=folderRow;
        folderMenu.IsOpen=true;await Idle(window);
        var folderAction=folderMenu.Items.OfType<System.Windows.Controls.MenuItem>().Single(x=>Equals(x.Header,"פתח באמצעות"));
        check("Folder saved row menu visible and disabled",folderAction.IsVisible && !folderAction.IsEnabled);
        await Render(folderMenu,output,"folder-saved-row-menu");folderMenu.IsOpen=false;
        var captureVm=new MainViewModel(store,new NotImplementedLauncher(),capturer:new PickerCapturer()){ScanMinVisible=TimeSpan.Zero};
        window.DataContext=captureVm;captureVm.CaptureExistingCommand.Execute(null);await captureVm.CurrentScan!;
        captureVm.CapturePickWorkspaceCommand.Execute(captureVm.Workspaces.Single());
        captureVm.CaptureAddCommand.Execute(null);
        foreach(var staged in captureVm.StagedRows.ToArray()){
            captureVm.EditStagedCommand.Execute(staged);await Idle(window);
            var field=Descendants(window).OfType<ComboBox>().Single(x=>x.IsVisible && ReferenceEquals(x.DataContext,staged));
            check(staged.Type+" capture field enabled state",field.IsEnabled==(staged.Type==ItemType.File));
            await Take(window,output,staged.Type.ToString().ToLowerInvariant()+"-capture-editor");
            if(staged.Type==ItemType.File){
                check("Capture file target binding",OpenWithPicker.GetFileTarget(field)==staged.Target);
                staged.Target=@"C:\fixture.pdf";await Idle(window);
                check("Capture file target change refreshes picker",OpenWithPicker.GetFileTarget(field)==staged.Target);
                Other(window,field,output,"capture-file",check);
            }
        }
        var combo=new ComboBox();
        OpenWithPicker.SetFileTarget(combo,@"C:\fixture.txt");
        OpenWithPicker.SetPath(combo,"");
        var entries=combo.Items.Cast<OpenWithPicker.Choice>().ToArray();
        check("File choices include Windows default",entries[0].Value=="");
        check("File choices include Other",entries[^1].Label=="אחר...");
        var actual=WindowsFileHandlers.ForTarget(@"C:\fixture.txt");
        check("File choices reflect Windows handlers",entries.Skip(1).SkipLast(1).Select(x=>x.Value).SequenceEqual(actual.Select(x=>x.Path)));
        check("File handlers all exist",actual.All(x=>File.Exists(x.Path)));
        File.WriteAllText(Path.Combine(output,"file-handler-probe.json"),JsonSerializer.Serialize(new{extension=".txt",handlers=actual,choices=entries}));
        // An unknown type must not acquire an invented Chrome/Notepad fallback.
        OpenWithPicker.SetFileTarget(combo,@"C:\fixture.startapp_unknown_20261124");
        check("Unknown extension has only default and Other",combo.Items.Count==2);
        OpenWithPicker.SetFileTarget(combo,"");
        check("Empty file target has no browser fallback",combo.Items.Count==2);
        OpenWithPicker.SetPath(combo,@"C:\Legacy\saved.exe");
        check("Existing custom path is preserved",OpenWithPicker.GetPath(combo)==@"C:\Legacy\saved.exe" && combo.Items.Cast<OpenWithPicker.Choice>().Any(x=>x.Value==@"C:\Legacy\saved.exe"));
        var host=new Window{Title="File Open with probe",Width=520,Height=180,Content=combo,Owner=window};
        combo.Style=(Style)Application.Current.FindResource("OpenWithDropdown");host.Show();await Idle(host);
        await Take(host,output,"file-picker");Other(host,combo,output,"file",check);host.Close();
    }
    private sealed class PickerCapturer : IOpenItemCapturer
    {
        public CaptureScan Scan()=>new(new[]{new CapturedItem(ItemType.File,"Captured file",@"C:\capture.txt"),new CapturedItem(ItemType.Folder,"Captured folder",@"C:\Capture")},Array.Empty<string>());
    }
    private static void Other(Window window,ComboBox combo,string output,string name,Action<string,bool> check)
    {
        var original=OpenWithPicker.GetPath(combo);
        var choice=combo.Items.Cast<OpenWithPicker.Choice>().Single(x=>x.Label=="אחר...");
        var observed=false;Exception? error=null;
        var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(400)};
        timer.Tick+=(_,_)=>{
            timer.Stop();
            try {
                check(name+" Other preserves bound path",OpenWithPicker.GetPath(combo)==original);
                check(name+" Other restores selected path",OpenWithPicker.SelectedPath(combo)==original);
                var handle=FindWindow("#32770","פתח באמצעות");
                check(name+" native coming soon message is visible",handle!=IntPtr.Zero);
                if(handle!=IntPtr.Zero && GetWindowRect(handle,out var bounds)){
                    using var image=new System.Drawing.Bitmap(bounds.Right-bounds.Left,bounds.Bottom-bounds.Top);
                    using(var graphics=System.Drawing.Graphics.FromImage(image))graphics.CopyFromScreen(bounds.Left,bounds.Top,0,0,image.Size);
                    image.Save(Path.Combine(output,name+"-coming-soon.png"));
                }
                observed=true;
            }catch(Exception e){error=e;}
            finally{Key(0x1B);}
        };
        timer.Start();combo.SelectedItem=choice;
        if(error is not null)throw error;
        check(name+" Other message was observed",observed);
        check(name+" Other returns without path change",OpenWithPicker.GetPath(combo)==original);
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr FindWindow(string className,string title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window,out Rect bounds);
    [StructLayout(LayoutKind.Sequential)] private struct Rect {public int Left,Top,Right,Bottom;}
}
