using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
internal static partial class Program
{
    private static async Task Counts(Window window,JsonStore store,MainViewModel vm,string output,Action<string,bool> check)
    {
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());await Idle(window);await Take(window,output,"details-count-1");
        var id=vm.Selected!.Id;store.Mutate(c=>{for(var i=1;i<12;i++)c.AddToWorkspace(id,c.GetOrAddItem(ItemType.Link,"Site "+i,"https://example.com/"+i).Id);});
        vm.Reload();vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());await Idle(window);await Take(window,output,"details-count-12");
        var capture=new MainViewModel(store,new NotImplementedLauncher(),capturer:new CountCapturer()){ScanMinVisible=TimeSpan.Zero};
        window.DataContext=capture;capture.CaptureExistingCommand.Execute(null);await capture.CurrentScan!;capture.CapturePickWorkspaceCommand.Execute(capture.Workspaces.Single());
        for(var i=0;i<capture.CaptureRows.Count;i++)capture.CaptureRows[i].IsChecked=i<3;
        await Idle(window);await Take(window,output,"capture-count-12-selected-3");
        var labels=Descendants(window).OfType<TextBlock>().Where(x=>x.IsVisible).ToArray();
        var found=labels.Single(x=>x.Text=="נמצאו 12 פריטים");var selected=labels.Single(x=>x.Text=="3 נבחרו");
        var f=found.TransformToAncestor(window).TransformBounds(new System.Windows.Rect(0,0,found.ActualWidth,found.ActualHeight));var s=selected.TransformToAncestor(window).TransformBounds(new System.Windows.Rect(0,0,selected.ActualWidth,selected.ActualHeight));
        // Baseline records the overlap; candidate explicitly asserts disjoint bounds.
        var fixedLayout=AutomationProperties.GetAutomationId(found)=="CaptureFoundCount";
        if(fixedLayout)check("Found and selected counts do not overlap",!f.IntersectsWith(s));
        File.WriteAllText(Path.Combine(output,"count-bounds.txt"),$"found={f};selected={s};fixed={fixedLayout}");
    }
    private sealed class CountCapturer:IOpenItemCapturer {public CaptureScan Scan()=>new(Enumerable.Range(0,12).Select(i=>new CapturedItem(ItemType.Link,"Captured "+i,"https://capture.example/"+i)).ToArray(),Array.Empty<string>());}
}
