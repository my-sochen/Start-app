using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StartApp.App;
using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;

// Test-only executable with fixed candidate ProjectReference. No product changes.
// Native real MainWindow/BAML, isolated store, UIA Invoke and binding edits, fake
// scanner/launcher. No full App startup, real capture, network or resource launch.
internal static class Program
{
    private static readonly List<object> Results = new();
    private static string Output = "";
    private static string Phase = "";
    private static readonly string[] Edited = ["טיוטה שנשמרת", "תיאור שנערך", "https://example.invalid/edited", @"C:\Windows\notepad.exe"];
    private sealed class Launcher : IItemLauncher
    {
        public int Calls;
        public LaunchResult Launch(Item item, LaunchConfig? launch) { Calls++; return new(LaunchStatus.Started); }
    }
    private sealed class Scanner : IOpenItemCapturer
    {
        public CaptureScan Scan() => new([new CapturedItem(ItemType.Link, "first original", "https://example.invalid/first"), new CapturedItem(ItemType.Link, "second original", "https://example.invalid/second")], []);
    }
    private static void Check(string name, bool ok) { Results.Add(new { name, ok }); if (!ok) throw new Exception(name); }
    private static IEnumerable<DependencyObject> Desc(DependencyObject root)
    {
        for (var i=0; i<VisualTreeHelper.GetChildrenCount(root); i++) { var child=VisualTreeHelper.GetChild(root,i); yield return child; foreach(var x in Desc(child)) yield return x; }
    }
    private static async Task Idle(Window window) { await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
    private static async Task Click(Window w, Button b)
    {
        b.BringIntoView(); await Idle(w);
        if(!b.IsVisible || !b.IsEnabled) throw new Exception("Required UI button unavailable");
        ((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)).Invoke(); await Idle(w);
    }
    private static string[] Values(MainViewModel.StagedRow row) => [row.Name,row.Description,row.Target,row.OpenWith];
    private static async Task SetField(Window w, DependencyObject row, string label, string value)
    {
        var field=Desc(row).OfType<TextBox>().Single(x=>x.IsVisible && AutomationProperties.GetName(x)==label);
        field.BringIntoView(); await Idle(w); field.Text=value;
        field.GetBindingExpression(TextBox.TextProperty)?.UpdateSource(); await Idle(w);
    }
    private static void Shot(Window window,string name)
    {
        window.UpdateLayout(); var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var f=File.Create(Path.Combine(Output,name+".png"));encoder.Save(f);
    }
    private static void Persisted(Store store)
    {
        var ws=store.Workspaces.Single();var saved=ws.Items.Single(m=>store.Items[m.ItemId].Target==Edited[2]);
        Check(Phase+" staged changed values persisted", saved.Name==Edited[0] && saved.Description==Edited[1] && saved.Launch?.Browser==Edited[3]);
        var edited=ws.Items.Single(m=>store.Items[m.ItemId].Target=="https://example.invalid/saved");
        Check(Phase+" saved row edited values persisted", edited.Name=="שם שמור חדש" && edited.Description=="תיאור שמור חדש");
    }
    [STAThread] private static int Main(string[] args)
    {
        if(!OperatingSystem.IsWindows() || args.Length!=2 || args[0] is not ("write" or "reload")) return 2;
        Phase=args[0];Output=Path.GetFullPath(args[1]);
        if(Phase=="write" && (Directory.Exists(Output)||File.Exists(Output))) return 3;
        Directory.CreateDirectory(Output);var storePath=Path.Combine(Output,"isolated-store.json");
        var process=Process.GetCurrentProcess();
        File.WriteAllText(Path.Combine(Output,Phase+"-process.json"),JsonSerializer.Serialize(new { pid=process.Id, startUtc=process.StartTime.ToUniversalTime(), phase=Phase, binary=Environment.ProcessPath, testDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Program).Assembly.Location))).ToLowerInvariant(), exeSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Environment.ProcessPath!))).ToLowerInvariant(), appDllSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))).ToLowerInvariant(), output=Output }));
        var app=new Application();using var store=new JsonStore(storePath);var launcher=new Launcher();
        if(Phase=="write")store.Mutate(c=>{var ws=c.CreateWorkspace("Functional fixed TEST");c.AddToWorkspace(ws.Id,c.GetOrAddItem(ItemType.Link,"saved original","https://example.invalid/saved").Id);});
        var vm=new MainViewModel(store,launcher,capturer:new Scanner());
        var window=new MainWindow { DataContext=vm,Width=900,Height=750 };
        window.ContentRendered+=async(_,_)=>{
            try {
                if(Phase=="reload") {
                    Persisted(store.Load());vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());await Idle(window);
                    Check("reload visible rows contain persisted staged and saved edits",vm.Items.Any(r=>r.Name==Edited[0]) && vm.Items.Any(r=>r.Name=="שם שמור חדש"));
                    Check("reload no resource launches",launcher.Calls==0);Shot(window,"fresh-process-reload");
                } else {
                    vm.CaptureExistingCommand.Execute(null);await vm.CurrentScan!;vm.CapturePickWorkspaceCommand.Execute(vm.CaptureWorkspaceResults.Single());await Idle(window);
                    foreach(var row in vm.CaptureRows)vm.DropCaptured(row);await Idle(window);
                    Check("write added rows remain normal",vm.StagedRows.Count==2 && vm.StagedRows.All(r=>!r.IsEditing));
                    var first=vm.StagedRows[0];var second=vm.StagedRows[1];var before=File.ReadAllBytes(storePath);
                    StackPanel Host(MainViewModel.StagedRow row)=>Desc(window).OfType<StackPanel>().Single(x=>x.IsVisible&&x.ContextMenu!=null&&ReferenceEquals(x.DataContext,row));
                    Button Edit(StackPanel host)=>Desc(host).OfType<Button>().Single(x=>ReferenceEquals(x.Command,vm.EditStagedCommand));
                    var host=Host(first);await Click(window,Edit(host));
                    var labels=new[]{"Added item name","Added item description","Added item address","Added item open with"};
                    for(var i=0;i<labels.Length;i++)await SetField(window,host,labels[i],Edited[i]);
                    Check("write real bound staged fields changed",Values(first).SequenceEqual(Edited));
                    Check("write changed staged draft not persisted",File.ReadAllBytes(storePath).SequenceEqual(before));Shot(window,"changed-draft-before-Done");
                    await Click(window,Desc(host).OfType<Button>().Single(x=>ReferenceEquals(x.Command,vm.CloseStagedEditCommand)));
                    Check("write Done collapses without losing changed draft",!first.IsEditing && Values(first).SequenceEqual(Edited));
                    Check("write Done does not save",File.ReadAllBytes(storePath).SequenceEqual(before));Shot(window,"changed-draft-after-Done");
                    await Click(window,Edit(host));
                    Check("write reopen retains changed bound fields",labels.Select(label=>Desc(host).OfType<TextBox>().Single(x=>x.IsVisible&&AutomationProperties.GetName(x)==label).Text).SequenceEqual(Edited));
                    await Click(window,Edit(Host(second)));
                    Check("write second Edit isolates one editor and retains first draft",second.IsEditing&&!first.IsEditing&&Values(first).SequenceEqual(Edited)&&vm.StagedRows.Count(r=>r.IsEditing)==1);
                    await Click(window,Edit(host));Check("write switch back retains changed draft",first.IsEditing&&!second.IsEditing&&Values(first).SequenceEqual(Edited));
                    await Click(window,Desc(window).OfType<Button>().Single(x=>x.IsVisible&&ReferenceEquals(x.Command,vm.CaptureSaveCommand)));
                    Check("write Save clears staged rows",vm.StagedRows.Count==0);
                    vm.CaptureCloseCommand.Execute(null);await Idle(window);vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());await Idle(window);
                    var savedRow=vm.Items.Single(x=>x.Name=="saved original");
                    await Click(window,Desc(window).OfType<Button>().Single(x=>x.IsVisible&&AutomationProperties.GetName(x)=="Edit item"&&ReferenceEquals(x.DataContext,savedRow)));
                    await SetField(window,window,"Edit item name","שם שמור חדש");
                    await SetField(window,window,"Edit item description","תיאור שמור חדש");
                    await Click(window,Desc(window).OfType<Button>().Single(x=>x.IsVisible&&ReferenceEquals(x.Command,vm.SaveItemEditCommand)));
                    Persisted(store.Load());Check("write no resource launches",launcher.Calls==0);Shot(window,"after-staged-and-saved-row-save");
                }
                File.WriteAllText(Path.Combine(Output,Phase+"-checks.json"),JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(0);
            }catch(Exception e){File.WriteAllText(Path.Combine(Output,Phase+"-error.txt"),e.ToString());File.WriteAllText(Path.Combine(Output,Phase+"-checks.json"),JsonSerializer.Serialize(Results));try{Shot(window,Phase+"-failure");}catch{}app.Shutdown(1);}
        };return app.Run(window);
    }
}
