using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StartApp.App;
using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Application = System.Windows.Application;

// Test-only executable. Actual production MainWindow/XAML, fixed scan adapter and isolated synthetic store.
// Same binary source runs against baseline and candidate on the same Windows machine, no product hooks.
internal static class Program
{
    private sealed class FixedCapture : IOpenItemCapturer
    {
        public CaptureScan Scan() => new(new CapturedItem[] {
            new(ItemType.Link,"Example site","https://example.com/"),
            new(ItemType.Folder,"Project files",@"C:\RefactorFixture"),
            new(ItemType.Program,"Editor",@"C:\RefactorFixture\editor.exe") }, Array.Empty<string>());
    }
    private static readonly List<object> Results = new();
    private static readonly List<object> SameProcess = new();
    private static readonly List<object> DialogFrames = new();
    private static readonly List<string> FrameFailures = new();
    [STAThread] private static int Main(string[] args)
    {
        if (args.Length != 1 || Directory.Exists(args[0])) return 2;
        var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        // Use the same deterministic WPF software renderer for both products. No text/layout overrides.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var app = new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/StartApp.App;component/SharedResources.xaml") });
        var window = new MainWindow(new UpdateViewModel(new InactiveUpdateSource(), null, _ => throw new Exception("Unexpected browser"))) { Width=960, Height=650, Left=12, Top=12 };
        using var repo = new JsonStore(Path.Combine(output,"fixture","store.json"));
        var vm = new MainViewModel(repo,new NotImplementedLauncher(),capturer:new FixedCapture()) { ScanMinVisible=TimeSpan.Zero };
        window.DataContext=vm;
        var code=0;
        void Check(string name,bool ok) { Results.Add(new{name,ok}); if(!ok)throw new Exception(name); }
        window.ContentRendered += async (_,_) => {
            try {
                vm.NewName="Capture workspace"; vm.CreateWorkspaceCommand.Execute(null); vm.BackCommand.Execute(null);
                vm.CaptureNewCommand.Execute(null); await vm.CurrentScan!; await Settle(window);
                Check("New capture has fixed three selected rows",vm.CaptureRows.Count==3 && vm.CaptureRows.All(r=>r.IsChecked));
                vm.NewName="New captured environment"; await Take(window,output,"01-new-form");
                // New-form name is unsaved; clear it before ordinary close.
                vm.NewName="";vm.CaptureCloseCommand.Execute(null); Check("Close new capture",!vm.CaptureOpen);
                vm.CaptureExistingCommand.Execute(null);await vm.CurrentScan!;await Take(window,output,"02-pick-environment");
                var workspace=vm.Workspaces.Single();vm.CapturePickWorkspaceCommand.Execute(workspace);await Take(window,output,"03-side-empty");
                vm.CaptureAddCommand.Execute(null);Check("Staging does not save",repo.Load().Workspaces.Single().Items.Count==0 && vm.StagedRows.Count==3);
                await Take(window,output,"04-staged-summary");
                var first=vm.StagedRows.First();vm.EditStagedCommand.Execute(first);first.Name="Edited local name";first.Description="Independent staged description";
                await Take(window,output,"05-staged-editor");vm.CloseStagedEditCommand.Execute(first);vm.CaptureSaveCommand.Execute(null);
                Check("Save commits staged rows and keeps capture open",repo.Load().Workspaces.Single().Items.Count==3 && vm.StagedRows.Count==0 && vm.IsCaptureSide);
                await Take(window,output,"06-saved-side");vm.CaptureAddCommand.Execute(null);
                Check("Grouped duplicate warning",vm.CaptureDupGroupPending && vm.CaptureDupChoices.Count==3);await Take(window,output,"07-grouped-duplicates");
                vm.CancelCaptureDupGroupCommand.Execute(null);vm.DropCaptured(vm.CaptureRows.First());
                Check("Single duplicate warning",vm.CaptureDupSinglePending);await Take(window,output,"08-drag-duplicate");
                vm.CancelCaptureDupSingleCommand.Execute(null);vm.CaptureCloseCommand.Execute(null);Check("Close saved capture",!vm.CaptureOpen);
                window.Width=720;window.Height=400;vm.CaptureExistingCommand.Execute(null);await vm.CurrentScan!;vm.CapturePickWorkspaceCommand.Execute(workspace);
                await Take(window,output,"09-minimum-size");
                vm.CaptureCloseCommand.Execute(null);
                // Candidate-only coverage of each actual dialog frame. These are visual fixture states,
                // not workflow/permission claims; ordinary capture states above still run identically.
                {
                    window.Width=960; window.Height=650; await Settle(window);
                    var names=new[]{"CaptureDupSinglePending","CaptureDupGroupPending","EmptyWorkspaceOpen","DefaultConfirmOpen","AddressEditOpen","OpenWithOpen","PickerOpen","DesignEditOpen","CategoryEditOpen","UnsavedOpen","SelectionPending","OpenDecisionPending","LastResultsOpen"};
                    var roots=names.Select(name=>Descendants(window).OfType<Grid>().Single(g=>System.Windows.Data.BindingOperations.GetBinding(g,UIElement.VisibilityProperty)?.Path?.Path==name)).Cast<UIElement>().ToList();
                    var cards=roots.Select(r=>((Grid)r).Children.OfType<Border>().Single()).ToList();
                    var originalVisibility=roots.Select(r=>r.Visibility).ToArray();
                    foreach(var root in roots)root.SetCurrentValue(UIElement.VisibilityProperty,Visibility.Collapsed);
                    for(var i=0;i<cards.Count;i++)
                    {
                        var root=(UIElement)cards[i].Parent; root.SetCurrentValue(UIElement.VisibilityProperty,Visibility.Visible);
                        await Take(window,output,"frame-"+(i+1).ToString("00"),false);
                        root.SetCurrentValue(UIElement.VisibilityProperty,Visibility.Collapsed);
                    }
                    for(var i=0;i<roots.Count;i++)roots[i].SetCurrentValue(UIElement.VisibilityProperty,originalVisibility[i]);
                    // Actual secondary-window constructors with synthetic data; no user store mutation.
                    var assembly=typeof(MainWindow).Assembly;
                    var promptType=assembly.GetType("StartApp.App.SavedItemPromptWindow")!;
                    foreach(var notice in new[]{false,true})
                    {
                        var prompt=(Window)Activator.CreateInstance(promptType,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new object[]{"Fixture notice","Synthetic conflict or notice body","Continue",notice},null)!;
                        prompt.Owner=window;prompt.Show();await Take(prompt,output,notice?"prompt-notice":"prompt-conflict",false);prompt.Close();
                    }
                    var workspacePrompt=(Window)Activator.CreateInstance(assembly.GetType("StartApp.App.NewWorkspaceFromItemWindow")!,vm,new SavedItemTransfer(ItemType.Link,"Fixture item","https://example.com/","",null))!;
                    workspacePrompt.Owner=window;workspacePrompt.Show();await Take(workspacePrompt,output,"prompt-new-workspace",false);workspacePrompt.Close();
                }
                File.WriteAllText(Path.Combine(output,"same-process.json"),JsonSerializer.Serialize(SameProcess));
                File.WriteAllText(Path.Combine(output,"dialog-frames.json"),JsonSerializer.Serialize(DialogFrames));
                File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true}));
                if(FrameFailures.Count>0)throw new Exception("Shared dialog frame pixels differ: "+string.Join(",",FrameFailures));
            } catch(Exception e){code=1;File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());}
            finally {window.Close();app.Shutdown(code);}
        };
        app.Run(window);return code;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var next in Descendants(child))yield return next;}
    }
    private static async Task Settle(Window window)
    {
        window.Activate();Keyboard.ClearFocus();window.Focus();window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        await Task.Delay(120);
    }
    private static async Task Take(Window window,string output,string name,bool captureComparison=true)
    {
        await Settle(window);
        // Loaded focus handlers can post after ContentRendered; clear the actual focused descendant.
        if(Keyboard.FocusedElement is UIElement focused)focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        Keyboard.ClearFocus();FocusManager.SetFocusedElement(window,null);window.Focus();await Settle(window);
        var width=(int)Math.Ceiling(window.ActualWidth);var height=(int)Math.Ceiling(window.ActualHeight);
        // Require an unchanged frame inside this process before comparing across products.
        // No pixel filtering, tolerance or product render settings are applied.
        RenderTargetBitmap? image=null; byte[]? previous=null; var steady=0;
        for (var i=0;i<30;i++)
        {
            image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(window);
            var pixels=new byte[width*height*4];image.CopyPixels(pixels,width*4,0);
            steady=previous is not null && previous.AsSpan().SequenceEqual(pixels) ? steady+1 : 0;
            if(steady>=3)break;
            previous=pixels;await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(80);
        }
        if(steady<3 || image is null)throw new Exception("WPF frame never became exactly stable: "+name);
        // Independently compare the shared frame with its original Border shape in this exact
        // window/process. Copy every effective local frame value, move the same child, add no wrapper.
        var frames = Descendants(window).OfType<Border>().Where(e => e.GetType().FullName == "StartApp.App.DialogFrame").ToList();
        if (frames.Count > 0)
        {
            // No type substitution: measure whether first exposure survives its own reparent.
            foreach(var frame in frames)
            {
                var child=frame.Child;frame.Child=null;frame.Child=child;
                if(frame.Parent is System.Windows.Controls.Panel owner){var index=owner.Children.IndexOf(frame);owner.Children.RemoveAt(index);owner.Children.Insert(index,frame);}
                else if(ReferenceEquals(window.Content,frame)){window.Content=null;window.Content=frame;}
            }
            await Settle(window);
            var sameFrameImage=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);sameFrameImage.Render(window);
            var initialPixels=new byte[width*height*4];var sameFramePixels=new byte[initialPixels.Length];image.CopyPixels(initialPixels,width*4,0);sameFrameImage.CopyPixels(sameFramePixels,width*4,0);
            var selfDifference=0;for(var p=0;p<initialPixels.Length;p+=4)if(!initialPixels.AsSpan(p,4).SequenceEqual(sameFramePixels.AsSpan(p,4)))selfDifference++;
            var selfPng=new PngBitmapEncoder();selfPng.Frames.Add(BitmapFrame.Create(sameFrameImage));using(var f=File.Create(Path.Combine(output,name+"-shared-self-control.png")))selfPng.Save(f);
            File.AppendAllText(Path.Combine(output,"dialog-self.jsonl"),JsonSerializer.Serialize(new{state=name,shared_self_pixels=selfDifference})+Environment.NewLine);
            foreach(var frame in frames)
            {
                var child=frame.Child;frame.Child=null;frame.Child=child;
                if(frame.Parent is System.Windows.Controls.Panel owner){var index=owner.Children.IndexOf(frame);owner.Children.RemoveAt(index);owner.Children.Insert(index,frame);}
                else if(ReferenceEquals(window.Content,frame)){window.Content=null;window.Content=frame;}
            }
            await Settle(window);
            var secondSelf=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);secondSelf.Render(window);
            var secondSelfPixels=new byte[sameFramePixels.Length];secondSelf.CopyPixels(secondSelfPixels,width*4,0);
            var secondSelfDiff=0;for(var p=0;p<secondSelfPixels.Length;p+=4)if(!secondSelfPixels.AsSpan(p,4).SequenceEqual(sameFramePixels.AsSpan(p,4)))secondSelfDiff++;
            File.AppendAllText(Path.Combine(output,"dialog-self-repeat.jsonl"),JsonSerializer.Serialize(new{state=name,repeat_shared_self_pixels=secondSelfDiff})+Environment.NewLine);
            var swaps = new List<(Action<Border> Replace, Border Frame, Border Plain)>();
            foreach (var frame in frames)
            {
                Action<Border> replace;
                if(frame.Parent is System.Windows.Controls.Panel panel)
                {var index=panel.Children.IndexOf(frame);replace=value=>{panel.Children.RemoveAt(index);panel.Children.Insert(index,value);};}
                else if(ReferenceEquals(window.Content,frame))replace=value=>window.Content=value;
                else throw new Exception("Unsupported frame parent");
                var plain = new Border(); var values = frame.GetLocalValueEnumerator();
                while (values.MoveNext())
                {
                    var entry = values.Current; if (entry.Property.ReadOnly) continue;
                    var binding = System.Windows.Data.BindingOperations.GetBindingBase(frame, entry.Property);
                    if (binding is not null) System.Windows.Data.BindingOperations.SetBinding(plain, entry.Property, binding);
                    else plain.SetValue(entry.Property, entry.Value);
                }
                var child = frame.Child; frame.Child = null; plain.Child = child;
                replace(plain);
                swaps.Add((replace, frame, plain));
            }
            await Settle(window);
            var plainImage = new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); plainImage.Render(window);
            var actual = new byte[width*height*4]; var expected = new byte[actual.Length]; image.CopyPixels(actual,width*4,0); plainImage.CopyPixels(expected,width*4,0);
            var different = 0; for (var p=0;p<actual.Length;p+=4) if (!actual.AsSpan(p,4).SequenceEqual(expected.AsSpan(p,4))) different++;
            var equalInvalidationDifference=0;for(var p=0;p<expected.Length;p+=4)if(!sameFramePixels.AsSpan(p,4).SequenceEqual(expected.AsSpan(p,4)))equalInvalidationDifference++;
            DialogFrames.Add(new { state = name, different_pixels = different, equal_invalidation_pixels=equalInvalidationDifference, second_self_pixels=secondSelfDiff, frame_count = frames.Count });
            var plainPng = new PngBitmapEncoder(); plainPng.Frames.Add(BitmapFrame.Create(plainImage)); using (var f=File.Create(Path.Combine(output,name+"-plain-dialog-border.png"))) plainPng.Save(f);
            // Control: repeat the identical reparent using plain Border -> plain Border.
            // Preserve the original shared/plain failure; a red control never waives it.
            foreach (var swap in swaps)
            {
                var replacement = new Border(); var locals=swap.Plain.GetLocalValueEnumerator();
                while(locals.MoveNext())
                {
                    var entry=locals.Current; if(entry.Property.ReadOnly)continue;
                    var binding=System.Windows.Data.BindingOperations.GetBindingBase(swap.Plain,entry.Property);
                    if(binding is not null)System.Windows.Data.BindingOperations.SetBinding(replacement,entry.Property,binding);
                    else replacement.SetValue(entry.Property,entry.Value);
                }
                var child=swap.Plain.Child;swap.Plain.Child=null;replacement.Child=child;
                swap.Replace(replacement);
                // Keep the same restoration identity without adding or removing any layout layer.
                replacement.Child=null;swap.Plain.Child=child;
                swap.Replace(swap.Plain);
            }
            await Settle(window);
            var repeatImage=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);repeatImage.Render(window);
            var repeatBytes=new byte[expected.Length];repeatImage.CopyPixels(repeatBytes,width*4,0);
            var controlPixels=0;for(var p=0;p<expected.Length;p+=4)if(!expected.AsSpan(p,4).SequenceEqual(repeatBytes.AsSpan(p,4)))controlPixels++;
            var controlPng=new PngBitmapEncoder();controlPng.Frames.Add(BitmapFrame.Create(repeatImage));using(var f=File.Create(Path.Combine(output,name+"-plain-repeat-control.png")))controlPng.Save(f);
            File.AppendAllText(Path.Combine(output,"dialog-control.jsonl"),JsonSerializer.Serialize(new{state=name,shared_plain_pixels=different,plain_repeat_pixels=controlPixels})+Environment.NewLine);
            foreach (var swap in swaps) { var child=swap.Plain.Child; swap.Plain.Child=null; swap.Frame.Child=child; swap.Replace(swap.Frame); }
            await Settle(window);
            var restoredImage=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);restoredImage.Render(window);
            var restoredBytes=new byte[actual.Length];restoredImage.CopyPixels(restoredBytes,width*4,0);
            int initialRestored=0,plainRestored=0;
            for(var p=0;p<actual.Length;p+=4){if(!actual.AsSpan(p,4).SequenceEqual(restoredBytes.AsSpan(p,4)))initialRestored++;if(!expected.AsSpan(p,4).SequenceEqual(restoredBytes.AsSpan(p,4)))plainRestored++;}
            var restoredPng=new PngBitmapEncoder();restoredPng.Frames.Add(BitmapFrame.Create(restoredImage));using(var f=File.Create(Path.Combine(output,name+"-restored-shared-control.png")))restoredPng.Save(f);
            File.AppendAllText(Path.Combine(output,"dialog-restoration.jsonl"),JsonSerializer.Serialize(new{state=name,initial_restored_pixels=initialRestored,plain_restored_pixels=plainRestored})+Environment.NewLine);
            var actualPng = new PngBitmapEncoder(); actualPng.Frames.Add(BitmapFrame.Create(image)); using (var f=File.Create(Path.Combine(output,name+"-shared-dialog-frame.png"))) actualPng.Save(f);
            if (different != 0)
            {
                File.WriteAllText(Path.Combine(output,"dialog-frames.json"),JsonSerializer.Serialize(DialogFrames));
                // Continue collecting every state; the aggregate check below still fails on any pixel.
                FrameFailures.Add(name+":"+different);
            }
        }
        // In candidate only, compare original baseline Grid against extracted UserControl inside the
        // SAME actual MainWindow and VM, sharing glyph caches. Baseline assembly run has no CaptureView.
        var capture = Descendants(window).OfType<FrameworkElement>().FirstOrDefault(e => e.GetType().FullName == "StartApp.App.CaptureView");
        if(captureComparison && capture?.Parent is System.Windows.Controls.Panel parent)
        {
            var index=parent.Children.IndexOf(capture);var baseline=new CaptureBaselineGrid(window.Resources);
            parent.Children.RemoveAt(index);parent.Children.Insert(index,baseline);await Settle(window);
            var original=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);original.Render(window);
            var firstPixels=new byte[width*height*4];var secondPixels=new byte[firstPixels.Length];image.CopyPixels(firstPixels,width*4,0);original.CopyPixels(secondPixels,width*4,0);
            var count=0;for(var p=0;p<firstPixels.Length;p+=4)if(!firstPixels.AsSpan(p,4).SequenceEqual(secondPixels.AsSpan(p,4)))count++;
            SameProcess.Add(new { state=name, different_pixels=count });
            var originalPng=new PngBitmapEncoder();originalPng.Frames.Add(BitmapFrame.Create(original));using(var f=File.Create(Path.Combine(output,name+"-original-grid.png")))originalPng.Save(f);
            parent.Children.RemoveAt(index);parent.Children.Insert(index,capture);await Settle(window);
        }
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var f=File.Create(Path.Combine(output,name+".png")))png.Save(f);
        // Native desktop screenshot as separate evidence: includes actual window/composition and focus.
        var point=window.PointToScreen(new System.Windows.Point(0,0));
        using var bitmap=new System.Drawing.Bitmap(width,height);using(var graphics=System.Drawing.Graphics.FromImage(bitmap))
            graphics.CopyFromScreen((int)point.X,(int)point.Y,0,0,bitmap.Size);
        bitmap.Save(Path.Combine(output,name+"-desktop.png"),System.Drawing.Imaging.ImageFormat.Png);
    }
}
