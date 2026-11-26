using System.IO;
using ComboBox=System.Windows.Controls.ComboBox;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using StartApp.App;
using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
internal static partial class Program
{
    private static async Task ContextPicker(Window window,JsonStore store,MainViewModel vm,string output,Action<string,bool> check)
    {
        var workspace=store.Load().Workspaces.Single();
        store.Mutate(c=>{var w=workspace;var f=c.GetOrAddItem(ItemType.File,"File context",@"C:\fixture.txt");var row=c.AddToWorkspace(w.Id,f.Id);c.SetLaunch(w.Id,row.Id??row.ItemId,new LaunchConfig(OpenWith:@"C:\Legacy\missing.exe"));});
        vm.Reload();vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        foreach(var type in new[]{"Link","File"}) {
            var row=vm.Items.Single(x=>x.Type==type);vm.OpenWithCommand.Execute(row);await Idle(window);
            var view=Descendants(window).OfType<OpenWithEditorView>().Single(x=>x.IsVisible);
            var combo=Descendants(view).OfType<ComboBox>().Single();
            check(type+" shared noneditable dropdown",!combo.IsEditable && combo.Style==window.FindResource("OpenWithDropdown"));
            check(type+" target binding",OpenWithPicker.GetFileTarget(combo)==(type=="File"?row.Target:null) && OpenWithPicker.GetWebsiteTarget(combo)==row.Target);
            check(type+" selected value binding",OpenWithPicker.GetPath(combo)==vm.OpenWithProgram);
            if(type=="File")check("Missing saved path preserved",OpenWithPicker.SelectedPath(combo)==@"C:\Legacy\missing.exe");
            await Take(window,output,type.ToLowerInvariant()+"-context");await Popup(window,combo,output,type.ToLowerInvariant()+"-context-dropdown",check);
            Other(window,combo,output,type.ToLowerInvariant()+"-context",check);
            combo.SelectedItem=combo.Items.Cast<OpenWithPicker.Choice>().First();await Idle(window);
            check(type+" Windows default clears draft path",vm.OpenWithProgram=="");
            vm.CloseOpenWithCommand.Execute(null);await Idle(window);
            check(type+" cancellation leaves saved preference intact",store.Load().Workspaces.Single().Items.Single(x=>(x.Id??x.ItemId)==row.InstanceId).Launch?.OpenWith==(type=="File"?@"C:\Legacy\missing.exe":null));
        }
        var captureVm=new MainViewModel(store,new NotImplementedLauncher(),capturer:new ContextCapturer()){ScanMinVisible=TimeSpan.Zero};
        window.DataContext=captureVm;captureVm.CaptureExistingCommand.Execute(null);await captureVm.CurrentScan!;
        captureVm.CapturePickWorkspaceCommand.Execute(captureVm.Workspaces.Single());captureVm.CaptureAddCommand.Execute(null);
        foreach(var staged in captureVm.StagedRows){
            captureVm.StagedOpenWithCommand.Execute(staged);await Idle(window);
            var view=Descendants(window).OfType<OpenWithEditorView>().Single(x=>x.IsVisible);var combo=Descendants(view).OfType<ComboBox>().Single();
            check(staged.Type+" staged context binding",OpenWithPicker.GetFileTarget(combo)==(staged.Type==ItemType.File?staged.Target:null));
            Other(window,combo,output,staged.Type+"-staged-context",check);
            await Take(window,output,staged.Type+"-staged-context");captureVm.CloseOpenWithCommand.Execute(null);
            check(staged.Type+" staged cancel keeps preference empty",staged.OpenWith=="");
        }
    }
    private sealed class ContextCapturer:IOpenItemCapturer {public CaptureScan Scan()=>new(new[]{new CapturedItem(ItemType.File,"Staged context file",@"C:\capture.txt"),new CapturedItem(ItemType.Link,"Staged context site","https://staged.example/")},Array.Empty<string>());}
}
