using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StartApp.App;
using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
internal static partial class Program
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags,int x,int y,uint data,UIntPtr extra);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
    private static void InvokeButton(Button b) => ((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)).Invoke();
    private static Button NamedButton(Window w,string name)=>Descendants(w).OfType<Button>().Single(b=>b.IsVisible&&AutomationProperties.GetName(b)==name);
    private static void CheckReachable(FrameworkElement element,Window window,string label)
    {
        var point=element.PointToScreen(new Point(0,0));var frame=window.PointToScreen(new Point(0,0));
        Check(label+" has visible owner-contained bounds",element.IsVisible&&element.ActualWidth>0&&element.ActualHeight>0&&point.X>=frame.X&&point.Y>=frame.Y&&point.X+element.ActualWidth<=frame.X+window.ActualWidth&&point.Y+element.ActualHeight<=frame.Y+window.ActualHeight);
        for(DependencyObject? ancestor=VisualTreeHelper.GetParent(element);ancestor is not null;ancestor=VisualTreeHelper.GetParent(ancestor))
            if(ancestor is ScrollContentPresenter viewport){var origin=element.TranslatePoint(new Point(),viewport);Check(label+" inside scroll viewport",origin.X>=-1&&origin.Y>=-1&&origin.X+element.ActualWidth<=viewport.ActualWidth+1&&origin.Y+element.ActualHeight<=viewport.ActualHeight+1);}
    }
    private static void ContextKey(bool shiftF10)
    {
        if(shiftF10)keybd_event(0x10,0,0,UIntPtr.Zero);
        byte key=shiftF10?(byte)0x79:(byte)0x5d;keybd_event(key,0,0,UIntPtr.Zero);keybd_event(key,0,2,UIntPtr.Zero);
        if(shiftF10)keybd_event(0x10,0,2,UIntPtr.Zero);
    }
    private static IEnumerable<ContextMenu> OpenContextMenus()=>PresentationSource.CurrentSources.Cast<PresentationSource>().SelectMany(source=>source.RootVisual is DependencyObject root?Descendants(root).Prepend(root):Enumerable.Empty<DependencyObject>()).OfType<ContextMenu>().Where(m=>m.IsOpen).Distinct();
    private static void CheckSharePlaceholder(ContextMenu menu,bool otherWorkspace,string label)
    {
        var share=menu.Items.OfType<MenuItem>().Single(item=>(string)item.Header=="שיתוף לסביבה אחרת");
        Check(label+" exact disabled placeholder",!share.IsEnabled&&share.Command is null&&share.Visibility==(otherWorkspace?Visibility.Visible:Visibility.Collapsed));
    }
    private static void CaptureContext(ContextMenu menu,Window owner,string output,string name)
    {
        menu.UpdateLayout();var w=(int)Math.Ceiling(menu.ActualWidth);var h=(int)Math.Ceiling(menu.ActualHeight);
        Check(name+" positive native menu bounds",w>0&&h>0);
        var origin=menu.PointToScreen(new Point(0,0));var frame=owner.PointToScreen(new Point(0,0));
        Check(name+" fits owner window",origin.X>=frame.X&&origin.Y>=frame.Y&&origin.X+w<=frame.X+owner.ActualWidth&&origin.Y+h<=frame.Y+owner.ActualHeight);
        var bitmap=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);bitmap.Render(menu);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(Path.Combine(output,name+"-menu.png"));encoder.Save(stream);
    }
    private sealed class UxCapture : IOpenItemCapturer
    {
        public CaptureScan Scan()=>new(new[]{new CapturedItem(ItemType.Link,"מקור לקריאה בלבד","https://example.invalid/one"),new CapturedItem(ItemType.Program,"תוכנה להוספה","C:\\synthetic\\added.exe")},Array.Empty<string>());
    }
    private static int PrototypeUx(string directory)
    {
        if(!OperatingSystem.IsWindows())return 2;
        var output=Path.GetFullPath(directory);if(Directory.Exists(output)||File.Exists(output))return 3;Directory.CreateDirectory(output);
        var app=new Application();using var store=new JsonStore(Path.Combine(output,"synthetic","store.json"));
        store.Mutate(c=>{
            var ws=c.CreateWorkspace("סביבת בדיקה בעברית עם שם ארוך מאוד שממחיש את פריסת הפעולות");
            foreach(var t in new[]{ItemType.Link,ItemType.Folder,ItemType.File,ItemType.Program}){
                var target=t switch{ItemType.Link=>"https://example.invalid/long/path/with-mixed-language",ItemType.Folder=>"C:\\synthetic folder\\תיקייה בעברית",ItemType.Program=>"C:\\synthetic folder\\program.exe",_=>"C:\\synthetic folder\\מסמך בעברית עם שם ארוך.txt"};
                c.AddToWorkspace(ws.Id,c.GetOrAddItem(t,"אייטם ארוך בעברית עם טקסט נוסף לבדיקה "+t,target).Id);
            }
            c.CreateWorkspace("סביבה שנייה שאסור לשנות");
        });
        var launcher=new Launcher();var vm=new MainViewModel(store,launcher,new Preflight(),new Detector(),new UxCapture());
        var window=new MainWindow{DataContext=vm,Width=900,Height=750};vm.OpenDetailsCommand.Execute(vm.Workspaces.First());
        window.ContentRendered+=async(_,_)=>{
            try{
                foreach(var size in new[]{(900,750),(720,750),(720,400)}){
                    window.Width=size.Item1;window.Height=size.Item2;await Idle(window);Check("exact requested window dimensions "+size,Math.Abs(window.ActualWidth-size.Item1)<1&&Math.Abs(window.ActualHeight-size.Item2)<1);
                    var edits=Descendants(window).OfType<Button>().Where(b=>b.IsVisible&&AutomationProperties.GetName(b)=="Edit item").ToArray();
                    Check("four explicit Edit targets "+size,edits.Length==4&&edits.All(b=>(string)b.Content=="ערוך"));
                    Capture(window,output,$"{size.Item1}x{size.Item2}-saved-rows");
                    foreach(var kind in new[]{"Link","Folder","File","Program"}){
                        var edit=Descendants(window).OfType<Button>().Single(b=>b.IsVisible&&AutomationProperties.GetName(b)=="Edit item"&&b.DataContext is ItemRow r&&r.Type==kind);
                        edit.BringIntoView();await Idle(window);CheckReachable(edit,window,"edit "+kind+size);var row=(ItemRow)edit.DataContext;InvokeButton(edit);await Idle(window);
                        var name=Descendants(window).OfType<TextBox>().Single(b=>b.IsVisible&&AutomationProperties.GetName(b)=="Edit item name");
                        name.BringIntoView();await Idle(window);CheckReachable(name,window,"name input "+kind+size);Check( "focus on opened local name "+kind,ReferenceEquals(Keyboard.FocusedElement,name));
                        Check("editor bound to same instance "+kind,vm.EditingItemInstanceId==row.InstanceId);
                        var host=VisualTreeHelper.GetParent(edit);while(host is not null&&host is not Grid{ContextMenu:not null})host=VisualTreeHelper.GetParent(host);
                        Check("local editor in same row "+kind,host is not null&&Descendants(host).Contains(name));
                        Check("edit never launches "+kind,launcher.Sent.Count==0);
                        vm.EditItemName="";var save=NamedButton(window,"Save item edit");save.BringIntoView();await Idle(window);CheckReachable(save,window,"save "+kind+size);InvokeButton(save);await Idle(window);
                        Check("inline validation visible "+kind,vm.ItemEditOpen&&Descendants(host!).OfType<TextBlock>().Any(t=>t.IsVisible&&t.Text==vm.ItemEditError&&t.Text.Length>0));
                        var error=Descendants(host!).OfType<TextBlock>().Single(t=>t.IsVisible&&t.Text==vm.ItemEditError&&t.Text.Length>0);error.BringIntoView();await Idle(window);CheckReachable(error,window,"error "+kind+size);
                        Capture(window,output,$"{size.Item1}x{size.Item2}-{kind}-inline-error");
                        // Keep the invalid editor OPEN while testing the context popup on the row.
                        // This discriminates placement from the collapsed-row menu checks below.
                        var expandedAnchor=Descendants(host!).OfType<Button>().Single(button=>ReferenceEquals(button.Command,vm.OpenItemCommand));
                        expandedAnchor.BringIntoView();await Idle(window);CheckReachable(expandedAnchor,window,"expanded menu focus anchor "+kind+size);
                        Check("expanded menu anchor enabled and focused "+kind+size,expandedAnchor.IsEnabled&&expandedAnchor.Focus()&&ReferenceEquals(Keyboard.FocusedElement,expandedAnchor));ContextKey(true);await Task.Delay(250);await Idle(window);
                        var expandedMenu=((FrameworkElement)host!).ContextMenu!;
                        Check("expanded invalid editor menu keeps exact draft "+kind+size,expandedMenu.IsOpen&&ReferenceEquals(expandedMenu.DataContext,row)&&vm.ItemEditOpen&&vm.EditItemName==""&&vm.EditingItemInstanceId==row.InstanceId&&launcher.Sent.Count==0);
                        CheckSharePlaceholder(expandedMenu,true,"expanded "+kind+size);CaptureContext(expandedMenu,window,output,$"{size.Item1}x{size.Item2}-{kind}-expanded-invalid");expandedMenu.IsOpen=false;await Idle(window);
                        var cancel=NamedButton(window,"Cancel item edit");cancel.BringIntoView();await Idle(window);CheckReachable(cancel,window,"cancel "+kind+size);
                        var viewport=Descendants(window).OfType<ScrollViewer>().Single(v=>AutomationProperties.GetName(v)=="Workspace details viewport");viewport.ScrollToTop();await Idle(window);name.BringIntoView();await Idle(window);
                        Check("scroll retains exact row draft "+kind,vm.EditingItemInstanceId==row.InstanceId&&name.Text==""&&Descendants(window).OfType<TextBox>().Count(b=>b.IsVisible&&AutomationProperties.GetName(b)=="Edit item name")==1);
                        vm.EditItemName=row.Name+" טיוטה";vm.CancelItemEditCommand.Execute(null);await Idle(window);Check("dirty cancel guarded "+kind,vm.UnsavedOpen&&vm.UnsavedForItemEdit);
                        vm.ExitWithoutSavingCommand.Execute(null);await Idle(window);Check("cancel clears local editor "+kind,vm.EditingItemInstanceId==""&&launcher.Sent.Count==0);
                        // Exercise real right-click on the row through Windows input, not a direct handler call.
                        edit.BringIntoView();await Idle(window);var anchor=edit.PointToScreen(new Point(edit.ActualWidth/2,edit.ActualHeight/2));
                        window.Activate();SetCursorPos((int)anchor.X,(int)anchor.Y);mouse_event(0x08,0,0,0,UIntPtr.Zero);mouse_event(0x10,0,0,0,UIntPtr.Zero);await Task.Delay(250);await Idle(window);
                        var menu=((FrameworkElement)host!).ContextMenu!;Check("native rightclick opens exact row menu "+kind,menu.IsOpen&&ReferenceEquals(menu.DataContext,row)&&launcher.Sent.Count==0);
                        CheckSharePlaceholder(menu,true,"saved "+kind);
                        var openWith=menu.Items.OfType<MenuItem>().Single(i=>(string)i.Header=="פתח באמצעות");Check("Program excludes opener "+kind,(openWith.Visibility==Visibility.Collapsed)==(kind=="Program"));
                        Capture(window,output,$"{size.Item1}x{size.Item2}-{kind}-right-click");CaptureContext(menu,window,output,$"{size.Item1}x{size.Item2}-{kind}");menu.IsOpen=false;await Idle(window);
                        // Real keyboard context-menu key from the explicit Edit focus target.
                        foreach(var shiftF10 in new[]{false,true}){
                            edit.Focus();ContextKey(shiftF10);await Task.Delay(250);await Idle(window);
                            Check("keyboard context route exact row/no launch "+kind+shiftF10,menu.IsOpen&&ReferenceEquals(menu.DataContext,row)&&launcher.Sent.Count==0);CaptureContext(menu,window,output,$"{size.Item1}x{size.Item2}-{kind}-keyboard-{shiftF10}");menu.IsOpen=false;await Idle(window);
                        }
                    }
                }
                // Same handler must preserve the hidden disabled placeholder when only one workspace exists.
                var second=vm.Workspaces.Last();vm.Workspaces.Remove(second);await Idle(window);
                var oneEdit=Descendants(window).OfType<Button>().First(b=>b.IsVisible&&AutomationProperties.GetName(b)=="Edit item");oneEdit.BringIntoView();await Idle(window);oneEdit.Focus();ContextKey(false);await Task.Delay(250);await Idle(window);
                var oneMenu=OpenContextMenus().Single();CheckSharePlaceholder(oneMenu,false,"one-workspace saved");oneMenu.IsOpen=false;vm.Workspaces.Add(second);await Idle(window);
                vm.EditItemCommand.Execute(vm.Items.First());vm.EditItemName="נשמר רק בשורה זו";vm.SaveItemEditCommand.Execute(null);await Idle(window);
                Check("local save preserves other workspace",store.Load().Workspaces.Last().Items.Count==0&&vm.Items.First().Name=="נשמר רק בשורה זו"&&launcher.Sent.Count==0);
                vm.BackCommand.Execute(null);await Idle(window);
                var cards=Descendants(window).OfType<Border>().Where(b=>b.ContextMenu is not null).ToArray();Check("workspace card owns contextual menu",cards.Length>=2);
                var card=cards.First();card.BringIntoView();await Idle(window);CheckReachable(card,window,"workspace card action anchor");var p=card.PointToScreen(new Point(card.ActualWidth/2,card.ActualHeight/2));SetCursorPos((int)p.X,(int)p.Y);mouse_event(8,0,0,0,UIntPtr.Zero);mouse_event(16,0,0,0,UIntPtr.Zero);await Task.Delay(250);await Idle(window);
                Check("workspace rightclick does not launch",card.ContextMenu!.IsOpen&&launcher.Sent.Count==0);Capture(window,output,"workspace-right-click");CaptureContext(card.ContextMenu,window,output,"workspace");card.ContextMenu.IsOpen=false;
                vm.CaptureExistingCommand.Execute(null);await vm.CurrentScan!;vm.CapturePickWorkspaceCommand.Execute(vm.CaptureWorkspaceResults.First());await Idle(window);
                var readonlyLists=Descendants(window).OfType<ListBox>().Where(b=>b.IsVisible&&(AutomationProperties.GetName(b)=="Saved items in the workspace"||AutomationProperties.GetName(b)=="Open now list")).ToArray();
                Check("both required readonly capture lists exist",readonlyLists.Length==2&&readonlyLists.Count(b=>AutomationProperties.GetName(b)=="Saved items in the workspace")==1&&readonlyLists.Count(b=>AutomationProperties.GetName(b)=="Open now list")==1);
                foreach(var list in readonlyLists)Check("readonly list has no row menus "+AutomationProperties.GetName(list),!Descendants(list).OfType<FrameworkElement>().Any(e=>e.ContextMenu is not null));
                foreach(var row in vm.CaptureRows)vm.DropCaptured(row);await Idle(window);
                var added=Descendants(window).OfType<TextBox>().Where(b=>b.IsVisible&&AutomationProperties.GetName(b)=="Added item name").ToArray();Check("staged items directly editable without Edit gate",added.Length==2);
                var stagedHost=Descendants(window).OfType<StackPanel>().First(b=>b.IsVisible&&b.ContextMenu is not null&&b.DataContext is MainViewModel.StagedRow);
                var stagedInput=Descendants(stagedHost).OfType<TextBox>().First();
                foreach(var shiftF10 in new[]{false,true}){
                    stagedInput.BringIntoView();await Idle(window);stagedInput.Focus();ContextKey(shiftF10);await Task.Delay(250);await Idle(window);
                    var textMenu=OpenContextMenus().Single();
                    Check("textbox keeps ordinary editing menu "+shiftF10,!stagedHost.ContextMenu!.IsOpen&&!ReferenceEquals(textMenu,stagedHost.ContextMenu)&&textMenu.Items.OfType<MenuItem>().Any(item=>ReferenceEquals(item.Command,ApplicationCommands.Copy)||ReferenceEquals(item.Command,ApplicationCommands.Paste)));textMenu.IsOpen=false;await Idle(window);
                    stagedHost.BringIntoView();await Idle(window);Check("staged row focus anchor "+shiftF10,stagedHost.Focus());ContextKey(shiftF10);await Task.Delay(250);await Idle(window);
                    Check("staged keyboard menu targets exact instance "+shiftF10,stagedHost.ContextMenu!.IsOpen&&ReferenceEquals(stagedHost.ContextMenu.DataContext,stagedHost.DataContext));CheckSharePlaceholder(stagedHost.ContextMenu,true,"staged");
                    CaptureContext(stagedHost.ContextMenu,window,output,"staged-keyboard-"+shiftF10);stagedHost.ContextMenu.IsOpen=false;await Idle(window);
                }
                // Also check ordinary TextBox editing by physical right-click, not keyboard alone.
                stagedInput.BringIntoView();await Idle(window);CheckReachable(stagedInput,window,"staged text rightclick field");
                var textPoint=stagedInput.PointToScreen(new Point(stagedInput.ActualWidth/2,stagedInput.ActualHeight/2));SetCursorPos((int)textPoint.X,(int)textPoint.Y);mouse_event(8,0,0,0,UIntPtr.Zero);mouse_event(16,0,0,0,UIntPtr.Zero);await Task.Delay(250);await Idle(window);
                var textPointerMenu=OpenContextMenus().Single();Check("textbox physical rightclick keeps ordinary edit menu",!stagedHost.ContextMenu!.IsOpen&&!ReferenceEquals(textPointerMenu,stagedHost.ContextMenu)&&textPointerMenu.Items.OfType<MenuItem>().Any(item=>ReferenceEquals(item.Command,ApplicationCommands.Copy)||ReferenceEquals(item.Command,ApplicationCommands.Paste)));textPointerMenu.IsOpen=false;await Idle(window);
                // Physical right-click on the non-editable group label inside the exact staged row.
                var stagedLabel=Descendants(stagedHost).OfType<TextBlock>().First(t=>t.IsVisible&&t.Text.Length>0);stagedLabel.BringIntoView();await Idle(window);CheckReachable(stagedLabel,window,"staged rightclick label");
                var stagedPoint=stagedLabel.PointToScreen(new Point(stagedLabel.ActualWidth/2,stagedLabel.ActualHeight/2));SetCursorPos((int)stagedPoint.X,(int)stagedPoint.Y);mouse_event(8,0,0,0,UIntPtr.Zero);mouse_event(16,0,0,0,UIntPtr.Zero);await Task.Delay(250);await Idle(window);
                Check("staged physical rightclick exact row no launch",stagedHost.ContextMenu!.IsOpen&&ReferenceEquals(stagedHost.ContextMenu.DataContext,stagedHost.DataContext)&&launcher.Sent.Count==0);CaptureContext(stagedHost.ContextMenu,window,output,"staged-physical-rightclick");stagedHost.ContextMenu.IsOpen=false;await Idle(window);
                vm.Workspaces.Remove(second);stagedHost.Focus();ContextKey(false);await Task.Delay(250);await Idle(window);CheckSharePlaceholder(stagedHost.ContextMenu!,false,"one-workspace staged");stagedHost.ContextMenu!.IsOpen=false;vm.Workspaces.Add(second);await Idle(window);
                stagedHost.Focus();ContextKey(false);await Task.Delay(250);await Idle(window);
                Check("no automatic persistence or launch in staged context",store.Load().Workspaces.First().Items.Count==4&&launcher.Sent.Count==0);
                CaptureContext(stagedHost.ContextMenu,window,output,"staged");stagedHost.ContextMenu.IsOpen=false;Capture(window,output,"staged-direct-edit");
                vm.CaptureCloseCommand.Execute(null);vm.ExitWithoutSavingCommand.Execute(null);
                File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(Checks,new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(0);
            }catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(Checks));app.Shutdown(1);}
        };return app.Run(window);
    }
}
