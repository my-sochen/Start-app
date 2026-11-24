using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
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
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using ListView = System.Windows.Controls.ListView;

// Identical test source runs against actual dev and candidate. Synthetic data only.
internal static class Program
{
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    private static readonly List<object> Checks = new();
    private static readonly List<object> Focus = new();
    [STAThread] private static int Main(string[] args)
    {
        if (args.Length != 1 || Directory.Exists(args[0])) return 2;
        var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var app = new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/StartApp.App;component/SharedResources.xaml") });
        using var repo = new JsonStore(Path.Combine(output, "fixture", "store.json"));
        string id = "";
        repo.Mutate(c => {
            id = c.CreateWorkspace("Feature view workspace", "Fixture details", "כללי").Id;
            c.AddToWorkspace(id, c.GetOrAddItem(ItemType.Link, "Example site", "https://example.com/").Id);
            c.AddToWorkspace(id, c.GetOrAddItem(ItemType.File, "Fixture file", @"C:\Fixture\note.txt").Id);
            c.AddCustomCategory("Fixture category");
        });
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        var window = new MainWindow(new UpdateViewModel(new InactiveUpdateSource(), null, _ => throw new Exception("Unexpected browser"))) { DataContext = vm, Width = 960, Height = 650, Left = 12, Top = 12 };
        var result = 0;
        void Check(string name, bool ok) { Checks.Add(new { name, ok }); if (!ok) throw new Exception(name); }
        window.ContentRendered += async (_, _) => {
            try {
                vm.OpenDetailsCommand.Execute(vm.Workspaces.Single()); await Idle(window);
                Check("Details bindings use the selected workspace", Inputs(window).Any(x => x.Text == "Feature view workspace") && Descendants(window).OfType<ListView>().Any(x => x.IsVisible && x.Items.Count == 2));
                await Take(window, output, "01-details");
                vm.FocusAddCommand.Execute(null); await Idle(window);
                Check("Focus-add reveals and focuses the target field", Name(Keyboard.FocusedElement) == "Item target");
                await Tabs(window, "details-add", 8); await Take(window, output, "02-details-add");
                var row = vm.Items.First();
                // The saved row address action currently opens the full item editor. Exercise the
                // dedicated address adapter directly without changing that existing product route.
                typeof(MainViewModel).GetMethod("BeginAddressEdit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, new object[] { row });
                await Idle(window); Check("Address binding", Input(window, "Full address").Text == row.Target);
                await TabsFrom(window, "address", "Full address", 4); await Take(window, output, "03-address");
                vm.CloseAddressEditCommand.Execute(null); Check("Address close command", !vm.AddressEditOpen);
                vm.OpenWithCommand.Execute(row); await Idle(window);
                Check("Open-with text binding", BindingOperations.GetBindingExpression(Input(window, "Program path"), TextBox.TextProperty)?.DataItem == vm);
                await TabsFrom(window, "open-with", "Program path", 4); await Take(window, output, "04-open-with");
                vm.CloseOpenWithCommand.Execute(null); Check("Open-with close command", !vm.OpenWithOpen);
                vm.OpenPickerCommand.Execute(null); await Idle(window);
                Check("Picker has source rows", vm.PickerRows.Count == 1 && Descendants(window).OfType<ListBox>().Any(x => x.IsVisible && AutomationProperties.GetName(x) == "Resource list" && x.Items.Count == 1));
                await TabsFrom(window, "picker", "Search resources", 4); await Take(window, output, "05-picker");
                var pick = Descendants(window).OfType<Button>().Single(x => x.IsVisible && AutomationProperties.GetName(x) == "Pick resource");
                Check("Picker row command reaches view model", ReferenceEquals(pick.Command, vm.PickResourceCommand));
                pick.Command!.Execute(pick.CommandParameter); Check("Picker selection fills target and closes", !vm.PickerOpen && vm.NewTarget == row.Target);
                vm.DesignCardCommand.Execute(vm.Selected); await Idle(window);
                Check("Design binding", Input(window, "Card color").Text == vm.DesignColor);
                await TabsFrom(window, "design", "Card color", 6); await Take(window, output, "06-design");
                vm.CloseDesignCommand.Execute(null); Check("Design close command", !vm.DesignEditOpen);
                vm.EditCategoryPopupCommand.Execute(vm.Selected); await Idle(window);
                Check("Category has rows", vm.CategoryRows.Count > 1);
                await TabsFrom(window, "category", "New category name", 5); await Take(window, output, "07-category");
                vm.CloseCategoryPopupCommand.Execute(null); Check("Category close command", !vm.CategoryEditOpen);
                window.Width = 720; window.Height = 400; await Take(window, output, "08-details-minimum");
                window.Width = 960; window.Height = 650;
                // Default confirmation retains its window-owned keyboard focus and Escape route.
                vm.SetDefaultWorkspace(vm.Selected!.Id); vm.SetDefaultWorkspace(null); await Idle(window);
                Check("Default confirmation opens", vm.DefaultConfirmOpen);
                Key(0x1B); await Idle(window); Check("Escape closes default confirmation", !vm.DefaultConfirmOpen);
                File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(Checks));
                File.WriteAllText(Path.Combine(output, "focus.json"), JsonSerializer.Serialize(Focus));
            } catch (Exception e) { result = 1; File.WriteAllText(Path.Combine(output, "error.txt"), e.ToString()); }
            finally { window.Close(); app.Shutdown(result); }
        };
        app.Run(window); return result;
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
