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
                var row = ((Grid)window.Content).Children.OfType<Border>().Single(b => b.DataContext is UpdateViewModel);
                foreach (var width in new[] { 900, 720 })
                {
                    window.Width = width;
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    Capture(window, output, $"{width}-inactive");
                    CaptureSettings(window, window.Updates, output, $"{width}-settings-inactive", width);
                    var opened = new List<string>();
                    using var available = new UpdateViewModel(new SyntheticSource(_ => Task.FromResult<ApprovedUpdate?>(Patch)), "1.0.0", uri => opened.Add(uri.AbsoluteUri));
                    row.DataContext = available;
                    await available.CheckAsync();
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    Capture(window, output, $"{width}-available");
                    CaptureSettings(window, available, output, $"{width}-settings-available", width);
                    CapturePopup(window, available, output, $"{width}-startup-popup", width);
                    available.DownloadCommand.Execute(null);
                    if (opened.Count != 1 || opened[0] != UpdateViewModel.OfficialWebsite) throw new Exception("Synthetic handoff target mismatch");
                    var betaOpened = new List<string>();
                    using var beta = new UpdateViewModel(new SyntheticSource(_ => Task.FromResult<ApprovedUpdate?>(new("0.2.0-beta.1", new string('a',40), new string('b',64)))),
                        "0.1.0-proto.2", uri => betaOpened.Add(uri.AbsoluteUri), revalidateBeforeDownload: true);
                    int betaNotices = 0; beta.StartupUpdateAvailable += () => betaNotices++;
                    await beta.CheckOnStartupOnceAsync(); await beta.CheckOnStartupOnceAsync();
                    if (betaNotices != 1 || !beta.UpdateAvailable || !beta.Status.Contains("בטא")) throw new Exception("Beta startup notice missing");
                    CaptureSettings(window, beta, output, $"{width}-beta-settings", width);
                    CapturePopup(window, beta, output, $"{width}-beta-startup-popup", width);
                    if (betaOpened.Count != 0) throw new Exception("Beta notice opened browser without explicit click");
                    beta.DownloadCommand.Execute(null); await beta.CurrentCheck;
                    if (betaOpened.Count != 1 || betaOpened[0] != UpdateViewModel.OfficialWebsite) throw new Exception("Beta handoff mismatch");
                    using var failure = new UpdateViewModel(new SyntheticSource(_ => throw new HttpRequestException("synthetic")), "1.0.0", _ => throw new Exception("Unexpected browser call"));
                    row.DataContext = failure; await failure.CheckAsync();
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    Capture(window, output, $"{width}-failure");
                    CaptureSettings(window, failure, output, $"{width}-settings-failure", width);
                    var gate = new TaskCompletionSource<ApprovedUpdate?>();
                    using var busy = new UpdateViewModel(new SyntheticSource(_ => gate.Task), "1.0.0", _ => throw new Exception("Unexpected browser call"));
                    row.DataContext = busy; var pending = busy.CheckAsync();
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    Capture(window, output, $"{width}-busy");
                    CaptureSettings(window, busy, output, $"{width}-settings-busy", width);
                    gate.SetResult(null); await pending;
                    row.DataContext = window.Updates;
                }
                await CaptureProductionEventPath(window, output);
                File.WriteAllText(Path.Combine(output, "capture-evidence.json"), JsonSerializer.Serialize(Evidence, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(0);
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "capture-error.txt"), ex.ToString()); app.Shutdown(1); }
        };
        return app.Run(window);
    }
    private sealed class CatalogHandler(byte[] body) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            if (request.RequestUri!.AbsoluteUri != "https://my-sochen.github.io/Start-app/synthetic-beta-notice.json") throw new Exception("Wrong source");
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(body) };
            response.Content.Headers.ContentType = new("application/json"); return Task.FromResult(response);
        }
    }
    private static async Task CaptureProductionEventPath(Window owner, string output)
    {
        // Exact real composition reads actual App identity. Handler supplies bytes only, never a real request.
        var now = DateTimeOffset.UtcNow;
        var doc = new { schema=1, revision=1, channel="beta-notice", generated_utc=now.AddMinutes(-1).ToString("yyyy-MM-ddTHH:mm:ssZ"), expires_utc=now.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ssZ"), releases=new[] {new {version="0.2.0-beta.1", tag="v0.2.0-beta.1", commit=new string('a',40), release_id=1, asset_id=2, asset="StartApp-win-x64.zip", size=10, sha256=new string('b',64), layout="portable-v1", approval_record_id="synthetic-not-approval"}}};
        foreach (var width in new[] {900,720})
        {
            var handler = new CatalogHandler(JsonSerializer.SerializeToUtf8Bytes(doc)); var opened = new List<string>();
            var updates = UpdateComposition.CreateWithTransport(new("https://my-sochen.github.io/Start-app/synthetic-beta-notice.json"), handler, uri=>opened.Add(uri.AbsoluteUri), ()=>now);
            if (!updates.InstalledVersionDisplay.Contains("0.1.0-proto.2")) throw new Exception("Actual App identity not composed");
            using var store = new JsonStore(Path.Combine(output,"event-store-"+width,"store.json"));
            var actual = new MainWindow(updates) { Owner=owner, Width=width, Height=560, DataContext=new MainViewModel(store,new NotImplementedLauncher()) };
            actual.Show();
            var deadline=DateTime.UtcNow.AddSeconds(5); UpdateAvailableWindow[] notices=[];
            do {await actual.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);notices=actual.OwnedWindows.OfType<UpdateAvailableWindow>().ToArray();if(notices.Length==1)break;await Task.Delay(25);}while(DateTime.UtcNow<deadline);
            if(notices.Length!=1||handler.Calls!=1||opened.Count!=0||!updates.UpdateAvailable)throw new Exception("Real Loaded/source/event/popup chain failed");
            var popup=notices.Single();Capture(popup,output,width+"-event-beta-popup");CaptureSettings(actual,updates,output,width+"-event-beta-settings",width);
            await updates.CheckOnStartupOnceAsync();
            if(handler.Calls!=1||actual.OwnedWindows.OfType<UpdateAvailableWindow>().Count()!=1)throw new Exception("Duplicate startup discovery/popup");
            popup.Close();await actual.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
            if(!updates.UpdateAvailable||opened.Count!=0)throw new Exception("Dismissal affected optional offer");
            updates.DownloadCommand.Execute(null);await updates.CurrentCheck;
            if(handler.Calls!=2||opened.Count!=1||opened.Single()!=UpdateViewModel.OfficialWebsite)throw new Exception("Event path explicit click/revalidation failed");
            Evidence.Add(new {state=width+"-production-event-path", handler.Calls, popupCreatedBy="MainWindow.Loaded -> real stamped composition -> BetaNoticeSource HTTP/schema -> startup event -> owned actual popup", network="synthetic handler only", publication="none", explicitBrowserSinkCalls=opened.Count});
            actual.Close();
        }
    }
    private static void CapturePopup(Window owner, UpdateViewModel updates, string output, string name, int width)
    {
        var popup = new UpdateAvailableWindow(updates) { Owner = owner, Width = width == 900 ? 560 : 460, Height = 300 };
        popup.Show(); popup.UpdateLayout();
        var buttons = Descendants(popup).OfType<Button>().ToArray();
        if (!buttons.Any(b => ReferenceEquals(b.Command, updates.DownloadCommand) && b.IsEnabled))
            throw new Exception("Popup official update button missing or disabled");
        if (!buttons.Any(b => b.Content as string == "לא עכשיו")) throw new Exception("Popup dismissal missing");
        Capture(popup, output, name); popup.Close();
        if (!updates.UpdateAvailable) throw new Exception("Popup dismissal changed update state");
    }
    private static void CaptureSettings(Window owner, UpdateViewModel updates, string output, string name, int width)
    {
        // Actual settings XAML, shared synthetic VM. Closing this owned window
        // must not dispose the shared updater or make a second network call.
        var settings = new UpdateSettingsWindow(updates) { Owner = owner, Width = width, Height = 400 };
        settings.Show(); settings.UpdateLayout();
        var buttons = Descendants(settings).OfType<Button>().Where(b => b.Content is string text && text == "בדוק עדכונים").ToArray();
        if (buttons.Length != 1) throw new Exception("Settings must own exactly one manual check");
        if (Descendants(owner).OfType<Button>().Any(b => b.Content is string text && text == "בדוק עדכונים"))
            throw new Exception("Manual check must not remain in main top bar");
        if (!Descendants(settings).OfType<TextBlock>().Any(t => t.Text == "גרסאות ועדכונים"))
            throw new Exception("Exact versions/settings section missing");
        if (!Descendants(settings).OfType<TextBlock>().Any(t => t.Text == updates.InstalledVersionDisplay))
            throw new Exception("Installed version display missing");
        Capture(settings, output, name); settings.Close();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Descendants(child)) yield return next; }
    }
    private static void Capture(Window window, string output, string name)
    {
        window.UpdateLayout();
        // Render the window visual root, not its margin/RTL-transformed child.
        // The root includes the actual opaque window background and preserves
        // inherited RTL transforms, avoiding clipped/mirrored child-only renders.
        var width = checked((int)Math.Ceiling(window.ActualWidth)); var height = checked((int)Math.Ceiling(window.ActualHeight));
        var popup = window is UpdateAvailableWindow;
        if (width < (popup ? 460 : 720) || height < (popup ? 260 : 400))
            throw new Exception($"Invalid window render bounds: {width}x{height}");
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        // Window.ActualWidth/Height include nonclient space not painted by WPF.
        // Find every painted pixel. Only discard wholly transparent TRAILING
        // rows/columns, never content, leading space or a partly painted strip.
        var pixels = new byte[checked(width * height * 4)];
        bitmap.CopyPixels(pixels, checked(width * 4), 0);
        var paintedWidth = 0; var paintedHeight = 0;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (pixels[(y * width + x) * 4 + 3] != 0)
                { paintedWidth = Math.Max(paintedWidth, x + 1); paintedHeight = Math.Max(paintedHeight, y + 1); }
        // Refuse empty/implausible captures rather than hide a rendering failure.
        if (paintedWidth < width - 40 || paintedHeight < height - 80 || paintedWidth == 0 || paintedHeight == 0)
            throw new Exception($"Unexpected painted bounds: {paintedWidth}x{paintedHeight} within {width}x{height}");
        var painted = new CroppedBitmap(bitmap, new Int32Rect(0, 0, paintedWidth, paintedHeight));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(painted));
        using (var stream = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(stream);
        var buttons = Descendants(window).OfType<Button>().Where(b => b.Content is string s && (s == "בדוק עדכונים" || s == "פתח הורדה רשמית" || s == "עדכון - פתח הורדה רשמית" || s == "לא עכשיו")).Select(b => new { text = b.Content, enabled = b.IsEnabled, visible = b.IsVisible, width = b.ActualWidth }).ToArray();
        Evidence.Add(new { state = name, requestedWindowWidth = window.Width, actualWindowWidth = window.ActualWidth, minimumWindowWidth = window.MinWidth, width, height, paintedWidth, paintedHeight, buttons, rendering = "Actual WPF window visual root at 96dpi; only wholly transparent trailing nonclient strips omitted; native titlebar/taskbar not captured; synthetic update source and browser sink" });
    }
}
