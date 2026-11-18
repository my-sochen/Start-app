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
    private static readonly List<object> UxGeometry = new();
    private static Point[] LocalCorners(FrameworkElement element) => [new(),new(element.ActualWidth,0),new(0,element.ActualHeight),new(element.ActualWidth,element.ActualHeight)];
    private static bool Finite(Point point) => double.IsFinite(point.X)&&double.IsFinite(point.Y);
    private static Rect NormalizedBounds(Point[] corners) => corners.All(Finite)?new(corners.Min(p=>p.X),corners.Min(p=>p.Y),corners.Max(p=>p.X)-corners.Min(p=>p.X),corners.Max(p=>p.Y)-corners.Min(p=>p.Y)):Rect.Empty;
    private static Point[] ScreenCorners(FrameworkElement element) => LocalCorners(element).Select(element.PointToScreen).ToArray();
    private static Rect ScreenBounds(FrameworkElement element) => NormalizedBounds(ScreenCorners(element));
    private static void CheckReachable(FrameworkElement element,Window window,string label)
    {
        // RTL can mirror local X. Transform all corners into the same screen coordinate system.
        var corners=ScreenCorners(element);var ownerCorners=ScreenCorners(window);var bounds=NormalizedBounds(corners);var frame=NormalizedBounds(ownerCorners);
        UxGeometry.Add(new {label,kind="screen",flow=element.FlowDirection.ToString(),ownerFlow=window.FlowDirection.ToString(),visible=element.IsVisible,corners,ownerCorners,bounds,frame,actualWidth=element.ActualWidth,actualHeight=element.ActualHeight,elementDpi=VisualTreeHelper.GetDpi(element),ownerDpi=VisualTreeHelper.GetDpi(window)});
        var ownerOk=element.IsVisible&&!bounds.IsEmpty&&!frame.IsEmpty&&bounds.Width>0&&bounds.Height>0&&frame.Width>0&&frame.Height>0&&frame.Contains(bounds);
        var viewportChecks=new List<bool>();
        for(DependencyObject? ancestor=VisualTreeHelper.GetParent(element);ancestor is not null;ancestor=VisualTreeHelper.GetParent(ancestor))
            if(ancestor is ScrollContentPresenter viewport){var localCorners=LocalCorners(element).Select(p=>element.TranslatePoint(p,viewport)).ToArray();var inViewport=NormalizedBounds(localCorners);var clipCorners=LocalCorners(viewport);var viewportBounds=NormalizedBounds(clipCorners);var clip=new Rect(-1,-1,viewport.ActualWidth+2,viewport.ActualHeight+2);UxGeometry.Add(new {label,kind="viewport",localCorners,clipCorners,inViewport,viewportBounds,clip,flow=viewport.FlowDirection.ToString(),dpi=VisualTreeHelper.GetDpi(viewport)});viewportChecks.Add(!inViewport.IsEmpty&&!viewportBounds.IsEmpty&&viewportBounds.Width>0&&viewportBounds.Height>0&&clip.Contains(inViewport));}
        Check(label+" has visible owner-contained bounds",ownerOk);foreach(var viewportOk in viewportChecks)Check(label+" inside scroll viewport",viewportOk);
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
    private static void CheckOpenWithVisibility(ContextMenu menu,bool program,string label)
    {
        var opener=menu.Items.OfType<MenuItem>().Single(item=>(string)item.Header=="פתח באמצעות");
        Check(label+" exact opener visibility",opener.Visibility==(program?Visibility.Collapsed:Visibility.Visible));
    }
    private static readonly HashSet<FrameworkElement> PlacementTrackedHosts = new();
    private static readonly Dictionary<ContextMenu,int> PlacementCallbackCalls = new();
    private static void TrackMenuPlacement(FrameworkElement host)
    {
        if(!PlacementTrackedHosts.Add(host))return;
        host.AddHandler(ContextMenuService.ContextMenuOpeningEvent,new ContextMenuEventHandler((_,_)=>
        {
            var menu=host.ContextMenu;var original=menu?.CustomPopupPlacementCallback;
            if(menu is null||original is null)return;
            PlacementCallbackCalls[menu]=0;
            // Observe the product callback after its opening handler assigned it.
            // Return exactly its original placements, without modifying any point.
            menu.CustomPopupPlacementCallback=(popupSize,targetSize,offset)=>
            {
                var placements=original(popupSize,targetSize,offset);
                PlacementCallbackCalls[menu]=PlacementCallbackCalls.GetValueOrDefault(menu)+1;
                UxGeometry.Add(new {kind="placement-callback",popupSize,targetSize,offset,placements=placements.Select(p=>new {p.Point,axis=p.PrimaryAxis.ToString()}).ToArray(),placement=menu.Placement.ToString(),servicePlacement=ContextMenuService.GetPlacement(host).ToString(),hostFlow=host.FlowDirection.ToString(),hostCorners=ScreenCorners(host)});
                return placements;
            };
        }),true);
    }
    private static void CaptureContext(ContextMenu menu,Window owner,string output,string name)
    {
        menu.UpdateLayout();var w=(int)Math.Ceiling(menu.ActualWidth);var h=(int)Math.Ceiling(menu.ActualHeight);
        Check(name+" positive native menu bounds",w>0&&h>0);
        var corners=ScreenCorners(menu);var ownerCorners=ScreenCorners(owner);var bounds=NormalizedBounds(corners);var frame=NormalizedBounds(ownerCorners);UxGeometry.Add(new {label=name,kind="native-menu",placement=menu.Placement.ToString(),servicePlacement=menu.PlacementTarget is null?null:ContextMenuService.GetPlacement(menu.PlacementTarget).ToString(),placementTarget=menu.PlacementTarget?.GetType().Name,callbackCalls=PlacementCallbackCalls.GetValueOrDefault(menu),flow=menu.FlowDirection.ToString(),corners,ownerCorners,bounds,frame,menuDpi=VisualTreeHelper.GetDpi(menu),ownerDpi=VisualTreeHelper.GetDpi(owner)});
        var bitmap=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);bitmap.Render(menu);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var stream=File.Create(Path.Combine(output,name+"-menu.png")))encoder.Save(stream);
        // Preserve actual popup pixels before the unchanged strict containment check.
        Check(name+" uses observed custom placement",menu.Placement==System.Windows.Controls.Primitives.PlacementMode.Custom&&menu.PlacementTarget is not null&&ContextMenuService.GetPlacement(menu.PlacementTarget)==System.Windows.Controls.Primitives.PlacementMode.Custom&&PlacementCallbackCalls.GetValueOrDefault(menu)>0);
        Check(name+" fits owner window",!bounds.IsEmpty&&!frame.IsEmpty&&bounds.Width>0&&bounds.Height>0&&frame.Width>0&&frame.Height>0&&frame.Contains(bounds));
    }
    private sealed class UxCapture : IOpenItemCapturer
    {
        public CaptureScan Scan()=>new(new[]{new CapturedItem(ItemType.Link,"מקור לקריאה בלבד","https://example.invalid/one"),new CapturedItem(ItemType.Program,"תוכנה להוספה","C:\\synthetic\\added.exe")},Array.Empty<string>());
    }
    private static int PrototypeUx(string directory)
    {
        if(!OperatingSystem.IsWindows())return 2;
        var output=Path.GetFullPath(directory);if(Directory.Exists(output)||File.Exists(output))return 3;Directory.CreateDirectory(output);
        Check("geometry mirrored contained",new Rect(0,0,100,100).Contains(NormalizedBounds([new(80,20),new(20,20),new(80,60),new(20,60)])));
        foreach(var outside in new[]{new Rect(-1,20,10,10),new Rect(91,20,10,10),new Rect(20,-1,10,10),new Rect(20,91,10,10)})Check("geometry rejects real overflow "+outside,!new Rect(0,0,100,100).Contains(outside));
        Check("geometry rejects clipped viewport",!new Rect(-1,-1,102,52).Contains(new Rect(20,40,30,20)));
        Check("geometry rejects nonfinite",NormalizedBounds([new(double.NaN,0),new(10,10)]).IsEmpty);
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
                        Check("local editor in same row "+kind,host is not null&&Descendants(host).Contains(name));TrackMenuPlacement((FrameworkElement)host!);
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
                        CheckSharePlaceholder(expandedMenu,true,"expanded "+kind+size);CheckOpenWithVisibility(expandedMenu,kind=="Program","expanded "+kind+size);CaptureContext(expandedMenu,window,output,$"{size.Item1}x{size.Item2}-{kind}-expanded-invalid");expandedMenu.IsOpen=false;await Idle(window);
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
                        CheckOpenWithVisibility(menu,kind=="Program","saved "+kind+size);Capture(window,output,$"{size.Item1}x{size.Item2}-{kind}-right-click");CaptureContext(menu,window,output,$"{size.Item1}x{size.Item2}-{kind}");menu.IsOpen=false;await Idle(window);
                        // Real keyboard context-menu key from the explicit Edit focus target.
                        foreach(var shiftF10 in new[]{false,true}){
                            edit.Focus();ContextKey(shiftF10);await Task.Delay(250);await Idle(window);
                            Check("keyboard context route exact row/no launch "+kind+shiftF10,menu.IsOpen&&ReferenceEquals(menu.DataContext,row)&&launcher.Sent.Count==0);CheckOpenWithVisibility(menu,kind=="Program","saved keyboard "+kind+size+shiftF10);CaptureContext(menu,window,output,$"{size.Item1}x{size.Item2}-{kind}-keyboard-{shiftF10}");menu.IsOpen=false;await Idle(window);
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
                var card=cards.First();TrackMenuPlacement(card);card.BringIntoView();await Idle(window);CheckReachable(card,window,"workspace card action anchor");var p=card.PointToScreen(new Point(card.ActualWidth/2,card.ActualHeight/2));SetCursorPos((int)p.X,(int)p.Y);mouse_event(8,0,0,0,UIntPtr.Zero);mouse_event(16,0,0,0,UIntPtr.Zero);await Task.Delay(250);await Idle(window);
                Check("workspace rightclick does not launch",card.ContextMenu!.IsOpen&&launcher.Sent.Count==0);Capture(window,output,"workspace-right-click");CaptureContext(card.ContextMenu,window,output,"workspace");card.ContextMenu.IsOpen=false;
                vm.CaptureExistingCommand.Execute(null);await vm.CurrentScan!;vm.CapturePickWorkspaceCommand.Execute(vm.CaptureWorkspaceResults.First());await Idle(window);
                var readonlyLists=Descendants(window).OfType<ListBox>().Where(b=>b.IsVisible&&(AutomationProperties.GetName(b)=="Saved items in the workspace"||AutomationProperties.GetName(b)=="Open now list")).ToArray();
                Check("both required readonly capture lists exist",readonlyLists.Length==2&&readonlyLists.Count(b=>AutomationProperties.GetName(b)=="Saved items in the workspace")==1&&readonlyLists.Count(b=>AutomationProperties.GetName(b)=="Open now list")==1);
                foreach(var list in readonlyLists)Check("readonly list has no row menus "+AutomationProperties.GetName(list),!Descendants(list).OfType<FrameworkElement>().Any(e=>e.ContextMenu is not null));
                foreach(var row in vm.CaptureRows)vm.DropCaptured(row);await Idle(window);
                var added=Descendants(window).OfType<TextBox>().Where(b=>b.IsVisible&&AutomationProperties.GetName(b)=="Added item name").ToArray();Check("staged items directly editable without Edit gate",added.Length==2);
                var stagedMenuHosts=Descendants(window).OfType<StackPanel>().Where(host=>host.IsVisible&&host.ContextMenu is not null&&host.DataContext is MainViewModel.StagedRow).ToArray();
                Check("staged opener fixtures include exact Link and Program",stagedMenuHosts.Length==2&&stagedMenuHosts.Count(host=>((MainViewModel.StagedRow)host.DataContext).Type==ItemType.Link)==1&&stagedMenuHosts.Count(host=>((MainViewModel.StagedRow)host.DataContext).Type==ItemType.Program)==1);
                foreach(var menuHost in stagedMenuHosts){
                    var stagedRow=(MainViewModel.StagedRow)menuHost.DataContext;TrackMenuPlacement(menuHost);
                    menuHost.BringIntoView();await Idle(window);Check("staged opener row focus "+stagedRow.Type,menuHost.Focus());ContextKey(false);await Task.Delay(250);await Idle(window);
                    var stagedMenu=menuHost.ContextMenu!;Check("staged opener menu exact identity "+stagedRow.Type,stagedMenu.IsOpen&&ReferenceEquals(stagedMenu.DataContext,stagedRow)&&launcher.Sent.Count==0);
                    CheckOpenWithVisibility(stagedMenu,stagedRow.Type==ItemType.Program,"staged "+stagedRow.Type);CaptureContext(stagedMenu,window,output,"staged-opener-"+stagedRow.Type);stagedMenu.IsOpen=false;await Idle(window);
                }
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
                // Local save reloads the store and replaces Workspace objects; reacquire by stable ID.
                var liveSecond=vm.Workspaces.Single(workspace=>workspace.Id==second.Id);
                Check("staged two-workspace fixture is live",vm.Workspaces.Count==2&&vm.HasOtherWorkspaces&&ReferenceEquals(liveSecond,vm.Workspaces.Single(workspace=>workspace.Id==second.Id)));
                Check("staged fixture removes exact current workspace",vm.Workspaces.Remove(liveSecond)&&vm.Workspaces.Count==1&&!vm.HasOtherWorkspaces&&!vm.Workspaces.Any(workspace=>workspace.Id==second.Id));
                await Idle(window);Check("one-workspace staged anchor focused",stagedHost.Focus());ContextKey(false);await Task.Delay(250);await Idle(window);
                Check("one-workspace staged menu targets exact row",stagedHost.ContextMenu!.IsOpen&&ReferenceEquals(stagedHost.ContextMenu.DataContext,stagedHost.DataContext)&&ReferenceEquals(stagedHost.Tag,vm)&&launcher.Sent.Count==0);
                var oneStagedShare=stagedHost.ContextMenu.Items.OfType<MenuItem>().Single(item=>(string)item.Header=="שיתוף לסביבה אחרת");
                UxGeometry.Add(new {kind="one-workspace-staged-placeholder",workspaceCount=vm.Workspaces.Count,vm.HasOtherWorkspaces,currentIds=vm.Workspaces.Select(workspace=>workspace.Id).ToArray(),removedId=liveSecond.Id,oneStagedShare.IsEnabled,hasCommand=oneStagedShare.Command is not null,visibility=oneStagedShare.Visibility.ToString()});
                CaptureContext(stagedHost.ContextMenu,window,output,"one-workspace-staged");CheckSharePlaceholder(stagedHost.ContextMenu,false,"one-workspace staged");stagedHost.ContextMenu.IsOpen=false;vm.Workspaces.Add(liveSecond);await Idle(window);
                Check("staged fixture restores exact second workspace",vm.Workspaces.Count==2&&vm.HasOtherWorkspaces&&ReferenceEquals(vm.Workspaces.Single(workspace=>workspace.Id==second.Id),liveSecond));
                stagedHost.Focus();ContextKey(false);await Task.Delay(250);await Idle(window);
                Check("no automatic persistence or launch in staged context",store.Load().Workspaces.First().Items.Count==4&&launcher.Sent.Count==0);
                CaptureContext(stagedHost.ContextMenu,window,output,"staged");stagedHost.ContextMenu.IsOpen=false;Capture(window,output,"staged-direct-edit");
                var stagedReachStoreBefore=JsonSerializer.Serialize(store.Load());
                // Individually reveal existing staged controls, not the whole taller-than-viewport row.
                foreach(var rowHost in stagedMenuHosts){
                    var exactRow=(MainViewModel.StagedRow)rowHost.DataContext;
                    var fields=Descendants(rowHost).OfType<TextBox>().Where(field=>field.IsVisible).ToArray();
                    var expectedNames=new[]{"Added item name","Added item description","Added item address","Added item open with"};
                    Check("staged individual field inventory "+exactRow.Type,fields.Length==4&&expectedNames.All(label=>fields.Count(field=>AutomationProperties.GetName(field)==label)==1));
                    var remove=Descendants(rowHost).OfType<Button>().Single(button=>ReferenceEquals(button.Command,vm.RemoveStagedCommand)&&ReferenceEquals(button.CommandParameter,exactRow));
                    var retained=new[]{exactRow.Name,exactRow.Description,exactRow.Target,exactRow.OpenWith};
                    var controls=fields.Cast<FrameworkElement>().Append(remove).ToArray();
                    foreach(var control in controls){
                        var label=AutomationProperties.GetName(control);control.BringIntoView();await Idle(window);
                        Check("staged individual exact row and focus "+exactRow.Type+" "+label,ReferenceEquals(control.DataContext,exactRow)&&control.IsEnabled&&control.Focus()&&ReferenceEquals(Keyboard.FocusedElement,control)&&launcher.Sent.Count==0);
                        await Idle(window);CheckReachable(control,window,"staged focused individual "+exactRow.Type+" "+label);
                        Check("staged individual focus retained after layout "+exactRow.Type+" "+label,ReferenceEquals(Keyboard.FocusedElement,control));
                        Check("staged individual retains all field values "+exactRow.Type+" "+label,retained.SequenceEqual(new[]{exactRow.Name,exactRow.Description,exactRow.Target,exactRow.OpenWith})&&expectedNames.Select((name,index)=>fields.Single(field=>AutomationProperties.GetName(field)==name).Text==retained[index]).All(value=>value)&&vm.StagedRows.Any(row=>ReferenceEquals(row,exactRow)));
                        Capture(window,output,"staged-reachable-"+exactRow.Type+"-"+Array.IndexOf(controls,control));
                    }
                }
                Check("staged reachability did not save or remove rows",vm.StagedRows.Count==2&&JsonSerializer.Serialize(store.Load())==stagedReachStoreBefore&&launcher.Sent.Count==0);
                vm.CaptureCloseCommand.Execute(null);vm.ExitWithoutSavingCommand.Execute(null);
                File.WriteAllText(Path.Combine(output,"geometry.json"),JsonSerializer.Serialize(UxGeometry,new JsonSerializerOptions{WriteIndented=true,NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals}));File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(Checks,new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(0);
            }catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());File.WriteAllText(Path.Combine(output,"geometry.json"),JsonSerializer.Serialize(UxGeometry,new JsonSerializerOptions{WriteIndented=true,NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals}));try{Capture(window,output,"failure-current-state");}catch(Exception captureError){File.WriteAllText(Path.Combine(output,"failure-capture-error.txt"),captureError.ToString());}File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(Checks));app.Shutdown(1);}
        };return app.Run(window);
    }
}
