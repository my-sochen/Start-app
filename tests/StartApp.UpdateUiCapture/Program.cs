using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StartApp.App;
using StartApp.Presentation;
using StartApp.Storage.Json;

// Separate test executable, NOT compiled into the product or added to the solution.
// Renders the product XAML using WPF, synthetic source and fresh synthetic store.
// No real browser/network call, App.OnStartup or real LOCALAPPDATA is used.
internal static class Program
{
    private sealed class SyntheticSource(Func<CancellationToken, Task<ApprovedUpdate?>> read) : IApprovedUpdateSource
    { public Task<ApprovedUpdate?> ReadAsync(CancellationToken token) => read(token); }
    private static readonly ApprovedUpdate Patch = new("1.0.1", new string('a', 40), new string('b', 64));
    private static readonly List<object> Evidence = new();
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 1) return 2;
        var output = Path.GetFullPath(args[0]);
        // Refuse reuse, never delete anything. The caller must choose a NEW folder.
        if (Directory.Exists(output) || File.Exists(output)) return 3;
        Directory.CreateDirectory(output);
        var app = new Application();
        var window = new MainWindow { Width = 900, Height = 560 };
        using var store = new JsonStore(Path.Combine(output, "synthetic-store", "store.json"));
        window.DataContext = new MainViewModel(store, new NotImplementedLauncher());
        window.ContentRendered += async (_, _) =>
        {
            try
            {
                // Find the real production update row via its existing view-model type.
                var row = Descendants(window).OfType<Border>().Single(b => b.DataContext is UpdateViewModel);
                foreach (var width in new[] { 900, 720 })
                {
                    window.Width = width;
                    Capture(window, output, $"{width}-inactive");
                    var opened = new List<string>();
                    using var available = new UpdateViewModel(new SyntheticSource(_ => Task.FromResult<ApprovedUpdate?>(Patch)), "1.0.0", uri => opened.Add(uri.AbsoluteUri));
                    row.DataContext = available;
                    await available.CheckAsync();
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    Capture(window, output, $"{width}-available");
                    available.DownloadCommand.Execute(null);
                    if (opened.Count != 1 || opened[0] != UpdateViewModel.OfficialWebsite) throw new Exception("Synthetic handoff target mismatch");
                    using var failure = new UpdateViewModel(new SyntheticSource(_ => throw new HttpRequestException("synthetic")), "1.0.0", _ => throw new Exception("Unexpected browser call"));
                    row.DataContext = failure; await failure.CheckAsync();
                    Capture(window, output, $"{width}-failure");
                    var gate = new TaskCompletionSource<ApprovedUpdate?>();
                    using var busy = new UpdateViewModel(new SyntheticSource(_ => gate.Task), "1.0.0", _ => throw new Exception("Unexpected browser call"));
                    row.DataContext = busy; var pending = busy.CheckAsync();
                    Capture(window, output, $"{width}-busy");
                    gate.SetResult(null); await pending;
                    row.DataContext = window.Updates;
                }
                File.WriteAllText(Path.Combine(output, "capture-evidence.json"), JsonSerializer.Serialize(Evidence, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(0);
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "capture-error.txt"), ex.ToString()); app.Shutdown(1); }
        };
        return app.Run(window);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Descendants(child)) yield return next; }
    }
    private static void Capture(Window window, string output, string name)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var width = checked((int)Math.Ceiling(content.ActualWidth)); var height = checked((int)Math.Ceiling(content.ActualHeight));
        if (width < 600 || height < 300) throw new Exception("Invalid content render bounds");
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(stream);
        var buttons = Descendants(window).OfType<Button>().Where(b => b.Content is string s && (s == "בדוק עדכונים" || s == "פתח הורדה רשמית")).Select(b => new { text = b.Content, enabled = b.IsEnabled, visible = b.IsVisible, width = b.ActualWidth }).ToArray();
        Evidence.Add(new { state = name, width, height, buttons, rendering = "Actual WPF content at 96dpi; excludes native titlebar/taskbar; synthetic update source and browser sink" });
    }
}
